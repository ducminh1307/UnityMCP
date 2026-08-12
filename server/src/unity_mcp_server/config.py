"""Gateway defaults and hard security bounds."""

from __future__ import annotations

import os
import platform
from dataclasses import dataclass
from pathlib import Path


def default_descriptor_dir() -> Path:
    override = os.environ.get("UNITY_MCP_DESCRIPTOR_DIR")
    if override:
        return Path(override).expanduser()
    system = platform.system()
    if system == "Windows":
        root = os.environ.get("LOCALAPPDATA") or os.environ.get("APPDATA")
        return Path(root) / "UnityMCP" / "instances" if root else Path.home() / "AppData/Local/UnityMCP/instances"
    if system == "Darwin":
        return Path.home() / "Library/Application Support/UnityMCP/instances"
    root = os.environ.get("XDG_DATA_HOME")
    return Path(root) / "UnityMCP/instances" if root else Path.home() / ".local/share/UnityMCP/instances"


@dataclass(frozen=True, slots=True)
class GatewayLimits:
    max_descriptor_bytes: int = 16 * 1024
    max_registry_bytes: int = 4 * 1024 * 1024
    max_tool_result_bytes: int = 16 * 1024 * 1024
    max_job_result_bytes: int = 16 * 1024 * 1024
    max_request_bytes: int = 4 * 1024 * 1024
    max_tools: int = 512
    max_schema_bytes: int = 256 * 1024
    max_schema_depth: int = 64
    max_json_depth: int = 128
    default_timeout_seconds: float = 30.0
    connect_timeout_seconds: float = 2.0
    registry_poll_seconds: float = 1.0
    registry_min_refresh_seconds: float = 0.25
    reload_grace_seconds: float = 30.0


DEFAULT_LIMITS = GatewayLimits()
