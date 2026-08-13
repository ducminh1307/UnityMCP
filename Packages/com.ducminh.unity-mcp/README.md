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

Install the Python gateway first, then open **Window > UnityMCP > Tools** and use the
**Editor-managed HTTP gateway** panel:

1. Confirm the **Gateway executable** path (the default points to the recommended
   `UnityMCP/venv` location) or use **Browse**.
2. Set a preferred loopback port and MCP path; `/mcp` is the normal path.
3. Select **Start gateway**. The panel shows `Starting`, then `Running` only after the
   Python process has bound its endpoint.
4. Select **Configure Codex** to create or update this project's `.codex/config.toml`.
   UnityMCP preserves unrelated settings and keeps its marked server entry synchronized
   when the actual port or token changes. Restart Codex after the first configuration.
5. Use **Copy MCP config** only for another MCP client or manual setup. The clipboard
   contains the Streamable HTTP URL and bearer token. Treat that copied value as a
   password: do not commit it, log it, or share it.

The project-scoped Codex configuration is supported for trusted projects. Because its
static `Authorization` header contains the local bearer token, UnityMCP adds the exact
`.codex/config.toml` path to the repository's local `.git/info/exclude` when the project
is inside Git. This does not modify the shared `.gitignore`. Do not force-add the file.
UnityMCP refuses to inject a bearer token if that config file is already tracked.

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
existing UnityMCP-managed Codex entry is refreshed automatically.

Unity starts the HTTP gateway with its own process ID as `--parent-pid`. The Python
gateway watches that parent and exits when the Editor exits. Before a domain reload,
Unity stops its owned child and, if it was running, starts a fresh child after the
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
