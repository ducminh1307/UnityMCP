# UnityMCP tool reference

> [!NOTE]
> This file is generated from [`tool-catalog.json`](tool-catalog.json).
> Run `python tools/generate_tool_reference.py` after changing the catalog.

This page is the human-readable index of UnityMCP's built-in tools. The
connected Unity instance remains the source of truth: an MCP client sees only
tools that are implemented, valid for the current target, supported by installed
packages, and enabled in **Window > UnityMCP > Tools**.

The live MCP `tools/list` response is authoritative for each tool's current input
schema and annotations. This reference explains purpose and availability without
duplicating schemas that Unity generates at runtime.

## At a glance

- **187** cataloged tools in **20** categories.
- **185** implemented tools and **2** planned tools.
- **20** safe-read tools enabled in a fresh project.
- Catalog version: 1.0.0.

## How to read the reference

| Field | Meaning |
|---|---|
| Status | `implemented` has a compiled handler. `planned` is documented but never advertised by MCP. |
| Scope | `editor`, `runtime` Development Player, or both. |
| Safety | The permission and risk tier described below. |
| Default | Whether a fresh project enables the tool automatically. |
| Dependency | Unity core, Editor APIs, module, or optional package required by the handler. |

### Safety tiers

| Tier | Meaning |
|---|---|
| `safe-read` | Reads bounded state without intentionally changing the project or target. |
| `write` | Changes state and requires local enablement. Where the live schema includes `apply`, preview first with `apply: false`. |
| `destructive` | Deletes, reverts, replaces, or shuts down state; enable and call only with an explicit target. |
| `unsafe` | Invokes powerful or broad operations such as allowlisted reflection, builds, or batch execution. |

A tool being listed here does not grant permission to use it. Non-default tools
must be enabled locally, and dependencies must be available in the connected
project. Custom project tools are discovered dynamically and therefore do not
appear in this built-in catalog; use `custom-tool-list` to inspect them.

## Recommended call workflow

1. Call `unity-status` to verify the target project and registry state.
2. Use the narrowest read tool to identify stable object or asset references.
3. Inspect the live input schema before constructing arguments.
4. For a mutation whose schema supports dry-run, call it first with `apply: false`.
5. Apply the smallest intended change, then verify it with a read tool.
6. Poll `job-get` for operations that return a job identifier.

## Categories

- [System & Editor](#system-editor) (13)
- [Scene & GameObject](#scene-gameobject) (16)
- [Components & Reflection](#components-reflection) (12)
- [Assets & Importers](#assets-importers) (12)
- [Prefab & ScriptableObject](#prefab-scriptableobject) (9)
- [Scripts & Compilation](#scripts-compilation) (10)
- [Console & Tests](#console-tests) (7)
- [Packages & Build](#packages-build) (10)
- [Material, Shader & Texture](#material-shader-texture) (10)
- [Camera, Rendering & VFX](#camera-rendering-vfx) (12)
- [UI](#ui) (8)
- [Animation & Timeline](#animation-timeline) (8)
- [Physics & Navigation](#physics-navigation) (8)
- [Audio & Input](#audio-input) (7)
- [Screenshot & Visual QA](#screenshot-visual-qa) (5)
- [Profiler & Diagnostics](#profiler-diagnostics) (10)
- [Custom & Automation](#custom-automation) (12)
- [Project extensions](#project-extensions) (12)
- [ProBuilder](#probuilder) (2)
- [Runtime-only](#runtime-only) (4)

<a id="system-editor"></a>

## System & Editor

13 tools.

| Tool | Status | Scope | Safety | Default | Dependency | Description |
|---|---|---|---|---|---|---|
| `unity-status` | implemented | editor, runtime | safe-read | Yes | `unity-core` | Report bridge, registry, project, and target health. |
| `project-info` | implemented | editor, runtime | safe-read | Yes | `unity-core` | Read project identity, Unity version, platform, and paths safe to expose. |
| `editor-state-get` | implemented | editor | safe-read | Yes | `unity-editor` | Read compilation, play mode, pause, and update state. |
| `editor-play` | implemented | editor | write | No | `unity-editor` | Enter play mode after explicit opt-in. |
| `editor-stop` | implemented | editor | write | No | `unity-editor` | Exit play mode after explicit opt-in. |
| `editor-pause` | implemented | editor | write | No | `unity-editor` | Set or clear the Editor pause state. |
| `editor-step` | implemented | editor | write | No | `unity-editor` | Advance one frame while play mode is paused. |
| `editor-selection-get` | implemented | editor | safe-read | Yes | `unity-editor` | Read the active Editor selection. |
| `editor-selection-set` | implemented | editor | write | No | `unity-editor` | Set the active Editor selection by stable object references. |
| `editor-menu-execute` | implemented | editor | unsafe | No | `unity-editor` | Execute an allowlisted Unity Editor menu command. |
| `editor-refresh` | implemented | editor | write | No | `unity-editor` | Refresh the AssetDatabase and report resulting compilation state. |
| `editor-undo` | implemented | editor | write | No | `unity-editor` | Undo the most recent UnityMCP-compatible Editor operation. |
| `editor-redo` | implemented | editor | write | No | `unity-editor` | Redo the most recently undone Editor operation. |

<a id="scene-gameobject"></a>

## Scene & GameObject

16 tools.

| Tool | Status | Scope | Safety | Default | Dependency | Description |
|---|---|---|---|---|---|---|
| `scene-list` | implemented | editor, runtime | safe-read | Yes | `unity-core` | List loaded scenes and active-scene metadata. |
| `scene-hierarchy` | implemented | editor, runtime | safe-read | Yes | `unity-core` | Read a bounded hierarchy projection for one or more scenes. |
| `scene-create` | implemented | editor | write | No | `unity-editor` | Dry-run or create a new scene asset. |
| `scene-open` | implemented | editor | write | No | `unity-editor` | Dry-run or open a scene in single or additive mode. |
| `scene-close` | implemented | editor | write | No | `unity-editor` | Close a loaded Editor scene with explicit dirty-scene handling. |
| `scene-save` | implemented | editor | write | No | `unity-editor` | Dry-run or save a scene and return changed assets. |
| `scene-set-active` | implemented | editor, runtime | write | No | `unity-core` | Set the active loaded scene. |
| `scene-validate` | implemented | editor | safe-read | No | `unity-editor` | Find missing scripts, broken references, and common scene issues. |
| `gameobject-find` | implemented | editor, runtime | safe-read | Yes | `unity-core` | Find GameObjects with bounded name, tag, layer, path, or component filters. |
| `gameobject-get` | implemented | editor, runtime | safe-read | Yes | `unity-core` | Read identity, transform, properties, and component summaries. |
| `gameobject-create` | implemented | editor, runtime | write | No | `unity-core` | Dry-run or create a GameObject with an optional parent. |
| `gameobject-duplicate` | implemented | editor, runtime | write | No | `unity-core` | Duplicate a GameObject and return its new stable reference. |
| `gameobject-delete` | implemented | editor, runtime | destructive | No | `unity-core` | Dry-run or delete a GameObject, using Undo in the Editor. |
| `gameobject-set-parent` | implemented | editor, runtime | write | No | `unity-core` | Dry-run or reparent a GameObject with world-transform control. |
| `gameobject-set-transform` | implemented | editor, runtime | write | No | `unity-core` | Dry-run or update local or world position, rotation, and scale. |
| `gameobject-set-properties` | implemented | editor, runtime | write | No | `unity-core` | Dry-run or update name, active state, tag, layer, and static flags. |

<a id="components-reflection"></a>

## Components & Reflection

12 tools.

| Tool | Status | Scope | Safety | Default | Dependency | Description |
|---|---|---|---|---|---|---|
| `component-types` | implemented | editor, runtime | safe-read | Yes | `unity-core` | List attachable component types visible to the target. |
| `component-schema` | implemented | editor, runtime | safe-read | Yes | `unity-core` | Return the writable and readable schema for a component type. |
| `component-get` | implemented | editor, runtime | safe-read | Yes | `unity-core` | Read serialized fields and supported properties from a component. |
| `component-add` | implemented | editor, runtime | write | No | `unity-core` | Dry-run or add a component by validated type name. |
| `component-remove` | implemented | editor, runtime | destructive | No | `unity-core` | Dry-run or remove a component, using Undo in the Editor. |
| `component-set-property` | implemented | editor, runtime | write | No | `unity-core` | Dry-run or set one schema-validated component property. |
| `component-set-properties` | implemented | editor, runtime | write | No | `unity-core` | Dry-run or atomically set several supported component properties. |
| `object-get` | implemented | editor | safe-read | No | `unity-editor` | Read an allowlisted reflected Unity object projection. |
| `object-set` | implemented | editor | unsafe | No | `unity-editor` | Set allowlisted reflected Unity object members. |
| `method-find` | implemented | editor | safe-read | No | `unity-editor` | Find allowlisted reflected methods and their parameter schemas. |
| `method-call` | implemented | editor | unsafe | No | `unity-editor` | Invoke an explicitly allowlisted reflected method. |
| `type-schema` | implemented | editor, runtime | safe-read | No | `unity-core` | Describe a supported CLR or Unity value type as JSON Schema. |

<a id="assets-importers"></a>

## Assets & Importers

12 tools.

| Tool | Status | Scope | Safety | Default | Dependency | Description |
|---|---|---|---|---|---|---|
| `asset-search` | implemented | editor | safe-read | Yes | `unity-editor` | Search project assets with bounded AssetDatabase filters. |
| `asset-info` | implemented | editor | safe-read | Yes | `unity-editor` | Read GUID, type, path, labels, importer, and size metadata. |
| `asset-dependencies` | implemented | editor | safe-read | Yes | `unity-editor` | List direct or recursive AssetDatabase dependencies. |
| `asset-references` | implemented | editor | safe-read | No | `unity-editor` | Find project assets and scenes that reference a target asset. |
| `asset-create-folder` | implemented | editor | write | No | `unity-editor` | Dry-run or create a folder inside Assets with path containment. |
| `asset-import` | implemented | editor | write | No | `unity-editor` | Import a file already present under an allowed project path. |
| `asset-reimport` | implemented | editor | write | No | `unity-editor` | Reimport a project asset and report importer diagnostics. |
| `asset-copy` | implemented | editor | write | No | `unity-editor` | Dry-run or copy an asset with its metadata managed by Unity. |
| `asset-move` | implemented | editor | write | No | `unity-editor` | Dry-run or move an asset while preserving its GUID. |
| `asset-delete` | implemented | editor | destructive | No | `unity-editor` | Dry-run or delete a contained project asset and journal the change. |
| `asset-import-settings-get` | implemented | editor | safe-read | No | `unity-editor` | Read supported importer settings for an asset. |
| `asset-import-settings-set` | implemented | editor | write | No | `unity-editor` | Dry-run or update validated importer settings and reimport. |

<a id="prefab-scriptableobject"></a>

## Prefab & ScriptableObject

9 tools.

| Tool | Status | Scope | Safety | Default | Dependency | Description |
|---|---|---|---|---|---|---|
| `prefab-info` | implemented | editor | safe-read | Yes | `unity-editor` | Read prefab type, source, overrides, and hierarchy metadata. |
| `prefab-create` | implemented | editor | write | No | `unity-editor` | Dry-run or create a prefab asset from a scene object. |
| `prefab-instantiate` | implemented | editor | write | No | `unity-editor` | Dry-run or instantiate a prefab in an Editor scene. |
| `prefab-apply` | implemented | editor | write | No | `unity-editor` | Dry-run or apply selected instance overrides to a prefab asset. |
| `prefab-revert` | implemented | editor | destructive | No | `unity-editor` | Dry-run or revert selected prefab instance overrides. |
| `prefab-unpack` | implemented | editor | destructive | No | `unity-editor` | Dry-run or unpack a prefab instance completely or one level. |
| `scriptableobject-create` | implemented | editor | write | No | `unity-editor` | Create an asset from an allowlisted ScriptableObject type. |
| `scriptableobject-get` | implemented | editor | safe-read | No | `unity-editor` | Read supported serialized ScriptableObject fields. |
| `scriptableobject-set` | implemented | editor | write | No | `unity-editor` | Dry-run or update supported ScriptableObject fields. |

<a id="scripts-compilation"></a>

## Scripts & Compilation

10 tools.

| Tool | Status | Scope | Safety | Default | Dependency | Description |
|---|---|---|---|---|---|---|
| `script-search` | implemented | editor | safe-read | No | `unity-editor` | Search C# source text inside contained project paths. |
| `script-read` | implemented | editor | safe-read | No | `unity-editor` | Read bounded ranges from a contained project script. |
| `script-create` | implemented | editor | write | No | `unity-editor` | Dry-run or create a C# script under an allowed project folder. |
| `script-delete` | implemented | editor | destructive | No | `unity-editor` | Dry-run or delete a contained project script and its metadata. |
| `script-apply-text-edits` | implemented | editor | write | No | `unity-editor` | Apply revision-checked text edits to a project script. |
| `script-apply-structured-edits` | implemented | editor | write | No | `unity-editor` | Apply syntax-aware C# edits with conflict checks. |
| `script-validate` | implemented | editor | safe-read | No | `unity-editor` | Parse and validate a script without mutating project files. |
| `compile-status` | implemented | editor | safe-read | Yes | `unity-editor` | Read current compilation and domain-reload status. |
| `compile-request` | implemented | editor | write | No | `unity-editor` | Request script compilation and return an asynchronous job. |
| `compile-errors` | implemented | editor | safe-read | Yes | `unity-editor` | Read structured compiler errors and warnings. |

<a id="console-tests"></a>

## Console & Tests

7 tools.

| Tool | Status | Scope | Safety | Default | Dependency | Description |
|---|---|---|---|---|---|---|
| `console-read` | implemented | editor | safe-read | Yes | `unity-editor` | Read bounded structured Unity Console entries. |
| `console-clear` | implemented | editor | destructive | No | `unity-editor` | Clear Unity Console entries after explicit opt-in. |
| `console-analyze` | implemented | editor | safe-read | No | `unity-editor` | Group and summarize Console entries by signature and severity. |
| `test-list` | implemented | editor | safe-read | No | `com.unity.test-framework` | List available EditMode and PlayMode tests. |
| `test-run` | implemented | editor | write | No | `com.unity.test-framework` | Start a filtered Unity Test Framework run. |
| `test-job-get` | implemented | editor | safe-read | No | `com.unity.test-framework` | Read progress and results for a test job. |
| `test-cancel` | implemented | editor | write | No | `com.unity.test-framework` | Request cancellation of a running test job. |

<a id="packages-build"></a>

## Packages & Build

10 tools.

| Tool | Status | Scope | Safety | Default | Dependency | Description |
|---|---|---|---|---|---|---|
| `package-list` | implemented | editor | safe-read | Yes | `com.unity.modules.package-manager-ui` | List installed Unity packages and resolved versions. |
| `package-search` | implemented | editor | safe-read | No | `com.unity.modules.package-manager-ui` | Search package metadata available to Unity Package Manager. |
| `package-add` | implemented | editor | unsafe | No | `com.unity.modules.package-manager-ui` | Add a package with an explicit identifier and version. |
| `package-remove` | implemented | editor | destructive | No | `com.unity.modules.package-manager-ui` | Remove an explicitly identified project package. |
| `package-resolve` | implemented | editor | unsafe | No | `com.unity.modules.package-manager-ui` | Resolve project packages and report dependency errors. |
| `build-settings-get` | implemented | editor | safe-read | No | `unity-editor` | Read build scenes, target, options, and output settings. |
| `build-settings-set` | implemented | editor | write | No | `unity-editor` | Dry-run or update supported build settings. |
| `build-target-switch` | implemented | editor | unsafe | No | `unity-editor` | Switch the active build target as an asynchronous job. |
| `build-player` | implemented | editor | unsafe | No | `unity-editor` | Build a player to a contained output path as a job. |
| `build-job-get` | implemented | editor | safe-read | No | `unity-editor` | Read progress and report details for a build job. |

<a id="material-shader-texture"></a>

## Material, Shader & Texture

10 tools.

| Tool | Status | Scope | Safety | Default | Dependency | Description |
|---|---|---|---|---|---|---|
| `material-info` | implemented | editor | safe-read | Yes | `unity-core` | Read shader, keywords, render queue, and material properties. |
| `material-create` | implemented | editor | write | No | `unity-editor` | Dry-run or create a material asset using a known shader. |
| `material-set-property` | implemented | editor | write | No | `unity-core` | Dry-run or set a shader-declared material property. |
| `material-assign` | implemented | editor | write | No | `unity-core` | Assign a material to a validated renderer slot. |
| `shader-list` | implemented | editor | safe-read | No | `unity-editor` | List project and built-in shaders visible to the Editor. |
| `shader-info` | implemented | editor | safe-read | No | `unity-editor` | Read shader properties, keywords, passes, and diagnostics. |
| `shader-create` | implemented | editor | unsafe | No | `unity-editor` | Create a shader asset from a validated template. |
| `shader-edit` | implemented | editor | unsafe | No | `unity-editor` | Apply revision-checked edits to a shader source asset. |
| `texture-generate` | implemented | editor | write | No | `unity-editor` | Generate a bounded procedural texture asset. |
| `texture-import-settings-set` | implemented | editor | write | No | `unity-editor` | Dry-run or update TextureImporter settings and reimport. |

<a id="camera-rendering-vfx"></a>

## Camera, Rendering & VFX

12 tools.

| Tool | Status | Scope | Safety | Default | Dependency | Description |
|---|---|---|---|---|---|---|
| `camera-list` | implemented | editor, runtime | safe-read | No | `unity-core` | List enabled and disabled cameras with key properties. |
| `camera-info` | implemented | editor, runtime | safe-read | No | `unity-core` | Read a camera's projection, viewport, culling, and output state. |
| `camera-set` | implemented | editor, runtime | write | No | `unity-core` | Dry-run or update supported camera properties. |
| `cinemachine-create` | implemented | editor | write | No | `com.unity.cinemachine` | Create and configure a Cinemachine camera. |
| `render-pipeline-info` | implemented | editor | safe-read | No | `unity-core` | Report active render pipeline and pipeline asset metadata. |
| `render-settings-get` | implemented | editor | safe-read | No | `unity-core` | Read supported RenderSettings values. |
| `render-settings-set` | implemented | editor | write | No | `unity-core` | Dry-run or update supported RenderSettings values. |
| `lighting-settings-get` | implemented | editor | safe-read | No | `unity-editor` | Read scene lighting and bake settings. |
| `lighting-settings-set` | implemented | editor | write | No | `unity-editor` | Dry-run or update supported scene lighting settings. |
| `lighting-bake` | implemented | editor | unsafe | No | `unity-editor` | Start or cancel an asynchronous lighting bake. |
| `particle-set` | implemented | editor, runtime | write | No | `unity-core` | Update supported ParticleSystem modules from typed input. |
| `vfxgraph-set` | implemented | editor | write | No | `com.unity.visualeffectgraph` | Update exposed parameters on a Visual Effect Graph component. |

<a id="ui"></a>

## UI

8 tools.

| Tool | Status | Scope | Safety | Default | Dependency | Description |
|---|---|---|---|---|---|---|
| `ui-canvas-create` | implemented | editor | write | No | `com.unity.ugui` | Create a Canvas with validated render and scaling settings. |
| `ui-element-create` | implemented | editor | write | No | `com.unity.ugui` | Create a supported uGUI element under a Canvas. |
| `ui-element-set` | implemented | editor | write | No | `com.unity.ugui` | Update supported RectTransform and uGUI properties. |
| `ui-raycast` | implemented | editor | safe-read | No | `com.unity.ugui` | Return UI raycast hits at a screen coordinate. |
| `uitoolkit-scan` | implemented | editor | safe-read | No | `com.unity.ui` | Inspect a bounded UI Toolkit document and style tree. |
| `uitoolkit-uxml-edit` | implemented | editor | write | No | `com.unity.ui` | Apply revision-checked structured edits to a UXML asset. |
| `uitoolkit-uss-edit` | implemented | editor | write | No | `com.unity.ui` | Apply revision-checked structured edits to a USS asset. |
| `uitoolkit-controller-scaffold` | implemented | editor | write | No | `com.unity.ui` | Scaffold a UI Toolkit controller from typed bindings. |

<a id="animation-timeline"></a>

## Animation & Timeline

8 tools.

| Tool | Status | Scope | Safety | Default | Dependency | Description |
|---|---|---|---|---|---|---|
| `animation-clip-info` | implemented | editor | safe-read | No | `unity-editor` | Read animation clip metadata, bindings, and events. |
| `animation-clip-create` | implemented | editor | write | No | `unity-editor` | Create an AnimationClip asset from typed curves. |
| `animator-controller-create` | implemented | editor | write | No | `unity-editor` | Create an AnimatorController asset and base layer. |
| `animator-state-add` | implemented | editor | write | No | `unity-editor` | Add a state with an optional motion to an Animator layer. |
| `animator-transition-add` | implemented | editor | write | No | `unity-editor` | Add a validated Animator transition and conditions. |
| `animator-parameter-set` | implemented | editor, runtime | write | No | `unity-core` | Set a runtime Animator parameter or edit controller parameters. |
| `timeline-create` | implemented | editor | write | No | `com.unity.timeline` | Create a Timeline asset and optional PlayableDirector. |
| `timeline-edit` | implemented | editor | write | No | `com.unity.timeline` | Edit supported Timeline tracks, clips, and bindings. |

<a id="physics-navigation"></a>

## Physics & Navigation

8 tools.

| Tool | Status | Scope | Safety | Default | Dependency | Description |
|---|---|---|---|---|---|---|
| `physics-settings-get` | implemented | editor, runtime | safe-read | No | `unity-editor` | Read supported 3D and 2D physics project settings. |
| `physics-settings-set` | implemented | editor, runtime | write | No | `unity-editor` | Dry-run or update supported physics project settings. |
| `physics-collision-matrix-get` | implemented | editor, runtime | safe-read | No | `unity-editor` | Read the 3D or 2D layer collision matrix. |
| `physics-collision-matrix-set` | implemented | editor, runtime | write | No | `unity-editor` | Dry-run or update selected collision matrix pairs. |
| `physics-raycast` | implemented | editor, runtime | safe-read | No | `unity-core` | Perform a bounded physics raycast and return structured hits. |
| `physics-overlap` | implemented | editor, runtime | safe-read | No | `unity-core` | Perform a bounded overlap query and return collider references. |
| `navmesh-bake` | implemented | editor | unsafe | No | `com.unity.ai.navigation` | Bake NavMesh data as an asynchronous Editor job. |
| `navmesh-path-calculate` | implemented | editor, runtime | safe-read | No | `com.unity.ai.navigation` | Calculate a NavMesh path between two world positions. |

<a id="audio-input"></a>

## Audio & Input

7 tools.

| Tool | Status | Scope | Safety | Default | Dependency | Description |
|---|---|---|---|---|---|---|
| `audio-clip-info` | implemented | editor, runtime | safe-read | No | `unity-core` | Read duration, channels, frequency, load type, and format metadata. |
| `audio-source-create` | implemented | editor, runtime | write | No | `unity-core` | Create and configure an AudioSource component. |
| `audio-source-set` | implemented | editor, runtime | write | No | `unity-core` | Update supported AudioSource properties and playback state. |
| `input-actions-get` | implemented | editor | safe-read | No | `com.unity.inputsystem` | Read action maps, actions, bindings, and control schemes. |
| `input-action-create` | implemented | editor | write | No | `com.unity.inputsystem` | Add a typed action and bindings to an Input Actions asset. |
| `input-simulate-key` | implemented | editor, runtime | unsafe | No | `com.unity.inputsystem` | Simulate an allowlisted keyboard input event for testing. |
| `input-simulate-pointer` | implemented | editor, runtime | unsafe | No | `com.unity.inputsystem` | Simulate bounded pointer movement, buttons, or scroll for testing. |

<a id="screenshot-visual-qa"></a>

## Screenshot & Visual QA

5 tools.

| Tool | Status | Scope | Safety | Default | Dependency | Description |
|---|---|---|---|---|---|---|
| `screenshot-game-view` | implemented | runtime | safe-read | No | `unity-core` | Capture a bounded Game view or Development Player screenshot. |
| `screenshot-scene-view` | implemented | editor | safe-read | No | `unity-editor` | Capture the active Scene view with optional gizmos. |
| `screenshot-camera` | implemented | editor | safe-read | No | `unity-core` | Render one camera into a bounded image. |
| `screenshot-gameobject` | implemented | editor | safe-read | No | `unity-core` | Frame and capture a target GameObject using a temporary camera. |
| `screenshot-multiview` | implemented | editor | safe-read | No | `unity-core` | Capture a bounded set of named views in one request. |

<a id="profiler-diagnostics"></a>

## Profiler & Diagnostics

10 tools.

| Tool | Status | Scope | Safety | Default | Dependency | Description |
|---|---|---|---|---|---|---|
| `profiler-start` | implemented | editor | write | No | `com.unity.modules.profiling.core` | Start a bounded profiler recording session. |
| `profiler-stop` | implemented | editor | write | No | `com.unity.modules.profiling.core` | Stop a profiler recording and finalize its job result. |
| `profiler-status` | implemented | editor | safe-read | No | `com.unity.modules.profiling.core` | Read profiler availability and recording status. |
| `profiler-counters` | implemented | editor | safe-read | No | `com.unity.modules.profiling.core` | Sample an allowlisted bounded set of profiler counters. |
| `profiler-frame-capture` | implemented | editor | safe-read | No | `com.unity.modules.profiling.core` | Capture structured samples for selected profiler frames. |
| `memory-summary` | implemented | editor | safe-read | No | `com.unity.modules.profiling.core` | Read bounded managed, native, graphics, and asset memory totals. |
| `memory-snapshot-create` | planned | editor | unsafe | No | `com.unity.memoryprofiler` | Create a memory snapshot as a cancellable job. |
| `memory-snapshot-compare` | planned | editor | safe-read | No | `com.unity.memoryprofiler` | Compare two compatible memory snapshots. |
| `frame-debugger-events` | implemented | editor | safe-read | No | `unity-editor` | Read bounded frame debugger event metadata. |
| `scene-complexity-analyze` | implemented | editor | safe-read | No | `unity-editor` | Summarize scene renderers, triangles, lights, textures, and scripts. |

<a id="custom-automation"></a>

## Custom & Automation

12 tools.

| Tool | Status | Scope | Safety | Default | Dependency | Description |
|---|---|---|---|---|---|---|
| `custom-tool-scaffold` | implemented | editor | write | No | `unity-editor` | Generate a disabled typed C# custom-tool skeleton from a specification. |
| `custom-tool-validate` | implemented | editor | safe-read | No | `unity-editor` | Validate custom-tool signatures, schemas, names, and scope constraints. |
| `custom-tool-list` | implemented | editor | safe-read | No | `unity-core` | List discovered project custom tools including disabled entries. |
| `custom-tool-reload` | implemented | editor | write | No | `unity-editor` | Refresh custom-tool discovery after compilation. |
| `batch-execute` | implemented | editor | unsafe | No | `unity-core` | Execute a bounded ordered batch with per-step results and dry-run support. |
| `checkpoint-create` | implemented | editor | write | No | `unity-editor` | Create a UnityMCP change checkpoint for supported project state. |
| `checkpoint-list` | implemented | editor | safe-read | No | `unity-editor` | List locally available UnityMCP checkpoints. |
| `checkpoint-diff` | implemented | editor | safe-read | No | `unity-editor` | Compare a checkpoint with current supported project state. |
| `checkpoint-restore` | implemented | editor | destructive | No | `unity-editor` | Restore explicitly selected state from a checkpoint. |
| `job-get` | implemented | editor, runtime | safe-read | No | `unity-core` | Read state, progress, result, or error for a bridge job. |
| `job-cancel` | implemented | editor, runtime | write | No | `unity-core` | Request cancellation for a job that declares cancellation support. |
| `execute-csharp` | implemented | editor | unsafe | No | `unity-editor` | Invoke an explicitly allowlisted project C# command; never compile or evaluate source text. |

<a id="project-extensions"></a>

## Project extensions

12 tools.

| Tool | Status | Scope | Safety | Default | Dependency | Description |
|---|---|---|---|---|---|---|
| `addressables-groups-list` | implemented | editor | safe-read | No | `com.unity.addressables` | List Addressables groups, schemas, and entry counts. |
| `addressables-group-create` | implemented | editor | write | No | `com.unity.addressables` | Create an Addressables group with validated schemas. |
| `addressables-entry-add` | implemented | editor | write | No | `com.unity.addressables` | Add or move an asset entry into an Addressables group. |
| `addressables-entry-remove` | implemented | editor | destructive | No | `com.unity.addressables` | Remove an asset entry from Addressables settings. |
| `addressables-build` | implemented | editor | unsafe | No | `com.unity.addressables` | Build Addressables content as an asynchronous job. |
| `localization-table-list` | implemented | editor | safe-read | No | `com.unity.localization` | List localization table collections and locales. |
| `localization-entry-get` | implemented | editor | safe-read | No | `com.unity.localization` | Read localized values and metadata for an entry. |
| `localization-entry-set` | implemented | editor | write | No | `com.unity.localization` | Set localized values for an entry across selected locales. |
| `terrain-height-set` | implemented | editor | write | No | `com.unity.modules.terrain` | Apply a bounded height patch to TerrainData. |
| `terrain-texture-paint` | implemented | editor | write | No | `com.unity.modules.terrain` | Apply bounded Terrain layer alphamap edits. |
| `sprite-slice` | implemented | editor | write | No | `com.unity.modules.imageconversion` | Set validated sprite rectangles in a TextureImporter. |
| `tilemap-set-tiles` | implemented | editor | write | No | `com.unity.2d.tilemap` | Set a bounded region of Tilemap cells from stable tile references. |

<a id="probuilder"></a>

## ProBuilder

2 tools.

| Tool | Status | Scope | Safety | Default | Dependency | Description |
|---|---|---|---|---|---|---|
| `probuilder-create` | implemented | editor | write | No | `com.unity.probuilder` | Create a validated ProBuilder primitive or mesh. |
| `probuilder-edit` | implemented | editor | write | No | `com.unity.probuilder` | Apply bounded topology or vertex edits to a ProBuilder mesh. |

<a id="runtime-only"></a>

## Runtime-only

4 tools.

| Tool | Status | Scope | Safety | Default | Dependency | Description |
|---|---|---|---|---|---|---|
| `runtime-state-get` | implemented | runtime | safe-read | No | `unity-core` | Read Development Player state, scene, frame, and timing metadata. |
| `runtime-time-scale-get` | implemented | runtime | safe-read | No | `unity-core` | Read the Development Player time scale and timing values. |
| `runtime-time-scale-set` | implemented | runtime | write | No | `unity-core` | Set Development Player time scale within configured limits. |
| `runtime-quit` | implemented | runtime | destructive | No | `unity-core` | Request an orderly Development Player shutdown. |

## Related documentation

- [Custom tools](custom-tools.md)
- [Architecture](architecture.md)
- [Protocol](protocol.md)
- [Security](security.md)
- [Canonical JSON catalog](tool-catalog.json)
