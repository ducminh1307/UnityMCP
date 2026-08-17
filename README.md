# UnityMCP

UnityMCP connects an MCP client such as Codex, Antigravity, or Claude Code to a
running Unity Editor. It combines a Unity 6 UPM package with a local Python
gateway so an AI client can inspect and operate on live Unity state through a
typed, permission-controlled tool registry.

- Unity is the source of truth for tool discovery, schemas, permissions, and
  execution.
- Only tools implemented and enabled by the connected Editor or Development
  Player are exposed to MCP clients.
- Project-specific C# methods can become MCP tools without adding matching
  Python registrations.
- All communication stays on the local machine. Remote access and telemetry are
  disabled by design.

## Requirements

- Unity 6 (`6000.0` or newer).
- Git, required when installing the Unity package from its Git URL.
- Python 3.11 or newer, required by the gateway.
- A desktop Unity Editor or desktop Development Player running on the same
  machine as the gateway.

Node.js is not required. Runtime bridging is not included in non-development
Player builds.

## Install and connect

UnityMCP has two local components:

| Component | Purpose | Installation |
|---|---|---|
| Unity client | Discovers tools and executes Unity operations | Unity Package Manager (UPM) |
| Python gateway | Presents the Unity tools to an MCP client | Guided setup in the UnityMCP window |

### 1. Install the UnityMCP client with UPM

#### Package Manager UI

1. In Unity, open **Window > Package Management > Package Manager**.
2. Select **+ > Install package from git URL...**.
3. Enter this URL:

   ```text
   https://github.com/ducminh1307/UnityMCP.git?path=/Packages/com.ducminh.unity-mcp
   ```

4. Select **Install** and wait for Unity to finish compiling.

#### Project manifest

Alternatively, add the following dependency to `Packages/manifest.json` in your
Unity project:

```json
{
  "dependencies": {
    "com.ducminh.unity-mcp": "https://github.com/ducminh1307/UnityMCP.git?path=/Packages/com.ducminh.unity-mcp"
  }
}
```

Keep the other dependencies already present in your manifest. For reproducible
builds, append a Git tag or full commit hash after the URL, for example
`#<full-commit-hash>`.

### 2. Install the Python gateway

1. Open **Window > UnityMCP > Tools**.
2. Select **Start gateway**.
3. On a new machine, the Connection page displays **Server not installed**.
   Select **Copy install commands**, run those commands in a terminal, then
   return to Unity and select **Retry after installation**.

UnityMCP only generates and copies the platform-specific commands; it never
downloads or executes them automatically. The guided setup stores both the
source checkout and virtual environment outside the Unity project:

- `UnityMCP/source` under the operating system's local application-data folder.
- `UnityMCP/venv`, containing the `unity-mcp` executable.

For reference, the generated Windows PowerShell setup is equivalent to:

```powershell
git clone --depth 1 --branch main --single-branch 'https://github.com/ducminh1307/UnityMCP.git' "$env:LOCALAPPDATA\UnityMCP\source"
py -3 -m venv "$env:LOCALAPPDATA\UnityMCP\venv"
& "$env:LOCALAPPDATA\UnityMCP\venv\Scripts\python.exe" -m pip install -e "$env:LOCALAPPDATA\UnityMCP\source\server"
```

If `UnityMCP/source` already exists, do not run the clone command again. On
macOS and Linux, the Unity window generates the equivalent commands with
`python3` and `UnityMCP/venv/bin/python` using the correct local data path.

### 3. Connect an MCP client

After the gateway state changes to **Running**, select the action matching your
client:

- **Configure Codex for this project** writes `.codex/config.toml`.
- **Configure Antigravity for this project** writes
  `.agents/mcp_config.json`.
- **Configure Claude for this project** writes `.mcp.json`.

Restart or reload the MCP client after its first configuration. Claude Code may
also ask you to approve the project-scoped MCP server. To confirm the connection,
ask the client to call `unity-status` for the open project.

These actions modify only the current Unity project. Existing unrelated client
settings are preserved, and no global or user-level MCP configuration is
changed. Each action also installs a project-local `unity-mcp` skill so the
client knows when to use the live Unity tools.

> [!IMPORTANT]
> The generated client configuration contains a local bearer token. UnityMCP
> adds the exact file to the repository's local `.git/info/exclude`; do not
> force-add, log, or share that file.

## Connection modes

| Mode | Transport | Started by | Recommended for |
|---|---|---|---|
| Editor-managed | Streamable HTTP on `127.0.0.1` | UnityMCP window | Quick project-scoped setup for Codex, Antigravity, and Claude Code |
| Client-managed | stdio | MCP client | Clients that launch and own local MCP processes |

The setup above uses the Editor-managed mode. Its endpoint is loopback-only,
uses one token and one gateway per open Unity project, and stops when the owning
Unity process exits.

For an advanced client-managed stdio setup, configure the installed command in
your MCP client:

```console
unity-mcp --instance <instance-id>
```

If exactly one Unity instance is running, `--instance` can be omitted. Use
`unity-mcp list-instances` when multiple Editors or Development Players are
open. Stdio is launched and owned by the MCP client; it is not started by the
Unity Editor.

To start Streamable HTTP manually instead:

```console
unity-mcp --transport streamable-http --instance <instance-id> --port 8765 --http-token <local-secret>
```

See the [gateway documentation](server/README.md) for environment variables,
parent-process monitoring, readiness events, and manual configuration details.

## Tools and permissions

A fresh project enables only the 20 built-in `safe-read` tools. All mutating
tools and project-defined tools require explicit local opt-in in **Window >
UnityMCP > Tools**. Mutating tools use dry-run behavior by default and require
`apply: true` when their contract supports applying changes.

The gateway advertises only tools that are implemented, valid, enabled, and in
scope for the connected Unity process. Planned catalog entries are never exposed
as callable MCP tools.

## Custom tools

Import the package's **Custom Tool** sample or follow the
[custom-tool guide](docs/custom-tools.md). A custom tool is a static C# method
marked with `[UnityMcpTool]`; Unity derives its MCP schema and handles discovery.
Custom tools always start disabled and must be enabled locally before a client
can use them.

## Development Player

Runtime bridging is opt-in and limited to desktop Development Builds. Create a
profile from **Assets > Create > UnityMCP > Development Player Runtime Profile**,
enable the server, and select the tools to include before building. Production
Player builds never start the bridge.

## Documentation

- [Architecture](docs/architecture.md) — components, lifecycle, registry, and
  execution model.
- [Protocol](docs/protocol.md) — authenticated Unity bridge protocol.
- [Security](docs/security.md) — trust boundaries, tokens, and local
  configuration.
- [Custom tools](docs/custom-tools.md) — typed project-specific C# tools.
- [Roadmap](docs/roadmap.md) — planned scope and constraints.
- [Tool catalog](docs/tool-catalog.json) — canonical machine-readable catalog.

Validate the catalog from the repository root with:

```console
python tools/validate_catalog.py
```

## Repository layout

```text
UnityMCP/
|-- Packages/com.ducminh.unity-mcp/  # Unity UPM package
|-- server/                           # Python MCP gateway
|-- docs/                             # Architecture and reference docs
`-- tools/                            # Repository validation utilities
```

## License

[MIT](LICENSE)
