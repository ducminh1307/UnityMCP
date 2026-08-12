#!/usr/bin/env python3
"""Smoke-test an installed UnityMCP gateway against one live Unity instance."""

from __future__ import annotations

import argparse
import asyncio
import json
import sys
from pathlib import Path

from mcp import ClientSession
from mcp.client.stdio import StdioServerParameters, stdio_client


async def smoke(instance: str, descriptor_dir: Path) -> dict[str, object]:
    params = StdioServerParameters(
        command=sys.executable,
        args=[
            "-m",
            "unity_mcp_server",
            "--instance",
            instance,
            "--descriptor-dir",
            str(descriptor_dir),
        ],
    )
    async with stdio_client(params) as streams, ClientSession(*streams) as session:
        await session.initialize()
        listed = await session.list_tools()
        status = await session.call_tool("unity-status", {})
        resource = await session.read_resource("unity://instance")

    instance_resource = json.loads(resource.contents[0].text)
    if "token" in instance_resource:
        raise RuntimeError("unity://instance leaked the bridge bearer token")
    if status.is_error:
        raise RuntimeError("unity-status returned an MCP tool error")
    return {
        "instanceId": instance_resource["instanceId"],
        "toolCount": len(listed.tools),
        "tools": sorted(tool.name for tool in listed.tools),
        "unityStatus": status.structured_content,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--instance", required=True)
    parser.add_argument("--descriptor-dir", type=Path, required=True)
    args = parser.parse_args()
    print(json.dumps(asyncio.run(smoke(args.instance, args.descriptor_dir)), indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
