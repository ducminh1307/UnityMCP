# Unity MCP UPM package

`com.ducminh.unity-mcp` (`v0.2.0`) extends Unity's official **Unity CLI** (`unity`)
and **`com.unity.pipeline`** (`1.0.1`) with **47 extension commands across 19
non-colliding groups**, plus project-scoped MCP/rule/skill setup for Antigravity,
Codex, and Claude Code.

- Unity's `com.unity.pipeline` package owns command discovery (`CommandRegistry`),
  schema generation, loopback HTTP hosting (`127.0.0.1`), and execution.
- The official `unity` CLI handles instance discovery, direct shell execution
  (`unity status`, `unity command`, `unity recompile`, `unity test`, `unity build`),
  and stdio MCP hosting (`unity mcp --tool-groups`).
- No separate Python gateway or bearer-token sidecar is required.

## Editor window (**Window > UnityMCP > Tools**)

The UI Toolkit window provides two tabs:

1. **Unity CLI & MCP**:
   - Detects the `unity` CLI executable and version, and offers copyable install
     commands (`irm https://unity.com/install.ps1 | iex` on Windows;
     `curl -fsSL https://unity.com/install.sh | bash` on macOS/Linux) when missing.
   - Displays live `com.unity.pipeline` server status (Running/Stopped, PID, port)
     from `Library/Pipeline/.unity-pipeline-port` without reading or logging `evalToken`,
     plus shortcuts to **Project Settings > Pipeline > Editor** and **Runtime**.
   - Provides one-click project-scoped client setup:
     - **Antigravity**: writes `.agents/mcp_config.json`, `.agents/skills/unity-mcp/`,
       `.agents/rules/unity-mcp.md`, and the managed context block in `AGENTS.md`.
     - **Codex**: writes `.codex/config.toml`, `.agents/skills/unity-mcp/`, and
       `.agents/rules/unity-mcp.md`.
     - **Claude Code**: writes `.mcp.json`, `.claude/skills/unity-mcp/`, and
       `.claude/rules/unity-mcp.md`.
   - All generated configs launch `unity mcp --tool-groups --project-path <projectRoot>`
     under server key `"unity"` and contain zero bearer tokens or HTTP URLs.
2. **Extension Commands**:
   - Lists non-built-in `[CliGroupCommand]` and `[CliCommand]` commands discovered
     via `TypeCache` across `com.ducminh.unity-mcp`, independent companion packages
     (such as `com.ducminh.ui-maker`), and project scripts (filtered by search query
     or `CommandEffect`), with copyable `unity command <group> <name>` snippets.

## Extension command groups (19 groups, 47 commands)

- **Always available (Runtime + Editor)**: `particle` (`create`, `get`, `configure`,
  `preview`), `physics_ext` (`get_collision_matrix`, `set_collision_matrix`, `raycast`,
  `overlap`), `navmesh_ext` (`calculate_path`).
- **Always available (Editor)**: `particle save_prefab`, `uitoolkit` (`scan`,
  `edit_uxml`, `edit_uss`, `scaffold_controller`), `ugui` (`raycast`), `scene_ext`
  (`validate`, `analyze_complexity`), `asset_ext` (`get_dependencies`, `find_references`),
  `render_ext` (`get_settings`, `set_settings`, `generate_texture`), `visual_ext`
  (`capture_gameobject`, `capture_multiview`, `frame_debugger_events`), `checkpoint`
  (`create`, `list`, `diff`, `restore`).
- **Package-gated via `versionDefines` (Editor)**: `addressables` (5), `localization` (3),
  `terrain` (2), `sprite` (1), `tilemap` (1), `probuilder` (2), `cinemachine` (1),
  `vfxgraph` (1), `input_actions` (2).

### Companion packages

- **`com.ducminh.ui-maker`** is a standalone package with zero dependency on
  `com.ducminh.unity-mcp`. When installed alongside `com.unity.pipeline`, its optional
  `DucMinh.UiMaker.Editor.Pipeline` assembly automatically registers the **`ui_ai`**
  command group (10 declarative uGUI commands), which is discovered automatically by
  `unity command`, `unity mcp --tool-groups`, and the **Extension Commands** tab.

Supported mutating commands expose `--dry-run` (default `false`; pass `--dry-run true`
to preview). Destructive or overwriting commands (`checkpoint restore`,
`particle save_prefab --overwrite true`, `addressables remove_entry`, `addressables build`)
require `--confirm true` unless `--dry-run true` is passed.

## Development Player

Runtime Pipeline availability is configured via **Project Settings > Pipeline > Runtime**
(`ENABLE_RUNTIME_PIPELINE_DEV` / `ENABLE_RUNTIME_PIPELINE`). Because
`DucMinh.UnityMcp.Runtime.asmdef` references only `Unity.Pipeline.Attributes` and
`Unity.Newtonsoft.Json` (never `Unity.Pipeline` or `UnityEditor`), non-development
Release Player builds compile cleanly with no Pipeline server included.

## Custom commands

Import the **Custom Tool** sample (`Samples~/CustomTool/ProjectStatusTool.cs`) or
declare a `static` method with `[CliGroupCommand]` / `[CliCommand]`, `[CliArg]`,
`CommandContext`, and `ExitCode` (or `Task<ExitCode>`). `CommandRegistry` discovers
it automatically after compilation.

- Set `Effect` (`ReadOnly`, `Idempotent`, `Additive`, or `Destructive`).
- Use `IStructuredCommandInput` (from `Unity.Pipeline`) on a DTO for structured
  multi-field arguments.
- Never name a `[CliArg]` `format`, `json`, `fields`, `verbose`, or `omit-nulls`,
  and never reuse any of `com.unity.pipeline`'s 23 built-in group names.
