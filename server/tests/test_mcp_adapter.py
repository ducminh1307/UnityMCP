from __future__ import annotations

import json

import pytest
from mcp import Client

from unity_mcp_server.mcp_adapter import create_mcp_server
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
