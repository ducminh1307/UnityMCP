"""Transport-neutral Unity gateway behavior used by the MCP adapter and tests."""

from __future__ import annotations

import json
from collections.abc import Mapping
from dataclasses import dataclass
from typing import Any

from .bridge import UnityBridgeClient
from .config import DEFAULT_LIMITS, GatewayLimits
from .errors import BridgeError, SchemaValidationError
from .models import InstanceDescriptor, ToolDescriptor
from .registry import DynamicToolRegistry
from .validation import validate_instance


@dataclass(frozen=True, slots=True)
class ToolCallOutput:
    content: tuple[dict[str, Any], ...]
    structured_content: dict[str, Any] | None
    is_error: bool
    meta: dict[str, Any]


class UnityGatewayService:
    def __init__(
        self,
        descriptor: InstanceDescriptor,
        bridge: UnityBridgeClient,
        registry: DynamicToolRegistry,
        *,
        limits: GatewayLimits = DEFAULT_LIMITS,
    ) -> None:
        self.descriptor = descriptor
        self.bridge = bridge
        self.registry = registry
        self.limits = limits

    async def list_tools(self) -> tuple[ToolDescriptor, ...]:
        snapshot = await self.registry.ensure_loaded()
        return snapshot.advertised(self.descriptor.kind)

    async def call_tool(self, name: str, arguments: Mapping[str, Any] | None) -> ToolCallOutput:
        snapshot = await self.registry.ensure_loaded()
        tool = snapshot.by_name(name, self.descriptor.kind)
        if tool is None:
            raise BridgeError(
                "tool_unavailable", f"Tool {name!r} is not enabled for this Unity instance", status_code=404
            )
        if arguments is None:
            args: dict[str, Any] = {}
        elif isinstance(arguments, Mapping):
            args = dict(arguments)
        else:
            raise SchemaValidationError("Tool arguments must be a JSON object", phase="input")
        validate_instance(args, tool.input_schema, phase="input", limits=self.limits)
        try:
            raw = await self.bridge.call_tool(
                tool.name,
                args,
                snapshot.revision,
                timeout_seconds=tool.timeout_ms / 1000,
            )
        except BridgeError as exc:
            registry_conflicts = {"registry_conflict", "registry_revision_mismatch", "stale_registry"}
            if exc.code not in registry_conflicts:
                raise
            snapshot = await self.registry.refresh(force=True)
            refreshed = snapshot.by_name(name, self.descriptor.kind)
            if refreshed is None:
                raise BridgeError(
                    "tool_unavailable", f"Tool {name!r} became unavailable after the registry changed", status_code=404
                ) from None
            validate_instance(args, refreshed.input_schema, phase="input", limits=self.limits)
            tool = refreshed
            raw = await self.bridge.call_tool(
                tool.name,
                args,
                snapshot.revision,
                timeout_seconds=tool.timeout_ms / 1000,
            )
        return self._normalize_tool_result(tool, raw)

    async def get_job(self, job_id: str) -> dict[str, Any]:
        return await self.bridge.get_job(job_id)

    async def cancel_job(self, job_id: str) -> dict[str, Any]:
        return await self.bridge.cancel_job(job_id)

    def instance_resource(self) -> dict[str, Any]:
        snapshot = self.registry.snapshot
        return {
            **self.descriptor.public_dict(),
            "registryRevision": snapshot.revision,
            "registryState": snapshot.state,
        }

    def tools_resource(self) -> dict[str, Any]:
        snapshot = self.registry.snapshot
        return {
            "instanceId": self.descriptor.instance_id,
            "registryRevision": snapshot.revision,
            "state": snapshot.state,
            "tools": [tool.catalog_dict() for tool in snapshot.tools],
            "invalidTools": [diagnostic.catalog_dict() for diagnostic in snapshot.invalid_tools],
        }

    def _normalize_tool_result(self, tool: ToolDescriptor, raw: Mapping[str, Any]) -> ToolCallOutput:
        is_error = raw.get("isError", False)
        if not isinstance(is_error, bool):
            raise BridgeError("invalid_response", "Unity tool result isError must be a boolean")
        content_raw = raw.get("content", [])
        if not isinstance(content_raw, list) or len(content_raw) > 256:
            raise BridgeError("invalid_response", "Unity tool result content must be a bounded array")
        content: list[dict[str, Any]] = []
        for index, item in enumerate(content_raw):
            if not isinstance(item, Mapping):
                raise BridgeError("invalid_response", f"Unity tool result content[{index}] must be an object")
            item_dict = dict(item)
            if item_dict.get("type") not in {"text", "image", "audio", "resource", "resource_link"}:
                raise BridgeError("invalid_response", f"Unity tool result content[{index}] has an unsupported type")
            content.append(item_dict)
        structured_raw = raw.get("structuredContent")
        if isinstance(structured_raw, Mapping):
            structured = dict(structured_raw)
        elif structured_raw is not None and self._uses_result_wrapper(tool):
            structured = {"result": structured_raw}
        elif structured_raw is not None:
            raise BridgeError("invalid_response", "Unity structuredContent does not match its object output schema")
        else:
            structured = None
        if not is_error and tool.output_schema is not None:
            if structured is None:
                raise BridgeError("invalid_response", f"Unity tool {tool.name!r} omitted promised structuredContent")
            validate_instance(structured, tool.output_schema, phase="output", limits=self.limits)
        if not content:
            if structured is not None:
                text = json.dumps(structured, ensure_ascii=False, sort_keys=True)
            else:
                text = "Unity tool completed without a result."
            content.append({"type": "text", "text": text})
        meta_raw = raw.get("meta", raw.get("_meta", {}))
        if not isinstance(meta_raw, Mapping):
            raise BridgeError("invalid_response", "Unity tool result meta must be an object")
        meta = dict(meta_raw)
        job_id = raw.get("jobId")
        if job_id is not None:
            if not isinstance(job_id, str) or not job_id or len(job_id) > 256:
                raise BridgeError("invalid_response", "Unity tool result jobId is invalid")
            meta["com.ducminh.unity-mcp/jobId"] = job_id
            meta["com.ducminh.unity-mcp/jobUri"] = f"unity://jobs/{job_id}"
        meta["com.ducminh.unity-mcp/instanceId"] = self.descriptor.instance_id
        return ToolCallOutput(tuple(content), structured, is_error, meta)

    @staticmethod
    def _uses_result_wrapper(tool: ToolDescriptor) -> bool:
        schema = tool.output_schema
        if not isinstance(schema, Mapping):
            return False
        properties = schema.get("properties")
        required = schema.get("required")
        return (
            isinstance(properties, Mapping)
            and set(properties) == {"result"}
            and isinstance(required, list)
            and "result" in required
        )
