# Declarative UI JSON Schema and ui_ai Commands Reference

All declarative DTOs reject unknown JSON keys during validation (`[JsonExtensionData]` + `UISchemaValidationUtility.ValidateNoUnknownKeys`). Check exact field names below before sending JSON to Unity.

## 1. UILayoutNode

Represents one node in a declarative UI hierarchy.

| Field | Type | Required | Constraints / Notes |
| --- | --- | --- | --- |
| `id` | `string` | Yes | Non-empty, unique across the entire hierarchy (case-insensitive). Stored on `UIManagedNodeId`. |
| `name` | `string` | No | Instantiated GameObject name (falls back to `id` if omitted). Governed by `DefaultNodeContract.allowNameOverrideByDefault`. |
| `active` | `bool` | No | Initial `GameObject.SetActive` state. Governed by `DefaultNodeContract.allowActiveOverrideByDefault`. |
| `kind` | `string` | Conditional | Set `"emptyRect"` for layout containers. Mutually exclusive (XOR) with `useBase`. |
| `useBase` | `string` | Conditional | Base Prefab ID from `catalog.prefabs`. Mutually exclusive (XOR) with `kind: "emptyRect"`. |
| `rect` | `SemanticRectConfig` | No | Semantic anchor/size/padding/offset/pivot. Governed by `DefaultNodeContract.allowRectOverrideByDefault`. |
| `layoutGroup` | `UILayoutGroupConfig` | No | Applies `HorizontalLayoutGroup`, `VerticalLayoutGroup`, or `GridLayoutGroup` on `emptyRect` nodes. |
| `layoutElement` | `UIExplicitLayoutElementConfig` | No | Explicit `LayoutElement` overrides; takes precedence over automatic `LayoutGroupAdapter` sizing. |
| `contentSizeFitter` | `UIContentSizeFitterConfig` | No | Applies `ContentSizeFitter` (`horizontalFit`, `verticalFit`). |
| `props` | `object` (`string -> any`) | No | Exposed prop overrides. Forbidden (must be empty/omitted) on `emptyRect` nodes. |
| `partOverrides` | `object` (`partName -> UIPartOverrideConfig`) | No | Overrides for exposed child prefab parts. Forbidden (must be empty/omitted) on `emptyRect` nodes. |
| `rootScript` | `UIRootScriptConfig` | No | Attaches and configures a `[AIManagedScript]` (or registered script) from `catalog.scripts`. |
| `bindToRoot` | `string` | No | Field name on the root `rootScript` to bind this node's GameObject or matching Component. |
| `bindToParent` | `UIParentBindingConfig` | No | Binds this node to a field on an ancestor script. |
| `slotChildren` | `object` (`slotName -> UILayoutNode[]`) | No | Child nodes placed inside declared `useBase` slots. Forbidden on `emptyRect` nodes. |
| `children` | `UILayoutNode[]` | No | Child nodes under an `emptyRect` container. Do not use on `useBase` nodes (use `slotChildren`). |
| `refs` | `object` (`containerKey -> UIRefEntryConfig[]`) | No | Optional project-defined reference container entries (only when declared in `catalog.referenceContainers`). Omit `refs` when the project uses standard `rootScript` / `bindToRoot` / `bindToParent` bindings instead. |

Common `UILayoutNode` typo traps:
- Use `slotChildren`, not `slots`.
- Use `partOverrides`, not `parts`.
- Use `props`, not `properties`.
- Use `kind: "emptyRect"` or `useBase`, not `type`.
- Use `rootScript`, not `script`.

## 2. UIPartOverrideConfig

Overrides an exposed nested child prefab listed in `prefab.parts` of the parent Base Prefab. Supports recursive nesting for multi-level composite prefabs.

| Field | Type | Required | Constraints / Notes |
| --- | --- | --- | --- |
| `useBase` | `string` | No | Emitted by `inspect_ui` for round-trip clarity (matches `part.baseId` in catalog). |
| `targetPath` | `string` | No | Emitted by `inspect_ui`; relative path inside parent prefab. |
| `active` | `bool` | No | Sets part GameObject active state (governed by `DefaultNodeContract.allowActiveOverrideByDefault`). |
| `rect` | `SemanticRectConfig` | No | Allowed only when both `allowRectOverrideByDefault` and the part's `allowRectOverride` are `true`. |
| `props` | `object` (`string -> any`) | No | Exposed props defined on the part's own `baseId` manifest. |
| `partOverrides` | `object` (`partName -> UIPartOverrideConfig`) | No | Recursive nested overrides for parts exposed on the child `baseId` manifest. |

Common `UIPartOverrideConfig` typo traps:
- Use `props`, `rect`, `active`, `partOverrides` (NOT `setProps`, `setRect`, `setActive`, `parts`).

## 3. UIPatchDocument

Mutation payload for `patch_ui_node`. Requires exact target XOR and at least one non-empty mutation field.

| Field | Type | Required | Constraints / Notes |
| --- | --- | --- | --- |
| `targetNodeId` | `string` | Conditional | `UIManagedNodeId` value in target hierarchy. XOR with `targetNodePath`. |
| `targetNodePath` | `string` | Conditional | Relative Transform path inside target root, or `"."` for root itself. XOR with `targetNodeId`. |
| `setActive` | `bool` | No | Toggles target GameObject active state (requires `allowActiveOverrideByDefault`). |
| `setProps` | `object` (`string -> any`) | No | Mutates declared `exposedProps` on a manifested target. |
| `setRect` | `SemanticRectConfig` | No | Updates target `RectTransform` via `SemanticAnchorEngine` (requires `allowRectOverride` on parts). |
| `setLayoutGroup` | `UILayoutGroupConfig` | No | Updates or attaches `Horizontal`/`Vertical`/`Grid` layout group settings. |
| `setLayoutElement` | `UIExplicitLayoutElementConfig` | No | Updates or attaches `LayoutElement` overrides. |
| `setContentSizeFitter` | `UIContentSizeFitterConfig` | No | Updates or attaches `ContentSizeFitter` (`horizontalFit`, `verticalFit`). |
| `partOverrides` | `object` (`partName -> UIPartOverrideConfig`) | No | Mutates declared parts recursively (supports nested `partOverrides`). |
| `slotOperations` | `UISlotOperation[]` | No | Ordered child mutations on declared slots (or `"children"`/`"."` on managed `emptyRect`). |
| `setRefs` | `object` (`containerKey -> UIRefEntryConfig[]`) | No | Replaces the reference list for each specified `containerKey`. |
| `mergeRefs` | `object` (`containerKey -> UIRefEntryConfig[]`) | No | Non-destructive upsert/remove by `id` or `legacyKey`; keeps untouched base prefab refs intact. |

Common `UIPatchDocument` typo traps:
- Use `setProps`, `setRect`, `setActive`, `setRefs`, `slotOperations`, `setLayoutGroup`, `setLayoutElement`, `setContentSizeFitter` (NOT `props`, `rect`, `active`, `refs`, `slots`, `slotChildren`, `layoutGroup`, `layoutElement`, `contentSizeFitter`).

## 4. UISlotOperation

| Field | Type | Required | Constraints / Notes |
| --- | --- | --- | --- |
| `slotName` | `string` | Yes | Slot name from `manifest.slots`. On a managed `emptyRect` container, use `"children"` or `"."`. |
| `action` | `string` | Yes | One of: `"append"`, `"insert"`, `"replace"`, `"remove"`, `"reorder"`, `"clear"` (case-insensitive). Must be permitted by slot `allowedOperations`. |
| `index` | `int` | Conditional | Target sibling index (`>= 0`). Required for `"reorder"`; optional/recommended for `"insert"`; valid selector with/instead of `nodeId` for `"replace"`. |
| `node` | `UILayoutNode` | Conditional | Required for `"append"`, `"insert"`, and `"replace"`. Child category must satisfy slot `acceptedCategories`, and new `id` values must be unique in the hierarchy. |
| `nodeId` | `string` | Conditional | Target child `UIManagedNodeId` (or GameObject name fallback). Required for `"remove"` and `"reorder"`; optional selector for `"replace"`. |

Common `UISlotOperation` typo traps:
- Use `action` (NOT `operation`) and `nodeId` (NOT `targetNodeId` or `id`).

## 5. UIRefEntryConfig

Configures one entry inside a node-level reference container (`refs`, `setRefs`, `mergeRefs`).

| Field | Type | Required | Constraints / Notes |
| --- | --- | --- | --- |
| `id` | `string` | Yes (or `legacyKey`) | Unique reference key string (e.g. `"buy_button"`, `"coin_popup"`). |
| `targetNodeId` | `string` | No | For `SceneObject` containers (when enabled in catalog): resolves to the node with this `UIManagedNodeId`. |
| `targetPath` | `string` | No | For `SceneObject` containers: relative Transform path under container (`""` or `"."` for self). |
| `assetPath` | `string` | No | For `AssetObject` containers (when enabled in catalog): project asset path (e.g. `"Assets/Prefabs/CoinItem.prefab"`). |
| `legacyKey` | `string` | No | Optional legacy compatibility key stored alongside `id`. |
| `componentType` | `string` | No | For `SceneObject` containers: binds the specified `Component` on the target GameObject (e.g. `"TMP_Text"`, `"Button"`) instead of the `GameObject` itself. |
| `remove` | `bool` | No | When `true` inside `mergeRefs`, deletes the entry matching `id` or `legacyKey` from the container. |

## 6. UIRootScriptConfig and UIParentBindingConfig

### UIRootScriptConfig

| Field | Type | Required | Constraints / Notes |
| --- | --- | --- | --- |
| `fullTypeName` | `string` | Yes | Assembly-qualified key from `catalog.scripts` (e.g. `"Game.UI.ShopPopupView, Assembly-CSharp"`). |
| `fields` | `object` (`fieldName -> value`) | No | Primitive, enum, string, or Vector2 serialized field values to assign on the root script. |

### UIParentBindingConfig (`bindToParent`)

| Field | Type | Required | Constraints / Notes |
| --- | --- | --- | --- |
| `field` | `string` | Yes | Serialized field name on the ancestor script. |
| `scriptType` | `string` | No | Full or short type name of the ancestor component to locate. |
| `nodeId` | `string` | No | `UIManagedNodeId` of the target ancestor node. |

## 7. Target Path Syntax

- **Scene target**: `scene:Canvas/ShopPopup` (or `Canvas/ShopPopup`, `/Canvas/ShopPopup`). Case-sensitive exact match is attempted first, then case-insensitive and descendant search unless anchored with `/`.
- **Prefab asset target**: `asset:Assets/UI/ShopPopup.prefab` (or `Assets/UI/ShopPopup.prefab`). Must start with `Assets/` (or `asset:Assets/`) and end with `.prefab`.

## 8. EditorUiAiTools Reference (10 `ui_ai` Commands)

Invoke via Unity CLI (`unity command ui_ai <command> ...`) or `unity mcp` (`ui_ai` group tool).

**Unity CLI Dry-Run Contract:**
Commands that support previewing expose `--dry-run` (`bool`, default `false`):
- **To preview without mutating:** pass `--dry-run true`.
- **To execute the mutation:** omit `--dry-run` or pass `--dry-run false`.
- **Reserved CLI options:** never treat `--format`, `--json`, `--fields`, `--verbose`, or `--omit-nulls` as command arguments; they are global Unity CLI / Pipeline executor options.

| Command (`ui_ai <name>`) | Effect | Parameters & Behavior |
| --- | --- | --- |
| `get_ui_catalog` | `ReadOnly` | `--category` (`string`), `--query` (`string`). Returns filtered `UICatalogDocument` (`prefabs`, `scripts`, `referenceContainers`). |
| `rebuild_ui_catalog` | `Idempotent` | No args. Rescans `UICatalogRegistry` assets (or all prefabs if no registry exists), `[AIManagedScript]` types, and `referenceContainers`, writing `Assets/UnityMCP/ui_catalog.json`. |
| `build_ui_from_json` | `Additive` | `--layout-json`, `--screen-name`, `--parent-path-in-scene`, `--spec-directory`, `--save-spec` (`bool`), `--save-as-prefab-path`, `--overwrite-existing-prefab` (`bool`), `--instantiate-in-scene` (`bool`, default `true`), `--create-canvas-if-missing` (`bool`), `--dry-run` (`bool`, default `false`). Builds from `--layout-json` or saved `--screen-name`. |
| `inspect_ui` | `ReadOnly` | `--target-path` (`string`, required). Returns `UIInspectResult` (`managed`, `roundTrip`, `root`, `unsupported`, `warnings`). |
| `patch_ui_node` | `Idempotent` | `--target-path` (`string`, required), `--patch-json` (`string`, required), `--dry-run` (`bool`, default `false`). Atomically mutates managed scene or prefab UI. |
| `scaffold_prefab_manifest` | `Idempotent` | `--prefab-path` (`string`, required), `--id`, `--category`, `--description`, `--manifest-json`, `--auto-infer` (`bool`, default `true`), `--dry-run` (`bool`, default `false`). |
| `preview_ui` | `ReadOnly` | `--target-path`, `--layout-json`, `--width` (default `1080`), `--height` (default `1920`), `--include-alpha` (`bool`), `--save-path`, `--include-inline-image` (`bool`, default `false`), `--include-hierarchy` (`bool`, default `true`). Returns `{ savedPath, width, height, encoding, bytes, source, base64, report }`. |
| `list_ui_screen_specs` | `ReadOnly` | `--directory` (optional, default `Assets/UI/Specs`). Lists saved `<ScreenName>` entries. |
| `save_ui_screen_spec` | `Idempotent` | `--screen-name` (required), `--layout-json` OR `--target-path`, `--directory`. Saves `<ScreenName>.ui.json`. |
| `load_ui_screen_spec` | `ReadOnly` | `--screen-name` (required), `--directory`. Returns `found`, `screenName`, `specPath`, and `root` (`UILayoutNode`). |

### Key `build_ui_from_json` Flag Distinctions

- `--save-spec true` + `--screen-name "ShopPopup"`: saves the declarative JSON spec to `Assets/UI/Specs/ShopPopup.ui.json` (or `--spec-directory`). Does NOT create a `.prefab`.
- `--save-as-prefab-path "Assets/UI/Prefabs/ShopPopup.prefab"`: saves a Prefab Variant (for `useBase` root) or Regular Prefab (for `emptyRect` root).
- `--overwrite-existing-prefab true`: allows `--save-as-prefab-path` to update an existing `.prefab` file in-place while preserving its `.meta` GUID (without this, `build_ui_from_json` rejects existing prefab paths).
- `--instantiate-in-scene false`: builds only the prefab asset and cleans up the temporary scene instance.
- `--create-canvas-if-missing true`: creates a standard `Canvas` (`ScaleWithScreenSize`) if `--parent-path-in-scene` is omitted and the active scene has no `Canvas`.

### Key `preview_ui` Output and Issue Codes

Output object:
- `savedPath`: absolute path of the rendered PNG (`Temp/ui-maker-previews/capture-...png` when `--save-path` is omitted, or the resolved `--save-path`).
- `width`, `height`: rendered pixel dimensions.
- `encoding`: `"png"`.
- `bytes`: PNG byte size.
- `source`: `"ui:<targetPath>"` or `"ui:layoutJson"`.
- `base64`: base64-encoded PNG (included when `--save-path` is omitted, or when `--include-inline-image true`; omitted when `--save-path` is set without `--include-inline-image true`).
- `report`: `UIReviewReport` (`success`, `width`, `height`, `savedImagePath`, `issues`, `hierarchy`, `errors`, `warnings`).

Issue codes inside `report.issues`:
- `zero_size`: Active `Graphic` or `Selectable` has width or height `<= 0.5px`.
- `out_of_bounds`: Active node screen rect extends outside the `width x height` preview viewport.
- `text_overflow`: `TMP_Text` with `NoWrap` exceeds `rect.width`, or `preferredHeight` exceeds `rect.height`.
- `text_truncated`: `TMP_Text.isTextTruncated` is `true` (ellipsis or overflow clipping).
- `sibling_overlap`: Active visual siblings overlap unintentionally (not in overlap-allowed containers).
- `missing_sprite`: Active `Image` with non-zero alpha has `sprite == null` (renders as flat rectangle) and is not a `Mask`.
- `missing_font`: `TMP_Text` or `Text` is missing its font asset.
