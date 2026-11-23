---
description: Always prioritize Unity CLI and UnityMCP tools for any tasks interacting with or querying Unity before falling back to other methods.
always_on: true
---

# UnityMCP Project Rules

<!-- UnityMCP managed project rule. -->

Always prioritize **Unity CLI** (when a shell is available) and the project-scoped **`unity` MCP server** (`unity mcp --tool-groups`) for any task, query, or operation involving Unity before attempting any other method.

## Interaction Guidelines

1. **Prioritize UnityMCP First**:
   - **When a shell is available**, run official Unity CLI commands first:
     - `unity status --json` to confirm the running Editor / Pipeline connection.
     - `unity command <group> <name> ...` (and `unity command --query <term>`) to run built-in `com.unity.pipeline` commands and `com.ducminh.unity-mcp` extension commands.
     - `unity recompile`, `unity test`, and `unity build` for compilation, testing, and builds.
   - **When using MCP** (or when shell access is unavailable), invoke the project-scoped `unity` MCP server (`unity mcp --tool-groups --project-path <projectRoot>`).
   - Do not guess live state, hand-parse raw serialized `.unity`/`.prefab`/`.asset` files, or write temporary Editor scripts when a Unity CLI or MCP command can inspect or author them directly.

2. **Source of Truth**:
   - Treat Unity CLI / `unity mcp` results as the single source of truth for live Editor state, active scenes, hierarchy, console messages, compilation status, and test results.

3. **Live Verification**:
   - After modifying C# scripts or project assets, verify effects via `unity recompile`, `unity command console ...`, and `unity test` (or their MCP equivalents).

4. **Fallback Protocol**:
   - **Only** switch to alternative methods (such as reading project source files directly or giving manual instructions) if:
     - No Unity Editor or `com.unity.pipeline` server is running or reachable.
     - The `unity` CLI or `unity mcp` server is not installed or unavailable.
     - The Unity CLI or MCP call returns an error or lacks the required command.
   - When falling back, explicitly inform the user why the fallback method is being used.

5. **Safe Execution**:
   - Inspect target state first using read-only commands.
   - Preview mutations with `--dry-run true` when supported before applying changes.
   - Require explicit confirmation (`--confirm true`) for destructive operations (`checkpoint restore`, `particle save_prefab --overwrite true`, `addressables remove_entry`, `addressables build`).

6. **Available Command Surfaces**:
   - **Built-in `com.unity.pipeline` groups**: `editor`, `editor.playmode`, `scene`, `gameobject`, `component`, `asset`, `prefab`, `material`, `script`, `console`, `test`, `build`, `capture`, `lighting`, `navmesh`, `occlusion`, `animation.*`, `authoring`, `settings`, `package`, `pipeline`, `job`, `diagnostics`, `runtime`.
   - **`com.ducminh.unity-mcp` extension groups (19)**: `particle`, `physics_ext`, `navmesh_ext`, `uitoolkit`, `ugui`, `scene_ext`, `asset_ext`, `render_ext`, `visual_ext`, `checkpoint`, plus optional package groups (`addressables`, `localization`, `terrain`, `sprite`, `tilemap`, `probuilder`, `cinemachine`, `vfxgraph`, `input_actions`). Independent companion packages such as `com.ducminh.ui-maker` also register their own Pipeline groups (`ui_ai`) automatically when installed.
