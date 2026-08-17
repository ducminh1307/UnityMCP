#!/usr/bin/env python3
"""Generate the human-readable UnityMCP tool reference from the catalog."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

ROOT = Path(__file__).resolve().parents[1]
CATALOG_PATH = ROOT / "docs" / "tool-catalog.json"
OUTPUT_PATH = ROOT / "docs" / "tools.md"


def escape_cell(value: object) -> str:
    return str(value).replace("|", "\\|").replace("\n", " ")


def render_reference(catalog: dict[str, Any]) -> str:
    categories = catalog["categories"]
    tools = catalog["tools"]
    by_category = {
        category["id"]: [tool for tool in tools if tool["category"] == category["id"]]
        for category in categories
    }
    implemented_count = sum(tool["status"] == "implemented" for tool in tools)
    planned_count = sum(tool["status"] == "planned" for tool in tools)
    default_count = sum(tool["defaultEnabled"] for tool in tools)

    lines = [
        "# UnityMCP tool reference",
        "",
        "> [!NOTE]",
        "> This file is generated from [`tool-catalog.json`](tool-catalog.json).",
        "> Run `python tools/generate_tool_reference.py` after changing the catalog.",
        "",
        "This page is the human-readable index of UnityMCP's built-in tools. The",
        "connected Unity instance remains the source of truth: an MCP client sees only",
        "tools that are implemented, valid for the current target, supported by installed",
        "packages, and enabled in **Window > UnityMCP > Tools**.",
        "",
        "The live MCP `tools/list` response is authoritative for each tool's current input",
        "schema and annotations. This reference explains purpose and availability without",
        "duplicating schemas that Unity generates at runtime.",
        "",
        "## At a glance",
        "",
        f"- **{len(tools)}** cataloged tools in **{len(categories)}** categories.",
        f"- **{implemented_count}** implemented tools and **{planned_count}** planned tools.",
        f"- **{default_count}** safe-read tools enabled in a fresh project.",
        "- Catalog version: " + str(catalog["catalogVersion"]) + ".",
        "",
        "## How to read the reference",
        "",
        "| Field | Meaning |",
        "|---|---|",
        "| Status | `implemented` has a compiled handler. `planned` is documented but never advertised by MCP. |",
        "| Scope | `editor`, `runtime` Development Player, or both. |",
        "| Safety | The permission and risk tier described below. |",
        "| Default | Whether a fresh project enables the tool automatically. |",
        "| Dependency | Unity core, Editor APIs, module, or optional package required by the handler. |",
        "",
        "### Safety tiers",
        "",
        "| Tier | Meaning |",
        "|---|---|",
        "| `safe-read` | Reads bounded state without intentionally changing the project or target. |",
        "| `write` | Changes state and requires local enablement. Where the live schema includes `apply`, preview first with `apply: false`. |",
        "| `destructive` | Deletes, reverts, replaces, or shuts down state; enable and call only with an explicit target. |",
        "| `unsafe` | Invokes powerful or broad operations such as allowlisted reflection, builds, or batch execution. |",
        "",
        "A tool being listed here does not grant permission to use it. Non-default tools",
        "must be enabled locally, and dependencies must be available in the connected",
        "project. Custom project tools are discovered dynamically and therefore do not",
        "appear in this built-in catalog; use `custom-tool-list` to inspect them.",
        "",
        "## Recommended call workflow",
        "",
        "1. Call `unity-status` to verify the target project and registry state.",
        "2. Use the narrowest read tool to identify stable object or asset references.",
        "3. Inspect the live input schema before constructing arguments.",
        "4. For a mutation whose schema supports dry-run, call it first with `apply: false`.",
        "5. Apply the smallest intended change, then verify it with a read tool.",
        "6. Poll `job-get` for operations that return a job identifier.",
        "",
        "## Categories",
        "",
    ]

    for category in categories:
        lines.append(f"- [{category['title']}](#{category['id']}) ({category['declaredCount']})")

    for category in categories:
        category_tools = by_category[category["id"]]
        lines.extend(
            [
                "",
                f"<a id=\"{category['id']}\"></a>",
                "",
                f"## {category['title']}",
                "",
                f"{len(category_tools)} tools.",
                "",
                "| Tool | Status | Scope | Safety | Default | Dependency | Description |",
                "|---|---|---|---|---|---|---|",
            ]
        )
        for tool in category_tools:
            values = (
                f"`{tool['name']}`",
                tool["status"],
                ", ".join(tool["scope"]),
                tool["safety"],
                "Yes" if tool["defaultEnabled"] else "No",
                f"`{tool['dependency']}`",
                tool["summary"],
            )
            lines.append("| " + " | ".join(escape_cell(value) for value in values) + " |")

    lines.extend(
        [
            "",
            "## Related documentation",
            "",
            "- [Custom tools](custom-tools.md)",
            "- [Architecture](architecture.md)",
            "- [Protocol](protocol.md)",
            "- [Security](security.md)",
            "- [Canonical JSON catalog](tool-catalog.json)",
            "",
        ]
    )
    return "\n".join(lines)


def load_catalog() -> dict[str, Any]:
    with CATALOG_PATH.open("r", encoding="utf-8") as stream:
        value = json.load(stream)
    if not isinstance(value, dict):
        raise TypeError("catalog root must be a JSON object")
    return value


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--check",
        action="store_true",
        help="fail if docs/tools.md is missing or out of date",
    )
    args = parser.parse_args(argv)

    expected = render_reference(load_catalog())
    if args.check:
        actual = OUTPUT_PATH.read_text(encoding="utf-8") if OUTPUT_PATH.exists() else None
        if actual != expected:
            print(
                "tool reference is out of date; run "
                "python tools/generate_tool_reference.py",
                file=sys.stderr,
            )
            return 1
        print(f"tool reference OK: {OUTPUT_PATH.relative_to(ROOT)}")
        return 0

    OUTPUT_PATH.write_text(expected, encoding="utf-8", newline="\n")
    print(f"wrote {OUTPUT_PATH.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
