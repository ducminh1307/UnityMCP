#!/usr/bin/env python3
"""Generate the human-readable UnityMCP extension command reference from the catalog."""

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
    groups = catalog["groups"]
    commands = catalog["commands"]
    by_group = {
        group["id"]: [cmd for cmd in commands if cmd["group"] == group["id"]]
        for group in groups
    }
    runtime_count = sum("runtime" in cmd["scope"] for cmd in commands)
    conditional_count = sum(cmd["define"] is not None for cmd in commands)

    lines = [
        "# UnityMCP extension command reference",
        "",
        "> [!NOTE]",
        "> This file is generated from [`tool-catalog.json`](tool-catalog.json).",
        "> Run `python tools/generate_tool_reference.py` after changing the catalog.",
        "",
        "This page indexes the `com.ducminh.unity-mcp` extension commands built on top of",
        "Unity's official `com.unity.pipeline` (`1.0.1`) package and invoked via the",
        "`unity` CLI (`unity command <group> <name>`) or `unity mcp --tool-groups`.",
        "",
        "Built-in Unity Pipeline commands (`editor`, `editor.playmode`, `scene`, `gameobject`,",
        "`component`, `prefab`, `asset`, `script`, `script.methodreload`, `console`, `test`,",
        "`build`, `build.target`, `capture`, `lighting`, `navmesh`, `occlusion`, `animation.*`,",
        "`material`, `package`, `settings.*`, `job`, `diagnostics`, `authoring`, `pipeline`,",
        "`runtime`, `multiplayer.role`) are documented by `com.unity.pipeline` and listed live",
        "by `unity command` / `unity command <group>`.",
        "",
        "Independent companion packages register their own Pipeline command groups directly",
        "when installed alongside `com.unity.pipeline` — for example, `com.ducminh.ui-maker`",
        "contributes the `ui_ai` group (10 declarative UI commands) via its optional",
        "`DucMinh.UiMaker.Editor.Pipeline` assembly without requiring `com.ducminh.unity-mcp`.",
        "",
        "## At a glance",
        "",
        f"- **{len(commands)}** extension commands in **{len(groups)}** non-colliding groups.",
        f"- **{runtime_count}** Runtime + Editor commands (`particle` creation/inspection/config/preview, `physics_ext`, `navmesh_ext`).",
        f"- **{len(commands) - runtime_count}** Editor-only commands.",
        f"- **{conditional_count}** package-gated commands compiled automatically via `versionDefines` when their Unity package is installed.",
        f"- Catalog version: `{catalog['catalogVersion']}` (targeting `com.unity.pipeline` `{catalog['pipelineVersion']}`).",
        "",
        "## Command effects and safety conventions",
        "",
        "| `CommandEffect` | MCP hints | Meaning |",
        "|---|---|---|",
        "| `ReadOnly` | `readOnlyHint: true` | Reads bounded state without changing the project or scene. |",
        "| `Idempotent` | `idempotentHint: true` | Repeatable mutation that leaves equivalent state and does not discard user work. |",
        "| `Additive` | all false | Creates new objects, groups, actions, or checkpoints. |",
        "| `Destructive` | `destructiveHint: true` | Can overwrite or remove state; requires `--confirm true` unless `--dry-run true`. |",
        "",
        "## CLI invocation workflow",
        "",
        "1. Verify the running Editor or Runtime Pipeline instance:",
        "   ```bash",
        "   unity status --format json",
        "   ```",
        "2. Inspect live groups, subcommands, and `--help` flags before invoking unfamiliar commands (guessing unknown names logs `CommandNotFound` errors in the Unity Console):",
        "   ```bash",
        "   unity command",
        "   unity command <group>",
        "   unity command <group> <name> --help",
        "   ```",
        "3. Preview supported mutations before applying with `--dry-run true` (default is `false` when omitted):",
        "   ```bash",
        "   unity command physics_ext set_collision_matrix --layer-a 0 --layer-b 8 --ignore true --dry-run true",
        "   ```",
        "4. Pass `--confirm true` on destructive or overwriting commands (`checkpoint restore`, `particle save_prefab --overwrite true`, `addressables remove_entry`, `addressables build`).",
        "5. Avoid passing reserved Pipeline executor flags (`--format`, `--json`, `--fields`, `--verbose`, `--omit-nulls`) as custom command parameters.",
        "",
        "## Groups",
        "",
    ]

    for group in groups:
        anchor = group["id"].replace("_", "-")
        define_note = f", `{group['define']}`" if group["define"] else ""
        lines.append(
            f"- [{group['title']} (`{group['id']}`)](#{anchor}) ({group['declaredCount']}{define_note})"
        )

    for group in groups:
        anchor = group["id"].replace("_", "-")
        group_commands = by_group[group["id"]]
        define_label = f"`{group['define']}`" if group["define"] else "Always compiled"
        lines.extend(
            [
                "",
                f"<a id=\"{anchor}\"></a>",
                "",
                f"## {group['title']} (`{group['id']}`)",
                "",
                f"{len(group_commands)} command(s) | Dependency: `{group['dependency']}` | Gate: {define_label}.",
                "",
                "| Command | Scope | Effect | Dependency | Description |",
                "|---|---|---|---|---|",
            ]
        )
        for cmd in group_commands:
            values = (
                f"`{cmd['command']}`",
                ", ".join(cmd["scope"]),
                f"`{cmd['effect']}`",
                f"`{cmd['dependency']}`",
                cmd["summary"],
            )
            lines.append("| " + " | ".join(escape_cell(value) for value in values) + " |")

    lines.extend(
        [
            "",
            "## Related documentation",
            "",
            "- [README](../README.md)",
            "- [Architecture](architecture.md)",
            "- [Custom commands](custom-tools.md)",
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
