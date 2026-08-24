from __future__ import annotations

import asyncio
import gc
import inspect
import json
from contextlib import AsyncExitStack
from types import SimpleNamespace
from unittest.mock import AsyncMock

import mcp_types as types
import pytest
from mcp import Client
from mcp.server.subscriptions import ResourcesListChanged, ResourceUpdated, ToolsListChanged
from mcp.shared.exceptions import MCPError

from unity_mcp_server.mcp_adapter import _LegacyConnectionRegistry, create_mcp_server
from unity_mcp_server.registry import DynamicToolRegistry
from unity_mcp_server.service import UnityGatewayService

from .test_service import FakeBridge, descriptor, instance


@pytest.mark.asyncio
async def test_low_level_server_lists_calls_and_reads_resources() -> None:
    bridge = FakeBridge([descriptor()])
    registry = DynamicToolRegistry(bridge)
    service = UnityGatewayService(instance(), bridge, registry)
    server = create_mcp_server(service)
    initialization = server.create_initialization_options()

    async with Client(server) as client:
        listed = await client.list_tools()
        result = await client.call_tool("echo", {"value": 5})
        instance_resource = await client.read_resource("unity://instance")
        catalog_resource = await client.read_resource("unity://tools")

    assert [tool.name for tool in listed.tools] == ["echo"]
    assert initialization.capabilities.tools is not None
    assert initialization.capabilities.tools.list_changed is True
    assert initialization.capabilities.resources is not None
    assert initialization.capabilities.resources.list_changed is True
    assert result.structured_content == {"echo": 5}
    assert "token" not in json.loads(instance_resource.contents[0].text)
    assert json.loads(catalog_resource.contents[0].text)["tools"][0]["name"] == "echo"


@pytest.mark.asyncio
async def test_active_legacy_client_receives_list_changed_notifications() -> None:
    bridge = FakeBridge([descriptor()])
    registry = DynamicToolRegistry(bridge)
    service = UnityGatewayService(instance(), bridge, registry)
    server = create_mcp_server(service)
    changed = registry._callbacks[0]
    legacy_connections = inspect.getclosurevars(changed).nonlocals["legacy_connections"]
    notifications = []

    async def handle_message(message) -> None:
        notifications.append(message)

    async with Client(server, mode="legacy", message_handler=handle_message) as client:
        await client.list_tools()
        gc.collect()
        assert len(legacy_connections) == 1

        await changed(registry.snapshot)
        for _ in range(100):
            if len(notifications) == 2:
                break
            await asyncio.sleep(0.001)

        assert [type(notification) for notification in notifications] == [
            types.ToolListChangedNotification,
            types.ResourceListChangedNotification,
        ]

    assert len(legacy_connections) == 0


@pytest.mark.asyncio
async def test_legacy_connection_tracking_is_bounded_and_cleans_up_churn() -> None:
    bridge = FakeBridge([descriptor()])
    registry = DynamicToolRegistry(bridge)
    service = UnityGatewayService(instance(), bridge, registry)
    server = create_mcp_server(service)
    changed = registry._callbacks[0]
    legacy_connections = inspect.getclosurevars(changed).nonlocals["legacy_connections"]

    for _ in range(25):
        async with Client(server, mode="legacy") as client:
            await client.list_tools()
        assert len(legacy_connections) == 0

    connections = []
    bounded = _LegacyConnectionRegistry(limit=3, ttl_seconds=1800)
    for index in range(5):
        connection = SimpleNamespace(session_id=str(index), exit_stack=AsyncExitStack())
        connections.append(connection)
        bounded.remember(
            SimpleNamespace(
                protocol_version="2025-11-25",
                session=SimpleNamespace(_connection=connection),
            )
        )

    assert len(bounded) == 3

    for connection in connections:
        await connection.exit_stack.aclose()

    assert len(bounded) == 0


def test_legacy_connection_tracking_expires_idle_entries() -> None:
    now = [0.0]
    tracked = _LegacyConnectionRegistry(limit=3, ttl_seconds=10, clock=lambda: now[0])
    connection = SimpleNamespace(session_id="idle", exit_stack=AsyncExitStack())
    tracked.remember(
        SimpleNamespace(
            protocol_version="2025-11-25",
            session=SimpleNamespace(_connection=connection),
        )
    )

    assert len(tracked) == 1
    now[0] = 10.0

    assert len(tracked) == 0


@pytest.mark.asyncio
async def test_hanging_legacy_connection_does_not_block_registry_notifications(monkeypatch) -> None:
    bridge = FakeBridge([descriptor()])
    registry = DynamicToolRegistry(bridge)
    service = UnityGatewayService(instance(), bridge, registry)
    create_mcp_server(service)
    changed = registry._callbacks[0]
    legacy_connections = inspect.getclosurevars(changed).nonlocals["legacy_connections"]
    monkeypatch.setattr("unity_mcp_server.mcp_adapter._LEGACY_NOTIFICATION_TIMEOUT_SECONDS", 0.01)

    never = asyncio.Event()
    hanging = SimpleNamespace(
        session_id="hanging",
        exit_stack=AsyncExitStack(),
        send_tool_list_changed=AsyncMock(side_effect=never.wait),
        send_resource_list_changed=AsyncMock(),
    )
    healthy = SimpleNamespace(
        session_id="healthy",
        exit_stack=AsyncExitStack(),
        send_tool_list_changed=AsyncMock(),
        send_resource_list_changed=AsyncMock(),
    )
    for connection in (hanging, healthy):
        legacy_connections.remember(
            SimpleNamespace(
                protocol_version="2025-11-25",
                session=SimpleNamespace(_connection=connection),
            )
        )

    try:
        await asyncio.wait_for(changed(registry.snapshot), timeout=0.5)

        assert len(legacy_connections) == 1
        healthy.send_tool_list_changed.assert_awaited_once_with()
        healthy.send_resource_list_changed.assert_awaited_once_with()
    finally:
        await hanging.exit_stack.aclose()
        await healthy.exit_stack.aclose()


@pytest.mark.asyncio
async def test_registry_changes_publish_tool_and_resource_events() -> None:
    bridge = FakeBridge([descriptor()])
    registry = DynamicToolRegistry(bridge)
    service = UnityGatewayService(instance(), bridge, registry)
    create_mcp_server(service)
    changed = registry._callbacks[0]
    bus = inspect.getclosurevars(changed).nonlocals["bus"]
    events = []
    unsubscribe = bus.subscribe(events.append)

    try:
        await changed(registry.snapshot)
    finally:
        unsubscribe()

    assert [type(event) for event in events] == [
        ToolsListChanged,
        ResourcesListChanged,
        ResourceUpdated,
        ResourceUpdated,
    ]
    assert [event.uri for event in events if isinstance(event, ResourceUpdated)] == [
        "unity://instance",
        "unity://tools",
    ]


@pytest.mark.asyncio
async def test_modern_resource_subscription_receives_updated_resources() -> None:
    bridge = FakeBridge([descriptor()])
    registry = DynamicToolRegistry(bridge)
    service = UnityGatewayService(instance(), bridge, registry)
    server = create_mcp_server(service)
    changed = registry._callbacks[0]

    async with Client(server, mode="2026-07-28") as client, client.listen(
        resource_subscriptions=["unity://instance", "unity://tools"]
    ) as subscription:
        assert subscription.honored.resource_subscriptions == ["unity://instance", "unity://tools"]

        await changed(registry.snapshot)
        events = [
            await asyncio.wait_for(anext(subscription), timeout=1),
            await asyncio.wait_for(anext(subscription), timeout=1),
        ]

    assert events == [ResourceUpdated("unity://instance"), ResourceUpdated("unity://tools")]


@pytest.mark.asyncio
async def test_job_resource_accepts_percent_encoded_opaque_id() -> None:
    bridge = FakeBridge([descriptor()])
    registry = DynamicToolRegistry(bridge)
    service = UnityGatewayService(instance(), bridge, registry)
    server = create_mcp_server(service)

    async with Client(server, mode="legacy") as client:
        resource = await client.read_resource("unity://jobs/build%3Fjob%23part")

    assert json.loads(resource.contents[0].text) == {"jobId": "build?job#part", "status": "running"}


@pytest.mark.asyncio
@pytest.mark.parametrize("uri", ["unity://jobs/build%2Fjob", "unity://jobs/build%5Cjob"])
async def test_job_resource_rejects_encoded_route_separators(uri: str) -> None:
    bridge = FakeBridge([descriptor()])
    registry = DynamicToolRegistry(bridge)
    service = UnityGatewayService(instance(), bridge, registry)
    server = create_mcp_server(service)

    async with Client(server, mode="legacy") as client:
        with pytest.raises(MCPError, match="Invalid Unity job resource URI"):
            await client.read_resource(uri)
