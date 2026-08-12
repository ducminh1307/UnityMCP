"""Atomic, ETag-aware projection of Unity's dynamic tool registry."""

from __future__ import annotations

import asyncio
import inspect
import time
from collections.abc import Awaitable, Callable, Mapping
from contextlib import suppress
from typing import Any

from .bridge import UnityBridgeClient
from .config import DEFAULT_LIMITS, GatewayLimits
from .errors import BridgeError, RegistryError
from .models import InvalidToolDiagnostic, RegistrySnapshot, ToolDescriptor, canonical_json
from .validation import check_schema

ChangeCallback = Callable[[RegistrySnapshot], Awaitable[None] | None]


class DynamicToolRegistry:
    def __init__(
        self,
        bridge: UnityBridgeClient,
        *,
        limits: GatewayLimits = DEFAULT_LIMITS,
        clock: Callable[[], float] = time.monotonic,
    ) -> None:
        self.bridge = bridge
        self.limits = limits
        self._clock = clock
        self._snapshot = RegistrySnapshot.empty()
        self._refresh_lock = asyncio.Lock()
        self._callbacks: list[ChangeCallback] = []
        self._identity_verified = False
        self._last_attempt = 0.0
        self._last_success = 0.0

    @property
    def snapshot(self) -> RegistrySnapshot:
        return self._snapshot

    def on_change(self, callback: ChangeCallback) -> None:
        self._callbacks.append(callback)

    async def ensure_loaded(self) -> RegistrySnapshot:
        if self._snapshot.fetched_at == 0:
            return await self.refresh(force=True)
        return await self.refresh(force=False)

    async def refresh(self, *, force: bool = False) -> RegistrySnapshot:
        now = self._clock()
        if not force and now - self._last_attempt < self.limits.registry_min_refresh_seconds:
            return self._snapshot
        async with self._refresh_lock:
            now = self._clock()
            if not force and now - self._last_attempt < self.limits.registry_min_refresh_seconds:
                return self._snapshot
            self._last_attempt = now
            previous = self._snapshot
            try:
                if not self._identity_verified:
                    await self.bridge.verify_instance()
                    self._identity_verified = True
                result = await self.bridge.fetch_tools(previous.etag)
                if result.not_modified:
                    if previous.fetched_at == 0:
                        raise RegistryError("Unity returned 304 before a registry snapshot existed")
                    ready_state = "ready_with_invalid_tools" if previous.invalid_tools else "ready"
                    current = RegistrySnapshot(
                        previous.revision,
                        result.etag or previous.etag,
                        previous.tools,
                        now,
                        ready_state,
                        previous.invalid_tools,
                    )
                else:
                    if result.payload is None:
                        raise RegistryError("Unity returned a registry response without a payload")
                    current = self._parse_registry(result.payload, result.etag, now)
                self._last_success = now
            except (BridgeError, RegistryError) as exc:
                code = exc.code if isinstance(exc, BridgeError) else "invalid_registry"
                current = self._offline_snapshot(previous, now, code)
            changed = self._fingerprint(current) != self._fingerprint(previous)
            self._snapshot = current  # atomic pointer swap after complete validation
        if changed:
            await self._notify(current)
        return current

    def _parse_registry(self, payload: Mapping[str, Any], etag: str | None, now: float) -> RegistrySnapshot:
        revision = payload.get("registryRevision")
        raw_tools = payload.get("tools")
        if isinstance(revision, bool) or not isinstance(revision, (str, int)):
            raise RegistryError("Unity registryRevision must be a string or integer")
        revision_string = str(revision)
        if not revision_string or len(revision_string) > 256:
            raise RegistryError("Unity registryRevision is invalid")
        if not isinstance(raw_tools, list):
            raise RegistryError("Unity registry tools must be an array")
        if len(raw_tools) > self.limits.max_tools:
            raise RegistryError(f"Unity registry exceeds {self.limits.max_tools} tools")
        parsed_by_name: dict[str, ToolDescriptor] = {}
        seen_names: dict[str, int] = {}
        invalid_names: set[str] = set()
        diagnostics: list[InvalidToolDiagnostic] = []
        for index, raw in enumerate(raw_tools):
            name = raw.get("name") if isinstance(raw, Mapping) and isinstance(raw.get("name"), str) else None
            if name is not None and name in seen_names:
                parsed_by_name.pop(name, None)
                invalid_names.add(name)
                diagnostics.append(
                    InvalidToolDiagnostic(
                        index=index,
                        name=name[:128],
                        code="duplicate_tool_name",
                        message=f"Duplicate tool name {name!r}; all descriptors with this name were quarantined"[
                            :1024
                        ],
                    )
                )
                continue
            if name is not None:
                seen_names[name] = index
            try:
                if not isinstance(raw, Mapping):
                    raise RegistryError("Tool descriptor must be an object")
                tool = ToolDescriptor.from_dict(raw)
                if tool.name in invalid_names:
                    raise RegistryError(f"Tool name {tool.name!r} was already quarantined")
                check_schema(tool.input_schema, tool_name=tool.name, phase="input", limits=self.limits)
                if tool.output_schema is not None:
                    check_schema(tool.output_schema, tool_name=tool.name, phase="output", limits=self.limits)
                parsed_by_name[tool.name] = tool
            except RegistryError as exc:
                diagnostics.append(
                    InvalidToolDiagnostic(
                        index=index,
                        name=name[:128] if name else None,
                        code="invalid_tool_descriptor",
                        message=str(exc)[:1024],
                    )
                )
        parsed = list(parsed_by_name.values())
        parsed.sort(key=lambda item: item.name)
        state = "ready" if not diagnostics else "ready_with_invalid_tools"
        return RegistrySnapshot(revision_string, etag, tuple(parsed), now, state, tuple(diagnostics))

    def _offline_snapshot(self, previous: RegistrySnapshot, now: float, state: str) -> RegistrySnapshot:
        if previous.fetched_at and now - self._last_success <= self.limits.reload_grace_seconds:
            return RegistrySnapshot(
                previous.revision,
                previous.etag,
                previous.tools,
                previous.fetched_at,
                state,
                previous.invalid_tools,
            )
        status_only = tuple(tool for tool in previous.tools if tool.name == "unity-status")
        return RegistrySnapshot(
            previous.revision,
            None,
            status_only,
            previous.fetched_at,
            "unavailable",
            previous.invalid_tools,
        )

    @staticmethod
    def _fingerprint(snapshot: RegistrySnapshot) -> str:
        return canonical_json(
            {
                "revision": snapshot.revision,
                "tools": [tool.catalog_dict() for tool in snapshot.tools],
                "invalidTools": [diagnostic.catalog_dict() for diagnostic in snapshot.invalid_tools],
            }
        )

    async def _notify(self, snapshot: RegistrySnapshot) -> None:
        for callback in tuple(self._callbacks):
            try:
                result = callback(snapshot)
                if inspect.isawaitable(result):
                    await result
            except Exception:
                # A client notification must never roll back a valid registry swap.
                continue

    async def poll(self, stop: asyncio.Event) -> None:
        while not stop.is_set():
            await self.refresh(force=True)
            with suppress(TimeoutError):
                await asyncio.wait_for(stop.wait(), timeout=self.limits.registry_poll_seconds)
