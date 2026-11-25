---
name: ui-maker
description: Build, design, clone from mockups, inspect, patch, save, and visually review Unity uGUI screens, popups, and HUDs using declarative JSON specs and Unity CLI / MCP ui_ai commands. Use whenever creating or modifying Unity UI hierarchies, prefab variants, screen specs, or layout/visual styling without adding raw UGUI components.
---

# UI Maker

Declarative Unity uGUI composition using registered Base Prefabs (`useBase`), semantic layout containers (`kind: "emptyRect"`), and `ui_ai` commands invoked through Unity CLI (`unity command ui_ai <name> ...`) or `unity mcp` (`ui_ai` group tool).

For authoring or updating `UIPrefabManifest`, `UICatalogRegistry`, `AIMetadataConfig`, or `versionDefines` Editor Bridges, see [ui-maker-manifest-authoring](../ui-maker-manifest-authoring/SKILL.md).

## Core Iron Rules

1. **Never create raw visual UGUI components.** Never add or construct raw `Image`, `RawImage`, `TMP_Text`, `Text`, `Button`, `ScrollRect`, `Slider`, or `Toggle` components via `component add` or manual GameObjects. Compose strictly from catalog `useBase` prefabs and `kind: "emptyRect"` layout containers.
2. **Always inspect the project catalog first.** Run `ui_ai get_ui_catalog` (or read `Assets/UnityMCP/ui_catalog.json` offline) before authoring layout or patch JSON so you use exact `prefabs` (`props`, `slots`, `parts`), `scripts` (`fields`), and optional project `referenceContainers`. If `referenceContainers` is empty or omitted, do not emit `refs`; bind UI nodes via `rootScript`, `bindToRoot`, and `bindToParent` instead.
3. **Respect the Unity CLI mutation contract.** Mutating `ui_ai` commands (`build_ui_from_json`, `patch_ui_node`, `scaffold_prefab_manifest`) accept `--dry-run` (default `false`). Pass `--dry-run true` to preview and validate without mutating the scene or disk; omit `--dry-run` (or pass `--dry-run false`) when executing the real change. Never confuse `--save-spec true` (writes `<ScreenName>.ui.json`) with `--save-as-prefab-path` (creates a Prefab asset) or `--overwrite-existing-prefab true` (regenerates an existing Prefab asset while preserving its GUID).
4. **Prefer surgical `patch_ui_node` on existing managed UI.** Use `ui_ai patch_ui_node` to update props, rects, layouts, parts, slot children, or refs on existing scene or prefab UI. Use `ui_ai build_ui_from_json` for new UI or full regenerations (`--overwrite-existing-prefab true` to preserve GUID when regenerating an existing prefab, `--instantiate-in-scene false` for asset-only prefab builds, `--create-canvas-if-missing true` when building into an empty scene).
5. **Always run `preview_ui` after building or patching.** `ui_ai preview_ui` returns `{ savedPath, width, height, encoding, bytes, source, base64, report }`. Visually inspect the rendered PNG (comparing against any reference image or mockup) and inspect `report.issues` (`zero_size`, `out_of_bounds`, `text_overflow`, `text_truncated`, `sibling_overlap`, `missing_sprite`, `missing_font`). Fix every issue before finishing.

## Invocation Surface

- **Unity CLI (shell):** `unity command ui_ai <command> [--flag value ...]`
  - Example: `unity command ui_ai get_ui_catalog --category Popup`
  - Example: `unity command ui_ai build_ui_from_json --layout-json '...' --dry-run true`
- **Unity MCP (`unity mcp --tool-groups`):** Call the `ui_ai` group tool with the command name (`get_ui_catalog`, `build_ui_from_json`, etc.) and kebab-case arguments (`layout-json`, `target-path`, `dry-run`, etc.).

## Workflows

### A. New Screen from Scratch or Reference Image

1. Call `ui_ai get_ui_catalog` (filter with `--category` or `--query` if large) to discover available Base Prefabs, exposed `props`, `slots`, `parts`, managed `scripts`, and `referenceContainers`.
2. Decompose the screen or mockup into a `UILayoutNode` tree (`useBase` nodes + `emptyRect` containers with `rect`, `layoutGroup`, `layoutElement`, `contentSizeFitter`, `partOverrides`, `slotChildren`, `rootScript`, `bindToRoot`/`bindToParent`, and `refs`).
3. Validate locally with `scripts/validate_ui_json.py`:
   ```bash
   python scripts/validate_ui_json.py --layout Assets/UI/Specs/ShopPopup.ui.json --catalog Assets/UnityMCP/ui_catalog.json
   ```
4. Preview or dry-run before writing:
   - Run `ui_ai preview_ui` with transient `--layout-json` (and `--width`/`--height` matching target resolution) to catch layout/text issues before mutating the scene or disk.
   - Or run `ui_ai build_ui_from_json` with `--dry-run true`.
5. Execute `ui_ai build_ui_from_json` (default `--dry-run false`, setting `--parent-path-in-scene`, `--create-canvas-if-missing`, `--instantiate-in-scene`, `--save-as-prefab-path`, `--overwrite-existing-prefab`, `--save-spec`, and `--screen-name` as needed).
6. Call `ui_ai preview_ui` on the resulting `--target-path` (`scene:Canvas/ShopPopup` or `Assets/UI/ShopPopup.prefab`), inspect `savedPath` / `base64` and `report.issues`, and fix any problems with `ui_ai patch_ui_node`.

### B. Inspect and Surgically Patch Scene or Prefab

1. Call `ui_ai inspect_ui` with `--target-path` (`scene:Canvas/ShopPopup` or `asset:Assets/UI/ShopPopup.prefab`).
   - Confirm `managed: true` (and note `roundTrip` and any `unsupported` items). If unmanaged, do not blindly patch; ask or wrap with managed prefabs/manifests first.
   - Note node `id` values (`UIManagedNodeId`), `partOverrides`, `slots`, and `refs`.
2. Build a `UIPatchDocument` targeting `targetNodeId` (preferred) or `targetNodePath` (`"."` for the inspected root).
3. Validate offline with `python scripts/validate_ui_json.py --patch patch.json --catalog Assets/UnityMCP/ui_catalog.json`, then dry-run via `ui_ai patch_ui_node --target-path ... --patch-json ... --dry-run true`.
4. Apply via `ui_ai patch_ui_node --target-path ... --patch-json ...` (default `--dry-run false`). Prefab asset patches back up externally and roll back atomically on failure or GUID change.
5. Run `ui_ai preview_ui` on `--target-path` and verify both the rendered PNG and `report.issues`.

### C. Per-Screen Spec Lifecycle

Keep reusable per-screen declarative specs separate from `ui_catalog.json` (default folder: `Assets/UI/Specs/<ScreenName>.ui.json`):

- `ui_ai list_ui_screen_specs`: list saved screen names in `--directory` (defaults to `Assets/UI/Specs`).
- `ui_ai load_ui_screen_spec`: load `<ScreenName>.ui.json` into a `UILayoutNode`.
- `ui_ai save_ui_screen_spec`: save from `--layout-json` or by inspecting `--target-path` into `<ScreenName>.ui.json`.
- `ui_ai build_ui_from_json` can build directly from `--screen-name` (when `--layout-json` is omitted) or persist `--layout-json` with `--save-spec true` and `--screen-name`.

### D. Offline / No-Unity Mode

When no Unity Editor / Pipeline server is running:

1. Read `Assets/UnityMCP/ui_catalog.json` directly for `prefabs`, `scripts`, and `referenceContainers`.
2. Author `<ScreenName>.ui.json` or patch JSON files directly.
3. Run `python scripts/validate_ui_json.py --layout <spec.ui.json> --catalog Assets/UnityMCP/ui_catalog.json` (or `--patch <patch.json>`).
4. Report clearly that live Unity build, patch, and `preview_ui` verification require a running Unity Editor with Pipeline, and provide the validated JSON artifacts.

## References and Scripts

- [JSON Schema and ui_ai Commands](references/json-schema.md): Exact DTO fields, unknown-key rules, `targetPath` syntax, and all 10 `ui_ai` command contracts.
- [Layout, Anchors, and ScrollRect](references/layout-and-anchors.md): 14 anchor presets, padding order warning, `layoutGroup`, `layoutElement`, `contentSizeFitter`, and `GridLayoutGroup` rules.
- [Props, Colors, and Assets](references/props-and-assets.md): Supported component properties, `imageConfig`/`textConfig`, color formats, and `DefaultUIAssetResolver` syntax.
- [Examples and Troubleshooting](references/examples-and-troubleshooting.md): Copy-ready Shop Popup, Vertical Scroll, nested composite `partOverrides`, surgical patch recipes, and error fixes.
- [validate_ui_json.py](scripts/validate_ui_json.py): Standalone Python 3 stdlib validator for layout and patch JSON (with optional `--catalog` checks and `--json` output).
- [test_validate_ui_json.py](scripts/test_validate_ui_json.py): Unit test suite for the validator.
- [install_project_skill.py](scripts/install_project_skill.py): Cross-agent installer for `.agents/skills`, `.agent/skills`, `.claude/skills`, and `.gemini/skills`.
