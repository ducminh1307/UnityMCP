"""Stable error vocabulary shared by bridge, registry, and MCP adapters."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Any


class UnityMcpError(Exception):
    """Base exception for gateway failures."""


class ConfigurationError(UnityMcpError):
    """Invalid or ambiguous local configuration."""


class DescriptorError(ConfigurationError):
    """A Unity instance descriptor is invalid."""


class InstanceNotFoundError(ConfigurationError):
    """No matching live Unity instance exists."""


class AmbiguousInstanceError(ConfigurationError):
    """More than one live instance exists and none was selected."""


@dataclass(slots=True)
class BridgeError(UnityMcpError):
    """A typed, sanitized error returned by or while reaching Unity."""

    code: str
    message: str
    status_code: int | None = None
    retryable: bool = False
    details: Any = None

    def __str__(self) -> str:
        return self.message


class RegistryError(UnityMcpError):
    """Unity returned an invalid registry."""


class SchemaValidationError(UnityMcpError):
    """Arguments or structured output do not satisfy a tool schema."""

    def __init__(self, message: str, *, path: str = "$", schema_path: str = "$", phase: str = "input") -> None:
        super().__init__(message)
        self.path = path
        self.schema_path = schema_path
        self.phase = phase

