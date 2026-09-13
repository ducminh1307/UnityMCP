#!/usr/bin/env python3
"""Summarize opt-in UnityMCP telemetry JSONL by tool."""

from __future__ import annotations

import argparse
import json
from collections import defaultdict
from pathlib import Path
from typing import Any


def _bytes(value: Any) -> int:
    return value if isinstance(value, int) and value >= 0 else 0


def _format_bytes(value: int) -> str:
    units = ("B", "KiB", "MiB", "GiB")
    amount = float(value)
    for unit in units:
        if amount < 1024 or unit == units[-1]:
            return f"{amount:.1f} {unit}" if unit != "B" else f"{value} B"
        amount /= 1024
    return f"{value} B"


def load_events(path: Path) -> list[dict[str, Any]]:
    events: list[dict[str, Any]] = []
    with path.open("r", encoding="utf-8") as stream:
        for line_number, line in enumerate(stream, 1):
            text = line.strip()
            if not text:
                continue
            try:
                value = json.loads(text)
            except json.JSONDecodeError as exc:
                raise SystemExit(f"{path}:{line_number}: invalid JSONL: {exc}") from None
            if isinstance(value, dict):
                events.append(value)
    return events


def summarize(events: list[dict[str, Any]], limit: int) -> str:
    totals: dict[str, dict[str, Any]] = defaultdict(
        lambda: {
            "calls": 0,
            "errors": 0,
            "requestBytes": 0,
            "responseBytes": 0,
            "contentBytes": 0,
            "structuredBytes": 0,
            "durationMs": 0.0,
            "truncated": 0,
        }
    )
    list_events = [event for event in events if event.get("event") == "tools/list"]
    for event in events:
        if event.get("event") != "tools/call":
            continue
        name = str(event.get("toolName") or "<unknown>")
        item = totals[name]
        item["calls"] += 1
        item["errors"] += 1 if event.get("isError") else 0
        item["requestBytes"] += _bytes(event.get("requestBytes"))
        item["responseBytes"] += _bytes(event.get("responseBytes"))
        item["contentBytes"] += _bytes(event.get("contentBytes"))
        item["structuredBytes"] += _bytes(event.get("structuredBytes"))
        item["durationMs"] += float(event.get("durationMs") or 0)
        item["truncated"] += 1 if event.get("truncationFlags") else 0

    rows = sorted(totals.items(), key=lambda pair: pair[1]["responseBytes"], reverse=True)[:limit]
    lines = [
        "# UnityMCP telemetry summary",
        "",
        f"- Tool call events: {sum(item['calls'] for item in totals.values())}",
        f"- Tools/list events: {len(list_events)}",
    ]
    if list_events:
        lines.append(f"- Latest tools/list advertised tools: {list_events[-1].get('toolCount', 0)}")
        lines.append(f"- Latest tools/list payload: {_format_bytes(_bytes(list_events[-1].get('responseBytes')))}")
    lines.extend(
        [
            "",
            "| Tool | Calls | Errors | Request | Response | Content | Structured | Avg duration | Truncated |",
            "|---|---:|---:|---:|---:|---:|---:|---:|---:|",
        ]
    )
    for name, item in rows:
        calls = max(1, item["calls"])
        lines.append(
            "| "
            + " | ".join(
                [
                    f"`{name}`",
                    str(item["calls"]),
                    str(item["errors"]),
                    _format_bytes(item["requestBytes"]),
                    _format_bytes(item["responseBytes"]),
                    _format_bytes(item["contentBytes"]),
                    _format_bytes(item["structuredBytes"]),
                    f"{item['durationMs'] / calls:.1f} ms",
                    str(item["truncated"]),
                ]
            )
            + " |"
        )
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("path", type=Path, help="Telemetry JSONL file produced by UNITY_MCP_TELEMETRY_PATH")
    parser.add_argument("--limit", type=int, default=20, help="Maximum tools to print")
    args = parser.parse_args()
    print(summarize(load_events(args.path), max(1, args.limit)))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
