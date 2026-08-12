"""MCP Python SDK v2 low-level adapter for Unity's dynamic schemas."""

from __future__ import annotations

import asyncio
import json
from contextlib import asynccontextmanager
from typing import Any
from urllib.parse import unquote, urlsplit

import mcp_types as types
from mcp.server import Server, ServerRequestContext
from mcp.server.lowlevel.server import NotificationOptions
from mcp.server.subscriptions import InMemorySubscriptionBus, ListenHandler, ToolsListChanged
from mcp.shared.exceptions import MCPError

from .errors import BridgeError, SchemaValidationError
from .models import RegistrySnapshot, ToolDescriptor
from .service import ToolCallOutput, UnityGatewayService


class UnityMcpServer(Server[Any]):
    """Server whose dynamic-list capabilities are identical across transports."""

    def create_initialization_options(
        self,
        notification_options: NotificationOptions | None = None,
        experimental_capabilities: dict[str, dict[str, Any]] | None = None,
        extensions: dict[str, dict[str, Any]] | None = None,
    ):
        supplied = notification_options or NotificationOptions()
        dynamic_options = NotificationOptions(
            prompts_changed=supplied.prompts_changed,
            resources_changed=True,
            tools_changed=True,
        )
        return super().create_initialization_options(
            notification_options=dynamic_options,
            experimental_capabilities=experimental_capabilities,
            extensions=extensions,
        )


def _tool_annotations(tool: ToolDescriptor) -> types.ToolAnnotations:
    raw = dict(tool.annotations)
    if tool.title and "title" not in raw:
        raw["title"] = tool.title
    raw.setdefault("readOnlyHint", tool.safety == "safe-read")
    raw.setdefault("destructiveHint", tool.safety in {"destructive", "unsafe"})
    allowed = {"title", "readOnlyHint", "destructiveHint", "idempotentHint", "openWorldHint"}
    return types.ToolAnnotations.model_validate({key: value for key, value in raw.items() if key in allowed})


def _mcp_tool(tool: ToolDescriptor) -> types.Tool:
    kwargs: dict[str, Any] = {
        "name": tool.name,
        "title": tool.title,
        "description": tool.description,
        "input_schema": dict(tool.input_schema),
        "annotations": _tool_annotations(tool),
        "_meta": {
            "com.ducminh.unity-mcp/category": tool.category,
            "com.ducminh.unity-mcp/source": tool.source,
            "com.ducminh.unity-mcp/safety": tool.safety,
            "com.ducminh.unity-mcp/schemaHash": tool.schema_hash,
            "com.ducminh.unity-mcp/supportsDryRun": tool.supports_dry_run,
            "com.ducminh.unity-mcp/returnsJob": tool.returns_job,
        },
    }
    if tool.output_schema is not None:
        kwargs["output_schema"] = dict(tool.output_schema)
    return types.Tool(**kwargs)


def _content_block(raw: dict[str, Any]) -> Any:
    block_type = raw.get("type")
    block_types = {
        "text": types.TextContent,
        "image": types.ImageContent,
        "audio": types.AudioContent,
        "resource": types.EmbeddedResource,
        "resource_link": types.ResourceLink,
    }
    model = block_types.get(block_type)
    if model is None:
        raise BridgeError("invalid_response", f"Unity returned unsupported MCP content type {block_type!r}")
    return model.model_validate(raw)


def _tool_error(code: str, message: str, **meta: Any) -> types.CallToolResult:
    return types.CallToolResult(
        content=[types.TextContent(type="text", text=f"{code}: {message}")],
        is_error=True,
        _meta={"com.ducminh.unity-mcp/errorCode": code, **meta},
    )


def _result(output: ToolCallOutput) -> types.CallToolResult:
    return types.CallToolResult(
        content=[_content_block(dict(item)) for item in output.content],
        structured_content=output.structured_content,
        is_error=output.is_error,
        _meta=output.meta or None,
    )


def create_mcp_server(service: UnityGatewayService) -> Server[Any]:
    """Build one server bound to exactly one Unity instance."""
    bus = InMemorySubscriptionBus()
    listen_handler = ListenHandler(bus, max_subscriptions=128, max_buffered_events=64)
    legacy_sessions: dict[int, Any] = {}

    def remember(ctx: ServerRequestContext[Any]) -> None:
        version = str(getattr(ctx, "protocol_version", "") or "")
        if not version.startswith("2026-"):
            legacy_sessions[id(ctx.session)] = ctx.session

    async def list_tools(
        ctx: ServerRequestContext[Any], params: types.PaginatedRequestParams | None
    ) -> types.ListToolsResult:
        remember(ctx)
        tools = await service.list_tools()
        return types.ListToolsResult(tools=[_mcp_tool(tool) for tool in tools])

    async def call_tool(
        ctx: ServerRequestContext[Any], params: types.CallToolRequestParams
    ) -> types.CallToolResult:
        remember(ctx)
        try:
            return _result(await service.call_tool(params.name, params.arguments))
        except SchemaValidationError as exc:
            return _tool_error(
                "schema_validation_failed",
                str(exc),
                **{
                    "com.ducminh.unity-mcp/phase": exc.phase,
                    "com.ducminh.unity-mcp/path": exc.path,
                    "com.ducminh.unity-mcp/schemaPath": exc.schema_path,
                },
            )
        except BridgeError as exc:
            return _tool_error(
                exc.code,
                exc.message,
                **{
                    "com.ducminh.unity-mcp/retryable": exc.retryable,
                    "com.ducminh.unity-mcp/statusCode": exc.status_code,
                },
            )
        except Exception:
            return _tool_error("gateway_error", "Unity gateway could not complete the tool call")

    async def list_resources(
        ctx: ServerRequestContext[Any], params: types.PaginatedRequestParams | None
    ) -> types.ListResourcesResult:
        remember(ctx)
        instance_id = service.descriptor.instance_id
        return types.ListResourcesResult(
            resources=[
                types.Resource(
                    uri="unity://instance",
                    name="Unity instance",
                    description=f"Non-secret metadata for Unity instance {instance_id}",
                    mime_type="application/json",
                ),
                types.Resource(
                    uri="unity://tools",
                    name="Unity tool catalog",
                    description="Full enabled/disabled/planned tool catalog reported by Unity",
                    mime_type="application/json",
                ),
            ]
        )

    async def list_resource_templates(
        ctx: ServerRequestContext[Any], params: types.PaginatedRequestParams | None
    ) -> types.ListResourceTemplatesResult:
        remember(ctx)
        return types.ListResourceTemplatesResult(
            resource_templates=[
                types.ResourceTemplate(
                    uri_template="unity://jobs/{jobId}",
                    name="Unity job",
                    description="Current status/result for a Unity long-running job",
                    mime_type="application/json",
                )
            ]
        )

    async def read_resource(
        ctx: ServerRequestContext[Any], params: types.ReadResourceRequestParams
    ) -> types.ReadResourceResult:
        remember(ctx)
        uri = str(params.uri)
        if uri == "unity://instance":
            await service.registry.ensure_loaded()
            payload = service.instance_resource()
        elif uri == "unity://tools":
            await service.registry.ensure_loaded()
            payload = service.tools_resource()
        else:
            parsed = urlsplit(uri)
            if parsed.scheme != "unity" or parsed.netloc != "jobs" or not parsed.path.startswith("/"):
                raise MCPError(types.INVALID_PARAMS, "Unknown UnityMCP resource URI")
            job_id = unquote(parsed.path[1:])
            if not job_id or "/" in job_id or parsed.query or parsed.fragment:
                raise MCPError(types.INVALID_PARAMS, "Invalid Unity job resource URI")
            try:
                payload = await service.get_job(job_id)
            except BridgeError as exc:
                raise MCPError(types.INVALID_PARAMS, exc.message, data={"code": exc.code}) from None
        try:
            text = json.dumps(
                payload, ensure_ascii=False, sort_keys=True, allow_nan=False, separators=(",", ":")
            )
        except (TypeError, ValueError):
            raise MCPError(types.INTERNAL_ERROR, "Unity returned a resource that is not valid JSON") from None
        return types.ReadResourceResult(
            contents=[types.TextResourceContents(uri=params.uri, text=text, mime_type="application/json")]
        )

    async def changed(_: RegistrySnapshot) -> None:
        await bus.publish(ToolsListChanged())
        dead: list[int] = []
        for session_id, session in tuple(legacy_sessions.items()):
            try:
                await session.send_tool_list_changed()
                await session.send_resource_list_changed()
            except Exception:
                dead.append(session_id)
        for session_id in dead:
            legacy_sessions.pop(session_id, None)

    service.registry.on_change(changed)

    @asynccontextmanager
    async def lifespan(_: Server[Any]):
        stop = asyncio.Event()
        poll_task = asyncio.create_task(service.registry.poll(stop), name="unity-mcp-registry-poll")
        try:
            yield service
        finally:
            stop.set()
            await poll_task
            listen_handler.close()
            await service.bridge.aclose()

    return UnityMcpServer(
        "unity-mcp",
        version="0.1.1",
        title="UnityMCP",
        description="Dynamic MCP gateway for one Unity Editor or Development Player",
        instructions=(
            "Only tools implemented and enabled in the connected Unity instance are listed. "
            "Mutation tools normally require apply=true; inspect dry-run output first when supported."
        ),
        lifespan=lifespan,
        on_list_tools=list_tools,
        on_call_tool=call_tool,
        on_list_resources=list_resources,
        on_list_resource_templates=list_resource_templates,
        on_read_resource=read_resource,
        on_subscriptions_listen=listen_handler,
    )
