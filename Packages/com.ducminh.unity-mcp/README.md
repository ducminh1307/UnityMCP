# Unity MCP UPM package

This Unity 6 package provides the C# half of UnityMCP: a typed dynamic tool registry,
a bearer-authenticated loopback HTTP bridge, Editor tool enablement, and an opt-in
Development Player bridge. Production Players never start the bridge.

## Editor

Open **Window > UnityMCP > Tools**. Only the 20 built-in `safe-read` tools are enabled
for a fresh project. Mutating and project-defined tools remain disabled until the local
user enables each one. Mutating tools are dry-run by default and require `apply: true`.

Instance descriptors are written to the platform-local application-data directory under
`UnityMCP/instances`. The Python gateway discovers these descriptors and communicates
only over loopback with the generated bearer token.

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
