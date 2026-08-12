from __future__ import annotations

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
