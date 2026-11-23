#!/usr/bin/env python3
"""Smoke-test a live Unity Editor via unity CLI and `unity mcp --tool-groups`."""

from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_PROJECT_PATH = ROOT / "ValidationProject"


def resolve_unity_exe(override: str | None) -> str:
    if override:
        return override
    found = shutil.which("unity")
    if found:
        return found
    if os.name == "nt":
        local_app_data = os.environ.get("LOCALAPPDATA", "")
        candidate = Path(local_app_data) / "Unity" / "bin" / "unity.exe"
        if candidate.is_file():
            return str(candidate)
    return "unity"


def run_cli_json(cmd: list[str], timeout: float) -> dict[str, Any]:
    proc = subprocess.run(
        cmd,
        capture_output=True,
        text=True,
        timeout=timeout,
        check=False,
    )
    if proc.returncode != 0:
        raise RuntimeError(
            f"Command failed ({proc.returncode}): {' '.join(cmd)}\n"
            f"stdout: {proc.stdout.strip()}\nstderr: {proc.stderr.strip()}"
        )
    try:
        return json.loads(proc.stdout)
    except json.JSONDecodeError as exc:
        raise RuntimeError(
            f"Expected JSON output from {' '.join(cmd)}, got: {proc.stdout.strip()}"
        ) from exc


def normalize_path(path_value: str) -> str:
    return os.path.normcase(str(Path(path_value).resolve()))


def verify_unity_status(unity_exe: str, project_path: Path, timeout: float) -> dict[str, Any]:
    status = run_cli_json([unity_exe, "status", "--format", "json"], timeout)
    if not status.get("success"):
        raise RuntimeError(f"unity status returned unsuccessful payload: {status}")

    data = status.get("data") or {}
    instances = data.get("instances") or data.get("items") or []
    if not instances:
        raise RuntimeError(
            f"No live Unity Editor instances found for project '{project_path}'. "
            "Open the project in Unity with com.unity.pipeline enabled."
        )

    target_norm = normalize_path(str(project_path))
    matching = [
        inst
        for inst in instances
        if isinstance(inst, dict)
        and inst.get("projectPath")
        and normalize_path(inst["projectPath"]) == target_norm
    ]
    if not matching:
        raise RuntimeError(
            f"No live Unity Editor instance matches project '{project_path}'. "
            f"Running instances: {instances}"
        )
    return matching[0]


def verify_unity_command(unity_exe: str, project_path: Path, timeout: float) -> dict[str, Any]:
    catalog_payload = run_cli_json(
        [
            unity_exe,
            "command",
            "--project-path",
            str(project_path),
            "--format",
            "json",
            "--",
            "pipeline",
            "list_commands",
            "--detail",
            "compact",
        ],
        timeout,
    )
    checkpoint_payload = run_cli_json(
        [
            unity_exe,
            "command",
            "--project-path",
            str(project_path),
            "--format",
            "json",
            "--",
            "checkpoint",
            "list",
        ],
        timeout,
    )
    return {
        "pipelineListCommandsOk": True,
        "pipelineCatalogKeys": sorted(catalog_payload.keys()) if isinstance(catalog_payload, dict) else [],
        "checkpointListResult": checkpoint_payload,
    }


def read_jsonrpc_response(
    proc: subprocess.Popen[str],
    expected_id: int,
) -> dict[str, Any]:
    assert proc.stdout is not None
    while True:
        line = proc.stdout.readline()
        if not line:
            stderr = proc.stderr.read() if proc.stderr else ""
            raise RuntimeError(
                f"unity mcp closed stdout before answering id={expected_id}. stderr={stderr.strip()}"
            )
        text = line.strip()
        if not text:
            continue
        try:
            payload = json.loads(text)
        except json.JSONDecodeError:
            continue
        if isinstance(payload, dict) and payload.get("id") == expected_id:
            if "error" in payload:
                raise RuntimeError(f"JSON-RPC error for id={expected_id}: {payload['error']}")
            return payload


def build_group_call_arguments(tool: dict[str, Any]) -> tuple[str, dict[str, Any]]:
    name = str(tool["name"])
    schema = tool.get("inputSchema") or {}
    props = schema.get("properties") or {}

    preferred_subcommands = {
        "checkpoint": "list",
        "editor": "status",
        "pipeline": "progress",
        "scene_ext": "validate",
        "render_ext": "get_settings",
    }
    subcommand = preferred_subcommands.get(name, "status")

    args: dict[str, Any] = {}
    for key in ("command", "subcommand", "action", "name", "tool"):
        if key in props:
            args[key] = subcommand
            break
    for param_key in ("parameters", "arguments", "args", "params"):
        if param_key in props and props[param_key].get("type") == "object":
            args[param_key] = {}
            break
    return name, args


def verify_unity_mcp(unity_exe: str, project_path: Path, timeout: float) -> dict[str, Any]:
    cmd = [
        unity_exe,
        "mcp",
        "--tool-groups",
        "--project-path",
        str(project_path),
    ]
    proc = subprocess.Popen(
        cmd,
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
    )
    try:
        assert proc.stdin is not None
        messages = [
            {
                "jsonrpc": "2.0",
                "id": 1,
                "method": "initialize",
                "params": {
                    "protocolVersion": "2024-11-05",
                    "capabilities": {},
                    "clientInfo": {"name": "unitymcp-smoke", "version": "0.2.0"},
                },
            },
            {"jsonrpc": "2.0", "method": "notifications/initialized"},
            {"jsonrpc": "2.0", "id": 2, "method": "tools/list"},
        ]

        proc.stdin.write(json.dumps(messages[0]) + "\n")
        proc.stdin.flush()
        init_resp = read_jsonrpc_response(proc, 1)

        proc.stdin.write(json.dumps(messages[1]) + "\n")
        proc.stdin.write(json.dumps(messages[2]) + "\n")
        proc.stdin.flush()
        list_resp = read_jsonrpc_response(proc, 2)

        tools = (list_resp.get("result") or {}).get("tools") or []
        tool_names = [t["name"] for t in tools if isinstance(t, dict) and "name" in t]
        if not tool_names or tool_names == ["unity-cli-status"]:
            raise RuntimeError(
                f"unity mcp --tool-groups did not expose live Editor tool groups: {tool_names}"
            )

        by_name = {t["name"]: t for t in tools if isinstance(t, dict) and "name" in t}
        selected = None
        for candidate in ("checkpoint", "editor", "pipeline", "scene_ext", "render_ext"):
            if candidate in by_name:
                selected = by_name[candidate]
                break
        if selected is None:
            raise RuntimeError(f"No expected read-only group tool found in {sorted(tool_names)}")

        tool_name, call_args = build_group_call_arguments(selected)
        call_msg = {
            "jsonrpc": "2.0",
            "id": 3,
            "method": "tools/call",
            "params": {
                "name": tool_name,
                "arguments": call_args,
            },
        }
        proc.stdin.write(json.dumps(call_msg) + "\n")
        proc.stdin.flush()
        call_resp = read_jsonrpc_response(proc, 3)
        result = call_resp.get("result") or {}
        if result.get("isError"):
            raise RuntimeError(f"MCP tools/call '{tool_name}' returned error: {result}")

        return {
            "serverInfo": (init_resp.get("result") or {}).get("serverInfo"),
            "toolGroupCount": len(tool_names),
            "toolGroups": sorted(tool_names),
            "calledTool": tool_name,
            "callArguments": call_args,
            "callResult": result,
        }
    finally:
        if proc.stdin:
            proc.stdin.close()
        try:
            proc.wait(timeout=min(timeout, 5.0))
        except subprocess.TimeoutExpired:
            proc.kill()
            proc.wait(timeout=5.0)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--project-path",
        type=Path,
        default=DEFAULT_PROJECT_PATH,
        help="Path to the open Unity project (defaults to ValidationProject).",
    )
    parser.add_argument(
        "--unity-exe",
        default=None,
        help="Optional path or name of the official Unity CLI executable.",
    )
    parser.add_argument(
        "--timeout",
        type=float,
        default=20.0,
        help="Per-step subprocess timeout in seconds.",
    )
    args = parser.parse_args(argv)

    project_path = args.project_path.resolve()
    unity_exe = resolve_unity_exe(args.unity_exe)

    instance = verify_unity_status(unity_exe, project_path, args.timeout)
    cli_check = verify_unity_command(unity_exe, project_path, args.timeout)
    mcp_check = verify_unity_mcp(unity_exe, project_path, args.timeout)

    report = {
        "projectPath": str(project_path),
        "unityExe": unity_exe,
        "statusInstance": instance,
        "unityCommand": cli_check,
        "unityMcp": mcp_check,
    }
    print(json.dumps(report, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
