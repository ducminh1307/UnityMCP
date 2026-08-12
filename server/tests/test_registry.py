from __future__ import annotations

import asyncio

import pytest

from unity_mcp_server.bridge import RegistryHttpResult
from unity_mcp_server.config import GatewayLimits
from unity_mcp_server.errors import BridgeError, RegistryError
from unity_mcp_server.models import ToolDescriptor
from unity_mcp_server.registry import DynamicToolRegistry


def tool(name: str, *, enabled: bool = True, implemented: bool = True, scope: str = "editor") -> dict:
    return {
        "name": name,
        "description": name,
        "category": "test",
        "scopes": [scope],
        "inputSchema": {"type": "object", "properties": {}, "additionalProperties": False},
        "safety": "safe-read",
        "implemented": implemented,
        "enabled": enabled,
        "valid": True,
        "timeoutMs": 1000,
    }


class FakeBridge:
    def __init__(self, responses) -> None:
        self.responses = list(responses)
        self.etags: list[str | None] = []
        self.verified = 0

    async def verify_instance(self):
        self.verified += 1

    async def fetch_tools(self, etag=None):
        self.etags.append(etag)
        response = self.responses.pop(0) if len(self.responses) > 1 else self.responses[0]
        if isinstance(response, Exception):
            raise response
        return response


@pytest.mark.asyncio
async def test_registry_etag_atomic_snapshot_and_filtering() -> None:
    clock_value = [10.0]
    bridge = FakeBridge(
        [
            RegistryHttpResult(
                False,
                '"one"',
                {
                    "registryRevision": 1,
                    "tools": [
                        tool("z-enabled"),
                        tool("a-disabled", enabled=False),
                        tool("planned", implemented=False),
                        tool("runtime-only", scope="runtime"),
                    ],
                },
            ),
            RegistryHttpResult(True, '"one"', None),
        ]
    )
    registry = DynamicToolRegistry(bridge, clock=lambda: clock_value[0])

    first = await registry.refresh(force=True)
    clock_value[0] = 11.0
    second = await registry.refresh(force=True)

    assert bridge.etags == [None, '"one"']
    assert bridge.verified == 1
    assert [entry.name for entry in first.advertised("editor")] == ["z-enabled"]
    assert [entry.name for entry in first.advertised("player")] == ["runtime-only"]
    assert second.tools is first.tools
    assert second.fetched_at == 11.0


@pytest.mark.asyncio
async def test_invalid_tools_are_quarantined_individually() -> None:
    good = RegistryHttpResult(
        False, '"one"', {"registryRevision": "1", "tools": [tool("scene-list")]}
    )
    duplicate = RegistryHttpResult(
        False,
        '"two"',
        {
            "registryRevision": "2",
            "tools": [
                tool("unity-status"),
                tool("project-info"),
                tool("scene-list"),
                tool("scene-list"),
            ],
        },
    )
    bridge = FakeBridge([good, duplicate])
    registry = DynamicToolRegistry(bridge)
    await registry.refresh(force=True)

    quarantined = await registry.refresh(force=True)
    assert [entry.name for entry in quarantined.tools] == ["project-info", "unity-status"]
    assert quarantined.state == "ready_with_invalid_tools"
    assert quarantined.invalid_tools[0].name == "scene-list"
    assert quarantined.invalid_tools[0].code == "duplicate_tool_name"
    assert quarantined.by_name("scene-list", "editor") is None


@pytest.mark.asyncio
async def test_malformed_registry_envelope_is_quarantined_and_poll_can_continue() -> None:
    good = RegistryHttpResult(
        False, '"one"', {"registryRevision": "1", "tools": [tool("scene-list")]}
    )
    malformed = RegistryHttpResult(
        False,
        '"two"',
        {"registryRevision": "2", "tools": "not-an-array"},
    )
    bridge = FakeBridge([good, malformed, RegistryHttpResult(True, '"one"', None)])
    limits = GatewayLimits(registry_poll_seconds=0.001, registry_min_refresh_seconds=0.0)
    registry = DynamicToolRegistry(bridge, limits=limits)
    original = await registry.refresh(force=True)

    quarantined = await registry.refresh(force=True)
    assert quarantined.tools == original.tools
    assert quarantined.state == "invalid_registry"

    stop = asyncio.Event()
    poll = asyncio.create_task(registry.poll(stop))
    await asyncio.sleep(0.005)
    stop.set()
    await poll
    assert not poll.cancelled()
    assert len(bridge.etags) >= 3


@pytest.mark.asyncio
async def test_invalid_json_schema_does_not_hide_valid_tools() -> None:
    invalid = tool("bad-schema")
    invalid["inputSchema"] = {"type": "not-a-json-schema-type"}
    bridge = FakeBridge(
        [
            RegistryHttpResult(
                False,
                '"bad"',
                {"registryRevision": "bad", "tools": [tool("unity-status"), invalid]},
            )
        ]
    )
    registry = DynamicToolRegistry(bridge)

    snapshot = await registry.refresh(force=True)

    assert snapshot.state == "ready_with_invalid_tools"
    assert [entry.name for entry in snapshot.tools] == ["unity-status"]
    assert snapshot.invalid_tools[0].name == "bad-schema"
    assert "input schema" in snapshot.invalid_tools[0].message


@pytest.mark.asyncio
async def test_offline_grace_expiry_clears_etag_before_reconnect() -> None:
    clock_value = [10.0]

    class ReconnectingBridge:
        def __init__(self) -> None:
            self.etags: list[str | None] = []

        async def verify_instance(self) -> None:
            return None

        async def fetch_tools(self, etag=None):
            self.etags.append(etag)
            if len(self.etags) == 1:
                return RegistryHttpResult(
                    False,
                    '"one"',
                    {"registryRevision": "1", "tools": [tool("unity-status"), tool("scene-list")]},
                )
            if len(self.etags) == 2:
                raise BridgeError("target_unavailable", "Unity is reloading", retryable=True)
            if etag is not None:
                return RegistryHttpResult(True, etag, None)
            return RegistryHttpResult(
                False,
                '"two"',
                {"registryRevision": "2", "tools": [tool("unity-status"), tool("scene-list")]},
            )

    bridge = ReconnectingBridge()
    limits = GatewayLimits(registry_min_refresh_seconds=0.0, reload_grace_seconds=30.0)
    registry = DynamicToolRegistry(bridge, limits=limits, clock=lambda: clock_value[0])
    await registry.refresh(force=True)

    clock_value[0] = 41.0
    offline = await registry.refresh(force=True)
    assert [entry.name for entry in offline.tools] == ["unity-status"]
    assert offline.etag is None

    clock_value[0] = 42.0
    recovered = await registry.refresh(force=True)
    assert bridge.etags == [None, '"one"', None]
    assert recovered.revision == "2"
    assert [entry.name for entry in recovered.tools] == ["scene-list", "unity-status"]


def test_descriptor_defaults_match_unity_wire_and_empty_output_means_unstructured() -> None:
    raw = tool("unity-wire")
    raw.pop("implemented")
    raw.pop("valid")
    raw["outputSchema"] = {}

    parsed = ToolDescriptor.from_dict(raw)

    assert parsed.implemented is True
    assert parsed.valid is True
    assert parsed.status == "implemented"
    assert parsed.output_schema is None


def test_security_booleans_are_not_coerced_from_strings() -> None:
    raw = tool("not-enabled")
    raw["enabled"] = "false"

    with pytest.raises(RegistryError, match="enabled must be a boolean"):
        ToolDescriptor.from_dict(raw)
