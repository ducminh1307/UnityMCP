# Examples and Troubleshooting

All JSON payloads below follow the strict DTO schemas in [json-schema.md](json-schema.md). Replace prefab IDs, asset paths, and `rootScript.fullTypeName` values with exact entries returned by `get_ui_catalog` in your project.

## Example 1: Modal Shop Popup

Uses `Base_Popup`, nested `partOverrides.panel` (`Base_Image`), a 3-column fixed `Grid` inside `content`, horizontal footer buttons in `bottom`, `rootScript` with `bindToRoot`, and `refs.objectRef` (including `componentType`).

```json
{
  "id": "shop_popup_root",
  "name": "ShopPopup",
  "useBase": "Base_Popup",
  "rect": {
    "anchor": "StretchAll",
    "padding": [0, 0, 0, 0]
  },
  "props": {
    "windowRect": {
      "anchor": "MiddleCenter",
      "size": [780, 960]
    },
    "showClose": true,
    "closeOnOverlay": true,
    "showUIBackground": true
  },
  "partOverrides": {
    "panel": {
      "props": {
        "baseimageColor": "#1E2436F5",
        "baseimageType": "Sliced"
      }
    }
  },
  "rootScript": {
    "fullTypeName": "Game.UI.ShopPopupView, Assembly-CSharp",
    "fields": {
      "currencyCode": "GOLD"
    }
  },
  "refs": {
    "objectRef": [
      {
        "id": "title_text",
        "targetNodeId": "shop_title",
        "componentType": "TMP_Text"
      },
      {
        "id": "items_grid",
        "targetNodeId": "shop_grid"
      },
      {
        "id": "buy_button",
        "targetNodeId": "btn_buy",
        "componentType": "Button"
      }
    ]
  },
  "slotChildren": {
    "top": [
      {
        "id": "shop_title",
        "useBase": "Base_Text",
        "rect": {
          "anchor": "StretchAll",
          "padding": [24, 8, 24, 8]
        },
        "props": {
          "text": "DAILY SHOP",
          "fontSize": 40,
          "color": "#FFD166FF",
          "alignment": "Center",
          "fontStyle": "Bold"
        }
      }
    ],
    "content": [
      {
        "id": "shop_grid",
        "name": "ItemsGrid",
        "kind": "emptyRect",
        "rect": {
          "anchor": "StretchAll",
          "padding": [20, 20, 20, 20]
        },
        "layoutGroup": {
          "type": "Grid",
          "cellSize": [220, 260],
          "spacing": [16, 16],
          "padding": [12, 12, 12, 12],
          "startCorner": "UpperLeft",
          "startAxis": "Horizontal",
          "constraint": "FixedColumnCount",
          "constraintCount": 3,
          "childAlignment": "UpperCenter"
        },
        "children": [
          {
            "id": "offer_slot_1",
            "name": "OfferSlot1",
            "useBase": "Base_Image",
            "props": {
              "baseimageColor": "#2C354DFF",
              "baseimageType": "Sliced"
            }
          },
          {
            "id": "offer_slot_2",
            "name": "OfferSlot2",
            "useBase": "Base_Image",
            "props": {
              "baseimageColor": "#2C354DFF",
              "baseimageType": "Sliced"
            }
          },
          {
            "id": "offer_slot_3",
            "name": "OfferSlot3",
            "useBase": "Base_Image",
            "props": {
              "baseimageColor": "#2C354DFF",
              "baseimageType": "Sliced"
            }
          }
        ]
      }
    ],
    "bottom": [
      {
        "id": "shop_footer_row",
        "name": "FooterButtons",
        "kind": "emptyRect",
        "rect": {
          "anchor": "StretchAll",
          "padding": [24, 16, 24, 16]
        },
        "layoutGroup": {
          "type": "Horizontal",
          "spacing": 20,
          "padding": [0, 0, 0, 0],
          "childAlignment": "MiddleCenter",
          "childControlWidth": true,
          "childControlHeight": true,
          "childForceExpandWidth": false,
          "childForceExpandHeight": false
        },
        "children": [
          {
            "id": "btn_refresh",
            "useBase": "Base_Button",
            "rect": {
              "anchor": "MiddleCenter",
              "size": [220, 76]
            },
            "partOverrides": {
              "buttonText": {
                "props": {
                  "text": "REFRESH",
                  "fontSize": 26,
                  "color": "#FFFFFFFF"
                }
              },
              "buttonImage": {
                "props": {
                  "baseimageColor": "#455A7DFF"
                }
              }
            }
          },
          {
            "id": "btn_buy",
            "useBase": "Base_Button",
            "bindToRoot": "primaryActionButton",
            "rect": {
              "anchor": "MiddleCenter",
              "size": [260, 76]
            },
            "partOverrides": {
              "buttonText": {
                "props": {
                  "text": "BUY BUNDLE",
                  "fontSize": 28,
                  "color": "#1A1E29FF"
                }
              },
              "buttonImage": {
                "props": {
                  "baseimageColor": "#06D6A0FF"
                }
              }
            }
          }
        ]
      }
    ]
  }
}
```

## Example 2: Vertical Scroll Quest / Inventory Screen

Uses an `emptyRect` root screen hosting a header and `Base_V_Scroll`. Inside `Base_V_Scroll`'s `content` slot, an `emptyRect` list container uses `Vertical` layout and `contentSizeFitter` (`verticalFit: "PreferredSize"`) with fixed-height item cards.

```json
{
  "id": "quest_screen_root",
  "name": "QuestScreen",
  "kind": "emptyRect",
  "rect": {
    "anchor": "StretchAll",
    "padding": [0, 0, 0, 0]
  },
  "children": [
    {
      "id": "quest_bg",
      "useBase": "Base_Image",
      "rect": {
        "anchor": "StretchAll",
        "padding": [0, 0, 0, 0]
      },
      "props": {
        "baseimageColor": "#121622FF",
        "baseimageRaycastTarget": true
      }
    },
    {
      "id": "quest_header_title",
      "useBase": "Base_Text",
      "rect": {
        "anchor": "TopStretch",
        "padding": [32, 0, 32, 36],
        "size": [0, 72]
      },
      "props": {
        "text": "DAILY QUESTS",
        "fontSize": 42,
        "color": "#FFFFFFFF",
        "alignment": "Center",
        "fontStyle": "Bold"
      }
    },
    {
      "id": "quest_scroll",
      "useBase": "Base_V_Scroll",
      "rect": {
        "anchor": "StretchAll",
        "padding": [28, 36, 28, 124]
      },
      "slotChildren": {
        "content": [
          {
            "id": "quest_list_stack",
            "name": "QuestListStack",
            "kind": "emptyRect",
            "rect": {
              "anchor": "TopStretch",
              "padding": [0, 0, 0, 0],
              "size": [0, 420]
            },
            "layoutGroup": {
              "type": "Vertical",
              "spacing": 14,
              "padding": [8, 8, 8, 8],
              "childAlignment": "UpperCenter",
              "childControlWidth": true,
              "childControlHeight": true,
              "childForceExpandWidth": true,
              "childForceExpandHeight": false
            },
            "contentSizeFitter": {
              "horizontalFit": "Unconstrained",
              "verticalFit": "PreferredSize"
            },
            "children": [
              {
                "id": "quest_card_1",
                "name": "QuestCard1",
                "kind": "emptyRect",
                "rect": {
                  "anchor": "TopCenter",
                  "size": [900, 148]
                },
                "layoutElement": {
                  "minHeight": 148,
                  "preferredHeight": 148,
                  "flexibleHeight": 0
                },
                "children": [
                  {
                    "id": "quest_card_1_bg",
                    "useBase": "Base_Image",
                    "rect": {
                      "anchor": "StretchAll",
                      "padding": [0, 0, 0, 0]
                    },
                    "props": {
                      "baseimageColor": "#232B40FF",
                      "baseimageType": "Sliced"
                    }
                  },
                  {
                    "id": "quest_card_1_title",
                    "useBase": "Base_Text",
                    "rect": {
                      "anchor": "TopStretch",
                      "padding": [20, 0, 220, 16],
                      "size": [0, 44]
                    },
                    "props": {
                      "text": "Win 3 Arena Matches",
                      "fontSize": 28,
                      "color": "#F4F6FBFF",
                      "alignment": "MidlineLeft"
                    }
                  },
                  {
                    "id": "quest_card_1_progress",
                    "useBase": "Base_Slider",
                    "rect": {
                      "anchor": "BottomStretch",
                      "padding": [20, 20, 220, 0],
                      "size": [0, 28]
                    },
                    "props": {
                      "minValue": 0,
                      "maxValue": 3,
                      "wholeNumbers": true,
                      "value": 2,
                      "interactable": false
                    }
                  },
                  {
                    "id": "quest_card_1_claim",
                    "useBase": "Base_Button",
                    "rect": {
                      "anchor": "MiddleRight",
                      "size": [172, 68],
                      "offset": [-20, 0]
                    },
                    "partOverrides": {
                      "buttonText": {
                        "props": {
                          "text": "2 / 3",
                          "fontSize": 24,
                          "color": "#FFFFFFFF"
                        }
                      },
                      "buttonImage": {
                        "props": {
                          "baseimageColor": "#3A4663FF"
                        }
                      }
                    }
                  }
                ]
              }
            ]
          }
        ]
      }
    }
  ]
}
```

## Example 3: Multi-Level Composite `partOverrides`

Demonstrates two patterns:
1. `Base_Popup` overriding its `panel` part (`Base_Image`) while hosting `Base_Button` children that override their own `buttonText` (`Base_Text`) and `buttonImage` (`Base_Image`) parts.
2. Recursive nested `partOverrides` inside a composite part (`root.partOverrides.headerCard.partOverrides.confirmButton.partOverrides.buttonText`).

```json
{
  "id": "composite_reward_dialog",
  "name": "CompositeRewardDialog",
  "useBase": "Base_Popup",
  "props": {
    "windowRect": {
      "anchor": "MiddleCenter",
      "size": [720, 620]
    },
    "showClose": false
  },
  "partOverrides": {
    "panel": {
      "active": true,
      "rect": {
        "anchor": "StretchAll",
        "padding": [8, 8, 8, 8]
      },
      "props": {
        "baseimageColor": "#1B2234FA",
        "baseimageType": "Sliced"
      }
    }
  },
  "slotChildren": {
    "bottom": [
      {
        "id": "claim_button",
        "useBase": "Base_Button",
        "rect": {
          "anchor": "MiddleCenter",
          "size": [300, 88]
        },
        "partOverrides": {
          "buttonImage": {
            "props": {
              "baseimageColor": "#FFB703FF",
              "baseimageType": "Sliced"
            }
          },
          "buttonText": {
            "rect": {
              "anchor": "StretchAll",
              "padding": [16, 8, 16, 8]
            },
            "props": {
              "text": "CLAIM REWARD",
              "fontSize": 30,
              "color": "#141824FF",
              "fontStyle": "Bold"
            }
          }
        }
      }
    ]
  }
}
```

If your project defines a deeper composite prefab (for example `Base_Screen -> part "headerCard" (Base_Header) -> part "confirmButton" (Base_Button) -> part "buttonText" (Base_Text)`), override all levels cleanly in one `partOverrides` tree:

```json
{
  "id": "nested_composite_example",
  "useBase": "Base_Screen",
  "partOverrides": {
    "headerCard": {
      "active": true,
      "partOverrides": {
        "confirmButton": {
          "partOverrides": {
            "buttonText": {
              "props": {
                "text": "CONTINUE",
                "fontSize": 26
              }
            }
          }
        }
      }
    }
  }
}
```

## Example 4: Surgical `patch_ui_node` Recipes

Always call `unity command ui_ai patch_ui_node` first with `--dry-run true`, then omit `--dry-run` (or pass `--dry-run false`) to apply.

### 4a. Update Root Props + Nested `partOverrides`

```json
{
  "targetNodePath": ".",
  "setProps": {
    "showClose": true,
    "closeOnOverlay": false
  },
  "partOverrides": {
    "panel": {
      "props": {
        "baseimageColor": "#26304BFF"
      }
    }
  }
}
```

### 4b. Slot Operations (`insert`, `replace`, `clear`)

Insert a new button into `bottom` at index `0`, replace an existing card by `nodeId` on a managed `emptyRect` (`slotName: "children"`), or clear a slot:

```json
{
  "targetNodeId": "shop_popup_root",
  "slotOperations": [
    {
      "slotName": "bottom",
      "action": "insert",
      "index": 0,
      "node": {
        "id": "btn_info",
        "useBase": "Base_Button",
        "rect": {
          "anchor": "MiddleCenter",
          "size": [140, 76]
        },
        "partOverrides": {
          "buttonText": {
            "props": {
              "text": "INFO",
              "fontSize": 24
            }
          }
        }
      }
    }
  ]
}
```

Replace or clear children on a managed `emptyRect` container (`slotName: "children"` or `"."`):

```json
{
  "targetNodeId": "shop_grid",
  "slotOperations": [
    {
      "slotName": "children",
      "action": "replace",
      "nodeId": "offer_slot_2",
      "node": {
        "id": "offer_slot_2_featured",
        "useBase": "Base_Image",
        "props": {
          "baseimageColor": "#3D4C73FF"
        }
      }
    }
  ]
}
```

```json
{
  "targetNodeId": "shop_grid",
  "slotOperations": [
    {
      "slotName": "children",
      "action": "clear"
    }
  ]
}
```

### 4c. Patch `emptyRect` LayoutGroup, LayoutElement, and ContentSizeFitter

```json
{
  "targetNodeId": "quest_list_stack",
  "setLayoutGroup": {
    "type": "Vertical",
    "spacing": 18,
    "padding": [12, 12, 16, 16],
    "childControlWidth": true,
    "childControlHeight": true,
    "childForceExpandWidth": true,
    "childForceExpandHeight": false
  },
  "setLayoutElement": {
    "minHeight": 200,
    "flexibleWidth": 1
  },
  "setContentSizeFitter": {
    "horizontalFit": "Unconstrained",
    "verticalFit": "PreferredSize"
  }
}
```

### 4d. Non-Destructive `mergeRefs` (Upsert + `remove: true`)

Use `mergeRefs` to update or remove specific reference entries without wiping inherited base prefab `ObjectRef` slots (whereas `setRefs` replaces the entire array for each specified key):

```json
{
  "targetNodePath": ".",
  "mergeRefs": {
    "objectRef": [
      {
        "id": "title_text",
        "targetNodeId": "shop_title",
        "componentType": "TMP_Text"
      },
      {
        "id": "legacy_subtitle",
        "remove": true
      }
    ]
  }
}
```

## Troubleshooting Guide

| Error / Symptom | Root Cause | Exact Fix |
| --- | --- | --- |
| Preview ran without changing scene or asset | `--dry-run true` was passed on `build_ui_from_json`, `patch_ui_node`, or `scaffold_prefab_manifest` | Omit `--dry-run` or pass `--dry-run false` to execute the mutation. |
| `preview_ui` response omitted inline `base64` | `--save-path` was passed without `--include-inline-image true` | Read the PNG from `savedPath`, or pass `--include-inline-image true` (or omit `--save-path`). |
| `Unknown key 'slots'` / `'parts'` / `'props'` / `'width'` | Used wrong DTO field name | Use `slotChildren`, `partOverrides`, `props` on `UILayoutNode`; `setProps`/`setRect`/`slotOperations` on `UIPatchDocument`; `size: [w, h]` and `offset: [x, y]` on `SemanticRectConfig`. |
| Stretch preset padding looks rotated or wrong edges | Swapped `rect.padding` and `layoutGroup.padding` orders | Remember `rect.padding` is `[left, bottom, right, top]` while `layoutGroup.padding` is `[left, right, top, bottom]`. |
| `Child node ... conflicts with parent GridLayoutGroup.cellSize` | Grid child `rect.size` differs from parent `cellSize` | Omit `rect.size` on Grid children (use `StretchAll` or omit `rect`), or set `rect.size` equal to parent `cellSize`. |
| ScrollRect Content collapses to 0 height or overflows weirdly | Used `StretchAll` with `ContentSizeFitter`, or `childForceExpandHeight: true` | Anchor Content to `TopStretch` (vertical scroll) or `LeftStretch` (horizontal scroll), set `verticalFit: "PreferredSize"`, and set `childForceExpandHeight: false`. |
| `emptyRect node ... cannot have 'props' / 'slotChildren' / 'partOverrides'` | Attached prefab-only fields to an `emptyRect` | Use a `useBase` prefab for visual/prop nodes, or move child list to `children` on `emptyRect`. |
| `saveAsPrefabPath ... already exists` | `build_ui_from_json` defaults `overwriteExistingPrefab: false` | Pass `overwriteExistingPrefab: true` to regenerate in-place (preserving GUID), or use `patch_ui_node` for surgical edits. |
| `Target node ... is not exposed in its 'parts' contract` | Attempted to patch an internal prefab child directly | Expose the child in the parent prefab's `UIPrefabManifest.parts` or patch via an exposed prop on the parent. |
| `preview_ui` issue `zero_size` | Parent `Horizontal`/`VerticalLayoutGroup` has `childControlWidth`/`Height: true` on a child with 0 preferred size | Add `layoutElement: { "preferredWidth": ..., "preferredHeight": ... }` or give the child a `rect.size`. |
| `preview_ui` issue `text_overflow` or `text_truncated` | Fixed text rect is too small for string length or `NoWrap` is set | Increase rect width/height, lower `fontSize`, enable `enableAutoSizing: true`, or wrap inside a `ContentSizeFitter` container. |
| `preview_ui` issue `out_of_bounds` | Element extends past viewport edges at target resolution | Switch from fixed large `size` to stretch anchors (`StretchAll`/`TopStretch`) or reduce size/padding. |
| `preview_ui` issue `sibling_overlap` | Two sibling visuals overlap without a `LayoutGroup` or `ignoreLayout` | Place siblings inside a `Horizontal`/`Vertical`/`Grid` `emptyRect`, adjust offsets, or separate into distinct layers. |
| `preview_ui` issue `missing_sprite` / `missing_font` | `Image.sprite` is null on non-Mask visible image, or `TMP_Text.font` is null | Assign a valid `Assets/...` or `atlas:Atlas/Sprite` / font asset in `props` or on the Base Prefab. |
