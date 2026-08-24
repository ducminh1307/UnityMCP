"""`unity-mcp` entrypoint for stdio and loopback Streamable HTTP."""

from __future__ import annotations

import argparse
import asyncio
import json
import logging
import os
import sys
import threading
import time
from collections.abc import Callable, Sequence
from pathlib import Path

from .config import DEFAULT_LIMITS, default_descriptor_dir
from .discovery import discover_instances, pid_is_alive, select_instance
from .errors import ConfigurationError

_PARENT_POLL_INTERVAL_SECONDS = 0.5
_PARENT_LIVENESS_FAILURE_THRESHOLD = 3
_PARENT_STARTUP_RETRY_INTERVAL_SECONDS = 0.1
_HTTP_SESSION_IDLE_TIMEOUT_SECONDS = 30 * 60


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="unity-mcp", description="UnityMCP Python gateway")
    parser.add_argument("command", nargs="?", choices=("serve", "list-instances"), default="serve")
    parser.add_argument("--instance", help="Unity instanceId; mandatory when multiple instances are live")
    parser.add_argument("--descriptor-dir", type=Path, default=None, help="Override descriptor discovery directory")
    parser.add_argument("--transport", choices=("stdio", "streamable-http"), default="stdio")
    parser.add_argument("--port", type=int, default=8765, help="MCP Streamable HTTP loopback port")
    parser.add_argument("--mcp-path", default="/mcp", help="Streamable HTTP endpoint path")
    parser.add_argument(
        "--parent-pid",
        type=int,
        default=None,
        help="For Streamable HTTP, exit when this Unity Editor or Player process exits",
    )
    parser.add_argument(
        "--http-token",
        default=None,
        help="Bearer token for Streamable HTTP (prefer UNITY_MCP_HTTP_TOKEN to avoid process-list exposure)",
    )
    parser.add_argument("--log-level", choices=("DEBUG", "INFO", "WARNING", "ERROR"), default="WARNING")
    return parser


def _build_service(descriptor):
    from .bridge import UnityBridgeClient
    from .registry import DynamicToolRegistry
    from .service import UnityGatewayService

    bridge = UnityBridgeClient(descriptor)
    registry = DynamicToolRegistry(bridge)
    return UnityGatewayService(descriptor, bridge, registry)


async def _run_stdio(server) -> None:
    from mcp.server.lowlevel.server import NotificationOptions
    from mcp.server.stdio import stdio_server

    async with stdio_server() as (read_stream, write_stream):
        options = server.create_initialization_options(
            notification_options=NotificationOptions(tools_changed=True, resources_changed=True)
        )
        await server.run(read_stream, write_stream, options)


def _is_process_alive(pid: int) -> bool:
    """Reuse the descriptor liveness semantics for the launcher watchdog."""
    return pid_is_alive(pid)


def _parent_is_alive_at_startup(
    pid: int,
    *,
    liveness: Callable[[int], bool] | None = None,
    wait: Callable[[float], None] | None = None,
) -> bool:
    """Require the same consecutive-failure threshold used by the runtime watchdog."""
    check = liveness or _is_process_alive
    pause = wait or time.sleep
    for attempt in range(_PARENT_LIVENESS_FAILURE_THRESHOLD):
        if check(pid):
            return True
        if attempt + 1 < _PARENT_LIVENESS_FAILURE_THRESHOLD:
            pause(_PARENT_STARTUP_RETRY_INTERVAL_SECONDS)
    return False


def _emit_http_event(event: str, payload: dict[str, object]) -> None:
    """Emit a machine-readable lifecycle event without contaminating stdio MCP."""
    print(
        f"UNITY_MCP_{event} {json.dumps(payload, ensure_ascii=False, separators=(',', ':'), sort_keys=True)}",
        file=sys.stderr,
        flush=True,
    )


def _try_emit_http_event(event: str, payload: dict[str, object]) -> bool:
    """Keep lifecycle diagnostics from terminating the watchdog thread."""
    try:
        _emit_http_event(event, payload)
    except Exception:
        logging.exception("Could not emit UnityMCP %s lifecycle event", event.lower())
        return False
    return True


def _monitor_http_server(server, *, port: int, path: str, parent_pid: int | None, stopped: threading.Event) -> None:
    """Report readiness after bind and stop an editor-owned gateway with Unity."""
    ready_emitted = False
    consecutive_parent_liveness_failures = 0
    while not stopped.wait(_PARENT_POLL_INTERVAL_SECONDS):
        if parent_pid is not None:
            if _is_process_alive(parent_pid):
                consecutive_parent_liveness_failures = 0
            else:
                consecutive_parent_liveness_failures += 1
                if consecutive_parent_liveness_failures >= _PARENT_LIVENESS_FAILURE_THRESHOLD:
                    server.should_exit = True
                    _try_emit_http_event("PARENT_EXITED", {"parentPid": parent_pid})
                    return
        if not ready_emitted and server.started and not server.should_exit:
            ready_emitted = _try_emit_http_event(
                "READY",
                {
                    "endpoint": f"http://127.0.0.1:{port}{path}",
                    "mcpPath": path,
                    "parentPid": parent_pid,
                    "pid": os.getpid(),
                    "port": port,
                    "transport": "streamable-http",
                },
            )


def _run_http(server, port: int, path: str, log_level: str, token: str, parent_pid: int | None = None) -> None:
    import uvicorn
    from mcp.server.auth.settings import AuthSettings

    from .http_auth import HTTP_AUTH_SCOPE, StaticTokenVerifier

    base_url = f"http://127.0.0.1:{port}"
    app = server.streamable_http_app(
        streamable_http_path=path,
        host="127.0.0.1",
        json_response=False,
        stateless_http=False,
        max_request_body_size=DEFAULT_LIMITS.max_request_bytes,
        auth=AuthSettings(
            issuer_url=base_url,
            resource_server_url=f"{base_url}{path}",
            required_scopes=[HTTP_AUTH_SCOPE],
        ),
        token_verifier=StaticTokenVerifier(token),
    )
    # Stateful sessions survive transient HTTP disconnects, but abandoned clients must not
    # remain in the SDK's session table forever. This comfortably exceeds the longest tool timeout.
    server.session_manager.session_idle_timeout = _HTTP_SESSION_IDLE_TIMEOUT_SECONDS
    uvicorn_server = uvicorn.Server(
        uvicorn.Config(app, host="127.0.0.1", port=port, log_level=log_level.lower(), workers=1)
    )
    stopped = threading.Event()
    monitor = threading.Thread(
        target=_monitor_http_server,
        kwargs={
            "server": uvicorn_server,
            "port": port,
            "path": path,
            "parent_pid": parent_pid,
            "stopped": stopped,
        },
        name="unity-mcp-http-monitor",
        daemon=True,
    )
    monitor.start()
    try:
        uvicorn_server.run()
    finally:
        stopped.set()
        monitor.join(timeout=_PARENT_POLL_INTERVAL_SECONDS + 0.1)


def _print_instances(directory: Path | None) -> int:
    instances = discover_instances(directory)
    print(
        json.dumps(
            {
                "descriptorDirectory": str((directory or default_descriptor_dir()).expanduser()),
                "instances": [item.public_dict() for item in instances],
            },
            ensure_ascii=False,
            indent=2,
        )
    )
    return 0


def run(argv: Sequence[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    logging.basicConfig(level=getattr(logging, args.log_level), stream=sys.stderr)
    if args.command == "list-instances":
        return _print_instances(args.descriptor_dir)
    if not (1 <= args.port <= 65535):
        raise ConfigurationError("--port must be between 1 and 65535")
    if not args.mcp_path.startswith("/") or ".." in args.mcp_path:
        raise ConfigurationError("--mcp-path must be an absolute path without '..'")
    if args.parent_pid is not None:
        if args.transport != "streamable-http":
            raise ConfigurationError("--parent-pid is supported only with --transport streamable-http")
        if args.parent_pid <= 0:
            raise ConfigurationError("--parent-pid must be a positive process ID")
        if not _parent_is_alive_at_startup(args.parent_pid):
            raise ConfigurationError(f"--parent-pid {args.parent_pid} is not running")
    http_token: str | None = None
    if args.transport == "streamable-http":
        from .http_auth import resolve_http_token

        http_token = resolve_http_token(args.http_token)

    def descriptor_liveness(pid: int) -> bool:
        if args.parent_pid is not None and pid == args.parent_pid:
            return True
        return _is_process_alive(pid)

    descriptor = select_instance(
        discover_instances(args.descriptor_dir, liveness=descriptor_liveness),
        args.instance,
    )
    if args.parent_pid is not None and descriptor.pid != args.parent_pid:
        raise ConfigurationError(
            f"Selected Unity descriptor PID {descriptor.pid} does not match --parent-pid {args.parent_pid}"
        )
    service = _build_service(descriptor)
    from .mcp_adapter import create_mcp_server

    server = create_mcp_server(service)
    if args.transport == "stdio":
        asyncio.run(_run_stdio(server))
    else:
        assert http_token is not None
        _run_http(server, args.port, args.mcp_path, args.log_level, http_token, args.parent_pid)
    return 0


def main() -> None:
    try:
        raise SystemExit(run())
    except ConfigurationError as exc:
        print(f"unity-mcp: {exc}", file=sys.stderr)
        raise SystemExit(2) from None
