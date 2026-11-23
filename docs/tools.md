# UnityMCP extension command reference

> [!NOTE]
> This file is generated from [`tool-catalog.json`](tool-catalog.json).
> Run `python tools/generate_tool_reference.py` after changing the catalog.

This page indexes the `com.ducminh.unity-mcp` extension commands built on top of
Unity's official `com.unity.pipeline` (`1.0.1`) package and invoked via the
`unity` CLI (`unity command <group> <name>`) or `unity mcp --tool-groups`.

Built-in Unity Pipeline commands (`editor`, `scene`, `gameobject`, `component`,
`prefab`, `asset`, `script`, `console`, `test`, `build`, `capture`, `lighting`,
`navmesh`, `occlusion`, `animation.*`, `material`, `package`, `settings`, `job`,
`diagnostics`, `authoring`, `pipeline`, `runtime`) are documented by
`com.unity.pipeline` and listed live by `unity command`.

Independent companion packages register their own Pipeline command groups directly
when installed alongside `com.unity.pipeline` — for example, `com.ducminh.ui-maker`
contributes the `ui_ai` group (10 declarative UI commands) via its optional
`DucMinh.UiMaker.Editor.Pipeline` assembly without requiring `com.ducminh.unity-mcp`.

## At a glance

- **47** extension commands in **19** non-colliding groups.
- **9** Runtime + Editor commands (`particle` creation/inspection/config/preview, `physics_ext`, `navmesh_ext`).
- **38** Editor-only commands.
- **18** package-gated commands compiled automatically via `versionDefines` when their Unity package is installed.
- Catalog version: `0.2.0` (targeting `com.unity.pipeline` `1.0.1`).

## Command effects and safety conventions

| `CommandEffect` | MCP hints | Meaning |
|---|---|---|
| `ReadOnly` | `readOnlyHint: true` | Reads bounded state without changing the project or scene. |
| `Idempotent` | `idempotentHint: true` | Repeatable mutation that leaves equivalent state and does not discard user work. |
| `Additive` | all false | Creates new objects, groups, actions, or checkpoints. |
| `Destructive` | `destructiveHint: true` | Can overwrite or remove state; requires `--confirm true` unless `--dry-run true`. |

## CLI invocation workflow

1. Verify the running Editor or Runtime Pipeline instance:
   ```bash
   unity status --format json
   ```
2. Inspect live groups, commands, and `--help` flags:
   ```bash
   unity command
   unity command <group>
   unity command <group> <name> --help
   ```
3. Preview supported mutations before applying with `--dry-run true` (default is `false` when omitted):
   ```bash
   unity command physics_ext set_collision_matrix --layer-a 0 --layer-b 8 --ignore true --dry-run true
   ```
4. Pass `--confirm true` on destructive or overwriting commands (`checkpoint restore`, `particle save_prefab --overwrite true`, `addressables remove_entry`, `addressables build`).
5. Avoid passing reserved Pipeline executor flags (`--format`, `--json`, `--fields`, `--verbose`, `--omit-nulls`) as custom command parameters.

## Groups

- [Particle Systems (`particle`)](#particle) (5)
- [Addressables (`addressables`)](#addressables) (5, `DM_ADDRESSABLES_PRESENT`)
- [Localization (`localization`)](#localization) (3, `DM_LOCALIZATION_PRESENT`)
- [Terrain (`terrain`)](#terrain) (2, `DM_TERRAIN_PRESENT`)
- [2D Sprite (`sprite`)](#sprite) (1, `DM_2D_SPRITE_PRESENT`)
- [2D Tilemap (`tilemap`)](#tilemap) (1, `DM_TILEMAP_PRESENT`)
- [ProBuilder (`probuilder`)](#probuilder) (2, `DM_PROBUILDER_PRESENT`)
- [Cinemachine (`cinemachine`)](#cinemachine) (1, `DM_CINEMACHINE_PRESENT`)
- [VFX Graph (`vfxgraph`)](#vfxgraph) (1, `DM_VFXGRAPH_PRESENT`)
- [Input System Actions (`input_actions`)](#input-actions) (2, `DM_INPUTSYSTEM_PRESENT`)
- [Physics Extensions (`physics_ext`)](#physics-ext) (4)
- [NavMesh Extensions (`navmesh_ext`)](#navmesh-ext) (1)
- [UI Toolkit (`uitoolkit`)](#uitoolkit) (4)
- [uGUI Raycast (`ugui`)](#ugui) (1)
- [Scene Analysis Extensions (`scene_ext`)](#scene-ext) (2)
- [Asset Graph Extensions (`asset_ext`)](#asset-ext) (2)
- [Render & Texture Extensions (`render_ext`)](#render-ext) (3)
- [Visual QA Extensions (`visual_ext`)](#visual-ext) (3)
- [Scene Checkpoints (`checkpoint`)](#checkpoint) (4)

<a id="particle"></a>

## Particle Systems (`particle`)

5 command(s) | Dependency: `com.unity.modules.particlesystem` | Gate: Always compiled.

| Command | Scope | Effect | Dependency | Description |
|---|---|---|---|---|
| `particle create` | editor, runtime | `Additive` | `com.unity.modules.particlesystem` | Create and configure a Shuriken ParticleSystem GameObject. |
| `particle get` | editor, runtime | `ReadOnly` | `com.unity.modules.particlesystem` | Read supported Shuriken ParticleSystem module settings. |
| `particle configure` | editor, runtime | `Idempotent` | `com.unity.modules.particlesystem` | Configure Shuriken ParticleSystem modules from JSON configuration. |
| `particle preview` | editor, runtime | `Idempotent` | `com.unity.modules.particlesystem` | Play, stop, clear, or deterministically simulate a Shuriken ParticleSystem. |
| `particle save_prefab` | editor | `Destructive` | `com.unity.modules.particlesystem` | Save a scene ParticleSystem GameObject as a prefab asset (requires --confirm true when overwriting). |

<a id="addressables"></a>

## Addressables (`addressables`)

5 command(s) | Dependency: `com.unity.addressables` | Gate: `DM_ADDRESSABLES_PRESENT`.

| Command | Scope | Effect | Dependency | Description |
|---|---|---|---|---|
| `addressables list_groups` | editor | `ReadOnly` | `com.unity.addressables` | List Addressables groups and schema types from an existing settings asset without creating configuration. |
| `addressables create_group` | editor | `Additive` | `com.unity.addressables` | Create one Addressables group by copying validated schema types from an existing template/default group. |
| `addressables add_entry` | editor | `Idempotent` | `com.unity.addressables` | Add or move one existing non-Resources asset into a writable Addressables group, optionally setting its address. |
| `addressables remove_entry` | editor | `Destructive` | `com.unity.addressables` | Remove one existing writable Addressables entry without deleting its asset (requires --confirm true unless --dry-run true). |
| `addressables build` | editor | `Destructive` | `com.unity.addressables` | Build Addressables player content with the project's configured default settings (requires --confirm true unless --dry-run true). |

<a id="localization"></a>

## Localization (`localization`)

3 command(s) | Dependency: `com.unity.localization` | Gate: `DM_LOCALIZATION_PRESENT`.

| Command | Scope | Effect | Dependency | Description |
|---|---|---|---|---|
| `localization list_tables` | editor | `ReadOnly` | `com.unity.localization` | List String Table collections and configured Localization locales. |
| `localization get_entry` | editor | `ReadOnly` | `com.unity.localization` | Read localized String Table values and entry metadata for an existing key. |
| `localization set_entry` | editor | `Idempotent` | `com.unity.localization` | Update existing String Table values across explicit locales with optimistic revision checking. |

<a id="terrain"></a>

## Terrain (`terrain`)

2 command(s) | Dependency: `com.unity.modules.terrain` | Gate: `DM_TERRAIN_PRESENT`.

| Command | Scope | Effect | Dependency | Description |
|---|---|---|---|---|
| `terrain set_height` | editor | `Idempotent` | `com.unity.modules.terrain` | Apply a bounded normalized TerrainData height patch. |
| `terrain paint_texture` | editor | `Idempotent` | `com.unity.modules.terrain` | Apply a bounded normalized TerrainData alphamap layer patch. |

<a id="sprite"></a>

## 2D Sprite (`sprite`)

1 command(s) | Dependency: `com.unity.2d.sprite` | Gate: `DM_2D_SPRITE_PRESENT`.

| Command | Scope | Effect | Dependency | Description |
|---|---|---|---|---|
| `sprite slice` | editor | `Idempotent` | `com.unity.2d.sprite` | Update validated existing Sprite Editor rectangles on a TextureImporter. |

<a id="tilemap"></a>

## 2D Tilemap (`tilemap`)

1 command(s) | Dependency: `com.unity.modules.tilemap` | Gate: `DM_TILEMAP_PRESENT`.

| Command | Scope | Effect | Dependency | Description |
|---|---|---|---|---|
| `tilemap set_tiles` | editor | `Idempotent` | `com.unity.modules.tilemap` | Set or clear a bounded set of Tilemap cells from stable TileBase asset paths. |

<a id="probuilder"></a>

## ProBuilder (`probuilder`)

2 command(s) | Dependency: `com.unity.probuilder` | Gate: `DM_PROBUILDER_PRESENT`.

| Command | Scope | Effect | Dependency | Description |
|---|---|---|---|---|
| `probuilder create` | editor | `Additive` | `com.unity.probuilder` | Create one ProBuilder cube with validated dimensions, transform, and optional scene parent. |
| `probuilder edit` | editor | `Idempotent` | `com.unity.probuilder` | Translate a bounded, validated set of ProBuilderMesh local vertices using ProBuilder's VertexPositioning API. |

<a id="cinemachine"></a>

## Cinemachine (`cinemachine`)

1 command(s) | Dependency: `com.unity.cinemachine` | Gate: `DM_CINEMACHINE_PRESENT`.

| Command | Scope | Effect | Dependency | Description |
|---|---|---|---|---|
| `cinemachine create` | editor | `Additive` | `com.unity.cinemachine` | Create a Cinemachine 3 CinemachineCamera with optional parent, Follow, LookAt, and priority. |

<a id="vfxgraph"></a>

## VFX Graph (`vfxgraph`)

1 command(s) | Dependency: `com.unity.visualeffectgraph` | Gate: `DM_VFXGRAPH_PRESENT`.

| Command | Scope | Effect | Dependency | Description |
|---|---|---|---|---|
| `vfxgraph set` | editor | `Idempotent` | `com.unity.visualeffectgraph` | Set validated exposed bool, int, float, Vector2, Vector3, or Vector4 Visual Effect Graph overrides. |

<a id="input-actions"></a>

## Input System Actions (`input_actions`)

2 command(s) | Dependency: `com.unity.inputsystem` | Gate: `DM_INPUTSYSTEM_PRESENT`.

| Command | Scope | Effect | Dependency | Description |
|---|---|---|---|---|
| `input_actions get` | editor | `ReadOnly` | `com.unity.inputsystem` | Read action maps, actions, bindings, and control schemes from an Input Actions asset. |
| `input_actions create_action` | editor | `Additive` | `com.unity.inputsystem` | Add a typed action with simple bindings to an existing Input Actions map. |

<a id="physics-ext"></a>

## Physics Extensions (`physics_ext`)

4 command(s) | Dependency: `com.unity.modules.physics` | Gate: Always compiled.

| Command | Scope | Effect | Dependency | Description |
|---|---|---|---|---|
| `physics_ext get_collision_matrix` | editor, runtime | `ReadOnly` | `com.unity.modules.physics` | Read the 3D physics layer collision matrix. |
| `physics_ext set_collision_matrix` | editor, runtime | `Idempotent` | `com.unity.modules.physics` | Set one 3D physics layer collision relationship. |
| `physics_ext raycast` | editor, runtime | `ReadOnly` | `com.unity.modules.physics` | Perform a bounded 3D physics raycast. |
| `physics_ext overlap` | editor, runtime | `ReadOnly` | `com.unity.modules.physics` | Perform a bounded sphere or box overlap query. |

<a id="navmesh-ext"></a>

## NavMesh Extensions (`navmesh_ext`)

1 command(s) | Dependency: `com.unity.modules.ai` | Gate: Always compiled.

| Command | Scope | Effect | Dependency | Description |
|---|---|---|---|---|
| `navmesh_ext calculate_path` | editor, runtime | `ReadOnly` | `com.unity.modules.ai` | Calculate a bounded NavMesh path between world positions using already-loaded NavMesh data. |

<a id="uitoolkit"></a>

## UI Toolkit (`uitoolkit`)

4 command(s) | Dependency: `unity-editor` | Gate: Always compiled.

| Command | Scope | Effect | Dependency | Description |
|---|---|---|---|---|
| `uitoolkit scan` | editor | `ReadOnly` | `unity-editor` | Inspect a bounded UI Toolkit UXML document tree and its declared style sheet sources. |
| `uitoolkit edit_uxml` | editor | `Idempotent` | `unity-editor` | Apply revision-checked bounded text edits to a valid UXML asset. |
| `uitoolkit edit_uss` | editor | `Idempotent` | `unity-editor` | Apply revision-checked bounded text edits to a syntactically balanced USS asset. |
| `uitoolkit scaffold_controller` | editor | `Additive` | `unity-editor` | Scaffold a bounded UI Toolkit MonoBehaviour controller with named VisualElement bindings. |

<a id="ugui"></a>

## uGUI Raycast (`ugui`)

1 command(s) | Dependency: `com.unity.ugui` | Gate: Always compiled.

| Command | Scope | Effect | Dependency | Description |
|---|---|---|---|---|
| `ugui raycast` | editor | `ReadOnly` | `com.unity.ugui` | Return Canvas RectTransforms containing a screen point. |

<a id="scene-ext"></a>

## Scene Analysis Extensions (`scene_ext`)

2 command(s) | Dependency: `unity-editor` | Gate: Always compiled.

| Command | Scope | Effect | Dependency | Description |
|---|---|---|---|---|
| `scene_ext validate` | editor | `ReadOnly` | `unity-editor` | Validate references and optional lifecycle hazards in one loaded scene. |
| `scene_ext analyze_complexity` | editor | `ReadOnly` | `unity-editor` | Analyze loaded scene hierarchy, components, renderers, and mesh complexity. |

<a id="asset-ext"></a>

## Asset Graph Extensions (`asset_ext`)

2 command(s) | Dependency: `unity-editor` | Gate: Always compiled.

| Command | Scope | Effect | Dependency | Description |
|---|---|---|---|---|
| `asset_ext get_dependencies` | editor | `ReadOnly` | `unity-editor` | List direct or recursive asset dependencies. |
| `asset_ext find_references` | editor | `ReadOnly` | `unity-editor` | Find project assets with a direct AssetDatabase dependency on a target asset. |

<a id="render-ext"></a>

## Render & Texture Extensions (`render_ext`)

3 command(s) | Dependency: `unity-editor` | Gate: Always compiled.

| Command | Scope | Effect | Dependency | Description |
|---|---|---|---|---|
| `render_ext get_settings` | editor | `ReadOnly` | `unity-editor` | Read active-scene render and ambient lighting settings. |
| `render_ext set_settings` | editor | `Idempotent` | `unity-editor` | Set active-scene render and ambient lighting settings. |
| `render_ext generate_texture` | editor | `Additive` | `unity-editor` | Generate a solid PNG texture asset under Assets/. |

<a id="visual-ext"></a>

## Visual QA Extensions (`visual_ext`)

3 command(s) | Dependency: `unity-editor` | Gate: Always compiled.

| Command | Scope | Effect | Dependency | Description |
|---|---|---|---|---|
| `visual_ext capture_gameobject` | editor | `ReadOnly` | `unity-editor` | Capture Unity's generated preview image for a GameObject or Component as PNG. |
| `visual_ext capture_multiview` | editor | `ReadOnly` | `unity-editor` | Capture several loaded Cameras as PNG images. |
| `visual_ext frame_debugger_events` | editor | `ReadOnly` | `unity-editor` | Read currently available Unity Frame Debugger events without enabling or changing the debugger. |

<a id="checkpoint"></a>

## Scene Checkpoints (`checkpoint`)

4 command(s) | Dependency: `unity-editor` | Gate: Always compiled.

| Command | Scope | Effect | Dependency | Description |
|---|---|---|---|---|
| `checkpoint create` | editor | `Additive` | `unity-editor` | Create a bounded on-disk snapshot of an Assets/*.unity scene under Library/UnityMCP/checkpoints. |
| `checkpoint list` | editor | `ReadOnly` | `unity-editor` | List valid UnityMCP scene checkpoints stored under Library/UnityMCP/checkpoints. |
| `checkpoint diff` | editor | `ReadOnly` | `unity-editor` | Compare a checkpoint with its Assets/*.unity source scene using hashes and a bounded line-by-line diff. |
| `checkpoint restore` | editor | `Destructive` | `unity-editor` | Restore an unloaded, clean Assets/*.unity scene from a local checkpoint (requires --confirm true unless --dry-run true). |

## Related documentation

- [README](../README.md)
- [Architecture](architecture.md)
- [Custom commands](custom-tools.md)
- [Security](security.md)
- [Canonical JSON catalog](tool-catalog.json)
