# UnityMCP & UI Maker

This repository hosts two decoupled, production-ready Unity 6 (`6000.0+`) UPM packages built around Unity's official **Unity CLI** (`unity`) and **`com.unity.pipeline`** (`1.0.1`):

1. **[`com.ducminh.unity-mcp`](Packages/com.ducminh.unity-mcp/README.md) (`v0.2.0`)** — Extends `com.unity.pipeline` with **47 Unity CLI / Pipeline extension commands across 19 non-colliding groups** plus one-click project-scoped MCP/skill/rule configuration for Antigravity, Codex, and Claude Code.
2. **[`com.ducminh.ui-maker`](Packages/com.ducminh.ui-maker/README.md) (`v1.0.0`)** — A **100% standalone** declarative uGUI authoring system featuring a `SceneView` Overlay palette, `UI Maker Hub` (`Window > UI Maker`), `UIPrefabManifest` contracts, `UICatalogRegistry`, offscreen visual review, bundled AI Skills, and an optional `com.unity.pipeline` bridge (`ui_ai`, 10 commands) with zero dependency on `com.ducminh.unity-mcp`.

### Highlights

- Built directly on `com.unity.pipeline` (`[CliGroupCommand]`, `[CliArg]`, `CommandContext`, `ExitCode`, `CommandEffect`) and `unity` CLI — no custom Python gateway or bearer-token sidecar required.
- Use **Unity CLI** directly from a shell (`unity status`, `unity command`, `unity recompile`, `unity test`, `unity build`) or connect any MCP client via `unity mcp --tool-groups --project-path <projectRoot>`.
- Both packages are completely independent: install either package on its own, or install both together in the same Unity project.
- All Pipeline HTTP communication stays on `127.0.0.1` loopback and is managed by `com.unity.pipeline` and the `unity` CLI.

## Requirements

- Unity 6 (`6000.0` or newer).
- Official **Unity CLI** (`unity` `1.0.1` or newer, required for CLI / MCP workflows; optional when using `com.ducminh.ui-maker` purely via Unity Editor UI):
  - Windows PowerShell: `irm https://unity.com/install.ps1 | iex`
  - macOS / Linux: `curl -fsSL https://unity.com/install.sh | bash`
- Git, when installing the UPM packages from Git URLs.

Node.js and a separate Python gateway are **not** required for runtime or Editor operation. Python 3.10+ is only used by optional repository validation scripts in `tools/` and `Packages/com.ducminh.ui-maker/AI~/Skills/`.

## Install and Connect

### 1. Install `com.ducminh.unity-mcp` via UPM

#### Package Manager UI

1. In Unity, open **Window > Package Management > Package Manager**.
2. Select **+ > Install package from git URL...**.
3. Enter:

   ```text
   https://github.com/ducminh1307/UnityMCP.git?path=/Packages/com.ducminh.unity-mcp
   ```

4. Select **Install** and wait for Unity to resolve `com.unity.pipeline` (`1.0.1`) and compile.

#### Project manifest

Alternatively, add one or both packages to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.ducminh.unity-mcp": "https://github.com/ducminh1307/UnityMCP.git?path=/Packages/com.ducminh.unity-mcp",
    "com.ducminh.ui-maker": "https://github.com/ducminh1307/UnityMCP.git?path=/Packages/com.ducminh.ui-maker"
  }
}
```

### 2. Configure your AI client from Unity (`com.ducminh.unity-mcp`)

Open **Window > UnityMCP > Tools** inside the Unity Editor. The window provides two focused tabs:

1. **Unity CLI & MCP**:
   - Displays `unity` CLI detection status & version (with one-click copyable install commands if missing).
   - Shows live `com.unity.pipeline` server status (Running/Stopped, port, PID) with shortcuts to **Project Settings > Pipeline > Editor** and **Runtime**.
   - Provides one-click project-scoped configuration for:
     - **Antigravity**: writes `.agents/mcp_config.json`, `.agents/skills/unity-mcp/`, `.agents/rules/unity-mcp.md`, and the managed context block in `AGENTS.md`.
     - **Codex**: writes `.codex/config.toml`, `.agents/skills/unity-mcp/`, and `.agents/rules/unity-mcp.md`.
     - **Claude Code**: writes `.mcp.json`, `.claude/skills/unity-mcp/`, and `.claude/rules/unity-mcp.md`.
2. **Extension Commands**: searchable catalog of discovered `[CliGroupCommand]` and `[CliCommand]` commands outside `Unity.Pipeline.*` (including effect badges, copyable `unity command <group> <name>` snippets, and automatic `TypeCache` discovery of `ui_ai` when `com.ducminh.ui-maker` is installed).

All generated client configs use stdio `unity mcp --tool-groups` and contain **no** bearer tokens or HTTP URLs:

```json
{
  "mcpServers": {
    "unity": {
      "command": "unity",
      "args": [
        "mcp",
        "--tool-groups",
        "--project-path",
        "/path/to/UnityProject"
      ]
    }
  }
}
```

You can also run `unity mcp configure <client>` from a terminal (`unity mcp configure --list` shows supported global client integrations).

### 3. Standalone Declarative UI Authoring (`com.ducminh.ui-maker` `v1.0.0`)

`com.ducminh.ui-maker` works 100% standalone in any Unity 6 project (with or without `com.ducminh.unity-mcp`):

- **Editor UI**:
  - **UI Maker Hub**: **Window > UI Maker** (or **Tools > UI Maker > UI Maker Hub**) — manage `Assets/UIMaker/ui_catalog.json` (with automatic read fallback to legacy `Assets/UnityMCP/ui_catalog.json`), `Assets/UIMaker/UICatalogRegistry.asset`, `AIMetadataConfig`, auto-scaffolding, and saved screen specs (`Assets/UI/Specs/*.ui.json`).
  - **SceneView Overlay**: enable **UI Maker** in `SceneView` to drag-and-drop Base Prefabs onto any `Canvas` or `PrefabStage`, apply quick layout presets, scaffold manifests, save specs, or open **Tools > UI Maker > UI Preview Window**.
  - **Bundled AI Skills**: run **Tools > UI Maker > Install UI Maker AI Skills** to install `ui-maker` and `ui-maker-manifest-authoring` into `.agents/skills`, `.agent/skills`, `.claude/skills`, and `.gemini/skills`.
- **Optional `com.unity.pipeline` Bridge (`DucMinh.UiMaker.Editor.Pipeline`)**:
  - Automatically activates via `versionDefines` whenever `com.unity.pipeline` is present, exposing all **10 `ui_ai` commands** (`get_ui_catalog`, `rebuild_ui_catalog`, `build_ui_from_json`, `inspect_ui`, `patch_ui_node`, `scaffold_prefab_manifest`, `preview_ui`, `list_ui_screen_specs`, `save_ui_screen_spec`, `load_ui_screen_spec`).
- Full documentation: [UI Maker README](Packages/com.ducminh.ui-maker/README.md) and [UI Maker Standalone Guide](Packages/com.ducminh.ui-maker/Documentation~/UI_MAKER_GUIDE.md).

## Unity CLI & Extension Commands

Verify a running Editor and inspect available commands directly:

```bash
unity status --format json
unity command --project-path /path/to/UnityProject
unity command --project-path /path/to/UnityProject --query particle
unity command --project-path /path/to/UnityProject checkpoint list --help
unity command --project-path /path/to/UnityProject ui_ai preview_ui --help
```

`com.ducminh.unity-mcp` adds **47 commands across 19 non-colliding groups** on top of `com.unity.pipeline`'s 23 built-in groups:

- **Always available (Runtime + Editor)**: `particle` (`create`, `get`, `configure`, `preview`), `physics_ext` (`get_collision_matrix`, `set_collision_matrix`, `raycast`, `overlap`), `navmesh_ext` (`calculate_path`).
- **Always available (Editor)**: `particle save_prefab`, `uitoolkit` (4), `ugui` (1), `scene_ext` (2), `asset_ext` (2), `render_ext` (3), `visual_ext` (3), `checkpoint` (4).
- **Package-gated via `versionDefines` (Editor)**: `addressables` (5), `localization` (3), `terrain` (2), `sprite` (1), `tilemap` (1), `probuilder` (2), `cinemachine` (1), `vfxgraph` (1), `input_actions` (2).
- **Companion standalone package (`com.ducminh.ui-maker` `v1.0.0`, Editor)**: adds `ui_ai` (10 commands) independently whenever both `com.ducminh.ui-maker` and `com.unity.pipeline` are installed.

See the complete [Extension command reference](docs/tools.md) or the canonical [JSON catalog](docs/tool-catalog.json).

## Mutation Safety & Dry-Run Contract

- Every command declares `CommandEffect` (`ReadOnly`, `Idempotent`, `Additive`, or `Destructive`), which `unity mcp` surfaces as MCP tool hints.
- Supported mutating commands expose `--dry-run` (default `false`); pass `--dry-run true` to validate preconditions and preview changes without mutating the scene or disk.
- Destructive or overwriting commands (`checkpoint restore`, `particle save_prefab --overwrite true`, `addressables remove_entry`, `addressables build`) require explicit `--confirm true` unless `--dry-run true` is passed.
- Editor scene mutations use `AuthoringUndoScope` / Undo groups for single-step Undo, and asset paths are validated against project roots.

## Custom Project Commands

Import the **Custom Tool** sample (`Samples~/CustomTool/ProjectStatusTool.cs`) or follow [Custom commands](docs/custom-tools.md). Declare any static C# method with `[CliGroupCommand]` and `CommandContext` — `CommandRegistry` discovers it automatically on recompile with no registration boilerplate.

## Development Player

Runtime Pipeline execution is controlled entirely by **Project Settings > Pipeline > Runtime** (`ENABLE_RUNTIME_PIPELINE_DEV` / `ENABLE_RUNTIME_PIPELINE`). Because `DucMinh.UnityMcp.Runtime.asmdef` references only `Unity.Pipeline.Attributes` and `Unity.Newtonsoft.Json`, non-development Release builds never pull in the `Unity.Pipeline` HTTP server or Roslyn interpreter. Likewise, `DucMinh.UiMaker.Runtime.asmdef` has zero external assembly references and supports all Player platforms including WebGL.

## Documentation

- [Architecture](docs/architecture.md) — Unity CLI, `com.unity.pipeline`, assembly boundaries, and Editor window.
- [Security](docs/security.md) — loopback Pipeline boundary, token-free configs, and mutation guards.
- [Custom commands](docs/custom-tools.md) — authoring `[CliGroupCommand]` commands in Runtime or Editor.
- [Extension command reference](docs/tools.md) — generated index of all 47 `com.ducminh.unity-mcp` extension commands across 19 groups.
- [Tool catalog](docs/tool-catalog.json) — canonical machine-readable extension catalog for `com.ducminh.unity-mcp`.
- [UI Maker Package README](Packages/com.ducminh.ui-maker/README.md) & [UI Maker Guide](Packages/com.ducminh.ui-maker/Documentation~/UI_MAKER_GUIDE.md) — standalone `com.ducminh.ui-maker` (`v1.0.0`) workflows, schemas, and `ui_ai` commands.
- [UI Maker Architecture Plan](docs/UI_MAKER_ARCHITECTURE_PLAN.md) — standalone decoupling design and history.

Validate the repository contracts from the root directory:

```bash
python -B tools/validate_catalog.py
python -B tools/generate_tool_reference.py --check
python -B Packages/com.ducminh.ui-maker/AI~/Skills/ui-maker/scripts/test_validate_ui_json.py -v
```

Smoke-test against a running Unity Editor:

```bash
python -B tools/smoke_live.py --project-path ValidationProject
```

## Repository Layout

```text
UnityMCP/
|-- Packages/com.ducminh.unity-mcp/  # Unity CLI & Pipeline extension UPM package (v0.2.0, 19 groups / 47 commands)
|-- Packages/com.ducminh.ui-maker/   # Standalone declarative uGUI authoring UPM package (v1.0.0, optional ui_ai Pipeline bridge + AI skills)
|-- ValidationProject/               # Unity 6 validation host project
|-- docs/                            # Architecture, security, custom tools, UI Maker plan, and command catalog
`-- tools/                           # Catalog validator, doc generator, and live smoke script
```

## License

[MIT](LICENSE)
