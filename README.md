# UnityMCP

UnityMCP is a Unity 6 package and Python 3.11+ MCP gateway. Unity owns the live,
typed tool registry; the gateway exposes only tools implemented and enabled by
the connected Editor or desktop Development Player. Project-specific C# methods
can become first-class MCP tools without changing Python code.

The repository is organized as:

- `server/`: Python MCP gateway (stdio by default, loopback Streamable HTTP optional).
- `Packages/com.ducminh.unity-mcp/`: Unity Package Manager package.
- `docs/`: architecture, wire protocol, security model, custom-tool guide, roadmap,
  and the machine-readable 187-tool catalog.
- `tools/`: repository validation utilities.

Start with [the architecture](docs/architecture.md), then read the
[protocol](docs/protocol.md) and [custom-tool guide](docs/custom-tools.md).
The canonical catalog is [docs/tool-catalog.json](docs/tool-catalog.json); validate
it with `python tools/validate_catalog.py`.

## Quick start

Add the local UPM package to a Unity 6 project, open **Window > UnityMCP > Tools**,
then install the Python gateway:

```console
python -m venv .venv
.venv/Scripts/python -m pip install -e "server[dev]"
unity-mcp list-instances
unity-mcp --instance <instance-id>
```

On macOS or Linux, use `.venv/bin/python` instead. Configure the resulting stdio
command in the MCP client. Streamable HTTP is optional and remains loopback-only:

```console
unity-mcp --transport streamable-http --instance <instance-id> --port 8765 --http-token <local-secret>
```

For an Editor-managed HTTP gateway, add `--parent-pid <unity-pid>`. The gateway
then emits `UNITY_MCP_READY {...}` to stderr only after its loopback endpoint is
bound, and exits when that Unity process stops. This flag is HTTP-only; stdio is
still launched and owned by the MCP client.

When that gateway is running, **Configure Codex** creates or updates the trusted
Unity project's `.codex/config.toml`. UnityMCP preserves unrelated Codex settings,
locally excludes the token-bearing file from Git, and keeps its marked MCP entry
synchronized when the gateway port or token changes.

The catalog is source-validated: only contracts with a compiled Unity handler are
marked implemented. The 20 explicitly allowlisted, built-in `safe-read` tools are
enabled in a fresh project. All other tools require local user opt-in, and planned
contracts are never advertised by MCP.

## Requirements

- Unity 6 (`6000.0+`).
- Python 3.11+.
- Desktop Unity Editor or desktop Development Player on the same machine as the
  gateway. Runtime bridging is excluded from non-development builds.

Node.js is not required. Telemetry and remote network access are disabled by design.

The repository CI validates that the 187-tool catalog and C# registrations agree
on the exact source-derived implementation set, then lints, compiles, and tests
the Python gateway. Unity
EditMode and desktop Development Player checks are intended for a licensed Unity CI
runner.
