# Unity MCP UPM package

This Unity 6 package provides the C# half of UnityMCP: a typed dynamic tool registry,
a bearer-authenticated loopback HTTP bridge, Editor tool enablement, and an opt-in
Development Player bridge. Production Players never start the bridge.

## Editor

Open **Window > UnityMCP > Tools**. Only the 20 built-in `safe-read` tools are enabled
for a fresh project. Mutating and project-defined tools remain disabled until the local
user enables each one. Mutating tools are dry-run by default and require `apply: true`.

The window is implemented with **UI Toolkit**. In addition to the tool-permission
controls, it contains an optional **Editor-managed HTTP gateway** panel. This is a
convenience path for clients that support Streamable HTTP; it does not replace the
default stdio workflow. Stdio remains started and owned by the MCP client, because an
Editor cannot safely own the client's stdin/stdout connection.

Instance descriptors are written to the platform-local application-data directory under
`UnityMCP/instances`. The Python gateway discovers these descriptors and communicates
only over loopback with the generated bearer token.

### Optional Editor-managed HTTP gateway

Open **Window > UnityMCP > Tools** and use the **Editor-managed HTTP gateway** panel:

1. Select **Start gateway**. If the Python gateway is missing, the panel shows
   **Server not installed** and generates commands for the current platform. The
   commands clone the repository to local application data under `UnityMCP/source`,
   create `UnityMCP/venv`, and install the production server package in editable mode.
2. Select **Copy install commands**, run them in a terminal, then select
   **Retry after installation**. Unity never runs the commands or downloads code.
   Git and Python 3.11 or newer must already be available.
3. If you use a custom environment, choose its executable with **Browse**. Otherwise,
   keep the recommended `UnityMCP/venv` executable path. Preferred port and MCP path
   remain available under **Advanced gateway settings**; `/mcp` is the normal path.
4. The panel shows `Starting`, then `Running` only after the
   Python process has bound its endpoint.
5. Select the matching project action for your client: **Configure Codex** writes
   `.codex/config.toml`, **Configure Antigravity** writes `.agents/mcp_config.json`, and
   **Configure Claude** writes `.mcp.json`. Each path is relative to this Unity project;
   UnityMCP never writes the clients' global or user-level config. Existing unrelated
   settings are preserved and the UnityMCP entry stays synchronized when the actual port
   or token changes. The same action installs a managed `unity-mcp` skill at
   `.agents/skills/unity-mcp/SKILL.md` for Codex/Antigravity or
   `.claude/skills/unity-mcp/SKILL.md` for Claude Code. Its trigger directs the client to
   use live UnityMCP tools proactively for Unity tasks without requiring “use MCP” in the
   prompt. Restart/reload the client after the first configuration; Claude Code also asks
   you to approve a project-scoped MCP server.
6. Use **Copy MCP config** only for manual setup. The clipboard
   contains the Streamable HTTP URL and bearer token. Treat that copied value as a
   password: do not commit it, log it, or share it.

These project-scoped configurations are supported for trusted projects. Because their
static `Authorization` headers contain the local bearer token, UnityMCP adds the exact
selected path to the repository's local `.git/info/exclude` when the project is inside
Git. This does not modify the shared `.gitignore`. Do not force-add these files. UnityMCP
refuses to inject a bearer token into a selected config file that is already tracked.
The generated skill contains no token and is not added to Git exclude. UnityMCP only
updates a skill bearing its managed marker and refuses to replace a user-authored
`unity-mcp` skill at the same project path.

The gateway launches only for this exact Editor descriptor and passes an explicit
`--instance` value; it never silently selects another open Unity project. It binds only
to `127.0.0.1`. The preferred port is not a promise: if it is occupied, Unity selects a
different free loopback port and the managed Codex entry uses the actual endpoint.

Each open project has its own local settings, bearer secret, Python child process, tool
registry, and MCP endpoint. Configure a separate MCP connection for each project. Do
not reuse Project A's copied URL/token for Project B.

The token is local per user/project and is kept outside `Assets` and source control. It
is passed to the child process through `UNITY_MCP_HTTP_TOKEN`, not the command line.
The UI does not render it; it is written or copied only by an explicit configuration
action. Select **Stop gateway** before **Regenerate token**, then start the gateway; an
existing UnityMCP-managed Codex, Antigravity, and Claude entries are refreshed automatically.

Unity starts the HTTP gateway with its own process ID as `--parent-pid`. The Python
gateway watches that parent and exits when the Editor exits. Before a domain reload,
Unity keeps its owned child running and reattaches it after the
bridge is ready again. On Editor quit it stops the child permanently, so an old gateway
does not remain attached to a later project session.

## Development Player

Create `Assets/UnityMCP/Resources/UnityMcpRuntimeProfile.asset`, enable the server, and
choose tools in the profile before making a desktop **Development Build**. A generated
runtime manifest and linker metadata preserve typed custom tools for IL2CPP. A normal
release build does not compile or start the bridge bootstrap.

## Custom tools

Import the **Custom Tool** sample or use `custom-tool-scaffold`. A project tool is a
static method marked with `[UnityMcpTool]`; Unity is the schema and discovery source of
truth, so Python does not need a matching registration. Custom tools always begin
disabled, even if their attribute requests `DefaultEnabled`.

Raw `object`, `JObject`, and `JToken` contracts require an explicit
`IUnityMcpSchemaProvider`. Typed DTOs, enums, nullable values, collections, nested DTOs,
and common Unity value types are handled automatically.
