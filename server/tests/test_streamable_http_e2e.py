from __future__ import annotations

import asyncio
import json
import os
import socket
import sys
import threading
from contextlib import suppress
from http.server import ThreadingHTTPServer

import httpx2
import pytest
from mcp import ClientSession
from mcp.client.streamable_http import streamable_http_client

from .test_stdio_e2e import UnityHandler


def _free_loopback_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as listener:
        listener.bind(("127.0.0.1", 0))
        return listener.getsockname()[1]


async def _wait_for_listener(process: asyncio.subprocess.Process, port: int, timeout: float = 10.0) -> None:
    deadline = asyncio.get_running_loop().time() + timeout
    while asyncio.get_running_loop().time() < deadline:
        if process.returncode is not None:
            stderr = await process.stderr.read() if process.stderr is not None else b""
            raise AssertionError(
                f"Streamable HTTP gateway exited with {process.returncode}: "
                f"{stderr.decode(errors='replace')}"
            )
        try:
            _, writer = await asyncio.open_connection("127.0.0.1", port)
        except OSError:
            await asyncio.sleep(0.05)
            continue
        writer.close()
        await writer.wait_closed()
        return
    raise AssertionError(f"Streamable HTTP gateway did not listen on 127.0.0.1:{port} within {timeout}s")


async def _stop_process(process: asyncio.subprocess.Process) -> None:
    if process.returncode is not None:
        return
    process.terminate()
    try:
        await asyncio.wait_for(process.wait(), timeout=5)
    except TimeoutError:
        process.kill()
        await asyncio.wait_for(process.wait(), timeout=5)


@pytest.mark.asyncio
async def test_real_streamable_http_subprocess_proxies_to_bridge(tmp_path) -> None:
    bridge = ThreadingHTTPServer(("127.0.0.1", 0), UnityHandler)
    bridge_port = bridge.server_address[1]
    gateway_port = _free_loopback_port()
    http_token = "streamable-http-e2e-token-" * 2
    UnityHandler.instance = {
        "port": bridge_port,
        "pid": os.getpid(),
        "projectId": "http-project",
        "instanceId": "http-instance",
        "kind": "editor",
        "buildId": "http-build",
    }
    descriptor = {**UnityHandler.instance, "token": UnityHandler.token}
    descriptor_path = tmp_path / "http-instance.json"
    descriptor_path.write_text(json.dumps(descriptor), encoding="utf-8")
    if os.name != "nt":
        descriptor_path.chmod(0o600)
    bridge_thread = threading.Thread(target=bridge.serve_forever, daemon=True)
    bridge_thread.start()
    process = await asyncio.create_subprocess_exec(
        sys.executable,
        "-m",
        "unity_mcp_server",
        "--transport",
        "streamable-http",
        "--port",
        str(gateway_port),
        "--instance",
        "http-instance",
        "--descriptor-dir",
        str(tmp_path),
        "--http-token",
        http_token,
        cwd=str(tmp_path),
        stdin=asyncio.subprocess.DEVNULL,
        stdout=asyncio.subprocess.DEVNULL,
        stderr=asyncio.subprocess.PIPE,
    )
    try:
        await _wait_for_listener(process, gateway_port)
        endpoint = f"http://127.0.0.1:{gateway_port}/mcp"
        async with httpx2.AsyncClient(trust_env=False) as unauthenticated_client:
            assert (await unauthenticated_client.get(endpoint)).status_code == 401
            assert (
                await unauthenticated_client.get(
                    endpoint,
                    headers={"Authorization": "Bearer wrong-streamable-http-token-value"},
                )
            ).status_code == 401
        async with httpx2.AsyncClient(
            headers={"Authorization": f"Bearer {http_token}"},
            trust_env=False,
        ) as authenticated_client, streamable_http_client(
            endpoint, http_client=authenticated_client
        ) as streams, ClientSession(*streams) as session:
            initialized = await session.initialize()
            listed = await session.list_tools()
            result = await session.call_tool("e2e-echo", {"value": 11})
    finally:
        await _stop_process(process)
        bridge.shutdown()
        bridge.server_close()
        bridge_thread.join(timeout=2)
        if process.stderr is not None:
            with suppress(Exception):
                await process.stderr.read()

    assert [tool.name for tool in listed.tools] == ["e2e-echo"]
    assert result.structured_content == {"echo": 11}
    assert initialized.capabilities.tools is not None
    assert initialized.capabilities.tools.list_changed is True
