---
name: unity-mcp
description: Proactively use Unity CLI (when a shell is available) or the project-scoped Unity MCP server (`unity mcp`) for every task or question involving the current Unity project, live Editor state, scenes, GameObjects, components, assets, packages, console, play mode, tests, builds, animation, UI, input, rendering, profiling, or Unity changes and verification. Trigger even when the user does not mention MCP. Prefer Unity CLI or MCP over guessing from files whenever live Unity data or an Editor operation is relevant. Do not trigger for tasks unrelated to Unity.
---

# UnityMCP

<!-- UnityMCP managed project skill. -->

Use Unity's official **Unity CLI** (`unity`) and `com.unity.pipeline` extension commands provided by `com.ducminh.unity-mcp` proactively whenever working on this Unity project.

## Choosing Unity CLI vs MCP

- **When a shell is available**: prefer the **Unity CLI** directly (`unity status`, `unity command <group> <name>`, `unity recompile`, `unity test`, `unity build`). The CLI covers Editor lifecycle, compilation, testing, builds, and all Pipeline + extension commands.
- **When using MCP** (or when no shell is available): use the project-scoped **`unity`** MCP server configured with `unity mcp --tool-groups --project-path <projectRoot>`.

## Workflow

1. Check live Editor / Pipeline readiness first:
   - CLI: `unity status --json`
   - MCP: query the `editor` / `pipeline` status tool on the `unity` server.
2. Discover available commands and arguments dynamically — **never guess command names** (unknown names log `BasePipelineServer:CommandNotFound` errors in the Unity Console):
   - List groups: `unity command`
   - List exact subcommands and flags in a group: `unity command <group>` (e.g., `unity command scene`, `unity command settings`, `unity command component`)
   - Search across CLI + live Editor commands: `unity command --query <term>`
   - Inspect exact flags for any command: `unity command <group> <name> --help`
3. Read before writing: query the narrowest read-only command (`scene list_open`, `scene get_hierarchy`, `gameobject find`, `component get_fields`, `console get`, `asset find`, `ui_ai inspect_ui`, etc.) before mutating anything.
4. Mutate safely:
   - Pass `--dry-run true` first on mutating commands that support previewing changes.
   - Pass `--confirm true` only when the user intends destructive/overwriting actions (`checkpoint restore`, `particle save_prefab --overwrite true`, `addressables remove_entry`, `addressables build`).
5. Verify after code or asset edits:
   - Run `unity recompile` to check compilation errors.
   - Check console logs (`unity command console get --tail 50`) and run relevant tests (`unity test`).
6. Never fabricate results, silently target a different running Unity project, or bypass safety confirmations.
7. Only if `unity` is unavailable, no Editor/Pipeline server is running, or a command errors, state the exact reason and fall back to the safest file-based inspection.

## Built-In `com.unity.pipeline` Command Groups

Use `unity command <group> <name>` (or the matching tool group in `unity mcp --tool-groups`). Run `unity command <group>` to list every exact subcommand before calling an unfamiliar one:

- **Scenes & Objects**:
  - `scene`: `list_open`, `get_hierarchy` (each node includes `instanceId`, `hierarchyPath`, and its `components` list), `open`, `save`, `create`, `set_active`, `add_to_build`, `remove_from_build` *(no `scene list` or `scene hierarchy`)*
  - `gameobject`: `find`, `create`, `create_many`, `delete`, `rename`, `set_active`, `set_layer`, `set_parent`, `set_tag`, `set_transform`, `get_selection`, `set_selection` *(no `gameobject list`)*
  - `component`: `add`, `remove`, `get_fields`, `set_field`, `get_properties`, `set_properties` *(no `component list`; use `scene get_hierarchy` to list a GameObject's components)*
  - `prefab`: `create`, `create_variant`, `instantiate`, `apply_overrides`, `revert_overrides`, `save_contents`, `unpack`
- **Project & Settings**:
  - `settings.*` *(dotted groups — always `settings.<domain> get|set`, never `settings <domain>`)*: `settings.player`, `settings.graphics`, `settings.quality`, `settings.physics`, `settings.audio`, `settings.input`, `settings.time`, `settings.tags_layers`, `settings.runtime_pipeline`, `settings.multiplayer_roles`
  - `package`: `list`, `search`, `status`, `add`, `remove`, `resolve`
- **Editor & Play Mode**: `editor` (`status`, `save_all`, `search`, `focus`, `invoke_menu`, `quit`, `set_auto_tick`), `editor.playmode` (`start`, `stop`, `pause`, `list_instances`, `set_run_in_background`), `pipeline` (`list_commands`, `progress`), `job`, `diagnostics`, `runtime`, `multiplayer.role`
- **Assets & Materials**: `asset` (`find`, `create`, `create_folder`, `copy`, `move`, `rename`, `delete`, `import`, `read_text`, `write_text`, `get_import_settings`, `set_import_settings`), `material`, `authoring`
- **Scripts & Verification**: `script` (`attach`, `create`, `eval`, `run`, `recompile`, `recompile_status`), `script.methodreload`, `console` (`get`, `status`, `clear`, `write`), `test` (`list`, `run`, `status`, `cancel`), `build`, `build.target`, `capture` (`scene_view`, `game_view`, `editor_window`)
- **Animation & Baking**: `animation.clip`, `animation.animator`, `animation.timeline`, `lighting`, `navmesh`, `occlusion`

Top-level CLI commands (also usable without an open Editor or to drive lifecycle):
- `unity status` — list running Editors and Pipeline ports
- `unity recompile` — trigger script compilation and report compiler diagnostics
- `unity test` — run EditMode / PlayMode tests
- `unity build` — execute Unity builds
- `unity command <group> <name> [args...]` — execute any Pipeline or extension command

## `com.ducminh.unity-mcp` Extension Groups (19 Groups)

### Always Available (Editor & Runtime)
- **`particle`**: `create`, `get`, `configure`, `preview` (Runtime + Editor); `save_prefab` (Editor)
- **`physics_ext`**: `get_collision_matrix`, `set_collision_matrix`, `raycast`, `overlap` (Runtime + Editor)
- **`navmesh_ext`**: `calculate_path` (Runtime + Editor)

### Always Available (Editor)
- **`uitoolkit`**: `scan`, `edit_uxml`, `edit_uss`, `scaffold_controller`
- **`ugui`**: `raycast`
- **`scene_ext`**: `validate`, `analyze_complexity`
- **`asset_ext`**: `get_dependencies`, `find_references`
- **`render_ext`**: `get_settings`, `set_settings`, `generate_texture`
- **`visual_ext`**: `capture_gameobject`, `capture_multiview`, `frame_debugger_events`
- **`checkpoint`**: `create`, `list`, `diff`, `restore`

### Conditional / Package-Gated Groups (Editor)
- **`addressables`** (`com.unity.addressables`): `list_groups`, `create_group`, `add_entry`, `remove_entry`, `build`
- **`localization`** (`com.unity.localization`): `list_tables`, `get_entry`, `set_entry`
- **`terrain`** (`com.unity.modules.terrain`): `set_height`, `paint_texture`
- **`sprite`** (`com.unity.2d.sprite`): `slice`
- **`tilemap`** (`com.unity.modules.tilemap`): `set_tiles`
- **`probuilder`** (`com.unity.probuilder`): `create`, `edit`
- **`cinemachine`** (`com.unity.cinemachine`): `create`
- **`vfxgraph`** (`com.unity.visualeffectgraph`): `set`
- **`input_actions`** (`com.unity.inputsystem`): `get`, `create_action`

## Independent Companion Packages

- **`ui_ai`** (`com.ducminh.ui-maker`): Independent standalone package whose optional `DucMinh.UiMaker.Editor.Pipeline` assembly automatically registers `get_ui_catalog`, `rebuild_ui_catalog`, `build_ui_from_json`, `inspect_ui`, `patch_ui_node`, `scaffold_prefab_manifest`, `preview_ui`, `list_ui_screen_specs`, `save_ui_screen_spec`, and `load_ui_screen_spec` when installed alongside `com.unity.pipeline`.
