# Security model

UnityMCP controls a powerful local development environment. Security is enforced
at both the Python boundary and the Unity execution boundary; neither side trusts
the other to be the only check.

## Local transport

- MCP Streamable HTTP and the Unity bridge bind `127.0.0.1` only. Stdio is the
  default MCP transport.
- Streamable HTTP additionally requires a caller-supplied bearer token; the
  gateway refuses to start that transport without one.
- The Editor-managed HTTP mode uses a separate MCP bearer secret from the Unity
  bridge descriptor token. The secret is generated locally per user/project and
  must never be placed in a Unity asset, repository, issue, screenshot, or log.
- Each Unity process creates a cryptographically random bearer token with at least
  256 bits of entropy. It lives only in a user-readable descriptor and memory.
- Unity validates peer address, bearer token, and exact loopback Host. Requests
  never follow redirects and the gateway never accepts a bridge base URL from an
  MCP argument.
- Descriptor identity is cross-checked with `/api/v1/health`; stale PID reuse or a
  mismatched `instanceId` invalidates discovery.

Loopback authentication prevents accidental cross-process access; it does not
defend against malware already executing as the same OS user.

## Editor-managed HTTP gateway secrets and lifecycle

The **Window > UnityMCP > Tools** UI Toolkit panel stores its gateway executable
path, preferred port, MCP path, and HTTP bearer secret in local per-user/per-project
editor preferences, outside `Assets` and version control. These preferences are
convenience storage, not a defense against another process running as the same user.

The token is never supplied on the Python command line. Unity passes it through the
`UNITY_MCP_HTTP_TOKEN` child-process environment variable, and the Python readiness
and parent-exit events contain no token. The UI does not display the secret in a text
field. Explicit client actions write it only to the trusted project's
`.codex/config.toml` (Codex), `.agents/mcp_config.json` (Antigravity), or `.mcp.json`
(Claude Code). UnityMCP never changes a global or user-level client config. In Git
repositories it adds the selected exact path to local `.git/info/exclude` without
changing shared ignore rules. An explicit **Copy MCP config** action places configuration
containing the `Authorization: Bearer <token>` header on the clipboard. Never force-add
the generated files, and remove the header before sharing client configuration or
diagnostics.

Client setup also writes an instruction-only project skill to
`.agents/skills/unity-mcp/SKILL.md` for Codex and Antigravity or
`.claude/skills/unity-mcp/SKILL.md` for Claude Code. These skill files contain no endpoint
or bearer token and no global skill location is touched. A managed marker permits safe
refreshes; an existing unmarked skill at the same path is never overwritten.

The gateway can be stopped and its token regenerated only while stopped. Rotation
invalidates every copied client configuration that used the old value. After the gateway
starts again, UnityMCP automatically refreshes previously managed project-scoped Codex,
Antigravity, and Claude Code entries. Other MCP clients need a replacement copied
configuration. The actual TCP port
may differ from the preferred port when another local gateway is already using that port,
so clients must use the generated endpoint rather than guessing a port.

Editor-managed HTTP processes receive `--parent-pid` for the exact Unity Editor that
started them. The Python gateway validates that PID before starting and watches it
thereafter; it exits when the parent is no longer live. Unity keeps the child it owns
across a domain reload and reattaches that verified child after its bridge is ready
again; it terminates the child permanently on Editor quit. This lifecycle isolation prevents a
gateway from silently surviving as an unowned endpoint, but it is not a substitute for
the bearer token or for keeping MCP connections scoped to the intended project.

## Permissions and safety tiers

| Tier | Meaning | Fresh-project policy |
|---|---|---|
| `safe-read` | Bounded observation with no Unity mutation | Enabled only for the explicit built-in allowlist |
| `write` | Creates or modifies state | Disabled |
| `destructive` | Deletes, reverts, clears, or stops state | Disabled |
| `unsafe` | Code/reflection/package/build/input or broad side effects | Disabled |

Only the 20 allowlisted built-in safe-read tools in the catalog are enabled by
default. Safety metadata alone never grants default access. All project custom
tools start disabled. Enablement is local per user and project; the Unity UI is
the authority, and no MCP tool can enable another tool. Runtime enablement is a
reviewed profile baked into a desktop Development Build.

Reflection, code execution, package/build mutation, and input simulation remain
unsafe even when arguments appear read-only. Package-dependent tools are absent
or invalid when their exact package dependency is unavailable.

`execute-csharp` is not an evaluator or compiler. It can invoke only one exact
public static project method named in a developer-created local
`UnityMcpCSharpCommandAllowlist` asset; the caller cannot supply C# source, a type,
or a method name. The reviewed project method remains the trust boundary, so the
tool is still `unsafe`, disabled by default, and does not invoke project code during
dry-run validation.

## Mutation controls

- Mutation contracts preview by default and require explicit `apply: true` where
  dry-run is supported.
- Inputs are validated against the exact advertised schema before dispatch. The
  bridge repeats authorization, scope, revision, and enabled-state checks.
- Scene writes form Unity Undo groups. Asset writes return an honest change journal
  rather than promising rollback that Unity cannot guarantee.
- Script scaffolding and asset paths are canonicalized and must remain below an
  allowed project directory. Reject `..`, symlink/junction escapes, unexpected
  absolute paths, and device/UNC paths.
- Execution time, main-thread queue size, recursion, object count, image dimensions,
  and request/result sizes are bounded. Cancellation is cooperative and advertised
  only where implemented.

## Registry and schema integrity

Unity owns the live registry. Python advertises only entries reported as
implemented, enabled, valid, and in target scope. Calls include the registry
revision, preventing time-of-check/time-of-use execution after schemas change.
Input and structured output are both JSON-Schema validated. Unknown formats do
not silently coerce values.

During a domain reload, calls fail retryably. A last-known registry may be retained
for display for 30 seconds, then target tools are removed. There is no static
fallback to planned catalog entries.

## Logging and data handling

Telemetry is off and no cloud service is contacted. Audit records contain time,
instance, tool, outcome, duration, safety tier, dry-run/apply state, and bounded
change metadata. They redact bearer tokens, authorization headers, environment
secrets, source payloads, binary/image data, and sensitive path components.
Client-facing errors are stable and sanitized; local debug logging may retain a
bounded stack trace but never credentials.

Production Player builds must neither include an active listener nor advertise a
descriptor. Release CI must verify this boundary for Mono and IL2CPP on every
supported desktop platform.
