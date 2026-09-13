"""Local opt-in telemetry for estimating MCP context cost."""

from __future__ import annotations

import json
import os
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any


def _json_size(value: Any) -> int:
    try:
        return len(json.dumps(value, ensure_ascii=False, allow_nan=False, separators=(",", ":")).encode("utf-8"))
    except (TypeError, ValueError):
        return 0


def _find_truncation_flags(value: Any) -> list[str]:
    flags: list[str] = []

    def visit(item: Any, path: str) -> None:
        if isinstance(item, dict):
            for key, nested in item.items():
                child_path = f"{path}.{key}" if path else str(key)
                if isinstance(nested, bool) and str(key).lower().endswith("truncated") and nested:
                    flags.append(child_path)
                else:
                    visit(nested, child_path)
        elif isinstance(item, list):
            for index, nested in enumerate(item):
                visit(nested, f"{path}[{index}]")

    visit(value, "")
    return flags


@dataclass(frozen=True, slots=True)
class ToolTelemetryRecorder:
    path: Path | None

    @classmethod
    def from_environment(cls) -> ToolTelemetryRecorder:
        enabled = os.environ.get("UNITY_MCP_TELEMETRY", "").strip().lower()
        configured_path = os.environ.get("UNITY_MCP_TELEMETRY_PATH", "").strip()
        if not configured_path and enabled not in {"1", "true", "yes", "on"}:
            return cls(None)
        path = Path(configured_path).expanduser() if configured_path else Path.cwd() / "unity-mcp-telemetry.jsonl"
        return cls(path)

    @property
    def enabled(self) -> bool:
        return self.path is not None

    def record(self, event: dict[str, Any]) -> None:
        if self.path is None:
            return
        payload = {
            "timestampUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
            **event,
        }
        try:
            self.path.parent.mkdir(parents=True, exist_ok=True)
            with self.path.open("a", encoding="utf-8", newline="\n") as stream:
                stream.write(json.dumps(payload, ensure_ascii=False, allow_nan=False, separators=(",", ":")))
                stream.write("\n")
        except OSError:
            return


def tool_call_event(
    *,
    instance_id: str,
    tool_name: str,
    arguments: dict[str, Any],
    duration_seconds: float,
    raw_response: dict[str, Any] | None = None,
    error_code: str | None = None,
) -> dict[str, Any]:
    content = raw_response.get("content") if raw_response else None
    structured = raw_response.get("structuredContent") if raw_response else None
    meta = raw_response.get("meta", raw_response.get("_meta")) if raw_response else None
    return {
        "event": "tools/call",
        "instanceId": instance_id,
        "toolName": tool_name,
        "durationMs": round(duration_seconds * 1000, 3),
        "requestBytes": _json_size(arguments),
        "responseBytes": _json_size(raw_response),
        "contentBytes": _json_size(content),
        "structuredBytes": _json_size(structured),
        "metaBytes": _json_size(meta),
        "isError": bool(raw_response.get("isError")) if raw_response else error_code is not None,
        "errorCode": error_code,
        "truncationFlags": _find_truncation_flags(raw_response),
    }


def tools_list_event(
    *, instance_id: str, tool_count: int, tools: list[dict[str, Any]], duration_seconds: float
) -> dict[str, Any]:
    return {
        "event": "tools/list",
        "instanceId": instance_id,
        "toolCount": tool_count,
        "durationMs": round(duration_seconds * 1000, 3),
        "responseBytes": _json_size(tools),
    }
