# Unity MCP Python gateway

`unity-mcp-server` is the local MCP-facing half of UnityMCP. It discovers one
running Unity Editor or desktop Development Player, mirrors that instance's
dynamic tool registry, validates JSON Schema at both boundaries, and proxies
tool and job calls over an authenticated loopback HTTP bridge.

## Requirements

- Python 3.11+
- Unity 6 with `com.ducminh.unity-mcp`
- No Node.js dependency

Install from this directory:

```console
python -m pip install -e .
```

Run over stdio (default):

```console
unity-mcp --instance <instance-id>
```

Or expose MCP Streamable HTTP on loopback only:

```console
unity-mcp --transport streamable-http --instance <instance-id> --port 8765 --http-token <local-secret>
```

`UNITY_MCP_HTTP_TOKEN` may supply the 32-512 character token without placing it
in process arguments. Every request must send `Authorization: Bearer <token>`.
Streamable HTTP refuses to start without an explicit token; it never creates a
random fallback that the client cannot discover.

### Unity-managed Streamable HTTP

An Editor window may launch an HTTP gateway as a child process. Pass the
launching Unity process ID so the gateway stops itself if that Editor or
Development Player exits:

```console
unity-mcp --transport streamable-http --instance <instance-id> --port 8765 --http-token <local-secret> --parent-pid <unity-pid>
```

`--parent-pid` is deliberately limited to `streamable-http`; stdio gateways
remain owned by the MCP client that launches them. Before discovering a Unity
instance, the gateway verifies that the supplied PID is live. It checks again
every 0.5 seconds and exits after three consecutive failed checks. Stateful HTTP
sessions which receive no traffic for 30 minutes are removed from the server's
session table.

Once Uvicorn has bound the loopback port, the HTTP gateway emits exactly one
machine-readable readiness line to **stderr** (never stdout):

```text
UNITY_MCP_READY {"endpoint":"http://127.0.0.1:8765/mcp","mcpPath":"/mcp","parentPid":1234,"pid":5678,"port":8765,"transport":"streamable-http"}
```

The token is never included in lifecycle events. A Unity launcher can wait for
this line before showing the endpoint as ready. If the watched parent exits,
the gateway writes `UNITY_MCP_PARENT_EXITED {"parentPid":1234}` to stderr and
then shuts down.

If exactly one live descriptor exists, `--instance` may be omitted. When two
or more Unity instances are available, selection is deliberately refused.
Use `unity-mcp list-instances` to inspect candidates. `UNITY_MCP_DESCRIPTOR_DIR`
can override descriptor discovery for CI or advanced local setups.

The MCP endpoint is `/mcp`. The gateway also exposes `unity://instance`,
`unity://tools`, and `unity://jobs/{jobId}` resources. Only tools that Unity
marks implemented, enabled, and valid for the selected instance are advertised.

### Token-conscious tool profiles

Large Unity projects can expose many enabled tools and produce large tool
results. To reduce MCP context size for API-backed clients, the gateway can
apply an extra allowlist before advertising or calling tools:

```console
UNITY_MCP_TOOL_PROFILE=minimal unity-mcp --instance <instance-id>
UNITY_MCP_TOOL_PROFILE=diagnostics unity-mcp --instance <instance-id>
UNITY_MCP_ALLOWED_TOOLS=unity-status,compile-status,compile-errors unity-mcp --instance <instance-id>
```

`default` preserves Unity's local enablement exactly. `minimal` exposes only
basic status, project, compilation, and Console reads. `diagnostics` adds common
scene, object, asset, package, and Console diagnostic reads. An explicit
`UNITY_MCP_ALLOWED_TOOLS` comma-separated list takes precedence over profiles.

### Local telemetry for tool cost analysis

UnityMCP can write local JSONL telemetry that estimates context pressure by
recording tool names, request bytes, response bytes, content bytes, structured
bytes, duration, error status, and truncation flags. It does not send telemetry
anywhere. For manually launched gateways, enable it explicitly:

```console
UNITY_MCP_TELEMETRY_PATH=Temp/unity-mcp-telemetry.jsonl unity-mcp --instance <instance-id>
```

Or set `UNITY_MCP_TELEMETRY=1` to write `unity-mcp-telemetry.jsonl` in the
gateway working directory. Summarize the largest tool payloads from the
repository root:

```console
python tools/analyze_mcp_telemetry.py Temp/unity-mcp-telemetry.jsonl
```

When the Unity Editor starts the gateway from **Window > UnityMCP > Tools**, it
sets `UNITY_MCP_TELEMETRY_PATH` automatically to the current project's
`Temp/UnityMcpTelemetry.jsonl`. Use the window's **Analytics** tab to inspect
that per-project file directly inside Unity.
