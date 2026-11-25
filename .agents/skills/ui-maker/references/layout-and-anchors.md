# Layout, Anchors, and ScrollRect Rules

## 1. CRITICAL Padding Array Order Warning

`rect.padding` and `layoutGroup.padding` use DIFFERENT index orders! Never mix them up:

- `rect.padding` (`SemanticRectConfig`, `float[4]`): **`[left, bottom, right, top]`**
- `layoutGroup.padding` (`UILayoutGroupConfig` / Unity `RectOffset`, `int[4]`): **`[left, right, top, bottom]`**

## 2. All 14 SemanticAnchorPresets

`SemanticRectConfig.anchor` defaults to `"MiddleCenter"` if omitted in JSON (if `"anchor"` is explicitly provided, it must be non-empty). `"Center"` is accepted as an alias for `"MiddleCenter"`. All preset names are case-insensitive.

### Point Anchors (9 presets + 1 alias)

Point anchors require `size: [width, height]` (both finite, `>= 0`). Optional: `offset: [x, y]` (default `[0, 0]`) and `pivot: [px, py]` (overrides default pivot). Math: `sizeDelta = size`, `anchoredPosition = offset`.

| Preset | Alias | `anchorMin` | `anchorMax` | Default `pivot` | Required Fields |
| --- | --- | --- | --- | --- | --- |
| `TopLeft` | - | `(0, 1)` | `(0, 1)` | `(0, 1)` | `size: [w, h]` |
| `TopCenter` | - | `(0.5, 1)` | `(0.5, 1)` | `(0.5, 1)` | `size: [w, h]` |
| `TopRight` | - | `(1, 1)` | `(1, 1)` | `(1, 1)` | `size: [w, h]` |
| `MiddleLeft` | - | `(0, 0.5)` | `(0, 0.5)` | `(0, 0.5)` | `size: [w, h]` |
| `MiddleCenter` | `Center` | `(0.5, 0.5)` | `(0.5, 0.5)` | `(0.5, 0.5)` | `size: [w, h]` |
| `MiddleRight` | - | `(1, 0.5)` | `(1, 0.5)` | `(1, 0.5)` | `size: [w, h]` |
| `BottomLeft` | - | `(0, 0)` | `(0, 0)` | `(0, 0)` | `size: [w, h]` |
| `BottomCenter` | - | `(0.5, 0)` | `(0.5, 0)` | `(0.5, 0)` | `size: [w, h]` |
| `BottomRight` | - | `(1, 0)` | `(1, 0)` | `(1, 0)` | `size: [w, h]` |

### Stretch Anchors (5 presets)

Stretch anchors require `padding: [left, bottom, right, top]` (`float[4]`). Negative padding values are valid (useful for bleed or outset margins).

| Preset | `anchorMin` | `anchorMax` | Default `pivot` | Required Fields | Offset Math (`offsetMin`, `offsetMax`) |
| --- | --- | --- | --- | --- | --- |
| `StretchAll` | `(0, 0)` | `(1, 1)` | `(0.5, 0.5)` | `padding: [L, B, R, T]` | `(L, B)`, `(-R, -T)` |
| `TopStretch` | `(0, 1)` | `(1, 1)` | `(0.5, 1)` | `padding: [L, 0, R, T]`, `size: [0, H]` | `(L, -T - H)`, `(-R, -T)` |
| `BottomStretch` | `(0, 0)` | `(1, 0)` | `(0.5, 0)` | `padding: [L, B, R, 0]`, `size: [0, H]` | `(L, B)`, `(-R, B + H)` |
| `LeftStretch` | `(0, 0)` | `(0, 1)` | `(0, 0.5)` | `padding: [L, B, 0, T]`, `size: [W, 0]` | `(L, B)`, `(L + W, -T)` |
| `RightStretch` | `(1, 0)` | `(1, 1)` | `(1, 0.5)` | `padding: [0, B, R, T]`, `size: [W, 0]` | `(-R - W, B)`, `(-R, -T)` |

## 3. UILayoutGroupConfig

Used on `emptyRect` containers (or via `patch_ui_node` `setLayoutGroup`).

| Field | Type | Applies To | Values / Notes |
| --- | --- | --- | --- |
| `type` | `string` | All | `"Horizontal"`, `"Vertical"`, `"Grid"` (defaults to `"Vertical"` when omitted). |
| `spacing` | `float` or `[x, y]` | All | Scalar `float` for `Horizontal`/`Vertical`; `[x, y]` (`float[2]`) for `Grid`. |
| `cellSize` | `float[2]` | `Grid` | `[width, height]`, both finite and `>= 0`. |
| `padding` | `int[4]` | All | **`[left, right, top, bottom]`** (`RectOffset` order!). |
| `childAlignment` | `string` | All | `TextAnchor`: `UpperLeft`, `UpperCenter`, `UpperRight`, `MiddleLeft`, `MiddleCenter`, `MiddleRight`, `LowerLeft`, `LowerCenter`, `LowerRight`. |
| `childControlWidth` | `bool` | H / V | Controls child width via `LayoutElement` / rect. |
| `childControlHeight` | `bool` | H / V | Controls child height via `LayoutElement` / rect. |
| `childForceExpandWidth` | `bool` | H / V | Forces children to share extra horizontal space. |
| `childForceExpandHeight` | `bool` | H / V | Forces children to share extra vertical space. |
| `childScaleWidth` | `bool` | H / V | Uses child `localScale.x` in layout calculation. |
| `childScaleHeight` | `bool` | H / V | Uses child `localScale.y` in layout calculation. |
| `reverseArrangement` | `bool` | H / V | Reverses child ordering along the layout axis. |
| `startCorner` | `string` | `Grid` | `UpperLeft`, `UpperRight`, `LowerLeft`, `LowerRight`. |
| `startAxis` | `string` | `Grid` | `Horizontal`, `Vertical`. |
| `constraint` | `string` | `Grid` | `Flexible`, `FixedColumnCount`, `FixedRowCount`. |
| `constraintCount` | `int` | `Grid` | Must be `> 0` when `FixedColumnCount` or `FixedRowCount` is used. |

## 4. UIExplicitLayoutElementConfig (`layoutElement` / `setLayoutElement`)

Explicit `LayoutElement` settings take precedence over automatic `LayoutGroupAdapter` calculations.

| Field | Type | Notes |
| --- | --- | --- |
| `ignoreLayout` | `bool` | Excludes this child from parent `LayoutGroup` (great for floating badges or dividers). |
| `minWidth` | `float` | Minimum width for layout group sizing. |
| `minHeight` | `float` | Minimum height for layout group sizing. |
| `preferredWidth` | `float` | Preferred width; overrides automatic `rect.Width` adaptation. |
| `preferredHeight` | `float` | Preferred height; overrides automatic `rect.Height` adaptation. |
| `flexibleWidth` | `float` | Extra horizontal stretch weight (`0` = fixed, `1` = absorb remaining width). |
| `flexibleHeight` | `float` | Extra vertical stretch weight (`0` = fixed, `1` = absorb remaining height). |
| `layoutPriority` | `int` | Priority when multiple `ILayoutElement` components exist. |

### Smart LayoutGroupAdapter Behavior

When a node is instantiated under a `HorizontalLayoutGroup` or `VerticalLayoutGroup` (and `ignoreLayout` is not `true`):
- Under `HorizontalLayoutGroup`: if `layoutElement.preferredWidth` is omitted and `rect.Width > 0`, `LayoutElement.preferredWidth` is automatically set to `rect.Width`. If `childControlHeight == true` and `childForceExpandHeight == false` and `layoutElement.preferredHeight` is omitted and `rect.Height > 0`, `LayoutElement.preferredHeight` is set to `rect.Height`.
- Under `VerticalLayoutGroup`: if `layoutElement.preferredHeight` is omitted and `rect.Height > 0`, `LayoutElement.preferredHeight` is automatically set to `rect.Height`. If `childControlWidth == true` and `childForceExpandWidth == false` and `layoutElement.preferredWidth` is omitted and `rect.Width > 0`, `LayoutElement.preferredWidth` is set to `rect.Width`.

## 5. UIContentSizeFitterConfig (`contentSizeFitter` / `setContentSizeFitter`)

| Field | Type | Allowed Values |
| --- | --- | --- |
| `horizontalFit` | `string` | `"Unconstrained"`, `"MinSize"`, `"PreferredSize"` |
| `verticalFit` | `string` | `"Unconstrained"`, `"MinSize"`, `"PreferredSize"` |

## 6. Critical GridLayoutGroup and ScrollRect Rules

### Rule A: Fatal Grid `cellSize` Conflict

When a parent container has `GridLayoutGroup` (either `layoutGroup.type == "Grid"` with `cellSize: [W, H]` or a `useBase` container with `props.cellSize: [W, H]`):
- **Never give a direct child a `rect.size` that differs from `[W, H]`** (tolerance `0.01`). `UILayoutBuilder` treats mismatched child `rect.size` as a **fatal validation error**.
- Either omit `rect` on Grid children, use `"anchor": "StretchAll", "padding": [0, 0, 0, 0]`, or set `rect.size` to `[W, H]` verbatim.
- `GridLayoutGroup` ignores `LayoutElement.preferredWidth` and `preferredHeight`.

### Rule B: ScrollRect + ContentSizeFitter Setup

1. **Vertical Scroll (`Base_V_Scroll`)**:
   - The `Content` object (inside `Viewport`) grows downward from the top.
   - Use `"anchor": "TopStretch"` on `Content` (never `"StretchAll"` with `ContentSizeFitter`, which conflicts with `PreferredSize` vertical resizing!).
   - Configure `Content` with `VerticalLayoutGroup` (`childControlWidth: true`, `childControlHeight: true`, `childForceExpandWidth: true`, `childForceExpandHeight: false`) and `contentSizeFitter: { "horizontalFit": "Unconstrained", "verticalFit": "PreferredSize" }`.
   - For a vertically scrolling grid inside `Base_V_Scroll`, place an `emptyRect` inside `slotChildren.content` with `layoutGroup: { "type": "Grid", "constraint": "FixedColumnCount", "constraintCount": 3, ... }` and `contentSizeFitter: { "horizontalFit": "Unconstrained", "verticalFit": "PreferredSize" }`, or set `rect` of cards using `layoutElement.preferredHeight`.
2. **Horizontal Scroll (`Base_H_Scroll`)**:
   - Use `"anchor": "LeftStretch"`, `HorizontalLayoutGroup`, and `contentSizeFitter: { "horizontalFit": "PreferredSize", "verticalFit": "Unconstrained" }`.
3. **Avoid `childForceExpandHeight: true` on Vertical Scroll items** unless every item should expand to fill the entire viewport height. Set `childForceExpandHeight: false` and give each item an explicit `rect.size` height or `layoutElement.preferredHeight`.
