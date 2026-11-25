#!/usr/bin/env python3
"""Install UI Maker Agent Skills into supported project-scoped directories."""

from __future__ import annotations

import argparse
import shutil
from pathlib import Path


SKILL_NAMES = (
    "ui-maker",
    "ui-maker-manifest-authoring",
)

SKILLS_ROOT = Path(__file__).resolve().parents[2]

TARGETS = {
    "universal": Path(".agents/skills"),
    "antigravity": Path(".agent/skills"),
    "claude": Path(".claude/skills"),
    "gemini": Path(".gemini/skills"),
}


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Install ui-maker and ui-maker-manifest-authoring into a Unity project."
    )
    parser.add_argument(
        "--project-root",
        type=Path,
        default=Path.cwd(),
        help="Target Unity project root directory (defaults to current directory).",
    )
    parser.add_argument(
        "--targets",
        default="all",
        help="Comma-separated targets: all or universal,antigravity,claude,gemini.",
    )
    parser.add_argument(
        "--force",
        action="store_true",
        help="Overwrite existing installed skill directories.",
    )
    return parser.parse_args(argv)


def selected_targets(value: str) -> list[str]:
    if value.strip().casefold() == "all":
        return list(TARGETS.keys())
    selected = [item.strip().casefold() for item in value.split(",") if item.strip()]
    if not selected:
        raise ValueError("No installation targets specified.")
    unknown = sorted(set(selected) - set(TARGETS.keys()))
    if unknown:
        raise ValueError("Unknown target(s): " + ", ".join(unknown))
    return selected


def validate_canonical_skills() -> list[Path]:
    skill_dirs: list[Path] = []
    for skill_name in SKILL_NAMES:
        skill_dir = SKILLS_ROOT / skill_name
        if not skill_dir.is_dir():
            raise SystemExit(f"Canonical skill directory not found: {skill_dir}")
        skill_md = skill_dir / "SKILL.md"
        if not skill_md.is_file():
            raise SystemExit(f"Canonical SKILL.md not found: {skill_md}")
        skill_dirs.append(skill_dir)
    return skill_dirs


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    project_root = args.project_root.resolve()
    if not project_root.is_dir():
        raise SystemExit(f"Project root does not exist: {project_root}")

    skill_dirs = validate_canonical_skills()

    try:
        targets = selected_targets(args.targets)
    except ValueError as exc:
        raise SystemExit(str(exc)) from exc

    ignore_fn = shutil.ignore_patterns("__pycache__", "*.pyc", "*.pyo", ".pytest_cache")

    for target_name in targets:
        target_rel = TARGETS[target_name]
        for skill_dir in skill_dirs:
            destination = project_root / target_rel / skill_dir.name
            if destination.resolve() == skill_dir.resolve():
                print(f"[{target_name}] already using canonical skill: {destination}")
                continue
            if destination.exists():
                if not args.force:
                    print(
                        f"[{target_name}] skipped existing skill (use --force): {destination}"
                    )
                    continue
                shutil.rmtree(destination)
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copytree(skill_dir, destination, ignore=ignore_fn)
            print(f"[{target_name}] installed {skill_dir.name}: {destination}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
