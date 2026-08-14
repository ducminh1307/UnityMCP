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

Add the UPM package to a Unity 6 project and open **Window > UnityMCP > Tools**.
Select **Start gateway**. On a new machine, the Connection page reports **Server not
installed** and provides platform-specific commands that you can copy and run in a
terminal. Unity never downloads or executes the installation commands itself.

The guided installation keeps the source and virtual environment outside the Unity
project under the platform's local application-data directory:

- `UnityMCP/source`: a shallow checkout of this repository's `main` branch.
- `UnityMCP/venv`: the Python environment containing the `unity-mcp` executable.

For example, the generated Windows PowerShell commands are:

```powershell
git clone --depth 1 --branch main --single-branch 'https://github.com/ducminh1307/UnityMCP.git' "$env:LOCALAPPDATA\UnityMCP\source"
py -3 -m venv "$env:LOCALAPPDATA\UnityMCP\venv"
& "$env:LOCALAPPDATA\UnityMCP\venv\Scripts\python.exe" -m pip install -e "$env:LOCALAPPDATA\UnityMCP\source\server"
```

If `UnityMCP/source` already exists, skip the clone command. On macOS or Linux the
Editor generates the equivalent `python3` commands using its exact local data path
and `UnityMCP/venv/bin/python`. After installation, select **Retry after installation**.

For the default stdio workflow, configure the installed `unity-mcp` command in the
MCP client. Streamable HTTP is optional and remains loopback-only:

```console
unity-mcp --transport streamable-http --instance <instance-id> --port 8765 --http-token <local-secret>
```

For an Editor-managed HTTP gateway, add `--parent-pid <unity-pid>`. The gateway
then emits `UNITY_MCP_READY {...}` to stderr only after its loopback endpoint is
bound, and exits when that Unity process stops. This flag is HTTP-only; stdio is
still launched and owned by the MCP client.

When that gateway is running, choose the project configuration action for Codex,
Antigravity, or Claude Code. UnityMCP writes only inside the current Unity project:
`.codex/config.toml`, `.agents/mcp_config.json`, or `.mcp.json`, respectively. It
preserves unrelated settings, locally excludes each token-bearing file from Git, and
keeps configured UnityMCP entries synchronized when the gateway port or token changes.
Each action also installs an instruction-only `unity-mcp` skill inside the current
project so the client proactively uses live UnityMCP tools for Unity work even when the
prompt does not mention MCP. Codex and Antigravity use
`.agents/skills/unity-mcp/SKILL.md`; Claude Code uses
`.claude/skills/unity-mcp/SKILL.md`. No global or user-level MCP configuration or skill
is modified.

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
