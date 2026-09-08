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
        "## Dry-run and change journal pattern",
        "",
        "Mutating operations (`Safety: write` or `destructive`) that support previewing include",
        "an `apply` boolean parameter (default: `false`):",
        "",
        "- **Preview (`apply: false`)**: Simulates the operation, verifies preconditions, checks",
        "  target bounds, and outputs a `ChangeOutput` detailing planned modifications without changing",
        "  the scene, project assets, or disk state.",
        "- **Execution (`apply: true`)**: Performs the change, creates an Editor Undo record where",
        "  supported, marks target objects or assets dirty, and saves modifications.",
        "",
        "```json",
        "// Example ChangeOutput structure",
        "{",
        '  "dryRun": true,',
        '  "changed": false,',
        '  "summary": "Preview: set property \'size\' on BoxCollider.",',
        '  "instanceId": 12345,',
        '  "journal": [',
        "    {",
        '      "operation": "setProperty",',
        '      "before": "{\\"x\\":1.0,\\"y\\":1.0,\\"z\\":1.0}",',
        '      "after": "{\\"x\\":2.0,\\"y\\":2.0,\\"z\\":2.0}"',
        "    }",
        "  ],",
        '  "rollbackSupported": true',
        "}",
        "```",
        "",
        "## Common workflows and payload examples",
        "",
        "### 1. Scene and GameObject inspection and manipulation",
        "",
        "#### Find GameObjects (`gameobject-find`)",
        "Search active or inactive scene objects by name, tag, or scene filter:",
        "```json",
        "{",
        '  "name": "Player",',
        '  "tag": "Player",',
        '  "includeInactive": true,',
        '  "limit": 10',
        "}",
        "```",
        "",
        "#### Inspect GameObject details (`gameobject-get`)",
        "Inspect transform hierarchies, active state, layers, and attached components:",
        "```json",
        "{",
        '  "path": "Environment/MainPlatform"',
        "}",
        "```",
        "",
        "#### Create a GameObject (`gameobject-create`)",
        "Spawn a new GameObject in the active scene under an optional parent:",
        "```json",
        "{",
        '  "name": "SpawnPoint",',
        '  "parentPath": "Environment",',
        '  "localPosition": { "x": 0.0, "y": 1.5, "z": 0.0 },',
        '  "apply": true',
        "}",
        "```",
        "",
        "#### Transform update (`gameobject-set-transform`)",
        "Update local or world coordinates, rotation angles, and scale:",
        "```json",
        "{",
        '  "path": "SpawnPoint",',
        '  "localPosition": { "x": 10.0, "y": 0.0, "z": -5.0 },',
        '  "localEulerAngles": { "x": 0.0, "y": 90.0, "z": 0.0 },',
        '  "apply": true',
        "}",
        "```",
        "",
        "### 2. Components and reflection",
        "",
        "#### Inspect component schema (`component-schema`)",
        "Query writable members and property types before modifying component fields:",
        "```json",
        "{",
        '  "type": "UnityEngine.BoxCollider"',
        "}",
        "```",
        "",
        "#### Read component properties (`component-get`)",
        "Inspect serialized field values of an attached component as JSON:",
        "```json",
        "{",
        '  "path": "SpawnPoint",',
        '  "type": "UnityEngine.BoxCollider"',
        "}",
        "```",
        "",
        "#### Add a component (`component-add`)",
        "Attach a component by full or short type name:",
        "```json",
        "{",
        '  "path": "SpawnPoint",',
        '  "type": "UnityEngine.BoxCollider",',
        '  "apply": true',
        "}",
        "```",
        "",
        "#### Set component field or property (`component-set-property`)",
        "Modify a serialized field or public property with typed JSON value:",
        "```json",
        "{",
        '  "path": "SpawnPoint",',
        '  "type": "UnityEngine.BoxCollider",',
        '  "property": "isTrigger",',
        '  "valueJson": "true",',
        '  "apply": true',
        "}",
        "```",
        "",
        "### 3. Prefabs and ScriptableObjects",
        "",
        "#### Inspect prefab metadata (`prefab-info`)",
        "Read prefab asset type, instance status, root name, and component counts:",
        "```json",
        "{",
        '  "path": "Assets/Prefabs/Enemy.prefab"',
        "}",
        "```",
        "",
        "#### Direct prefab asset edit (`prefab-edit`)",
        "Edit a prefab asset root or nested child, transform, or components without scene instantiation:",
        "```json",
        "{",
        '  "path": "Assets/Prefabs/Enemy.prefab",',
        '  "childPath": "Visuals/Mesh",',
        '  "active": true,',
        '  "componentType": "MeshRenderer",',
        '  "values": [',
        "    {",
        '      "property": "enabled",',
        '      "valueJson": "true"',
        "    }",
        "  ],",
        '  "apply": true',
        "}",
        "```",
        "",
        "#### Instantiate prefab into scene (`prefab-instantiate`)",
        "Instantiate a prefab asset into the active scene under a parent hierarchy:",
        "```json",
        "{",
        '  "path": "Assets/Prefabs/Enemy.prefab",',
        '  "parentPath": "Enemies",',
        '  "position": { "x": 0.0, "y": 0.0, "z": 10.0 },',
        '  "apply": true',
        "}",
        "```",
        "",
        "#### ScriptableObject create and edit (`scriptableobject-create` and `scriptableobject-set`)",
        "Instantiate and configure data container assets:",
        "```json",
        "{",
        '  "type": "GameSettings",',
        '  "path": "Assets/Data/GameSettings.asset",',
        '  "apply": true',
        "}",
        "```",
        "```json",
        "{",
        '  "path": "Assets/Data/GameSettings.asset",',
        '  "fields": [',
        "    {",
        '      "name": "masterVolume",',
        '      "valueJson": "0.8"',
        "    }",
        "  ],",
        '  "apply": true',
        "}",
        "```",
        "",
        "### 4. Console logs, compilation, and testing",
        "",
        "#### Read Console log entries (`console-read`)",
        "Filter Editor Console messages by severity (`error`, `warning`, `log`) or text search:",
        "```json",
        "{",
        '  "severity": "error",',
        '  "contains": "Exception",',
        '  "limit": 20',
        "}",
        "```",
        "",
        "#### Inspect compilation status and compiler errors (`compile-status` and `compile-errors`)",
        "Check domain-reload state or inspect structured C# compiler diagnostics:",
        "```json",
        "{",
        '  "includeWarnings": true',
        "}",
        "```",
        "",
        "#### Run EditMode or PlayMode tests (`test-run`)",
        "Execute tests filtered by namespace or class name and receive a job handle:",
        "```json",
        "{",
        '  "mode": "EditMode",',
        '  "filter": "DucMinh.UnityMcp"',
        "}",
        "```",
        "",
        "#### Poll asynchronous jobs (`job-get` or `test-job-get`)",
        "Poll long-running task status, progress, and results:",
        "```json",
        "{",
        '  "jobId": "test-run-1718000000"',
        "}",
        "```",
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
