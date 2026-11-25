# Manifest, AIMetadataConfig, and Editor Bridge Guide

## 1. Explicit `manifestJson` Format for `scaffold_prefab_manifest`

Pass `manifestJson` to `scaffold_prefab_manifest` (with `prefabPath` and `apply: true` to write, or `apply: false` to dry-run) when you want exact deterministic contracts instead of heuristic auto-inference.

```json
{
  "id": "Base_ShopCard",
  "category": "ItemSlot",
  "description": "Shop offer card with icon, price badge, and buy button part.",
  "props": [
    {
      "propName": "bgSprite",
      "targetPath": "Panel",
      "componentName": "Image",
      "propertyName": "sprite",
      "valueType": "Sprite",
      "forceSliced": true,
      "autoPreserveAspect": false,
      "disableRaycast": false
    },
    {
      "propName": "titleText",
      "targetPath": "Header/Title",
      "componentName": "TMP_Text",
      "propertyName": "text",
      "valueType": "string"
    }
  ],
  "slots": [
    {
      "slotName": "rewards",
      "targetPath": "Body/RewardsRow",
      "acceptedCategories": "ItemSlot,Image,Container",
      "allowedOperations": "append,insert,replace,remove,reorder,clear"
    }
  ],
  "parts": [
    {
      "partName": "buyButton",
      "targetPath": "Footer/BuyButton",
      "allowRectOverride": true
    }
  ]
}
```

### `manifestJson` Field Rules

- `targetPath`: relative path from prefab root (`root.transform.Find(path)`). Use `"."` or `""` for the prefab root GameObject itself.
- `props[]`:
  - `propName`: logical key exposed to `ui-maker` JSON (`props` / `setProps`). Must be unique within the manifest.
  - `componentName`: defaults to `"GameObject"`.
  - `propertyName`: defaults to `"active"`.
  - `valueType`: defaults to `"bool"`. Common types: `string`, `bool`, `int`, `float`, `Vector2`, `Color`, `Sprite`, `Material`, `object`, `RectConfig`.
  - Image safety booleans (only affect `Image` sprite assignment): `forceSliced`, `autoPreserveAspect`, `disableRaycast` (all default `false` in explicit `manifestJson`; `autoPreserveAspect` defaults `true` on `ExposedPropConfig` in Inspector).
- `slots[]`:
  - `slotName`: unique logical slot key.
  - `acceptedCategories`: comma/semicolon-separated category names or `"*"` (defaults to `"*"`). `emptyRect` nodes report category `"Container"`.
  - `allowedOperations`: comma/semicolon-separated operations or `"*"` (defaults to `"append,remove,reorder"`). Supported operations: `append`, `insert`, `replace`, `remove`, `reorder`, `clear`.
- `parts[]`:
  - `partName`: camelCase identifier (auto-derived from child object name or child `manifest.id` if omitted).
  - `targetPath`: must resolve to a child Transform carrying its own `UIPrefabManifest` with a non-empty `id` (cannot reference the root manifest itself).
  - `allowRectOverride`: defaults to `true`. Set `false` to lock the part's anchored layout while still allowing `props` / `active` / nested `partOverrides`.

## 2. Blackbox vs Parts Design Rules

`UILayoutBuilder`, `UILayoutPatcher`, `UIInspector`, and `UICatalogGenerator` enforce a strict contract boundary:

- **Blackbox internals**: Internal children of a Base Prefab that are neither inside a `slotContainer` nor declared in `parts` cannot be targeted directly by `patch_ui_node` (`targetNodePath` rejects unexposed internal children). To make an internal element editable, either expose a property on the parent (`exposedProps`) or promote the child prefab to a `part` (`parts`).
- **Parts isolation**:
  - During `AutoInferManifest` and Inspector **Auto-Fill Default Props**, `UIPrefabManifestPropertyCatalog.CollectScannableTransforms` stops at `slotContainers` and `partRoots` (`UIPrefabManifest` descendants). Properties of a nested `part` belong to that child's own `UIPrefabManifest`, not duplicated on the parent.
  - Multi-level composite prefabs (e.g. `Base_Popup -> confirmButton (Base_Button) -> buttonText (Base_Text)`) naturally support recursive `partOverrides` without duplicating props across levels.
- **When to use `parts` vs `exposedProps`**:
  - Use `exposedProps` for leaf controls inside a single prefab (`titleText`, `icon`, `bgSprite`, `showClose`, `windowRect`).
  - Use `parts` whenever a Base Prefab embeds a reusable sub-prefab (`Base_Button`, `Base_Image`, `Base_Text`, `Base_Badge`) whose full contract (`props`, `rect`, `active`, nested `parts`) should stay reusable and in sync with the sub-prefab.

## 3. Auto-Scaffolder Heuristic Naming Conventions

When `scaffold_prefab_manifest` runs with `autoInfer: true` (and no `manifestJson`):

1. **Category inference**:
   - Contains `Button` and `childCount <= 3` -> `"Button"`
   - Contains `GridLayoutGroup` or `ScrollRect` -> `"Container"`
   - Root name contains `Popup`, `Dialog`, `Window`, or `UI` -> `"Popup"`
   - Name contains `text` or has `TMP_Text` / `TMPro` component -> `"Text"`
   - Otherwise -> `"Common"`
2. **Slot inference** (case-insensitive child Transform name scan, skipping nested child `UIPrefabManifest` subtrees):
   - Contains `content`, `body`, `scroll_content`, or equals `items` -> slot `"content"` (`acceptedCategories: "*"`)
   - Contains `footer`, `bottom`, or `buttons` -> slot `"footer"` (`acceptedCategories: "Button,Container"`)
   - Contains `header` or `top` -> slot `"header"` (`acceptedCategories: "*"`)
3. **Prop inference**:
   - `windowRect` on root (`RectTransform.semanticRect`) when `Popup`/`Container` and `hideNodeBuiltInsFromExposedPropsDropdown == false`.
   - `close` -> `showClose` (`GameObject.active`); `overlay`/`dimmer` -> `showOverlay` (`GameObject.active`).
   - `panel`/`bg`/`background` -> `showUIBackground` (`GameObject.active`), plus `background` (`SpriteAtlasLoader.spriteReference`) or `bgSprite` (`Image.sprite`, `forceSliced: true`).
   - Text components: `title`/`header` -> `titleText`; `label` -> `label`; `txt`/`text`/root -> `text`.
   - `icon` with `Image` -> `icon` (`autoPreserveAspect: true`, `disableRaycast: true`).
   - `GridLayoutGroup` -> `cellSize`, `spacing`.
   - Plus `AutoFillDefaultProps` using enabled `autoExposeOnScaffold` rules from `AIMetadataRegistry`.
4. **Part inference**:
   - Every nested child `UIPrefabManifest` with a non-empty `id` is added to `parts` with camelCase `partName` and `allowRectOverride: true`.

## 4. `AIMetadataConfig` Reference & Simplified Inspector Workflow

Project C# scripts decorated with `[AIManagedScript]` and `[AIExposedField]` are discovered automatically and **do not need entries in `AIMetadataConfig`**. Only configure `AIMetadataConfig` when registering optional Reference Containers (ID -> Object/Prefab lists) or external DLL / attribute-free components:

- **1-Click Inspector Actions**:
  - `Auto-Scan Containers`: scans loaded project assemblies, auto-registers Reference Containers (`listFieldPath` / `idFieldPath` / `targetFieldPath`), and cleans up redundant pure-container duplicates in `customComponents`.
  - `Sync & Rebuild UI Catalog`: syncs `AIMetadataConfig` to `AIMetadataRegistry` and regenerates `ui_catalog.json`.
  - `Quick Add`: drop any Prefab, GameObject, Component, or `.cs` MonoScript (or search loaded types), then click `+ Add to View Scripts`, `+ Add to UI Components`, or `+ Add to Reference Containers`.
- **`1. View Scripts (Screen / Popup Controllers)` (`customComponents` where `isManagedScript == true`)**:
  - For external or attribute-free screen/popup scripts; expand card and check serialized `exposedFields` inline (`autoBindChildPath`, `description`).
- **`2. Custom UI Components (Extra Prefab Props)` (`customComponents` where `isManagedScript == false`)**:
  - For custom UI display/widget components; expand card and check writable `exposedProperties` inline (`Expose`, `Auto` on scaffold, `description`).
- **`3. Reference Containers (Optional)` (`referenceContainers`)**:
  - Leave empty (`0`) in projects that only use standard `MonoBehaviour` references (`bindToRoot` / `bindToParent`).
  - When configured: `containerKey`, `componentTypeName`, `targetKind` (`SceneObject = 0` or `AssetObject = 1`), `allowAutoAttach` (`bool`), and collapsible advanced field paths (`listFieldPath`, `idFieldPath`, `legacyIdFieldPath`, `targetFieldPath`).
  - Supports dropping a script/object directly onto a container card or clicking `Auto-Detect` to populate paths.
  - `UICatalogGenerator` resolves each rule's `componentTypeName` and emits only present project containers into `ui_catalog.json` under `referenceContainers` (skipping missing optional framework types safely with a warning).
- **`4. Built-in UGUI Defaults` (Collapsed Advanced Section)**:
  - **`DefaultNodeContract`**: `allowRectOverrideByDefault`, `allowActiveOverrideByDefault`, `allowNameOverrideByDefault`, `hideNodeBuiltInsFromExposedPropsDropdown`.
  - **`DefaultComponentContracts` (13 built-in UGUI components)**: `Image`, `TMP_Text`, `Text`, `SpriteAtlasLoader`, `GridLayoutGroup`, `Button`, `Toggle`, `Slider`, `ScrollRect`, `CanvasGroup`, `HorizontalLayoutGroup`, `VerticalLayoutGroup`, `LayoutElement`.

## 5. `AIMetadataRegistry.For(...)` Fluent API and Inheritance

Use C# attributes (`[AIManagedScript]`, `[AIExposedField]`) when you own the View class and already reference `DucMinh.UiMaker.Runtime`, or use `AIMetadataRegistry.For(...)` in an Editor Bridge when you cannot modify the runtime assembly or external DLLs.

### Fluent API Signatures

```csharp
AIMetadataRegistry.For<T>()                         // Compile-time type
AIMetadataRegistry.For(typeof(T))                   // Runtime Type
AIMetadataRegistry.For("Namespace.Type, Assembly")  // Safe string lookup (no-ops if TargetType == null)
    .AsManagedScript(string description, string category = "General")
    .ExposeField(string fieldName, string description, string autoBindChildPath = "")
    .ExposeProperty(string propertyName, string valueType, string description = "");
```

### Important Rules and Inheritance Notes

- **Inheritance works automatically**: `AIMetadataRegistry.GetManagedScriptAttribute`, `GetExposedFieldAttribute`, `GetExternalProperties`, and `UICatalogGenerator.ScanScripts` walk the base class chain (`inherit: true`).
- **Abstract base classes**: Registering an abstract base class (`For<BasePopupView>().AsManagedScript(...)`) shares its category, description, and `ExposeField(...)` rules with all concrete subclasses without emitting the abstract base class itself into `catalog.scripts`.
- **Serialized field requirement**: Every field registered with `[AIExposedField]` or `.ExposeField(...)` MUST be serialized by Unity (either `public` without `[NonSerialized]`, or marked `[SerializeField]`), and MUST NOT be `static` or `readonly`. Private `[SerializeField]` fields on base classes are supported via hierarchy field scanning.
- **No-op safety**: `AIMetadataRegistry.For("Optional.Type, Optional.Assembly")` returns a builder whose `TargetType` is `null` if the type is missing; chained `.AsManagedScript(...)` / `.ExposeField(...)` / `.ExposeProperty(...)` safely no-op without throwing.

## 6. `versionDefines` Editor Bridge Template

### Step 1: Editor `.asmdef` (`Assets/Editor/UIMakerBridge/Game.UiMakerBridge.Editor.asmdef`)

```json
{
  "name": "Game.UiMakerBridge.Editor",
  "rootNamespace": "Game.Editor.UiBridge",
  "references": [
    "DucMinh.UiMaker.Runtime",
    "Game.UI.Runtime"
  ],
  "includePlatforms": [
    "Editor"
  ],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "precompiledReferences": [],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [
    {
      "name": "com.ducminh.ui-maker",
      "expression": "",
      "define": "DM_UI_MAKER_PRESENT"
    }
  ],
  "noEngineReferences": false
}
```

### Step 2: `[InitializeOnLoad]` Bridge Script (`GameUiMakerBridge.cs`)

```csharp
#if DM_UI_MAKER_PRESENT
using DMCore.UI.AI;
using UnityEditor;

namespace Game.Editor.UiBridge
{
    [InitializeOnLoad]
    internal static class GameUiMakerBridge
    {
        static GameUiMakerBridge()
        {
            RegisterMetadata();
        }

        private static void RegisterMetadata()
        {
            // Example 1: Compile-time type from referenced runtime assembly
            AIMetadataRegistry.For<Game.UI.BasePopupView>()
                .AsManagedScript("Shared popup base view", "Popup")
                .ExposeField("_closeButton", "Close popup button", "SafeArea/Root/CloseButton");

            // Example 2: Optional external DLL / third-party type via assembly-qualified name
            AIMetadataRegistry.For("ExternalFramework.UI.RewardToast, ExternalFramework.Runtime")
                .AsManagedScript("External reward toast controller", "Popup")
                .ExposeField("titleLabel", "Toast title TMP_Text")
                .ExposeProperty("Duration", "float", "Toast display duration in seconds");
        }
    }
}
#endif
```

After adding or updating the bridge or `AIMetadataConfig`, trigger a domain reload and call `rebuild_ui_catalog` so `Assets/UnityMCP/ui_catalog.json` updates with the newly exposed scripts and fields.
