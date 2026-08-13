#!/usr/bin/env python3
"""Validate the canonical UnityMCP tool catalog using only the standard library."""

from __future__ import annotations

import json
import re
import sys
from collections import Counter
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_CATALOG = ROOT / "docs" / "tool-catalog.json"
PACKAGE_ROOT = ROOT / "Packages" / "com.ducminh.unity-mcp"
NAME_PATTERN = re.compile(r"^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$")
REQUIRED_FIELDS = {
    "name",
    "category",
    "status",
    "scope",
    "safety",
    "defaultEnabled",
    "schemaRevision",
    "dependency",
    "summary",
}
VALID_STATUSES = {"implemented", "planned"}
VALID_SCOPES = {"editor", "runtime"}
VALID_SAFETY = {"safe-read", "write", "destructive", "unsafe"}

DEFAULT_ENABLED = {
    "unity-status",
    "project-info",
    "editor-state-get",
    "editor-selection-get",
    "scene-list",
    "scene-hierarchy",
    "gameobject-find",
    "gameobject-get",
    "component-types",
    "component-schema",
    "component-get",
    "asset-search",
    "asset-info",
    "asset-dependencies",
    "prefab-info",
    "material-info",
    "compile-status",
    "compile-errors",
    "console-read",
    "package-list",
}

def require(condition: bool, message: str, errors: list[str]) -> None:
    if not condition:
        errors.append(message)


def load_catalog(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as stream:
        value = json.load(stream)
    if not isinstance(value, dict):
        raise ValueError("catalog root must be a JSON object")
    return value


def validate(catalog: dict[str, Any]) -> list[str]:
    errors: list[str] = []
    categories = catalog.get("categories")
    tools = catalog.get("tools")
    require(isinstance(categories, list), "categories must be an array", errors)
    require(isinstance(tools, list), "tools must be an array", errors)
    if not isinstance(categories, list) or not isinstance(tools, list):
        return errors

    category_ids: list[str] = []
    declared_counts: dict[str, int] = {}
    for index, category in enumerate(categories):
        label = f"categories[{index}]"
        if not isinstance(category, dict):
            errors.append(f"{label} must be an object")
            continue
        category_id = category.get("id")
        title = category.get("title")
        declared = category.get("declaredCount")
        require(isinstance(category_id, str) and bool(NAME_PATTERN.fullmatch(category_id)),
                f"{label}.id must be kebab-case", errors)
        require(isinstance(title, str) and bool(title.strip()),
                f"{label}.title must be a non-empty string", errors)
        require(isinstance(declared, int) and not isinstance(declared, bool) and declared > 0,
                f"{label}.declaredCount must be a positive integer", errors)
        if isinstance(category_id, str):
            category_ids.append(category_id)
            if isinstance(declared, int) and not isinstance(declared, bool):
                declared_counts[category_id] = declared

    duplicate_categories = sorted(
        category for category, count in Counter(category_ids).items() if count > 1
    )
    require(not duplicate_categories,
            f"duplicate category IDs: {', '.join(duplicate_categories)}", errors)
    category_set = set(category_ids)

    names: list[str] = []
    actual_counts: Counter[str] = Counter()
    implemented: set[str] = set()
    enabled: set[str] = set()
    for index, tool in enumerate(tools):
        label = f"tools[{index}]"
        if not isinstance(tool, dict):
            errors.append(f"{label} must be an object")
            continue
        missing = sorted(REQUIRED_FIELDS - tool.keys())
        extra = sorted(tool.keys() - REQUIRED_FIELDS)
        require(not missing, f"{label} missing fields: {', '.join(missing)}", errors)
        require(not extra, f"{label} has unknown fields: {', '.join(extra)}", errors)

        name = tool.get("name")
        category = tool.get("category")
        status = tool.get("status")
        scope = tool.get("scope")
        safety = tool.get("safety")
        default_enabled = tool.get("defaultEnabled")
        schema_revision = tool.get("schemaRevision")
        dependency = tool.get("dependency")
        summary = tool.get("summary")

        require(isinstance(name, str) and bool(NAME_PATTERN.fullmatch(name)),
                f"{label}.name must be kebab-case", errors)
        require(category in category_set,
                f"{label}.category references an unknown category: {category!r}", errors)
        require(status in VALID_STATUSES,
                f"{label}.status must be one of {sorted(VALID_STATUSES)}", errors)
        require(isinstance(scope, list) and bool(scope),
                f"{label}.scope must be a non-empty array", errors)
        if isinstance(scope, list):
            require(len(scope) == len(set(scope)), f"{label}.scope contains duplicates", errors)
            require(all(value in VALID_SCOPES for value in scope),
                    f"{label}.scope must contain only {sorted(VALID_SCOPES)}", errors)
        require(safety in VALID_SAFETY,
                f"{label}.safety must be one of {sorted(VALID_SAFETY)}", errors)
        require(isinstance(default_enabled, bool),
                f"{label}.defaultEnabled must be a boolean", errors)
        require(isinstance(schema_revision, int) and not isinstance(schema_revision, bool)
                and schema_revision > 0,
                f"{label}.schemaRevision must be a positive integer", errors)
        require(isinstance(dependency, str) and bool(dependency.strip()),
                f"{label}.dependency must be a non-empty string", errors)
        require(isinstance(summary, str) and bool(summary.strip()),
                f"{label}.summary must be a non-empty string", errors)

        if isinstance(name, str):
            names.append(name)
            if status == "implemented":
                implemented.add(name)
            if default_enabled is True:
                enabled.add(name)
        if isinstance(category, str):
            actual_counts[category] += 1

        if default_enabled is True:
            require(status == "implemented", f"{label}: planned tool cannot be enabled", errors)
            require(safety == "safe-read", f"{label}: enabled tool must be safe-read", errors)

    duplicate_names = sorted(name for name, count in Counter(names).items() if count > 1)
    require(not duplicate_names, f"duplicate tool names: {', '.join(duplicate_names)}", errors)

    expected_count = catalog.get("expectedToolCount")
    implemented_count = catalog.get("implementedTargetCount")
    require(expected_count == 187, "expectedToolCount must be 187", errors)
    require(len(tools) == expected_count,
            f"tool count {len(tools)} does not match expectedToolCount {expected_count}", errors)
    require(sum(declared_counts.values()) == expected_count,
            "sum of declared category counts must match expectedToolCount", errors)
    require(isinstance(implemented_count, int) and not isinstance(implemented_count, bool)
            and implemented_count >= len(DEFAULT_ENABLED),
            "implementedTargetCount must be an integer no smaller than the safe default profile", errors)
    require(len(implemented) == implemented_count,
            f"implemented count {len(implemented)} does not match {implemented_count}", errors)

    for category in sorted(category_set):
        require(actual_counts[category] == declared_counts.get(category),
                f"category {category!r}: actual {actual_counts[category]}, "
                f"declared {declared_counts.get(category)}", errors)

    require(enabled == DEFAULT_ENABLED,
            "default-enabled set differs from the 20-tool safe allowlist; "
            f"missing={sorted(DEFAULT_ENABLED - enabled)}, extra={sorted(enabled - DEFAULT_ENABLED)}",
            errors)
    source_pattern = re.compile(r'\[UnityMcpTool\("([a-z0-9-]+)"')
    source_names: list[str] = []
    for source in sorted(PACKAGE_ROOT.glob("**/*.cs")):
        if "Samples~" in source.parts or "Tests" in source.parts:
            continue
        source_names.extend(source_pattern.findall(source.read_text(encoding="utf-8")))
    source_set = set(source_names)
    source_duplicates = sorted(name for name, count in Counter(source_names).items() if count > 1)
    require(not source_duplicates,
            f"duplicate built-in UnityMcpTool attributes: {', '.join(source_duplicates)}", errors)
    require(source_set == implemented,
            "C# built-in attributes differ from the implemented catalog set; "
            f"missing={sorted(implemented - source_set)}, "
            f"extra={sorted(source_set - implemented)}", errors)
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

    categories = catalog["categories"]
    tools = catalog["tools"]
    enabled_count = sum(tool["defaultEnabled"] for tool in tools)
    print(
        f"catalog OK: {len(tools)} tools, {len(categories)} categories, "
        f"{catalog['implementedTargetCount']} implemented, {enabled_count} default-enabled"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
