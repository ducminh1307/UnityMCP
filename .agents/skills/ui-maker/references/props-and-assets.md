# Component Props, Colors, and Asset Syntax Reference

Only properties exposed on a Base Prefab's `UIPrefabManifest` (discoverable via `get_ui_catalog`) can be passed in `props` or `setProps`. The logical `propName` in the catalog maps to a specific component and property documented below.

## 1. Built-In Node Properties

| Component | Property | Value Type | Notes |
| --- | --- | --- | --- |
| `GameObject` | `active` | `bool` | Toggles `GameObject.SetActive`. Also supported as first-class `node.active` / `patch.setActive` when `DefaultNodeContract.allowActiveOverrideByDefault` is `true`. |
| `GameObject` | `name` | `string` | Sets GameObject name. Also supported via `node.name` when `allowNameOverrideByDefault` is `true`. |
| `RectTransform` | `semanticRect` | `RectConfig` (`object`) | Applies a `SemanticRectConfig` object (or JSON string) via `SemanticAnchorEngine`. Also supported as first-class `node.rect` / `patch.setRect`. |

## 2. Image and SpriteAtlasLoader

### `Image` (`UnityEngine.UI.Image`)

| Property / Alias | Value Type | Accepted Values & Behavior |
| --- | --- | --- |
| `sprite` | `Sprite` (`string`) | Resolved via `DefaultUIAssetResolver`. Applies manifest safety flags (`forceSliced`, `autoPreserveAspect`, `disableRaycast`) and `AutoAdjustSlicedPixelsPerUnit`. |
| `color` | `Color` | Hex string, HTML name, or `[r, g, b, a]` array (see Section 5). |
| `material` | `Material` (`string`) | Resolved via `DefaultUIAssetResolver` as `Material`. |
| `raycastTarget` | `bool` | Enables or disables graphic raycast hit testing. |
| `type` / `imageType` | `string` | `"Simple"`, `"Sliced"`, `"Tiled"`, `"Filled"` (case-insensitive `Image.Type`). Sliced automatically adjusts `pixelsPerUnitMultiplier` if 9-slice borders exceed rect dimensions. |
| `preserveAspect` | `bool` | Preserves sprite aspect ratio. |
| `pixelsPerUnitMultiplier` / `ppuMultiplier` | `float` | Multiplier applied to sprite PPU for `Sliced` and `Tiled` modes. |
| `fillAmount` | `float` | `0.0` to `1.0` when `type` is `"Filled"`. |
| `fillMethod` | `string` | `"Horizontal"`, `"Vertical"`, `"Radial90"`, `"Radial180"`, `"Radial360"` (`Image.FillMethod`). |
| `fillClockwise` | `bool` | Direction for radial fill modes. |
| `imageConfig` | `object` | Composite object containing any combination of the `Image` keys above (e.g. `{ "sprite": "Assets/UI/bg.png", "type": "Sliced", "color": "#FFFFFFCC" }`). |

### `SpriteAtlasLoader`

| Property / Alias | Value Type | Accepted Values & Behavior |
| --- | --- | --- |
| `spriteReference` / `atlasSprite` / `sprite` | `string` | Accepts `"atlas:AtlasName/SpriteName"` or `"AtlasName/SpriteName"`. Splits on `/` to populate `atlasType` / `atlas` and `spriteName` fields/properties and triggers sprite refresh (`ApplySprite`, `LoadSprite`, etc.). |

## 3. Text Components (`TMP_Text` and `Text`)

### `TMP_Text` (`TMPro.TextMeshProUGUI` / `TMPro.TMP_Text`)

| Property / Alias | Value Type | Accepted Values & Behavior |
| --- | --- | --- |
| `text` | `string` | Displayed text content. |
| `fontSize` / `size` | `float` | Font size in points. |
| `fontColor` / `color` / `vertexColor` | `Color` | Text vertex color. |
| `alignment` | `string` | `TMPro.TextAlignmentOptions` enum name (e.g. `"Center"`, `"Left"`, `"Right"`, `"TopLeft"`, `"Top"`, `"MidlineLeft"`, `"BottomRight"`). |
| `fontStyle` / `style` | `string` | `TMPro.FontStyles` enum name or comma-separated flags (e.g. `"Normal"`, `"Bold"`, `"Italic"`, `"Underline"`, `"UpperCase"`, `"LowerCase"`, `"SmallCaps"`, `"Strikethrough"`). |
| `enableAutoSizing` / `autoSize` | `bool` | Enables TMP auto-sizing within bounds. |
| `characterSpacing` | `float` | Additional character spacing. |
| `lineSpacing` | `float` | Additional line spacing. |
| `wordSpacing` | `float` | Additional word spacing. |
| `font` / `fontAsset` | `string` | Asset identifier resolved to `TMP_FontAsset`. |
| `material` / `materialPreset` / `fontMaterial` / `fontSharedMaterial` | `Material` (`string`) | Resolved via `DefaultUIAssetResolver` to `Material` and assigned to `fontSharedMaterial` / `fontMaterial`. |
| `textConfig` / `tmpConfig` | `object` | Composite object combining any of the `TMP_Text` keys above (e.g. `{ "text": "SHOP", "fontSize": 42, "fontColor": "#FFD166", "alignment": "Center" }`). |

### `Text` (`UnityEngine.UI.Text` legacy)

| Property / Alias | Value Type | Accepted Values & Behavior |
| --- | --- | --- |
| `text` | `string` | Displayed string content. |
| `fontSize` / `size` | `float` | Rounded to integer `fontSize`. |
| `fontColor` / `color` | `Color` | Text color. |
| `alignment` | `string` | `UnityEngine.TextAnchor` enum name (`UpperLeft` .. `LowerRight`). |
| `fontStyle` / `style` | `string` | `UnityEngine.FontStyle`: `"Normal"`, `"Bold"`, `"Italic"`, `"BoldAndItalic"`. |
| `textConfig` | `object` | Composite object combining `text`, `fontSize`, `color`/`fontColor`, `alignment`, `fontStyle`. |

## 4. Standard UGUI Interactive and Layout Components

| Component | Property | Type | Notes |
| --- | --- | --- | --- |
| `Button` | `interactable` | `bool` | Enables/disables button interaction. |
| `Toggle` | `isOn` | `bool` | Toggle checked state. |
| `Toggle` | `interactable` | `bool` | Enables/disables toggle interaction. |
| `Slider` | `value` | `float` | Current slider value. |
| `Slider` | `minValue` | `float` | Minimum slider value. |
| `Slider` | `maxValue` | `float` | Maximum slider value. |
| `Slider` | `wholeNumbers` | `bool` | Snap slider values to integers. |
| `Slider` | `interactable` | `bool` | Enables/disables slider interaction. |
| `ScrollRect` | `horizontal` | `bool` | Allow horizontal scrolling. |
| `ScrollRect` | `vertical` | `bool` | Allow vertical scrolling. |
| `ScrollRect` | `movementType` | `string` | `"Unrestricted"`, `"Elastic"`, `"Clamped"`. |
| `ScrollRect` | `elasticity` | `float` | Bounce back factor in Elastic mode. |
| `ScrollRect` | `inertia` | `bool` | Continue scroll momentum after drag release. |
| `ScrollRect` | `scrollSensitivity` | `float` | Scroll wheel sensitivity. |
| `CanvasGroup` | `alpha` | `float` | Group opacity (`0.0` .. `1.0`). |
| `CanvasGroup` | `interactable` | `bool` | Group input interactability. |
| `CanvasGroup` | `blocksRaycasts` | `bool` | Group raycast blocking. |
| `CanvasGroup` | `ignoreParentGroups` | `bool` | Ignore ancestor `CanvasGroup` settings. |
| `HorizontalLayoutGroup` | `spacing`, `childControlWidth`, `childControlHeight`, `childForceExpandWidth`, `childForceExpandHeight` | `float` / `bool` | Horizontal layout group properties. |
| `VerticalLayoutGroup` | `spacing`, `childControlWidth`, `childControlHeight`, `childForceExpandWidth`, `childForceExpandHeight` | `float` / `bool` | Vertical layout group properties. |
| `GridLayoutGroup` | `cellSize`, `spacing`, `constraint`, `constraintCount`, `childAlignment` | `Vector2` (`[w, h]`) / `string` / `int` | Grid layout properties (note: children must not specify conflicting `rect.size`). |
| `LayoutElement` | `preferredWidth`, `preferredHeight`, `minWidth`, `minHeight`, `flexibleWidth`, `flexibleHeight`, `ignoreLayout` | `float` / `bool` | Layout element overrides. |

## 5. Color Formats (`ParseColor`)

All `Color` properties accept any of the following formats:

- `#RRGGBB`: e.g. `"#FF8800"` (alpha defaults to `FF`).
- `#RRGGBBAA`: e.g. `"#FF8800CC"`.
- `#RGB` / `#RGBA`: standard Unity HTML shorthand, e.g. `"#F80"`.
- Unprefixed 6-char or 8-char hex: `"FF0000FF"`, `"1A2B3C"`.
- Unity HTML named colors: `"white"`, `"black"`, `"red"`, `"green"`, `"blue"`, `"yellow"`, `"cyan"`, `"magenta"`, `"grey"`, `"gray"`, `"clear"`.
- Normalized float JSON array (`0.0` to `1.0`): `[r, g, b]` or `[r, g, b, a]`, e.g. `[0.15, 0.2, 0.3, 0.95]`.

## 6. Asset Identifier Syntax (`DefaultUIAssetResolver`)

Used for `Sprite`, `Material`, `TMP_FontAsset`, and asset paths resolved during build/patch:

1. **Direct AssetDatabase path (recommended):**
   - `"Assets/UI/Sprites/panel_frame.png"`
   - `"Packages/com.mygame.ui/Assets/icon.png"`
   - If a texture contains multiple Sprite sub-assets, direct path returns `Ambiguous`; use `atlas:` notation or split assets.
2. **SpriteAtlas notation:**
   - `"atlas:Common/icon_coin"` (searches `SpriteAtlas` by unique asset name `Common`, then resolves `icon_coin`).
   - `"atlas:Assets/Atlases/Common.spriteatlas/icon_coin"` (explicit atlas path when atlas short names collide).
3. **Asset GUID:**
   - `"guid:0123456789abcdef0123456789abcdef"` or raw `"0123456789abcdef0123456789abcdef"` (32 hex chars).
4. **Short-name search (fallback only):**
   - `"icon_coin"` searches by name and asset type, but fails with `Ambiguous` if two assets share the name. Prefer explicit `"Assets/..."`, `"atlas:..."`, or `"guid:..."`.
