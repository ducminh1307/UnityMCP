from __future__ import annotations

import json
from types import SimpleNamespace

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


class _LimitedPassEvent:
    """Let the synchronous monitor run a fixed number of polling passes."""

    def __init__(self, passes: int) -> None:
        self._passes = passes
        self._waits = 0

    def wait(self, _: float) -> bool:
        self._waits += 1
        return self._waits > self._passes


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
    monkeypatch.setattr(cli, "_PARENT_STARTUP_RETRY_INTERVAL_SECONDS", 0)

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


def test_parent_startup_check_ignores_one_transient_liveness_failure() -> None:
    liveness = iter((False, True))
    waits = []

    alive = cli._parent_is_alive_at_startup(
        42,
        liveness=lambda _: next(liveness),
        wait=waits.append,
    )

    assert alive is True
    assert waits == [cli._PARENT_STARTUP_RETRY_INTERVAL_SECONDS]


def test_parent_startup_check_requires_consecutive_failure_threshold() -> None:
    checks = []
    waits = []

    alive = cli._parent_is_alive_at_startup(
        42,
        liveness=lambda pid: checks.append(pid) or False,
        wait=waits.append,
    )

    assert alive is False
    assert checks == [42] * cli._PARENT_LIVENESS_FAILURE_THRESHOLD
    assert waits == [cli._PARENT_STARTUP_RETRY_INTERVAL_SECONDS] * (
        cli._PARENT_LIVENESS_FAILURE_THRESHOLD - 1
    )


def test_verified_parent_is_not_rejected_by_a_second_descriptor_liveness_sample(monkeypatch) -> None:
    liveness = iter((False, True))
    monkeypatch.setattr(cli, "_is_process_alive", lambda _: next(liveness))
    monkeypatch.setattr(cli, "_PARENT_STARTUP_RETRY_INTERVAL_SECONDS", 0)
    candidate = SimpleNamespace(pid=42)

    def discover(_, *, liveness):
        assert liveness(42) is True
        return [candidate]

    monkeypatch.setattr(cli, "discover_instances", discover)
    monkeypatch.setattr(cli, "select_instance", lambda instances, _: instances[0])
    monkeypatch.setattr(cli, "_build_service", lambda _: object())
    monkeypatch.setattr("unity_mcp_server.mcp_adapter.create_mcp_server", lambda _: object())
    launches = []
    monkeypatch.setattr(cli, "_run_http", lambda *arguments: launches.append(arguments))

    result = cli.run(
        [
            "--transport",
            "streamable-http",
            "--parent-pid",
            "42",
            "--http-token",
            "x" * 32,
        ]
    )

    assert result == 0
    assert len(launches) == 1
    assert launches[0][-1] == 42


def test_selected_descriptor_pid_must_match_verified_parent(monkeypatch) -> None:
    monkeypatch.setattr(cli, "_is_process_alive", lambda _: True)
    candidate = SimpleNamespace(pid=99)
    monkeypatch.setattr(cli, "discover_instances", lambda *_, **__: [candidate])
    monkeypatch.setattr(cli, "select_instance", lambda instances, _: instances[0])
    monkeypatch.setattr(
        cli,
        "_build_service",
        lambda _: (_ for _ in ()).throw(AssertionError("must reject before building service")),
    )

    with pytest.raises(ConfigurationError, match="PID 99 does not match --parent-pid 42"):
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
        stopped=_LimitedPassEvent(cli._PARENT_LIVENESS_FAILURE_THRESHOLD),  # type: ignore[arg-type]
    )

    assert server.should_exit is True
    captured = capsys.readouterr()
    assert captured.out == ""
    assert captured.err.startswith("UNITY_MCP_PARENT_EXITED ")
    assert json.loads(captured.err.removeprefix("UNITY_MCP_PARENT_EXITED ")) == {"parentPid": 42}


def test_http_monitor_ignores_one_transient_parent_liveness_failure(monkeypatch, capsys) -> None:
    liveness = iter((False, True))
    monkeypatch.setattr(cli, "_is_process_alive", lambda _: next(liveness))
    monkeypatch.setattr(cli, "_PARENT_POLL_INTERVAL_SECONDS", 0)
    server = _FakeServer(started=True)

    cli._monitor_http_server(
        server,
        port=8765,
        path="/mcp",
        parent_pid=42,
        stopped=_LimitedPassEvent(2),  # type: ignore[arg-type]
    )

    assert server.should_exit is False
    captured = capsys.readouterr()
    assert "UNITY_MCP_PARENT_EXITED" not in captured.err
    assert captured.err.startswith("UNITY_MCP_READY ")


def test_http_monitor_resets_parent_failure_count_after_success(monkeypatch, capsys) -> None:
    liveness = iter((False, False, True, False, False))
    monkeypatch.setattr(cli, "_is_process_alive", lambda _: next(liveness))
    monkeypatch.setattr(cli, "_PARENT_POLL_INTERVAL_SECONDS", 0)
    server = _FakeServer()

    cli._monitor_http_server(
        server,
        port=8765,
        path="/mcp",
        parent_pid=42,
        stopped=_LimitedPassEvent(5),  # type: ignore[arg-type]
    )

    assert server.should_exit is False
    assert "UNITY_MCP_PARENT_EXITED" not in capsys.readouterr().err


def test_http_monitor_still_stops_when_parent_exit_event_cannot_be_emitted(monkeypatch) -> None:
    monkeypatch.setattr(cli, "_is_process_alive", lambda _: False)
    monkeypatch.setattr(cli, "_PARENT_POLL_INTERVAL_SECONDS", 0)
    monkeypatch.setattr(cli, "_emit_http_event", lambda *_: (_ for _ in ()).throw(RuntimeError("stderr closed")))
    server = _FakeServer()

    cli._monitor_http_server(
        server,
        port=8765,
        path="/mcp",
        parent_pid=42,
        stopped=_LimitedPassEvent(cli._PARENT_LIVENESS_FAILURE_THRESHOLD),  # type: ignore[arg-type]
    )

    assert server.should_exit is True


def test_http_monitor_survives_readiness_emit_failure_until_parent_exits(monkeypatch) -> None:
    liveness = iter((True, False, False, False))
    monkeypatch.setattr(cli, "_is_process_alive", lambda _: next(liveness))
    monkeypatch.setattr(cli, "_PARENT_POLL_INTERVAL_SECONDS", 0)
    monkeypatch.setattr(cli, "_emit_http_event", lambda *_: (_ for _ in ()).throw(RuntimeError("stderr closed")))
    server = _FakeServer(started=True)

    cli._monitor_http_server(
        server,
        port=8765,
        path="/mcp",
        parent_pid=42,
        stopped=_LimitedPassEvent(4),  # type: ignore[arg-type]
    )

    assert server.should_exit is True


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


def test_http_server_configures_idle_cleanup_for_abandoned_sessions(monkeypatch) -> None:
    import uvicorn

    class FakeMcpServer:
        def __init__(self) -> None:
            self.session_manager = SimpleNamespace(session_idle_timeout=None)
            self.app_options = None

        def streamable_http_app(self, **kwargs):
            self.app_options = kwargs
            return object()

    class FakeUvicornServer:
        def __init__(self, _: object) -> None:
            self.started = False
            self.should_exit = False

        def run(self) -> None:
            return None

    monkeypatch.setattr(uvicorn, "Config", lambda *args, **kwargs: object())
    monkeypatch.setattr(uvicorn, "Server", FakeUvicornServer)
    server = FakeMcpServer()

    cli._run_http(server, 8765, "/mcp", "WARNING", "x" * 32)

    assert server.app_options is not None
    assert server.app_options["stateless_http"] is False
    assert server.session_manager.session_idle_timeout == cli._HTTP_SESSION_IDLE_TIMEOUT_SECONDS
