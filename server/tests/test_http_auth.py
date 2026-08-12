from __future__ import annotations

import pytest

from unity_mcp_server.cli import run
from unity_mcp_server.errors import ConfigurationError
from unity_mcp_server.http_auth import HTTP_AUTH_SCOPE, StaticTokenVerifier, resolve_http_token


def test_streamable_http_requires_explicit_token_before_descriptor_discovery(monkeypatch) -> None:
    monkeypatch.delenv("UNITY_MCP_HTTP_TOKEN", raising=False)

    with pytest.raises(ConfigurationError, match="requires --http-token"):
        run(["--transport", "streamable-http"])


def test_http_token_environment_and_explicit_precedence() -> None:
    environment_token = "environment-http-token-" * 2
    explicit_token = "explicit-http-token-value-" * 2

    assert resolve_http_token(None, {"UNITY_MCP_HTTP_TOKEN": environment_token}) == environment_token
    assert resolve_http_token(explicit_token, {"UNITY_MCP_HTTP_TOKEN": environment_token}) == explicit_token

    with pytest.raises(ConfigurationError, match="32-512"):
        resolve_http_token("too-short", {})


@pytest.mark.asyncio
async def test_static_token_verifier_rejects_wrong_token() -> None:
    token = "static-verifier-token-" * 2
    verifier = StaticTokenVerifier(token)

    assert await verifier.verify_token("wrong-token-which-is-long-enough-123") is None
    accepted = await verifier.verify_token(token)
    assert accepted is not None
    assert accepted.scopes == [HTTP_AUTH_SCOPE]
