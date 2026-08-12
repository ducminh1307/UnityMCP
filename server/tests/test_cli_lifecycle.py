from __future__ import annotations

import json

import pytest

from unity_mcp_server import cli
from unity_mcp_server.errors import ConfigurationError


class _FakeServer:
    def __init__(self, *, started: bool = False) -> None:
        self.started = started
        self.should_exit = False


class _OnePassEvent:
    """Let the synchronous monitor run once without a real sleep."""

    def __init__(self) -> None:
        self._waits = 0

    def wait(self, _: float) -> bool:
        self._waits += 1
        return self._waits > 1


def test_parent_pid_is_http_only() -> None:
    with pytest.raises(ConfigurationError, match="only with --transport streamable-http"):
        cli.run(["--parent-pid", "42"])


def test_parent_pid_must_be_positive_before_descriptor_discovery() -> None:
    with pytest.raises(ConfigurationError, match="positive process ID"):
        cli.run(
            [
                "--transport",
                "streamable-http",
                "--parent-pid",
                "0",
                "--http-token",
                "x" * 32,
            ]
        )


def test_dead_parent_is_rejected_before_descriptor_discovery(monkeypatch) -> None:
    monkeypatch.setattr(cli, "_is_process_alive", lambda _: False)

    with pytest.raises(ConfigurationError, match="--parent-pid 42 is not running"):
        cli.run(
            [
                "--transport",
                "streamable-http",
                "--parent-pid",
                "42",
                "--http-token",
                "x" * 32,
            ]
        )


def test_http_monitor_stops_gateway_when_parent_exits(monkeypatch, capsys) -> None:
    monkeypatch.setattr(cli, "_is_process_alive", lambda _: False)
    monkeypatch.setattr(cli, "_PARENT_POLL_INTERVAL_SECONDS", 0)
    server = _FakeServer()

    cli._monitor_http_server(
        server,
        port=8765,
        path="/mcp",
        parent_pid=42,
        stopped=_OnePassEvent(),  # type: ignore[arg-type]
    )

    assert server.should_exit is True
    captured = capsys.readouterr()
    assert captured.out == ""
    assert captured.err.startswith("UNITY_MCP_PARENT_EXITED ")
    assert json.loads(captured.err.removeprefix("UNITY_MCP_PARENT_EXITED ")) == {"parentPid": 42}


def test_http_monitor_emits_machine_readable_readiness(capsys) -> None:
    server = _FakeServer(started=True)

    cli._monitor_http_server(
        server,
        port=8765,
        path="/mcp",
        parent_pid=None,
        stopped=_OnePassEvent(),  # type: ignore[arg-type]
    )

    captured = capsys.readouterr()
    assert captured.out == ""
    prefix, payload = captured.err.split(" ", maxsplit=1)
    assert prefix == "UNITY_MCP_READY"
    readiness = json.loads(payload)
    assert {key: value for key, value in readiness.items() if key != "pid"} == {
        "endpoint": "http://127.0.0.1:8765/mcp",
        "mcpPath": "/mcp",
        "parentPid": None,
        "port": 8765,
        "transport": "streamable-http",
    }
    assert isinstance(readiness["pid"], int)
    assert readiness["pid"] > 0
