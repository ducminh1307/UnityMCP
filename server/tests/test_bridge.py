from __future__ import annotations

import json

import httpx
import pytest

from unity_mcp_server.bridge import UnityBridgeClient
from unity_mcp_server.config import GatewayLimits
from unity_mcp_server.errors import BridgeError
from unity_mcp_server.models import InstanceDescriptor


def instance() -> InstanceDescriptor:
    return InstanceDescriptor.from_dict(
        {
            "port": 45678,
            "token": "secret-" * 8,
            "pid": 42,
            "projectId": "project",
            "instanceId": "instance",
            "kind": "editor",
            "buildId": "build",
        }
    )


@pytest.mark.asyncio
async def test_etag_authentication_and_tool_routing() -> None:
    requests: list[httpx.Request] = []

    def handler(request: httpx.Request) -> httpx.Response:
        requests.append(request)
        assert request.headers["Authorization"] == f"Bearer {instance().token}"
        assert request.headers["Host"] == "127.0.0.1:45678"
        if request.url.path == "/api/v1/tools" and "if-none-match" not in request.headers:
            return httpx.Response(
                200,
                headers={"ETag": '"rev-1"', "Content-Type": "application/json"},
                json={"registryRevision": "1", "tools": []},
            )
        if request.url.path == "/api/v1/tools":
            assert request.headers["If-None-Match"] == '"rev-1"'
            return httpx.Response(304, headers={"ETag": '"rev-1"'})
        assert request.url.path == "/api/v1/tools/project-enemy-spawn/call"
        assert json.loads(request.content) == {"arguments": {"count": 2}, "registryRevision": "1"}
        return httpx.Response(200, headers={"Content-Type": "application/json"}, json={"content": []})

    async with httpx.AsyncClient(
        base_url="http://127.0.0.1:45678", transport=httpx.MockTransport(handler)
    ) as http_client:
        bridge = UnityBridgeClient(instance(), client=http_client)
        first = await bridge.fetch_tools()
        second = await bridge.fetch_tools(first.etag)
        await bridge.call_tool("project-enemy-spawn", {"count": 2}, "1")

    assert first.payload == {"registryRevision": "1", "tools": []}
    assert second.not_modified is True
    assert len(requests) == 3


@pytest.mark.asyncio
async def test_health_identity_must_match_descriptor() -> None:
    public = instance().public_dict()

    def matching(_: httpx.Request) -> httpx.Response:
        return httpx.Response(
            200,
            headers={"Content-Type": "application/json"},
            json={"status": "ok", "registryRevision": "1", "instance": public},
        )

    async with httpx.AsyncClient(
        base_url="http://127.0.0.1:45678", transport=httpx.MockTransport(matching)
    ) as http_client:
        await UnityBridgeClient(instance(), client=http_client).verify_instance()

    public["instanceId"] = "another-instance"
    async with httpx.AsyncClient(
        base_url="http://127.0.0.1:45678", transport=httpx.MockTransport(matching)
    ) as http_client:
        with pytest.raises(BridgeError) as error:
            await UnityBridgeClient(instance(), client=http_client).verify_instance()

    assert error.value.code == "descriptor_mismatch"


@pytest.mark.asyncio
async def test_bridge_hard_caps_streamed_response() -> None:
    def handler(_: httpx.Request) -> httpx.Response:
        return httpx.Response(200, headers={"Content-Type": "application/json"}, content=b"{" + b"x" * 64 + b"}")

    async with httpx.AsyncClient(
        base_url="http://127.0.0.1:45678", transport=httpx.MockTransport(handler)
    ) as http_client:
        bridge = UnityBridgeClient(instance(), client=http_client, limits=GatewayLimits(max_registry_bytes=32))
        with pytest.raises(BridgeError) as error:
            await bridge.fetch_tools()

    assert error.value.code == "response_too_large"


@pytest.mark.asyncio
async def test_bridge_rejects_duplicate_json_keys() -> None:
    def handler(_: httpx.Request) -> httpx.Response:
        return httpx.Response(
            200,
            headers={"Content-Type": "application/json"},
            content=b'{"registryRevision":"1","tools":[],"tools":[]}',
        )

    async with httpx.AsyncClient(
        base_url="http://127.0.0.1:45678", transport=httpx.MockTransport(handler)
    ) as http_client:
        bridge = UnityBridgeClient(instance(), client=http_client)
        with pytest.raises(BridgeError) as error:
            await bridge.fetch_tools()

    assert error.value.code == "invalid_response"


@pytest.mark.asyncio
async def test_bridge_maps_sanitized_remote_error() -> None:
    def handler(_: httpx.Request) -> httpx.Response:
        return httpx.Response(
            409,
            headers={"Content-Type": "application/json"},
            json={"error": {"code": "registry_conflict", "message": "Refresh", "retryable": True}},
        )

    async with httpx.AsyncClient(
        base_url="http://127.0.0.1:45678", transport=httpx.MockTransport(handler)
    ) as http_client:
        bridge = UnityBridgeClient(instance(), client=http_client)
        with pytest.raises(BridgeError) as error:
            await bridge.call_tool("scene-list", {}, "stale")

    assert error.value.code == "registry_conflict"
    assert error.value.message == "Refresh"
    assert error.value.retryable is True


@pytest.mark.asyncio
async def test_bridge_maps_unity_result_error_envelope() -> None:
    def handler(_: httpx.Request) -> httpx.Response:
        return httpx.Response(
            409,
            headers={"Content-Type": "application/json"},
            json={
                "content": [{"type": "text", "text": "Registry revision is stale"}],
                "isError": True,
                "meta": {"errorCode": "stale_registry"},
            },
        )

    async with httpx.AsyncClient(
        base_url="http://127.0.0.1:45678", transport=httpx.MockTransport(handler)
    ) as http_client:
        with pytest.raises(BridgeError) as error:
            await UnityBridgeClient(instance(), client=http_client).call_tool("scene-list", {}, "stale")

    assert error.value.code == "stale_registry"
    assert error.value.message == "Registry revision is stale"
