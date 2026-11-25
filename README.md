# UnityMCP

`UnityMCP` hosts **[`com.ducminh.unity-mcp`](Packages/com.ducminh.unity-mcp/README.md) (`v0.2.0`)** — a production-ready Unity 6 (`6000.0+`) UPM package built directly on Unity's official **Unity CLI** (`unity`) and **`com.unity.pipeline`** (`1.0.1`).

It extends `com.unity.pipeline` with **47 Unity CLI / Pipeline extension commands across 19 non-colliding groups** plus one-click project-scoped MCP, skill, and rule configuration for Antigravity, Codex, and Claude Code.

> **Companion Package (`com.ducminh.ui-maker`)**: Declarative uGUI authoring (`UI Maker` SceneView Overlay, `UI Maker Hub`, `UIPrefabManifest`, `UICatalogRegistry`, and the optional `ui_ai` Pipeline command group) lives in its own standalone repository (`UIMaker` / `https://github.com/ducminh1307/UIMaker.git`) with zero dependency on `com.ducminh.unity-mcp`.

### Highlights

- Built directly on `com.unity.pipeline` (`[CliGroupCommand]`, `[CliArg]`, `CommandContext`, `ExitCode`, `CommandEffect`) and `unity` CLI — no custom Python gateway or bearer-token sidecar required.
- Use **Unity CLI** directly from a shell (`unity status`, `unity command`, `unity recompile`, `unity test`, `unity build`) or connect any MCP client via `unity mcp --tool-groups --project-path <projectRoot>`.
- Automatically discovers any additional `[CliGroupCommand]` / `[CliCommand]` packages installed alongside `com.unity.pipeline` (such as `com.ducminh.ui-maker` contributing `ui_ai`).
- All Pipeline HTTP communication stays on `127.0.0.1` loopback and is managed by `com.unity.pipeline` and the `unity` CLI.

## Requirements

- Unity 6 (`6000.0` or newer).
- Official **Unity CLI** (`unity` `1.0.1` or newer):
  - Windows PowerShell: `irm https://unity.com/install.ps1 | iex`
  - macOS / Linux: `curl -fsSL https://unity.com/install.sh | bash`
- Git, when installing the UPM package from a Git URL.

Node.js and a separate Python gateway are **not** required for runtime or Editor operation. Python 3.10+ is only used by optional repository validation scripts in `tools/`.

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

Alternatively, add `com.ducminh.unity-mcp` (and optionally `com.ducminh.ui-maker`) to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.ducminh.unity-mcp": "https://github.com/ducminh1307/UnityMCP.git?path=/Packages/com.ducminh.unity-mcp",
    "com.ducminh.ui-maker": "https://github.com/ducminh1307/UIMaker.git"
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
2. **Extension Commands**: searchable catalog of discovered `[CliGroupCommand]` and `[CliCommand]` commands outside `Unity.Pipeline.*` (including effect badges, copyable `unity command <group> <name>` snippets, and automatic `TypeCache` discovery of companion Pipeline groups such as `ui_ai` when `com.ducminh.ui-maker` is installed).

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

## Unity CLI & Extension Commands

Verify a running Editor and inspect available commands directly:

```bash
unity status --format json
unity command --project-path /path/to/UnityProject
unity command --project-path /path/to/UnityProject scene
unity command --project-path /path/to/UnityProject --query particle
unity command --project-path /path/to/UnityProject checkpoint list --help
```

`com.ducminh.unity-mcp` adds **47 commands across 19 non-colliding groups** on top of `com.unity.pipeline`'s 23 built-in groups:

- **Always available (Runtime + Editor)**: `particle` (`create`, `get`, `configure`, `preview`), `physics_ext` (`get_collision_matrix`, `set_collision_matrix`, `raycast`, `overlap`), `navmesh_ext` (`calculate_path`).
- **Always available (Editor)**: `particle save_prefab`, `uitoolkit` (4), `ugui` (1), `scene_ext` (2), `asset_ext` (2), `render_ext` (3), `visual_ext` (3), `checkpoint` (4).
- **Package-gated via `versionDefines` (Editor)**: `addressables` (5), `localization` (3), `terrain` (2), `sprite` (1), `tilemap` (1), `probuilder` (2), `cinemachine` (1), `vfxgraph` (1), `input_actions` (2).

See the complete [Extension command reference](docs/tools.md) or the canonical [JSON catalog](docs/tool-catalog.json).

## Mutation Safety & Dry-Run Contract

- Every command declares `CommandEffect` (`ReadOnly`, `Idempotent`, `Additive`, or `Destructive`), which `unity mcp` surfaces as MCP tool hints.
- Supported mutating commands expose `--dry-run` (default `false`); pass `--dry-run true` to validate preconditions and preview changes without mutating the scene or disk.
- Destructive or overwriting commands (`checkpoint restore`, `particle save_prefab --overwrite true`, `addressables remove_entry`, `addressables build`) require explicit `--confirm true` unless `--dry-run true` is passed.
- Editor scene mutations use `AuthoringUndoScope` / Undo groups for single-step Undo, and asset paths are validated against project roots.

## Custom Project Commands

Import the **Custom Tool** sample (`Samples~/CustomTool/ProjectStatusTool.cs`) or follow [Custom commands](docs/custom-tools.md). Declare any static C# method with `[CliGroupCommand]` and `CommandContext` — `CommandRegistry` discovers it automatically on recompile with no registration boilerplate.

## Development Player

Runtime Pipeline execution is controlled entirely by **Project Settings > Pipeline > Runtime** (`ENABLE_RUNTIME_PIPELINE_DEV` / `ENABLE_RUNTIME_PIPELINE`). Because `DucMinh.UnityMcp.Runtime.asmdef` references only `Unity.Pipeline.Attributes` and `Unity.Newtonsoft.Json`, non-development Release builds never pull in the `Unity.Pipeline` HTTP server or Roslyn interpreter.

## Documentation

- [Architecture](docs/architecture.md) — Unity CLI, `com.unity.pipeline`, assembly boundaries, and Editor window.
- [Security](docs/security.md) — loopback Pipeline boundary, token-free configs, and mutation guards.
- [Custom commands](docs/custom-tools.md) — authoring `[CliGroupCommand]` commands in Runtime or Editor.
- [Extension command reference](docs/tools.md) — generated index of all 47 `com.ducminh.unity-mcp` extension commands across 19 groups.
- [Tool catalog](docs/tool-catalog.json) — canonical machine-readable extension catalog for `com.ducminh.unity-mcp`.

Validate the repository contracts from the root directory:

```bash
python -B tools/validate_catalog.py
python -B tools/generate_tool_reference.py --check
```

Smoke-test against a running Unity Editor:

```bash
python -B tools/smoke_live.py --project-path ValidationProject
```

## Repository Layout

```text
UnityMCP/
|-- Packages/com.ducminh.unity-mcp/  # Unity CLI & Pipeline extension UPM package (v0.2.0, 19 groups / 47 commands)
|-- ValidationProject/               # Unity 6 validation host project
|-- docs/                            # Architecture, security, custom tools, and command catalog
`-- tools/                           # Catalog validator, doc generator, and live smoke script
```

## License

[MIT](LICENSE)
