from __future__ import annotations

import json

import pytest

from unity_mcp_server.bridge import RegistryHttpResult
from unity_mcp_server.errors import BridgeError, SchemaValidationError
from unity_mcp_server.models import InstanceDescriptor
from unity_mcp_server.registry import DynamicToolRegistry
from unity_mcp_server.service import UnityGatewayService


def instance() -> InstanceDescriptor:
    return InstanceDescriptor.from_dict(
        {
            "port": 45678,
            "token": "token-" * 8,
            "pid": 42,
            "projectId": "project",
            "instanceId": "editor-1",
            "kind": "editor",
            "buildId": "build",
        }
    )


def descriptor(name: str = "echo", *, enabled: bool = True) -> dict:
    return {
        "name": name,
        "description": "Echo a number",
        "category": "test",
        "scopes": ["editor"],
        "inputSchema": {
            "type": "object",
            "properties": {"value": {"type": "integer"}},
            "required": ["value"],
            "additionalProperties": False,
        },
        "outputSchema": {
            "type": "object",
            "properties": {"echo": {"type": "integer"}},
            "required": ["echo"],
            "additionalProperties": False,
        },
        "safety": "safe-read",
        "implemented": True,
        "enabled": enabled,
        "valid": True,
        "timeoutMs": 1000,
    }


class FakeBridge:
    def __init__(self, tools: list[dict]) -> None:
        self.tools = tools
        self.calls: list[tuple[str, dict, str, float]] = []
        self.revision = 1
        self.conflict_once = False
        self.bad_output = False
        self.fail_code: str | None = None

    async def verify_instance(self):
        return None

    async def fetch_tools(self, etag=None):
        return RegistryHttpResult(
            False,
            f'"{self.revision}"',
            {"registryRevision": str(self.revision), "tools": self.tools},
        )

    async def call_tool(self, name, arguments, revision, *, timeout_seconds):
        self.calls.append((name, arguments, revision, timeout_seconds))
        if self.fail_code is not None:
            raise BridgeError(self.fail_code, "forced failure", retryable=True)
        if self.conflict_once:
            self.conflict_once = False
            self.revision += 1
            raise BridgeError("registry_conflict", "stale", status_code=409)
        value = "bad" if self.bad_output else arguments["value"]
        return {
            "content": [{"type": "text", "text": "done"}],
            "structuredContent": {"echo": value},
            "isError": False,
        }

    async def get_job(self, job_id):
        return {"jobId": job_id, "status": "running"}

    async def cancel_job(self, job_id):
        return {"jobId": job_id, "cancelled": True}

    async def aclose(self):
        return None


def service(bridge: FakeBridge) -> UnityGatewayService:
    registry = DynamicToolRegistry(bridge)
    return UnityGatewayService(instance(), bridge, registry)


@pytest.mark.asyncio
async def test_service_validates_routes_and_validates_structured_output() -> None:
    bridge = FakeBridge([descriptor(), descriptor("disabled", enabled=False)])
    gateway = service(bridge)

    assert [item.name for item in await gateway.list_tools()] == ["echo"]
    output = await gateway.call_tool("echo", {"value": 7})
    assert output.structured_content == {"echo": 7}
    assert bridge.calls == [("echo", {"value": 7}, "1", 1.0)]

    with pytest.raises(SchemaValidationError) as invalid_input:
        await gateway.call_tool("echo", {"value": "seven"})
    assert invalid_input.value.path == "$.value"

    with pytest.raises(BridgeError) as unavailable:
        await gateway.call_tool("disabled", {"value": 1})
    assert unavailable.value.code == "tool_unavailable"

    bridge.bad_output = True
    with pytest.raises(SchemaValidationError) as invalid_output:
        await gateway.call_tool("echo", {"value": 8})
    assert invalid_output.value.phase == "output"


@pytest.mark.asyncio
async def test_service_refreshes_and_retries_one_registry_conflict() -> None:
    bridge = FakeBridge([descriptor()])
    bridge.conflict_once = True
    gateway = service(bridge)

    output = await gateway.call_tool("echo", {"value": 3})

    assert output.structured_content == {"echo": 3}
    assert [call[2] for call in bridge.calls] == ["1", "2"]


@pytest.mark.asyncio
async def test_service_limits_advertised_and_callable_tools_with_allowlist(monkeypatch) -> None:
    monkeypatch.setenv("UNITY_MCP_ALLOWED_TOOLS", "echo")
    bridge = FakeBridge([descriptor(), descriptor("other")])
    gateway = service(bridge)

    assert [item.name for item in await gateway.list_tools()] == ["echo"]
    output = await gateway.call_tool("echo", {"value": 2})
    assert output.structured_content == {"echo": 2}

    with pytest.raises(BridgeError) as unavailable:
        await gateway.call_tool("other", {"value": 2})
    assert unavailable.value.code == "tool_unavailable"


@pytest.mark.asyncio
async def test_service_minimal_profile_advertises_core_tools(monkeypatch) -> None:
    monkeypatch.setenv("UNITY_MCP_TOOL_PROFILE", "minimal")
    bridge = FakeBridge([descriptor("unity-status"), descriptor("compile-status"), descriptor("scene-hierarchy")])
    gateway = service(bridge)

    assert [item.name for item in await gateway.list_tools()] == ["compile-status", "unity-status"]


@pytest.mark.asyncio
async def test_service_writes_opt_in_tool_telemetry(monkeypatch, tmp_path) -> None:
    telemetry_path = tmp_path / "unity-mcp-telemetry.jsonl"
    monkeypatch.setenv("UNITY_MCP_TELEMETRY_PATH", str(telemetry_path))
    bridge = FakeBridge([descriptor()])
    gateway = service(bridge)

    await gateway.list_tools()
    await gateway.call_tool("echo", {"value": 9})

    events = [json.loads(line) for line in telemetry_path.read_text(encoding="utf-8").splitlines()]
    assert [event["event"] for event in events] == ["tools/list", "tools/call"]
    assert events[0]["toolCount"] == 1
    assert events[0]["responseBytes"] > 0
    assert events[1]["toolName"] == "echo"
    assert events[1]["requestBytes"] > 0
    assert events[1]["responseBytes"] > 0
    assert events[1]["isError"] is False


@pytest.mark.asyncio
async def test_service_writes_telemetry_for_bridge_errors(monkeypatch, tmp_path) -> None:
    telemetry_path = tmp_path / "unity-mcp-telemetry.jsonl"
    monkeypatch.setenv("UNITY_MCP_TELEMETRY_PATH", str(telemetry_path))
    bridge = FakeBridge([descriptor()])
    bridge.fail_code = "timeout"
    gateway = service(bridge)

    with pytest.raises(BridgeError):
        await gateway.call_tool("echo", {"value": 9})

    event = json.loads(telemetry_path.read_text(encoding="utf-8").splitlines()[0])
    assert event["event"] == "tools/call"
    assert event["toolName"] == "echo"
    assert event["isError"] is True
    assert event["errorCode"] == "timeout"


@pytest.mark.asyncio
async def test_tools_resource_reports_quarantined_descriptors_without_advertising_them() -> None:
    invalid = descriptor("bad-schema")
    invalid["inputSchema"] = {"type": "string"}
    bridge = FakeBridge([descriptor(), invalid])
    gateway = service(bridge)

    listed = await gateway.list_tools()
    catalog = gateway.tools_resource()

    assert [entry.name for entry in listed] == ["echo"]
    assert [entry["name"] for entry in catalog["tools"]] == ["echo"]
    assert catalog["state"] == "ready_with_invalid_tools"
    assert catalog["invalidTools"][0]["name"] == "bad-schema"


def test_service_wraps_primitive_unity_result_to_match_output_schema() -> None:
    raw = descriptor("primitive")
    raw["outputSchema"] = {
        "type": "object",
        "properties": {"result": {"type": "integer"}},
        "required": ["result"],
        "additionalProperties": False,
    }
    bridge = FakeBridge([raw])
    gateway = service(bridge)
    parsed = gateway.registry._parse_registry(
        {"registryRevision": "1", "tools": [raw]}, '"1"', 1.0
    ).tools[0]

    output = gateway._normalize_tool_result(
        parsed,
        {"content": [], "structuredContent": 4, "isError": False},
    )

    assert output.structured_content == {"result": 4}


def test_service_percent_encodes_opaque_job_uri() -> None:
    bridge = FakeBridge([descriptor()])
    gateway = service(bridge)
    parsed = gateway.registry._parse_registry(
        {"registryRevision": "1", "tools": [descriptor()]}, '"1"', 1.0
    ).tools[0]

    output = gateway._normalize_tool_result(
        parsed,
        {"content": [], "structuredContent": {"echo": 4}, "isError": False, "jobId": "build?job#part"},
    )

    assert output.meta["com.ducminh.unity-mcp/jobUri"] == "unity://jobs/build%3Fjob%23part"


@pytest.mark.parametrize("job_id", ["build/job", "build\\job", chr(0xD800)])
def test_service_rejects_job_ids_that_cannot_round_trip_to_unity(job_id: str) -> None:
    bridge = FakeBridge([descriptor()])
    gateway = service(bridge)
    parsed = gateway.registry._parse_registry(
        {"registryRevision": "1", "tools": [descriptor()]}, '"1"', 1.0
    ).tools[0]

    with pytest.raises(BridgeError, match="jobId is invalid"):
        gateway._normalize_tool_result(
            parsed,
            {"content": [], "structuredContent": {"echo": 4}, "isError": False, "jobId": job_id},
        )
