"""JSON/JSON-Schema validation with bounded, model-readable failures."""

from __future__ import annotations

import json
from collections.abc import Mapping, Sequence
from typing import Any

from jsonschema import Draft202012Validator, FormatChecker
from jsonschema.exceptions import SchemaError, ValidationError

from .config import DEFAULT_LIMITS, GatewayLimits
from .errors import RegistryError, SchemaValidationError
from .models import canonical_json


def _path(parts: Sequence[Any]) -> str:
    result = "$"
    for part in parts:
        result += f"[{part}]" if isinstance(part, int) else f".{part}"
    return result


def json_depth(value: Any, *, limit: int, _depth: int = 0) -> int:
    if _depth > limit:
        raise ValueError(f"JSON nesting exceeds limit {limit}")
    if isinstance(value, Mapping):
        return max([_depth, *(json_depth(v, limit=limit, _depth=_depth + 1) for v in value.values())])
    if isinstance(value, list):
        return max([_depth, *(json_depth(v, limit=limit, _depth=_depth + 1) for v in value)])
    return _depth


def check_schema(
    schema: Mapping[str, Any], *, tool_name: str, phase: str, limits: GatewayLimits = DEFAULT_LIMITS
) -> None:
    try:
        root_type = schema.get("type")
        if root_type != "object":
            raise RegistryError(f"Tool {tool_name!r} {phase} schema root type must be 'object'")
        size = len(canonical_json(schema).encode("utf-8"))
        if size > limits.max_schema_bytes:
            raise RegistryError(f"Tool {tool_name!r} {phase} schema exceeds {limits.max_schema_bytes} bytes")
        json_depth(schema, limit=limits.max_schema_depth)
        _reject_external_references(schema)
        Draft202012Validator.check_schema(schema)
    except RegistryError:
        raise
    except (SchemaError, ValueError, TypeError) as exc:
        raise RegistryError(f"Tool {tool_name!r} has invalid {phase} schema: {exc}") from None


def _reject_external_references(value: Any) -> None:
    if isinstance(value, Mapping):
        reference = value.get("$ref")
        if reference is not None and (not isinstance(reference, str) or not reference.startswith("#")):
            raise RegistryError("External JSON Schema references are not allowed")
        for child in value.values():
            _reject_external_references(child)
    elif isinstance(value, list):
        for child in value:
            _reject_external_references(child)


def validate_instance(
    value: Any,
    schema: Mapping[str, Any],
    *,
    phase: str,
    limits: GatewayLimits = DEFAULT_LIMITS,
) -> None:
    try:
        encoded = json.dumps(value, ensure_ascii=False, allow_nan=False, separators=(",", ":")).encode("utf-8")
        max_bytes = limits.max_request_bytes if phase == "input" else limits.max_tool_result_bytes
        if len(encoded) > max_bytes:
            raise SchemaValidationError(f"{phase.capitalize()} JSON exceeds {max_bytes} bytes", phase=phase)
        json_depth(value, limit=limits.max_json_depth)
        validator = Draft202012Validator(schema, format_checker=FormatChecker())
        error = next(iter(validator.iter_errors(value)), None)
    except SchemaValidationError:
        raise
    except (TypeError, ValueError) as exc:
        raise SchemaValidationError(f"{phase.capitalize()} is not bounded valid JSON: {exc}", phase=phase) from None
    if isinstance(error, ValidationError):
        raise SchemaValidationError(
            error.message,
            path=_path(list(error.absolute_path)),
            schema_path=_path(list(error.absolute_schema_path)),
            phase=phase,
        )
