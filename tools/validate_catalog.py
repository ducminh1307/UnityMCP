#!/usr/bin/env python3
"""Validate the UnityMCP Pipeline extension catalog against C# command attributes."""

from __future__ import annotations

import json
import re
import sys
from collections import Counter
from dataclasses import dataclass
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_CATALOG = ROOT / "docs" / "tool-catalog.json"
PACKAGE_ROOT = ROOT / "Packages" / "com.ducminh.unity-mcp"

GROUP_PATTERN = re.compile(r"^[a-z][a-z0-9_]*(?:\.[a-z][a-z0-9_]*)*$")
NAME_PATTERN = re.compile(r"^[a-z][a-z0-9_]*$")
REQUIRED_GROUP_FIELDS = {"id", "title", "declaredCount", "define", "dependency"}
REQUIRED_COMMAND_FIELDS = {
    "group",
    "name",
    "command",
    "scope",
    "effect",
    "define",
    "dependency",
    "summary",
}
VALID_SCOPES = {"editor", "runtime"}
VALID_EFFECTS = {"ReadOnly", "Idempotent", "Additive", "Destructive"}
RESERVED_CLI_ARGS = {"format", "json", "fields", "verbose", "omit-nulls"}

BUILTIN_PIPELINE_GROUPS = frozenset(
    {
        "animation",
        "asset",
        "authoring",
        "build",
        "capture",
        "component",
        "console",
        "diagnostics",
        "editor",
        "gameobject",
        "job",
        "lighting",
        "material",
        "navmesh",
        "occlusion",
        "package",
        "pipeline",
        "prefab",
        "runtime",
        "scene",
        "script",
        "settings",
        "test",
    }
)


@dataclass(frozen=True)
class SourceCommand:
    group: str
    name: str
    effect: str | None
    source: Path


def require(condition: bool, message: str, errors: list[str]) -> None:
    if not condition:
        errors.append(message)


def load_catalog(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as stream:
        value = json.load(stream)
    if not isinstance(value, dict):
        raise ValueError("catalog root must be a JSON object")
    return value


def strip_csharp_comments(source: str) -> str:
    """Replace C# comments with whitespace while preserving string literals and newlines."""
    out: list[str] = []
    i = 0
    n = len(source)
    while i < n:
        ch = source[i]
        if ch == "@" and i + 1 < n and source[i + 1] == '"':
            out.append('@"')
            i += 2
            while i < n:
                if source[i] == '"' and i + 1 < n and source[i + 1] == '"':
                    out.append('""')
                    i += 2
                elif source[i] == '"':
                    out.append('"')
                    i += 1
                    break
                else:
                    out.append(source[i])
                    i += 1
        elif ch == '"':
            out.append('"')
            i += 1
            while i < n:
                if source[i] == "\\" and i + 1 < n:
                    out.append(source[i : i + 2])
                    i += 2
                elif source[i] == '"':
                    out.append('"')
                    i += 1
                    break
                else:
                    out.append(source[i])
                    i += 1
        elif ch == "'":
            out.append("'")
            i += 1
            while i < n:
                if source[i] == "\\" and i + 1 < n:
                    out.append(source[i : i + 2])
                    i += 2
                elif source[i] == "'":
                    out.append("'")
                    i += 1
                    break
                else:
                    out.append(source[i])
                    i += 1
        elif ch == "/" and i + 1 < n and source[i + 1] == "/":
            while i < n and source[i] != "\n":
                i += 1
        elif ch == "/" and i + 1 < n and source[i + 1] == "*":
            i += 2
            while i + 1 < n and not (source[i] == "*" and source[i + 1] == "/"):
                if source[i] == "\n":
                    out.append("\n")
                i += 1
            i += 2
        else:
            out.append(ch)
            i += 1
    return "".join(out)


def derive_command_name(method_name: str) -> str:
    """Mirror Unity.Pipeline.Commands.CommandRegistry.DeriveName."""
    parts: list[str] = []
    for idx, ch in enumerate(method_name):
        if ch.isupper() and idx > 0:
            prev_upper = method_name[idx - 1].isupper()
            next_lower = idx + 1 < len(method_name) and method_name[idx + 1].islower()
            if not prev_upper or next_lower:
                parts.append("_")
        parts.append(ch.lower())
    return "".join(parts)


def split_top_level_args(body: str) -> list[str]:
    args: list[str] = []
    current: list[str] = []
    depth = 0
    i = 0
    n = len(body)
    while i < n:
        ch = body[i]
        if ch == '"':
            current.append(ch)
            i += 1
            while i < n:
                current.append(body[i])
                if body[i] == "\\" and i + 1 < n:
                    i += 1
                    current.append(body[i])
                elif body[i] == '"':
                    i += 1
                    break
                i += 1
            continue
        if ch in "([{":
            depth += 1
        elif ch in ")]}":
            depth = max(0, depth - 1)
        elif ch == "," and depth == 0:
            args.append("".join(current).strip())
            current = []
            i += 1
            continue
        current.append(ch)
        i += 1
    tail = "".join(current).strip()
    if tail:
        args.append(tail)
    return args


def resolve_string_expr(expr: str, constants: dict[str, str]) -> str | None:
    expr = expr.strip()
    m = re.fullmatch(r'"((?:\\.|[^"\\])*)"', expr)
    if m:
        return bytes(m.group(1), "utf-8").decode("unicode_escape")
    if expr in constants:
        return constants[expr]
    return None


def find_matching_paren(text: str, open_idx: int) -> int:
    depth = 0
    i = open_idx
    n = len(text)
    while i < n:
        ch = text[i]
        if ch == '"':
            i += 1
            while i < n:
                if text[i] == "\\" and i + 1 < n:
                    i += 2
                elif text[i] == '"':
                    i += 1
                    break
                else:
                    i += 1
            continue
        if ch == "(":
            depth += 1
        elif ch == ")":
            depth -= 1
            if depth == 0:
                return i
        i += 1
    return -1


def scan_package_commands(
    package_root: Path,
) -> tuple[list[SourceCommand], list[str]]:
    commands: list[SourceCommand] = []
    errors: list[str] = []

    attr_pattern = re.compile(r"\[\s*(CliGroupCommand|CliCommand)\s*\(")
    const_pattern = re.compile(r'\bconst\s+string\s+(\w+)\s*=\s*"((?:\\.|[^"\\])*)"\s*;')
    method_pattern = re.compile(
        r"^(?:\s*\[[^\]]*\]\s*)*(?:\s*(?:public|internal|private|protected|static|async|unsafe|new|partial)\b\s+)+[\w<>\[\],\.\?]+\s+(\w+)\s*\(",
        re.M,
    )
    reserved_arg_pattern = re.compile(r'\[CliArg\s*\(\s*"([^"]+)"')

    for source in sorted(package_root.rglob("*.cs")):
        rel_parts = source.relative_to(package_root).parts
        if "Samples~" in rel_parts or "Tests" in rel_parts:
            continue

        raw_text = source.read_text(encoding="utf-8")
        cleaned = strip_csharp_comments(raw_text)

        for arg_match in reserved_arg_pattern.finditer(cleaned):
            arg_name = arg_match.group(1)
            if arg_name in RESERVED_CLI_ARGS:
                errors.append(
                    f"{source.relative_to(ROOT)}: [CliArg] uses reserved Pipeline flag name '{arg_name}'"
                )

        constants = {m.group(1): m.group(2) for m in const_pattern.finditer(cleaned)}

        for attr_match in attr_pattern.finditer(cleaned):
            attr_kind = attr_match.group(1)
            open_paren = attr_match.end() - 1
            close_paren = find_matching_paren(cleaned, open_paren)
            if close_paren < 0:
                errors.append(f"{source.relative_to(ROOT)}: unclosed [{attr_kind}(...)]")
                continue

            close_bracket = cleaned.find("]", close_paren)
            if close_bracket < 0:
                errors.append(f"{source.relative_to(ROOT)}: unclosed attribute bracket for [{attr_kind}]")
                continue

            body = cleaned[open_paren + 1 : close_paren]
            args = split_top_level_args(body)

            positional: list[str] = []
            named: dict[str, str] = {}
            for item in args:
                if "=" in item:
                    lhs, rhs = item.split("=", 1)
                    key = lhs.strip()
                    if key in {"Name", "Effect", "MainThreadRequired", "Tags"}:
                        named[key] = rhs.strip()
                        continue
                positional.append(item)

            after_attr = cleaned[close_bracket + 1 :]
            method_match = method_pattern.match(after_attr)
            if not method_match:
                errors.append(
                    f"{source.relative_to(ROOT)}: could not locate static method after [{attr_kind}]"
                )
                continue
            method_name = method_match.group(1)

            group: str | None = None
            name: str | None = None
            if attr_kind == "CliGroupCommand":
                if len(positional) < 2:
                    errors.append(
                        f"{source.relative_to(ROOT)}:{method_name}: [CliGroupCommand] requires (group, description)"
                    )
                    continue
                group = resolve_string_expr(positional[0], constants)
                if "Name" in named:
                    name = resolve_string_expr(named["Name"], constants)
                else:
                    name = derive_command_name(method_name)
            else:
                if len(positional) >= 3:
                    group = resolve_string_expr(positional[0], constants)
                    name = resolve_string_expr(positional[1], constants)
                elif len(positional) == 2:
                    full = resolve_string_expr(positional[0], constants)
                    if full is not None:
                        if "." in full:
                            group, name = full.rsplit(".", 1)
                        else:
                            group, name = "global", full
                elif len(positional) == 1:
                    group = source.stem.lower()
                    name = derive_command_name(method_name)

            if not group or not name:
                errors.append(
                    f"{source.relative_to(ROOT)}:{method_name}: unable to resolve group/name from [{attr_kind}({body})]"
                )
                continue

            effect: str | None = None
            if "Effect" in named:
                eff_val = named["Effect"]
                eff_match = re.fullmatch(r"(?:CommandEffect\.)?(\w+)", eff_val)
                if eff_match:
                    effect = eff_match.group(1)

            commands.append(SourceCommand(group=group, name=name, effect=effect, source=source))

    return commands, errors


def validate(catalog: dict[str, Any], package_root: Path = PACKAGE_ROOT) -> list[str]:
    errors: list[str] = []

    groups = catalog.get("groups")
    commands = catalog.get("commands")
    require(isinstance(groups, list), "groups must be an array", errors)
    require(isinstance(commands, list), "commands must be an array", errors)
    if not isinstance(groups, list) or not isinstance(commands, list):
        return errors

    group_ids: list[str] = []
    declared_counts: dict[str, int] = {}
    group_meta: dict[str, dict[str, Any]] = {}
    for index, group_obj in enumerate(groups):
        label = f"groups[{index}]"
        if not isinstance(group_obj, dict):
            errors.append(f"{label} must be an object")
            continue

        missing = sorted(REQUIRED_GROUP_FIELDS - group_obj.keys())
        extra = sorted(group_obj.keys() - REQUIRED_GROUP_FIELDS)
        require(not missing, f"{label} missing fields: {', '.join(missing)}", errors)
        require(not extra, f"{label} has unknown fields: {', '.join(extra)}", errors)

        group_id = group_obj.get("id")
        title = group_obj.get("title")
        declared = group_obj.get("declaredCount")
        define = group_obj.get("define")
        dependency = group_obj.get("dependency")

        require(
            isinstance(group_id, str) and bool(GROUP_PATTERN.fullmatch(group_id)),
            f"{label}.id must be lowercase snake_case/dotted identifier",
            errors,
        )
        require(
            isinstance(title, str) and bool(title.strip()),
            f"{label}.title must be a non-empty string",
            errors,
        )
        require(
            isinstance(declared, int) and not isinstance(declared, bool) and declared > 0,
            f"{label}.declaredCount must be a positive integer",
            errors,
        )
        require(
            define is None or (isinstance(define, str) and bool(define.strip())),
            f"{label}.define must be null or a non-empty preprocessor symbol",
            errors,
        )
        require(
            isinstance(dependency, str) and bool(dependency.strip()),
            f"{label}.dependency must be a non-empty string",
            errors,
        )

        if isinstance(group_id, str):
            root_group = group_id.split(".", 1)[0]
            require(
                group_id not in BUILTIN_PIPELINE_GROUPS and root_group not in BUILTIN_PIPELINE_GROUPS,
                f"{label}.id '{group_id}' collides with a built-in com.unity.pipeline group",
                errors,
            )
            group_ids.append(group_id)
            group_meta[group_id] = group_obj
            if isinstance(declared, int) and not isinstance(declared, bool):
                declared_counts[group_id] = declared

    duplicate_groups = sorted(g for g, count in Counter(group_ids).items() if count > 1)
    require(not duplicate_groups, f"duplicate group IDs: {', '.join(duplicate_groups)}", errors)
    group_set = set(group_ids)

    catalog_pairs: list[tuple[str, str]] = []
    catalog_by_pair: dict[tuple[str, str], dict[str, Any]] = {}
    actual_counts: Counter[str] = Counter()

    for index, cmd_obj in enumerate(commands):
        label = f"commands[{index}]"
        if not isinstance(cmd_obj, dict):
            errors.append(f"{label} must be an object")
            continue

        missing = sorted(REQUIRED_COMMAND_FIELDS - cmd_obj.keys())
        extra = sorted(cmd_obj.keys() - REQUIRED_COMMAND_FIELDS)
        require(not missing, f"{label} missing fields: {', '.join(missing)}", errors)
        require(not extra, f"{label} has unknown fields: {', '.join(extra)}", errors)

        group = cmd_obj.get("group")
        name = cmd_obj.get("name")
        full_command = cmd_obj.get("command")
        scope = cmd_obj.get("scope")
        effect = cmd_obj.get("effect")
        define = cmd_obj.get("define")
        dependency = cmd_obj.get("dependency")
        summary = cmd_obj.get("summary")

        require(group in group_set, f"{label}.group references unknown group: {group!r}", errors)
        require(
            isinstance(name, str) and bool(NAME_PATTERN.fullmatch(name)),
            f"{label}.name must be snake_case: {name!r}",
            errors,
        )
        if isinstance(group, str) and isinstance(name, str):
            expected_full = f"{group} {name}"
            require(
                full_command == expected_full,
                f"{label}.command must equal {expected_full!r}, got {full_command!r}",
                errors,
            )
            pair = (group, name)
            catalog_pairs.append(pair)
            catalog_by_pair[pair] = cmd_obj
            actual_counts[group] += 1

        require(isinstance(scope, list) and bool(scope), f"{label}.scope must be a non-empty array", errors)
        if isinstance(scope, list):
            require(len(scope) == len(set(scope)), f"{label}.scope contains duplicates", errors)
            require(
                all(value in VALID_SCOPES for value in scope),
                f"{label}.scope must contain only {sorted(VALID_SCOPES)}",
                errors,
            )

        require(
            effect in VALID_EFFECTS,
            f"{label}.effect must be one of {sorted(VALID_EFFECTS)}",
            errors,
        )
        require(
            define is None or (isinstance(define, str) and bool(define.strip())),
            f"{label}.define must be null or a non-empty preprocessor symbol",
            errors,
        )
        require(
            isinstance(dependency, str) and bool(dependency.strip()),
            f"{label}.dependency must be a non-empty string",
            errors,
        )
        require(
            isinstance(summary, str) and bool(summary.strip()),
            f"{label}.summary must be a non-empty string",
            errors,
        )
        if group in group_meta:
            require(
                define == group_meta[group].get("define"),
                f"{label}.define {define!r} does not match group {group!r} define {group_meta[group].get('define')!r}",
                errors,
            )

    duplicate_pairs = sorted(
        f"{g} {n}" for (g, n), count in Counter(catalog_pairs).items() if count > 1
    )
    require(not duplicate_pairs, f"duplicate catalog commands: {', '.join(duplicate_pairs)}", errors)

    expected_groups = catalog.get("expectedGroupCount")
    expected_commands = catalog.get("expectedCommandCount")
    require(expected_groups == 19, "expectedGroupCount must be 19", errors)
    require(expected_commands == 47, "expectedCommandCount must be 47", errors)
    require(
        len(groups) == expected_groups,
        f"group count {len(groups)} does not match expectedGroupCount {expected_groups}",
        errors,
    )
    require(
        len(commands) == expected_commands,
        f"command count {len(commands)} does not match expectedCommandCount {expected_commands}",
        errors,
    )
    require(
        sum(declared_counts.values()) == expected_commands,
        "sum of declared group counts must match expectedCommandCount",
        errors,
    )

    for group_id in sorted(group_set):
        require(
            actual_counts[group_id] == declared_counts.get(group_id),
            f"group {group_id!r}: actual {actual_counts[group_id]}, declared {declared_counts.get(group_id)}",
            errors,
        )

    source_commands, scan_errors = scan_package_commands(package_root)
    errors.extend(scan_errors)

    source_pairs = [(cmd.group, cmd.name) for cmd in source_commands]
    duplicate_source = sorted(
        f"{g} {n}" for (g, n), count in Counter(source_pairs).items() if count > 1
    )
    require(
        not duplicate_source,
        f"duplicate C# [CliGroupCommand]/[CliCommand] registrations: {', '.join(duplicate_source)}",
        errors,
    )

    for cmd in source_commands:
        root_group = cmd.group.split(".", 1)[0]
        require(
            cmd.group not in BUILTIN_PIPELINE_GROUPS and root_group not in BUILTIN_PIPELINE_GROUPS,
            f"{cmd.source.relative_to(ROOT)}: extension group '{cmd.group}' collides with built-in com.unity.pipeline group",
            errors,
        )
        pair = (cmd.group, cmd.name)
        if pair in catalog_by_pair and cmd.effect is not None:
            catalog_effect = catalog_by_pair[pair]["effect"]
            require(
                cmd.effect == catalog_effect,
                f"{cmd.source.relative_to(ROOT)}: '{cmd.group} {cmd.name}' C# Effect={cmd.effect} != catalog effect={catalog_effect}",
                errors,
            )

    source_set = set(source_pairs)
    catalog_set = set(catalog_pairs)
    missing_in_source = sorted(f"{g} {n}" for g, n in (catalog_set - source_set))
    extra_in_source = sorted(f"{g} {n}" for g, n in (source_set - catalog_set))
    require(
        source_set == catalog_set,
        "C# [CliGroupCommand]/[CliCommand] declarations differ from docs/tool-catalog.json; "
        f"missing_in_csharp={missing_in_source}, extra_in_csharp={extra_in_source}",
        errors,
    )

    return errors


def main(argv: list[str]) -> int:
    path = Path(argv[1]).resolve() if len(argv) > 1 else DEFAULT_CATALOG
    try:
        catalog = load_catalog(path)
    except (OSError, ValueError, json.JSONDecodeError) as exc:
        print(f"catalog validation failed: {exc}", file=sys.stderr)
        return 1

    errors = validate(catalog)
    if errors:
        print(f"catalog validation failed with {len(errors)} error(s):", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1

    groups = catalog["groups"]
    commands = catalog["commands"]
    runtime_count = sum("runtime" in cmd["scope"] for cmd in commands)
    print(
        f"catalog OK: {len(commands)} commands in {len(groups)} extension groups "
        f"({runtime_count} runtime+editor, {len(commands) - runtime_count} editor-only)"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
