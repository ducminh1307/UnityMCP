---
name: ui-maker-manifest-authoring
description: Author and maintain UI Maker prefab contracts and metadata in Unity projects. Use when setting up UIPrefabManifest (exposedProps, slots, parts), running scaffold_prefab_manifest, curating UICatalogRegistry, configuring AIMetadataConfig (DefaultNodeContract, DefaultComponentContracts, CustomComponentMetadataEntry, ReferenceContainerRule), writing versionDefines Editor Bridges with AIMetadataRegistry.For(...), or running rebuild_ui_catalog.
---

# UI Maker Manifest Authoring

Configure Base Prefabs, component metadata, reference containers, and external script bridges so the `ui-maker` workflow can compose and patch project UI safely.

For building or patching screens from an existing catalog, use [ui-maker](../ui-maker/SKILL.md).

## Core Authoring Principles

1. **Enforce the Blackbox + Parts contract.** Keep internal styling and sub-children encapsulated inside each Base Prefab. Expose only intentional `exposedProps`, insertion `slots`, and reusable child `parts` (`UIPrefabManifest` on nested prefabs).
2. **Keep `UICatalogRegistry` focused on Base Prefabs and Base Variants.** Register reusable Base Prefabs and intentional Base Variants (with `idOverride` and `categoryOverride` on `UICatalogRegistry.Entry`). Never register screen/popup instances so `ui_catalog.json` stays lean.
3. **Prefer `AIMetadataConfig` or `versionDefines` bridges for third-party / DLL types.** Never add hard compile dependencies from standalone game/foundation runtime assemblies onto `DucMinh.UiMaker.Runtime` unless desired; use `AIMetadataConfig` assets or Editor-only `versionDefines` bridges (`DM_UI_MAKER_PRESENT`).
4. **Always rebuild and verify `Assets/UnityMCP/ui_catalog.json`.** After changing manifests, registries, metadata configs, or `[AIManagedScript]` classes, run `rebuild_ui_catalog` (or click **Rebuild UI Catalog** in `UICatalogRegistryEditor`) and confirm zero contract errors.

## Authoring Workflows

### A. Scaffold and Refine a Base Prefab (`UIPrefabManifest`)

1. Ensure the Base Prefab has clean naming (`Content`, `Footer`, `Header`, `Title`, `Panel`, `Icon`, `Btn_Close`) or nested child prefabs with their own `UIPrefabManifest`.
2. Dry-run `scaffold_prefab_manifest` first (`apply: false`) with `autoInfer: true` or explicit `manifestJson`, then apply (`apply: true`).
3. Review the scaffolded `UIPrefabManifest` in Inspector (`UIPrefabManifestEditor`):
   - **Props Contract**: verify `propName`, `targetObject`, `componentName`, `propertyName`, `valueType`, and Image safety flags (`forceSliced`, `autoPreserveAspect`, `disableRaycast`). Use **Auto-Fill Default Props** to populate enabled `autoExposeOnScaffold` rules.
   - **Slots Contract**: verify `slotName`, `slotContainer`, `acceptedCategories` (`*` or comma-separated categories), and `allowedOperations` (`append,insert,replace,remove,reorder,clear`).
   - **Parts Contract**: verify `partName`, nested `targetManifest`, and `allowRectOverride`.
4. Run `rebuild_ui_catalog` to emit the prefab into `Assets/UnityMCP/ui_catalog.json`.

### B. Curate `UICatalogRegistry`

1. Create or select a `UICatalogRegistry` asset (`Assets > Create > DMCore > UI > AI > UI Catalog Registry`).
2. Drag Base Prefabs or folders into the drop zone in `UICatalogRegistryEditor`.
3. For Base Variants that inherit a base prefab's `UIPrefabManifest`, expand **Overrides** and set `idOverride`, `categoryOverride`, and `descriptionOverride`.
4. Note: When one or more `UICatalogRegistry` assets exist in the project, `UICatalogGenerator` sources `catalog.prefabs` exclusively from active entries in those registries. If none exist, it scans all prefabs and skips variants with un-overridden inherited `id`.

### C. Configure `AIMetadataConfig`

1. Create or select an `AIMetadataConfig` asset (`Assets > Create > DMCore > UI > AI > AI Metadata Config`). `AIMetadataConfigLoader` loads and applies all project `AIMetadataConfig` assets automatically on domain reload, asset import, and Inspector validation. Project C# scripts marked `[AIManagedScript]` + `[AIExposedField]` work automatically without entries here.
2. Configure sections as needed:
   - **Quick Add / Auto-Scan Containers**: click **Auto-Scan Containers** to auto-detect project ID -> Object/Prefab list components, or drop any GameObject / Component / MonoScript into **Drop Target** to add it directly to the matching section.
   - **1. View Scripts (Screen / Popup Controllers)**: register external or attribute-free controller scripts and check serialized child-binding fields inline.
   - **2. Custom UI Components (Extra Prefab Props)**: register custom UI display components and check properties inline (including optional `Auto` scaffold flag).
   - **3. Reference Containers (Optional)**: configure only if the project uses dedicated reference container components (`SceneObject` or `AssetObject`). Leave empty in standard `MonoBehaviour` projects.
   - **4. Built-in UGUI Defaults (Advanced)**: node overrides (`DefaultNodeContract`) and the 13 built-in UGUI contracts (`Image`, `TMP_Text`, `Text`, `SpriteAtlasLoader`, `GridLayoutGroup`, `Button`, `Toggle`, `Slider`, `ScrollRect`, `CanvasGroup`, `HorizontalLayoutGroup`, `VerticalLayoutGroup`, `LayoutElement`).

### D. Author a `versionDefines` Editor Bridge

When integrating an external package or DLL without modifying its runtime source or requiring an asset, create an Editor-only assembly with `versionDefines` for `"com.ducminh.ui-maker"` -> `"DM_UI_MAKER_PRESENT"` and register types via `AIMetadataRegistry.For<T>()` or `AIMetadataRegistry.For("Namespace.Type, Assembly")` inside an `[InitializeOnLoad]` class.

### E. Rebuild and Verify Catalog

1. Call `rebuild_ui_catalog` (no arguments required; ensure write tool is enabled in UnityMCP).
2. Inspect `success`, `prefabCount`, `scriptCount`, `errors`, and `warnings`.
3. Confirm `Assets/UnityMCP/ui_catalog.json` reflects expected `prefabs` (`props`, `slots`, `parts`), `scripts` (`fields`), and `referenceContainers`.

## References

- [Manifest, Metadata, and Editor Bridge Guide](references/manifest-and-metadata-guide.md): Complete `manifestJson` format, Blackbox vs Parts rules, scaffolder heuristics, `AIMetadataRegistry.For(...)` Fluent API, inheritance behavior, `ReferenceContainerRule` examples, and copy-ready `.asmdef` + C# Editor Bridge templates.
