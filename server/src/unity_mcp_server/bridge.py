"""Authenticated asynchronous client for the Unity loopback bridge."""

from __future__ import annotations

import json
from dataclasses import dataclass
from typing import Any
from urllib.parse import quote

import httpx

from .config import DEFAULT_LIMITS, GatewayLimits
from .errors import BridgeError
from .models import InstanceDescriptor


@dataclass(frozen=True, slots=True)
class RegistryHttpResult:
    not_modified: bool
    etag: str | None
    payload: dict[str, Any] | None


def _reject_duplicate_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON key {key!r}")
        result[key] = value
    return result


def _reject_non_json_constant(value: str) -> None:
    raise ValueError(f"invalid JSON constant {value!r}")


class UnityBridgeClient:
    """One client is permanently pinned to one descriptor/loopback port."""

    def __init__(
        self,
        descriptor: InstanceDescriptor,
        *,
        client: httpx.AsyncClient | None = None,
        limits: GatewayLimits = DEFAULT_LIMITS,
    ) -> None:
        self.descriptor = descriptor
        self.limits = limits
        self._owns_client = client is None
        self._base_headers = {
            "Authorization": f"Bearer {descriptor.token}",
            "Accept": "application/json",
            "User-Agent": "unity-mcp-server/0.1.0",
            "Host": f"127.0.0.1:{descriptor.port}",
            "X-UnityMCP-Instance": descriptor.instance_id,
        }
        timeout = httpx.Timeout(limits.default_timeout_seconds, connect=limits.connect_timeout_seconds)
        self._client = client or httpx.AsyncClient(
            base_url=f"http://127.0.0.1:{descriptor.port}",
            timeout=timeout,
            follow_redirects=False,
            trust_env=False,
            limits=httpx.Limits(max_connections=8, max_keepalive_connections=4),
            headers=self._base_headers,
        )

    async def aclose(self) -> None:
        if self._owns_client:
            await self._client.aclose()

    async def __aenter__(self) -> UnityBridgeClient:
        return self

    async def __aexit__(self, *_: object) -> None:
        await self.aclose()

    async def health(self) -> dict[str, Any]:
        response = await self._request("GET", "/api/v1/health", max_bytes=64 * 1024)
        data = self._decode_json(response)
        if not isinstance(data, dict):
            raise BridgeError("invalid_response", "Unity health response must be a JSON object")
        return data

    async def verify_instance(self) -> None:
        """Reject a stale descriptor that resolves to a different Unity bridge."""
        data = await self.health()
        remote = data.get("instance")
        if not isinstance(remote, dict):
            raise BridgeError("descriptor_mismatch", "Unity health response omitted instance identity")
        expected = self.descriptor.public_dict()
        for key in ("port", "pid", "projectId", "instanceId", "kind", "buildId"):
            if remote.get(key) != expected[key]:
                raise BridgeError("descriptor_mismatch", "Unity bridge identity does not match its descriptor")

    async def fetch_tools(self, etag: str | None = None) -> RegistryHttpResult:
        headers = {"If-None-Match": etag} if etag else None
        response = await self._request(
            "GET",
            "/api/v1/tools",
            headers=headers,
            max_bytes=self.limits.max_registry_bytes,
            allowed_statuses={200, 304},
        )
        response_etag = response.headers.get("ETag")
        if response_etag is not None and len(response_etag) > 512:
            raise BridgeError("invalid_response", "Unity registry ETag exceeds the gateway limit")
        if response.status_code == 304:
            return RegistryHttpResult(True, response_etag or etag, None)
        data = self._decode_json(response)
        if not isinstance(data, dict):
            raise BridgeError("invalid_response", "Unity registry response must be a JSON object")
        return RegistryHttpResult(False, response_etag, data)

    async def call_tool(
        self,
        name: str,
        arguments: dict[str, Any],
        registry_revision: str,
        *,
        timeout_seconds: float | None = None,
    ) -> dict[str, Any]:
        body = {"arguments": arguments, "registryRevision": registry_revision}
        response = await self._request(
            "POST",
            f"/api/v1/tools/{quote(name, safe='')}/call",
            json_body=body,
            max_bytes=self.limits.max_tool_result_bytes,
            timeout=timeout_seconds,
        )
        data = self._decode_json(response)
        if not isinstance(data, dict):
            raise BridgeError("invalid_response", "Unity tool response must be a JSON object")
        return data

    async def get_job(self, job_id: str) -> dict[str, Any]:
        self._check_identifier(job_id, "job id")
        response = await self._request(
            "GET", f"/api/v1/jobs/{quote(job_id, safe='')}", max_bytes=self.limits.max_job_result_bytes
        )
        data = self._decode_json(response)
        if not isinstance(data, dict):
            raise BridgeError("invalid_response", "Unity job response must be a JSON object")
        return data

    async def cancel_job(self, job_id: str) -> dict[str, Any]:
        self._check_identifier(job_id, "job id")
        response = await self._request(
            "DELETE", f"/api/v1/jobs/{quote(job_id, safe='')}", max_bytes=256 * 1024
        )
        if response.status_code == 204 or not response.content:
            return {"jobId": job_id, "cancelled": True}
        data = self._decode_json(response)
        if not isinstance(data, dict):
            raise BridgeError("invalid_response", "Unity job cancellation response must be a JSON object")
        return data

    @staticmethod
    def _check_identifier(value: str, label: str) -> None:
        if not isinstance(value, str) or not value or len(value) > 256 or any(ord(c) < 32 for c in value):
            raise BridgeError("invalid_request", f"Invalid {label}")

    async def _request(
        self,
        method: str,
        path: str,
        *,
        headers: dict[str, str] | None = None,
        json_body: Any = None,
        max_bytes: int,
        timeout: float | None = None,
        allowed_statuses: set[int] | None = None,
    ) -> httpx.Response:
        if not path.startswith("/api/v1/") or ".." in path:
            raise BridgeError("invalid_request", "Refusing an invalid bridge path")
        if json_body is not None:
            try:
                encoded = json.dumps(
                    json_body, ensure_ascii=False, allow_nan=False, separators=(",", ":")
                ).encode("utf-8")
            except (TypeError, ValueError) as exc:
                raise BridgeError("invalid_request", f"Bridge request is not valid JSON: {exc}") from None
            if len(encoded) > self.limits.max_request_bytes:
                raise BridgeError("payload_too_large", "Bridge request exceeds the gateway size limit", status_code=413)
            content: bytes | None = encoded
            request_headers = {**self._base_headers, **(headers or {}), "Content-Type": "application/json"}
        else:
            content = None
            request_headers = {**self._base_headers, **(headers or {})}
        request_timeout = None
        if timeout is not None:
            request_timeout = httpx.Timeout(
                min(max(timeout, 0.1), 600.0), connect=self.limits.connect_timeout_seconds
            )
        try:
            request_kwargs: dict[str, Any] = {"headers": request_headers, "content": content}
            if request_timeout is not None:
                request_kwargs["timeout"] = request_timeout
            request = self._client.build_request(method, path, **request_kwargs)
            if request.url.host != "127.0.0.1" or request.url.port != self.descriptor.port:
                raise BridgeError("invalid_request", "Refusing to send the Unity token outside its loopback port")
            response = await self._client.send(request, stream=True, follow_redirects=False)
        except httpx.TimeoutException:
            raise BridgeError("timeout", "Unity bridge timed out", retryable=True) from None
        except httpx.RequestError:
            raise BridgeError("target_unavailable", "Unity bridge is unavailable", retryable=True) from None
        try:
            declared_length = response.headers.get("Content-Length")
            if declared_length:
                try:
                    length = int(declared_length)
                    if length < 0:
                        raise BridgeError("invalid_response", "Unity returned an invalid Content-Length")
                    if length > max_bytes:
                        raise BridgeError("response_too_large", "Unity response exceeds the gateway size limit")
                except ValueError:
                    raise BridgeError("invalid_response", "Unity returned an invalid Content-Length") from None
            received = bytearray()
            async for chunk in response.aiter_bytes():
                if len(received) + len(chunk) > max_bytes:
                    raise BridgeError("response_too_large", "Unity response exceeds the gateway size limit")
                received.extend(chunk)
            status_code = response.status_code
            response_headers = response.headers
            response_request = response.request
            response_extensions = response.extensions
            await response.aclose()
            response = httpx.Response(
                status_code,
                headers=response_headers,
                content=bytes(received),
                request=response_request,
                extensions=response_extensions,
            )
        except httpx.TimeoutException:
            await response.aclose()
            raise BridgeError("timeout", "Unity bridge timed out", retryable=True) from None
        except httpx.RequestError:
            await response.aclose()
            raise BridgeError("target_unavailable", "Unity bridge is unavailable", retryable=True) from None
        except BaseException:
            await response.aclose()
            raise
        if allowed_statuses and response.status_code in allowed_statuses:
            return response
        if 200 <= response.status_code < 300:
            return response
        raise self._map_http_error(response)

    def _decode_json(self, response: httpx.Response) -> Any:
        content_type = response.headers.get("Content-Type", "").lower()
        if response.content and "json" not in content_type:
            raise BridgeError("invalid_response", "Unity response is not JSON")
        try:
            return json.loads(
                response.content.decode("utf-8"),
                object_pairs_hook=_reject_duplicate_keys,
                parse_constant=_reject_non_json_constant,
            )
        except (UnicodeError, json.JSONDecodeError, ValueError):
            raise BridgeError("invalid_response", "Unity returned malformed JSON") from None

    def _map_http_error(self, response: httpx.Response) -> BridgeError:
        status = response.status_code
        code = {
            400: "invalid_request",
            401: "bridge_unauthorized",
            403: "bridge_forbidden",
            404: "not_found",
            409: "registry_conflict",
            413: "payload_too_large",
            422: "validation_error",
            429: "rate_limited",
            503: "target_reloading",
        }.get(status, "bridge_error" if status < 500 else "target_failure")
        retryable = status in {408, 409, 425, 429, 502, 503, 504}
        message = {
            401: "Unity bridge authentication failed",
            403: "Unity bridge refused the request",
            404: "Unity resource was not found",
            409: "Unity registry changed; refresh tools and retry",
            413: "Unity rejected an oversized payload",
            429: "Unity bridge is rate limited",
            503: "Unity target is reloading",
        }.get(status, f"Unity bridge returned HTTP {status}")
        details = None
        try:
            payload = self._decode_json(response)
            envelope = payload.get("error", payload) if isinstance(payload, dict) else {}
            if isinstance(envelope, dict):
                remote_code = envelope.get("code")
                remote_message = envelope.get("message")
                if not isinstance(remote_code, str):
                    meta = envelope.get("meta")
                    if isinstance(meta, dict):
                        remote_code = meta.get("errorCode")
                if not isinstance(remote_message, str):
                    content = envelope.get("content")
                    if isinstance(content, list) and content and isinstance(content[0], dict):
                        remote_message = content[0].get("text")
                if isinstance(remote_code, str) and 0 < len(remote_code) <= 128:
                    code = remote_code
                if isinstance(remote_message, str) and 0 < len(remote_message) <= 1024:
                    message = remote_message
                if isinstance(envelope.get("retryable"), bool):
                    retryable = envelope["retryable"]
                remote_details = envelope.get("details")
                if isinstance(remote_details, (dict, list, str, int, float, bool)) or remote_details is None:
                    details = remote_details
        except BridgeError:
            pass
        return BridgeError(code, message, status_code=status, retryable=retryable, details=details)
