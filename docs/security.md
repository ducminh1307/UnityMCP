# Security model

`com.ducminh.unity-mcp` relies on the security boundary provided by Unity's
official `com.unity.pipeline` (`1.0.1`) and `unity` CLI (`1.0.1`), paired with
path containment, dry-run/confirmation gates, and clean Player build isolation.

## Local transport and token isolation

- **Loopback-only listener**: `com.unity.pipeline` binds only `http://127.0.0.1:<port>/`,
  rejects non-loopback remote endpoints, and blocks browser `Origin` headers by
  default.
- **OS-protected descriptor**: The running Editor writes
  `<projectRoot>/Library/Pipeline/.unity-pipeline-port` (under git-ignored
  `Library/`) with user-only permissions carrying a 256-bit CSPRNG `evalToken`.
  Only the official `unity` CLI reads `evalToken` to authenticate HTTP requests.
- **No tokens in extension code or client configs**:
  - `UnityCliDiscovery.ReadPipelineStatus` parses `.unity-pipeline-port` with a
    streaming `JsonTextReader` and calls `reader.Skip()` on `evalToken` and all
    non-status properties — tokens are never stored, returned, or logged.
  - `.agents/mcp_config.json`, `.codex/config.toml`, and `.mcp.json` contain only
    the stdio command (`unity mcp --tool-groups --project-path <projectRoot>`),
    with zero bearer secrets or HTTP endpoints.
  - Project config writers (`UnityMcpProjectConfigFile.EnsureSafeProjectPath`)
    canonicalize targets under the project root and reject `ReparsePoint`
    symlink/junction escapes.

## Command effects, dry-run, and confirmations

| `CommandEffect` | Semantics | Controls |
|---|---|---|
| `ReadOnly` | Bounded inspection with no mutation | Marked `readOnlyHint: true` in MCP. |
| `Idempotent` | Repeatable state update | Supports `--dry-run true` where previewable. |
| `Additive` | Creates objects, groups, or checkpoints | Supports `--dry-run true` where previewable. |
| `Destructive` | Overwrites or removes existing state | Requires explicit `--confirm true` unless `--dry-run true` is set (`checkpoint restore`, `particle save_prefab --overwrite true`, `addressables remove_entry`, `addressables build`). |

- **No reserved executor flag shadowing**: Extension commands reject `[CliArg]`
  names `format`, `json`, `fields`, `verbose`, and `omit-nulls` (enforced by
  `tools/validate_catalog.py`) so executor options cannot clash with command flags.
- **No built-in group hijacking**: `tools/validate_catalog.py` asserts that no
  extension group collides with any of `com.unity.pipeline`'s 23 built-in groups.

## Path confinement and Undo safety

- Caller-supplied asset paths are normalized and confined via `ProjectPaths.Resolve`
  or explicit project-root validation; `..` traversal, rooted external paths, and
  UNC paths are rejected with `ExitCode.InvalidUsage`.
- Editor scene mutations use `AuthoringUndoScope` so changes collapse into a single
  Undo step; asset mutations return explicit change/warning output without claiming
  transactional rollback Unity cannot guarantee.
- Output caps bound collection sizes, scene complexity scans, diffs, and capture
  resolutions.

## Release Player isolation

- `DucMinh.UnityMcp.Runtime.asmdef` references only `Unity.Pipeline.Attributes`
  and `Unity.Newtonsoft.Json` — never `Unity.Pipeline` or `UnityEditor`.
- Runtime Pipeline HTTP servers compile into a Player **only** when explicitly
  enabled in **Project Settings > Pipeline > Runtime** (`ENABLE_RUNTIME_PIPELINE_DEV`
  or `ENABLE_RUNTIME_PIPELINE`). Production Release builds with those toggles off
  contain no Pipeline server or command endpoint.
