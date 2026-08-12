"""Discover and select local Unity bridge descriptors safely."""

from __future__ import annotations

import json
import os
from collections.abc import Callable
from pathlib import Path

from .config import DEFAULT_LIMITS, GatewayLimits, default_descriptor_dir
from .errors import AmbiguousInstanceError, DescriptorError, InstanceNotFoundError
from .models import InstanceDescriptor


def pid_is_alive(pid: int) -> bool:
    """Best-effort cross-platform process liveness check without extra packages."""
    if pid <= 0:
        return False
    if os.name == "nt":
        try:
            import ctypes
            from ctypes import wintypes

            process_query_limited_information = 0x1000
            kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
            kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
            kernel32.OpenProcess.restype = wintypes.HANDLE
            kernel32.GetExitCodeProcess.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD)]
            kernel32.GetExitCodeProcess.restype = wintypes.BOOL
            kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
            kernel32.CloseHandle.restype = wintypes.BOOL
            handle = kernel32.OpenProcess(process_query_limited_information, False, pid)
            if not handle:
                return False
            try:
                exit_code = wintypes.DWORD()
                if not kernel32.GetExitCodeProcess(handle, ctypes.byref(exit_code)):
                    return False
                return exit_code.value == 259  # STILL_ACTIVE
            finally:
                kernel32.CloseHandle(handle)
        except (AttributeError, OSError):
            return False
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        return True
    except OSError:
        return False
    return True


def _read_descriptor(path: Path, limits: GatewayLimits) -> InstanceDescriptor:
    try:
        if path.is_symlink() or not path.is_file():
            raise DescriptorError(f"Descriptor path is not a regular file: {path}")
        file_stat = path.stat()
        if os.name != "nt" and file_stat.st_mode & 0o077:
            raise DescriptorError(f"Descriptor permissions must deny group and other access: {path}")
        size = file_stat.st_size
        if size <= 0 or size > limits.max_descriptor_bytes:
            raise DescriptorError(f"Descriptor exceeds size limit: {path}")
        raw = json.loads(path.read_text(encoding="utf-8"))
    except DescriptorError:
        raise
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise DescriptorError(f"Cannot read descriptor {path.name}: {type(exc).__name__}") from None
    if not isinstance(raw, dict):
        raise DescriptorError(f"Descriptor {path.name} must contain a JSON object")
    return InstanceDescriptor.from_dict(raw, path=path)


def discover_instances(
    directory: Path | None = None,
    *,
    include_stale: bool = False,
    liveness: Callable[[int], bool] = pid_is_alive,
    limits: GatewayLimits = DEFAULT_LIMITS,
) -> list[InstanceDescriptor]:
    directory = (directory or default_descriptor_dir()).expanduser()
    if not directory.exists():
        return []
    if not directory.is_dir():
        raise DescriptorError(f"Descriptor location is not a directory: {directory}")
    instances: list[InstanceDescriptor] = []
    seen: set[str] = set()
    for path in sorted(directory.glob("*.json"), key=lambda item: item.name.casefold()):
        try:
            descriptor = _read_descriptor(path, limits)
        except DescriptorError:
            continue  # One interrupted/stale write must not hide healthy instances.
        if descriptor.instance_id in seen:
            continue
        if include_stale or liveness(descriptor.pid):
            instances.append(descriptor)
            seen.add(descriptor.instance_id)
    return sorted(instances, key=lambda item: (item.project_id, item.kind, item.instance_id))


def select_instance(
    instances: list[InstanceDescriptor], instance_id: str | None = None
) -> InstanceDescriptor:
    if instance_id:
        matches = [candidate for candidate in instances if candidate.instance_id == instance_id]
        if not matches:
            raise InstanceNotFoundError(f"No live Unity instance matches {instance_id!r}")
        if len(matches) > 1:
            raise AmbiguousInstanceError(f"Multiple descriptors claim Unity instance {instance_id!r}")
        return matches[0]
    if not instances:
        raise InstanceNotFoundError("No live UnityMCP Editor or Development Player was found")
    if len(instances) > 1:
        ids = ", ".join(candidate.instance_id for candidate in instances)
        raise AmbiguousInstanceError(f"Multiple Unity instances are live ({ids}); pass --instance explicitly")
    return instances[0]


def resolve_instance(
    instance_id: str | None = None,
    directory: Path | None = None,
    *,
    liveness: Callable[[int], bool] = pid_is_alive,
) -> InstanceDescriptor:
    return select_instance(discover_instances(directory, liveness=liveness), instance_id)
