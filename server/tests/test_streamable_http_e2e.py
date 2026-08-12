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


async def _wait_for_readiness(process: asyncio.subprocess.Process, timeout: float = 10.0) -> dict[str, object]:
    assert process.stderr is not None
    deadline = asyncio.get_running_loop().time() + timeout
    while asyncio.get_running_loop().time() < deadline:
        if process.returncode is not None:
            raise AssertionError(f"Streamable HTTP gateway exited with {process.returncode} before readiness")
        remaining = deadline - asyncio.get_running_loop().time()
        line = await asyncio.wait_for(process.stderr.readline(), timeout=max(remaining, 0.01))
        if not line:
            continue
        text = line.decode(errors="replace").strip()
        if text.startswith("UNITY_MCP_READY "):
            return json.loads(text.removeprefix("UNITY_MCP_READY "))
    raise AssertionError(f"Streamable HTTP gateway did not report readiness within {timeout}s")


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
        "--parent-pid",
        str(os.getpid()),
        cwd=str(tmp_path),
        stdin=asyncio.subprocess.DEVNULL,
        stdout=asyncio.subprocess.DEVNULL,
        stderr=asyncio.subprocess.PIPE,
    )
    try:
        readiness = await _wait_for_readiness(process)
        assert readiness["endpoint"] == f"http://127.0.0.1:{gateway_port}/mcp"
        assert readiness["parentPid"] == os.getpid()
        assert readiness["transport"] == "streamable-http"
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


@pytest.mark.asyncio
async def test_streamable_http_gateway_exits_when_watched_parent_exits(tmp_path) -> None:
    gateway_port = _free_loopback_port()
    http_token = "streamable-http-parent-token-" * 2
    descriptor = {
        "port": 65530,
        "pid": os.getpid(),
        "projectId": "parent-project",
        "instanceId": "parent-instance",
        "kind": "editor",
        "buildId": "parent-build",
        "token": UnityHandler.token,
    }
    (tmp_path / "parent-instance.json").write_text(json.dumps(descriptor), encoding="utf-8")
    parent = await asyncio.create_subprocess_exec(
        sys.executable,
        "-c",
        "import time; time.sleep(60)",
        stdin=asyncio.subprocess.DEVNULL,
        stdout=asyncio.subprocess.DEVNULL,
        stderr=asyncio.subprocess.DEVNULL,
    )
    gateway = await asyncio.create_subprocess_exec(
        sys.executable,
        "-m",
        "unity_mcp_server",
        "--transport",
        "streamable-http",
        "--port",
        str(gateway_port),
        "--instance",
        "parent-instance",
        "--descriptor-dir",
        str(tmp_path),
        "--http-token",
        http_token,
        "--parent-pid",
        str(parent.pid),
        cwd=str(tmp_path),
        stdin=asyncio.subprocess.DEVNULL,
        stdout=asyncio.subprocess.DEVNULL,
        stderr=asyncio.subprocess.PIPE,
    )
    try:
        readiness = await _wait_for_readiness(gateway)
        assert readiness["parentPid"] == parent.pid
        parent.terminate()
        await asyncio.wait_for(parent.wait(), timeout=5)
        assert await asyncio.wait_for(gateway.wait(), timeout=5) == 0
        assert gateway.stderr is not None
        stderr = (await gateway.stderr.read()).decode(errors="replace")
        assert "UNITY_MCP_PARENT_EXITED" in stderr
    finally:
        await _stop_process(gateway)
        await _stop_process(parent)
