"""Immutable protocol models; no MCP dependency so discovery can run standalone."""

from __future__ import annotations

import copy
import hashlib
import json
import re
from collections.abc import Mapping
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Literal

_TOOL_NAME = re.compile(r"^[A-Za-z0-9_.-]{1,128}$")
_SAFETY = frozenset({"safe-read", "write", "destructive", "unsafe"})
_SCOPE = frozenset({"editor", "runtime"})


def _json_copy(value: Any) -> Any:
    return copy.deepcopy(value)


def canonical_json(value: Any) -> str:
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(",", ":"))


def schema_hash(schema: Mapping[str, Any]) -> str:
    return hashlib.sha256(canonical_json(schema).encode("utf-8")).hexdigest()


@dataclass(frozen=True, slots=True)
class InstanceDescriptor:
    port: int
    token: str = field(repr=False)
    pid: int
    project_id: str
    instance_id: str
    kind: Literal["editor", "player"]
    build_id: str
    path: Path | None = field(default=None, compare=False)

    @classmethod
    def from_dict(cls, raw: Mapping[str, Any], *, path: Path | None = None) -> InstanceDescriptor:
        try:
            port = raw["port"]
            token = raw["token"]
            pid = raw["pid"]
            project_id = raw["projectId"]
            instance_id = raw["instanceId"]
            kind = raw["kind"]
            build_id = raw.get("buildId", "")
        except KeyError as exc:
            from .errors import DescriptorError

            raise DescriptorError(f"Descriptor is missing {exc.args[0]!r}") from None
        if isinstance(port, bool) or not isinstance(port, int) or not (1 <= port <= 65535):
            from .errors import DescriptorError

            raise DescriptorError("Descriptor port must be an integer between 1 and 65535")
        if isinstance(pid, bool) or not isinstance(pid, int) or pid <= 0:
            from .errors import DescriptorError

            raise DescriptorError("Descriptor pid must be a positive integer")
        if (
            not isinstance(token, str)
            or len(token) < 32
            or len(token) > 512
            or any(ord(c) < 33 or ord(c) > 126 for c in token)
        ):
            from .errors import DescriptorError

            raise DescriptorError("Descriptor token must contain 32-512 visible characters")
        for key, value in (("projectId", project_id), ("instanceId", instance_id)):
            if (
                not isinstance(value, str)
                or not value.strip()
                or len(value) > 256
                or any(ord(c) < 32 or ord(c) == 127 for c in value)
            ):
                from .errors import DescriptorError

                raise DescriptorError(f"Descriptor {key} must be a non-empty string up to 256 characters")
        if kind not in ("editor", "player"):
            from .errors import DescriptorError

            raise DescriptorError("Descriptor kind must be 'editor' or 'player'")
        if (
            not isinstance(build_id, str)
            or len(build_id) > 256
            or any(ord(c) < 32 or ord(c) == 127 for c in build_id)
        ):
            from .errors import DescriptorError

            raise DescriptorError("Descriptor buildId must be a string up to 256 characters")
        return cls(port, token, pid, project_id, instance_id, kind, build_id, path)

    def public_dict(self) -> dict[str, Any]:
        """Return non-secret instance metadata safe for MCP clients."""
        return {
            "port": self.port,
            "pid": self.pid,
            "projectId": self.project_id,
            "instanceId": self.instance_id,
            "kind": self.kind,
            "buildId": self.build_id,
        }


@dataclass(frozen=True, slots=True)
class ToolDescriptor:
    name: str
    title: str | None
    description: str
    category: str
    scopes: tuple[str, ...]
    input_schema: Mapping[str, Any]
    output_schema: Mapping[str, Any] | None
    source: str
    schema_hash: str
    safety: str
    annotations: Mapping[str, Any]
    main_thread: bool
    supports_dry_run: bool
    supports_cancel: bool
    returns_job: bool
    timeout_ms: int
    implemented: bool
    enabled: bool
    valid: bool
    status: str
    package_dependency: str | None = None
    schema_revision: int = 1

    @classmethod
    def from_dict(cls, raw: Mapping[str, Any]) -> ToolDescriptor:
        from .errors import RegistryError

        def boolean(key: str, default: bool) -> bool:
            value = raw.get(key, default)
            if not isinstance(value, bool):
                raise RegistryError(f"Tool {name!r} {key} must be a boolean")
            return value

        name = raw.get("name")
        if not isinstance(name, str) or not _TOOL_NAME.fullmatch(name):
            raise RegistryError(f"Invalid MCP tool name: {name!r}")
        input_schema = raw.get("inputSchema", {"type": "object", "properties": {}})
        output_schema = raw.get("outputSchema")
        if output_schema == {}:
            output_schema = None
        if not isinstance(input_schema, Mapping):
            raise RegistryError(f"Tool {name!r} inputSchema must be an object")
        if output_schema is not None and not isinstance(output_schema, Mapping):
            raise RegistryError(f"Tool {name!r} outputSchema must be an object")
        safety = raw.get("safety", "unsafe")
        if safety not in _SAFETY:
            raise RegistryError(f"Tool {name!r} has invalid safety tier {safety!r}")
        scopes_raw = raw.get("scopes", raw.get("scope", ["editor"]))
        if isinstance(scopes_raw, str):
            scopes_raw = [scopes_raw]
        if not isinstance(scopes_raw, list) or not scopes_raw or any(s not in _SCOPE for s in scopes_raw):
            raise RegistryError(f"Tool {name!r} has invalid scopes")
        annotations = raw.get("annotations", {})
        if not isinstance(annotations, Mapping):
            raise RegistryError(f"Tool {name!r} annotations must be an object")
        for key in ("readOnlyHint", "destructiveHint", "idempotentHint", "openWorldHint"):
            if key in annotations and not isinstance(annotations[key], bool):
                raise RegistryError(f"Tool {name!r} annotation {key} must be a boolean")
        if "title" in annotations and not isinstance(annotations["title"], str):
            raise RegistryError(f"Tool {name!r} annotation title must be a string")
        timeout_ms = raw.get("timeoutMs", 30_000)
        if isinstance(timeout_ms, bool) or not isinstance(timeout_ms, int) or not (100 <= timeout_ms <= 600_000):
            raise RegistryError(f"Tool {name!r} timeoutMs must be between 100 and 600000")
        supplied_hash = raw.get("schemaHash")
        combined_hash = schema_hash({"input": input_schema, "output": output_schema})
        if supplied_hash is not None and (not isinstance(supplied_hash, str) or len(supplied_hash) > 128):
            raise RegistryError(f"Tool {name!r} schemaHash is invalid")
        status_raw = raw.get("status")
        if status_raw is not None and status_raw not in {"planned", "implemented", "invalid"}:
            raise RegistryError(f"Tool {name!r} has invalid status {status_raw!r}")
        implemented = boolean("implemented", status_raw != "planned")
        status = status_raw or ("implemented" if implemented else "planned")
        for key in ("title", "description", "category", "source", "packageDependency"):
            if key in raw and raw[key] is not None and not isinstance(raw[key], str):
                raise RegistryError(f"Tool {name!r} {key} must be a string")
        schema_revision = raw.get("schemaRevision", 1)
        if isinstance(schema_revision, bool) or not isinstance(schema_revision, int) or schema_revision < 1:
            raise RegistryError(f"Tool {name!r} schemaRevision must be a positive integer")
        return cls(
            name=name,
            title=raw.get("title") if isinstance(raw.get("title"), str) else None,
            description=raw.get("description", "") if isinstance(raw.get("description", ""), str) else "",
            category=raw.get("category", "other") if isinstance(raw.get("category", "other"), str) else "other",
            scopes=tuple(dict.fromkeys(scopes_raw)),
            input_schema=_json_copy(input_schema),
            output_schema=_json_copy(output_schema),
            source=raw.get("source", "unity") if isinstance(raw.get("source", "unity"), str) else "unity",
            schema_hash=supplied_hash or combined_hash,
            safety=safety,
            annotations=_json_copy(annotations),
            main_thread=boolean("mainThread", True),
            supports_dry_run=boolean("supportsDryRun", False),
            supports_cancel=boolean("supportsCancel", False),
            returns_job=boolean("returnsJob", False),
            timeout_ms=timeout_ms,
            implemented=implemented,
            enabled=boolean("enabled", False),
            valid=boolean("valid", True),
            status=status,
            package_dependency=raw.get("packageDependency") if isinstance(raw.get("packageDependency"), str) else None,
            schema_revision=schema_revision,
        )

    def is_advertisable(self, kind: str) -> bool:
        scope = "editor" if kind == "editor" else "runtime"
        return self.implemented and self.enabled and self.valid and scope in self.scopes

    def catalog_dict(self) -> dict[str, Any]:
        return {
            "name": self.name,
            "title": self.title,
            "description": self.description,
            "category": self.category,
            "scopes": list(self.scopes),
            "inputSchema": _json_copy(self.input_schema),
            "outputSchema": _json_copy(self.output_schema),
            "source": self.source,
            "schemaHash": self.schema_hash,
            "safety": self.safety,
            "annotations": _json_copy(self.annotations),
            "mainThread": self.main_thread,
            "supportsDryRun": self.supports_dry_run,
            "supportsCancel": self.supports_cancel,
            "returnsJob": self.returns_job,
            "timeoutMs": self.timeout_ms,
            "implemented": self.implemented,
            "enabled": self.enabled,
            "valid": self.valid,
            "status": self.status,
            "packageDependency": self.package_dependency,
            "schemaRevision": self.schema_revision,
        }


@dataclass(frozen=True, slots=True)
class InvalidToolDiagnostic:
    index: int
    name: str | None
    code: str
    message: str

    def catalog_dict(self) -> dict[str, Any]:
        return {
            "index": self.index,
            "name": self.name,
            "code": self.code,
            "message": self.message,
        }


@dataclass(frozen=True, slots=True)
class RegistrySnapshot:
    revision: str
    etag: str | None
    tools: tuple[ToolDescriptor, ...]
    fetched_at: float
    state: str = "ready"
    invalid_tools: tuple[InvalidToolDiagnostic, ...] = ()

    @classmethod
    def empty(cls, *, state: str = "unavailable") -> RegistrySnapshot:
        return cls(revision="", etag=None, tools=(), fetched_at=0.0, state=state)

    def advertised(self, kind: str) -> tuple[ToolDescriptor, ...]:
        return tuple(tool for tool in self.tools if tool.is_advertisable(kind))

    def by_name(self, name: str, kind: str) -> ToolDescriptor | None:
        return next((tool for tool in self.advertised(kind) if tool.name == name), None)
