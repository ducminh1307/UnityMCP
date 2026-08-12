from __future__ import annotations

import json
import os
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import pytest
from mcp import ClientSession
from mcp.client.stdio import StdioServerParameters, stdio_client


class UnityHandler(BaseHTTPRequestHandler):
    token = "stdio-e2e-token-" * 3
    instance: dict = {}
    protocol_version = "HTTP/1.0"

    def log_message(self, *_: object) -> None:
        pass

    def _json(self, value: dict, status: int = 200, etag: str | None = None) -> None:
        payload = json.dumps(value, separators=(",", ":")).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(payload)))
        if etag:
            self.send_header("ETag", etag)
        self.end_headers()
        self.wfile.write(payload)

    def _authorized(self) -> bool:
        return self.headers.get("Authorization") == f"Bearer {self.token}"

    def do_GET(self) -> None:  # noqa: N802 - BaseHTTPRequestHandler API
        if not self._authorized():
            self._json({"error": {"code": "unauthorized", "message": "unauthorized"}}, 401)
            return
        if self.path == "/api/v1/health":
            self._json({"status": "ok", "registryRevision": "1", "instance": self.instance})
            return
        if self.path == "/api/v1/tools":
            if self.headers.get("If-None-Match") == '"1"':
                self.send_response(304)
                self.send_header("Content-Length", "0")
                self.send_header("ETag", '"1"')
                self.end_headers()
                return
            self._json(
                {
                    "registryRevision": "1",
                    "tools": [
                        {
                            "name": "e2e-echo",
                            "description": "Echo through the fake Unity bridge",
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
                            "enabled": True,
                            "mainThread": True,
                            "supportsDryRun": False,
                            "supportsCancel": False,
                            "returnsJob": False,
                            "timeoutMs": 1000,
                        }
                    ],
                },
                etag='"1"',
            )
            return
        self._json({"error": {"code": "not_found", "message": "not found"}}, 404)

    def do_POST(self) -> None:  # noqa: N802 - BaseHTTPRequestHandler API
        if not self._authorized():
            self._json({"error": {"code": "unauthorized", "message": "unauthorized"}}, 401)
            return
        length = int(self.headers.get("Content-Length", "0"))
        body = json.loads(self.rfile.read(length))
        assert self.path == "/api/v1/tools/e2e-echo/call"
        assert body["registryRevision"] == "1"
        value = body["arguments"]["value"]
        self._json(
            {
                "content": [{"type": "text", "text": f"echo {value}"}],
                "structuredContent": {"echo": value},
                "isError": False,
            }
        )


@pytest.mark.asyncio
async def test_real_stdio_subprocess_proxies_to_bridge(tmp_path) -> None:
    bridge = ThreadingHTTPServer(("127.0.0.1", 0), UnityHandler)
    port = bridge.server_address[1]
    UnityHandler.instance = {
        "port": port,
        "pid": os.getpid(),
        "projectId": "stdio-project",
        "instanceId": "stdio-instance",
        "kind": "editor",
        "buildId": "stdio-build",
    }
    descriptor = {**UnityHandler.instance, "token": UnityHandler.token}
    descriptor_path = tmp_path / "stdio-instance.json"
    descriptor_path.write_text(json.dumps(descriptor), encoding="utf-8")
    if os.name != "nt":
        descriptor_path.chmod(0o600)
    thread = threading.Thread(target=bridge.serve_forever, daemon=True)
    thread.start()
    params = StdioServerParameters(
        command=sys.executable,
        args=[
            "-m",
            "unity_mcp_server",
            "--instance",
            "stdio-instance",
            "--descriptor-dir",
            str(tmp_path),
        ],
        cwd=str(tmp_path),
    )
    try:
        async with stdio_client(params) as streams, ClientSession(*streams) as session:
            await session.initialize()
            listed = await session.list_tools()
            result = await session.call_tool("e2e-echo", {"value": 9})
    finally:
        bridge.shutdown()
        bridge.server_close()
        thread.join(timeout=2)

    assert [tool.name for tool in listed.tools] == ["e2e-echo"]
    assert result.structured_content == {"echo": 9}
