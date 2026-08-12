"""Static bearer authentication for the loopback Streamable HTTP transport."""

from __future__ import annotations

import os
import secrets
from collections.abc import Mapping

from mcp.server.auth.provider import AccessToken

from .errors import ConfigurationError

HTTP_TOKEN_ENV = "UNITY_MCP_HTTP_TOKEN"
HTTP_AUTH_SCOPE = "unity-mcp"


def resolve_http_token(explicit: str | None, environ: Mapping[str, str] | None = None) -> str:
    """Resolve and validate the operator-configured MCP-facing bearer token."""
    source = os.environ if environ is None else environ
    token = explicit if explicit is not None else source.get(HTTP_TOKEN_ENV)
    if token is None or token == "":
        raise ConfigurationError(
            "Streamable HTTP requires --http-token or the UNITY_MCP_HTTP_TOKEN environment variable"
        )
    if len(token) < 32 or len(token) > 512 or any(ord(character) < 33 or ord(character) > 126 for character in token):
        raise ConfigurationError("Streamable HTTP bearer token must contain 32-512 visible ASCII characters")
    return token


class StaticTokenVerifier:
    """Constant-time verifier for a single, explicitly provisioned local token."""

    def __init__(self, token: str) -> None:
        self._token = token

    async def verify_token(self, token: str) -> AccessToken | None:
        if not secrets.compare_digest(token, self._token):
            return None
        return AccessToken(
            token=token,
            client_id="unity-mcp-local-client",
            scopes=[HTTP_AUTH_SCOPE],
            subject="local-user",
        )
