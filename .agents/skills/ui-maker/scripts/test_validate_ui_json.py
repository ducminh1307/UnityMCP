#!/usr/bin/env python3
"""Tests for validate_ui_json.py and UI Maker skill suite assets."""

from __future__ import annotations

import copy
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

SCRIPTS_DIR = Path(__file__).resolve().parent
SKILLS_ROOT = SCRIPTS_DIR.parents[1]
if str(SCRIPTS_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPTS_DIR))

import validate_ui_json as v  # noqa: E402


DMCORE_CATALOG_PATH = Path(
    r"D:\UnityProjects\DMCore\UnityHost\Assets\UnityMCP\ui_catalog.json"
)


def make_sample_catalog() -> dict:
    return {
        "schemaVersion": 1,
        "prefabs": {
            "Base_Popup": {
                "id": "Base_Popup",
                "manifestVersion": 1,
                "category": "Popup",
                "prefabPath": "Assets/Base_Popup.prefab",
                "props": {
                    "windowRect": {
                        "targetPath": "Root",
                        "component": "RectTransform",
                        "property": "semanticRect",
                        "type": "RectConfig",
                    },
                    "showClose": {
                        "targetPath": "Root/Close",
                        "component": "GameObject",
                        "property": "active",
                        "type": "bool",
                    },
                },
                "slots": {
                    "content": {
                        "path": "Root/Content",
                        "acceptedCategories": ["*"],
                        "operations": ["append", "insert", "replace", "remove", "reorder", "clear"],
                    },
                    "footer": {
                        "path": "Root/Footer",
                        "acceptedCategories": ["Button", "Container"],
                        "operations": ["append", "remove", "reorder"],
                    },
                },
                "parts": {
                    "panel": {
                        "path": "Root/Panel",
                        "baseId": "Base_Image",
                        "allowRectOverride": True,
                    },
                    "lockedHeader": {
                        "path": "Root/Header",
                        "baseId": "Base_Image",
                        "allowRectOverride": False,
                    },
                    "confirmButton": {
                        "path": "Root/Footer/ConfirmButton",
                        "baseId": "Base_Button",
                        "allowRectOverride": True,
                    },
                },
            },
            "Base_Button": {
                "id": "Base_Button",
                "manifestVersion": 1,
                "category": "Button",
                "prefabPath": "Assets/Base_Button.prefab",
                "props": {
                    "interactable": {
                        "targetPath": ".",
                        "component": "Button",
                        "property": "interactable",
                        "type": "bool",
                    }
                },
                "slots": {},
                "parts": {
                    "buttonText": {
                        "path": "Label",
                        "baseId": "Base_Text",
                        "allowRectOverride": True,
                    },
                    "buttonImage": {
                        "path": "Bg",
                        "baseId": "Base_Image",
                        "allowRectOverride": False,
                    },
                },
            },
            "Base_Text": {
                "id": "Base_Text",
                "manifestVersion": 1,
                "category": "Text",
                "prefabPath": "Assets/Base_Text.prefab",
                "props": {
                    "text": {"targetPath": ".", "component": "TMP_Text", "property": "text", "type": "string"},
                    "fontSize": {"targetPath": ".", "component": "TMP_Text", "property": "fontSize", "type": "float"},
                    "color": {"targetPath": ".", "component": "TMP_Text", "property": "color", "type": "Color"},
                },
                "slots": {},
                "parts": {},
            },
            "Base_Image": {
                "id": "Base_Image",
                "manifestVersion": 1,
                "category": "Image",
                "prefabPath": "Assets/Base_Image.prefab",
                "props": {
                    "color": {"targetPath": ".", "component": "Image", "property": "color", "type": "Color"},
                    "type": {"targetPath": ".", "component": "Image", "property": "type", "type": "string"},
                },
                "slots": {},
                "parts": {},
            },
        },
        "scripts": {
            "Game.UI.ShopPopupView, Assembly-CSharp": {
                "category": "Popup",
                "fullTypeName": "Game.UI.ShopPopupView, Assembly-CSharp",
                "description": "Shop popup",
                "fields": {
                    "confirmBtn": {"type": "UnityEngine.UI.Button", "description": "Confirm"}
                },
            }
        },
        "referenceContainers": {
            "objectRef": {
                "containerKey": "objectRef",
                "componentTypeName": "DMCore.Unity.UI.ObjectRef, DMCore.GameFoundation.Unity",
                "targetKind": "SceneObject",
                "allowAutoAttach": True,
            },
            "poolRef": {
                "containerKey": "poolRef",
                "componentTypeName": "DMCore.Unity.UI.PoolRef, DMCore.GameFoundation.Unity",
                "targetKind": "AssetObject",
                "allowAutoAttach": True,
            },
        },
    }


class ValidateUiJsonTests(unittest.TestCase):
    def test_valid_complex_layout_with_catalog(self) -> None:
        catalog = make_sample_catalog()
        layout = {
            "id": "shop_root",
            "name": "ShopPopup",
            "active": True,
            "useBase": "Base_Popup",
            "rect": {"anchor": "StretchAll", "padding": [0, 0, 0, 0]},
            "props": {"showClose": True},
            "partOverrides": {
                "panel": {
                    "rect": {"anchor": "StretchAll", "padding": [12, 12, 12, 12]},
                    "props": {"color": "#1E2436FF", "type": "Sliced"},
                },
                "confirmButton": {
                    "active": True,
                    "props": {"interactable": True},
                    "partOverrides": {
                        "buttonText": {
                            "props": {"text": "BUY NOW", "fontSize": 28, "color": "#FFFFFFFF"}
                        },
                        "buttonImage": {"props": {"color": "#06D6A0FF"}},
                    },
                },
            },
            "rootScript": {
                "fullTypeName": "Game.UI.ShopPopupView, Assembly-CSharp",
                "fields": {"title": "Daily Deals"},
            },
            "refs": {
                "objectRef": [
                    {
                        "id": "grid_ref",
                        "targetNodeId": "items_grid",
                        "componentType": "GridLayoutGroup",
                    }
                ]
            },
            "slotChildren": {
                "content": [
                    {
                        "id": "items_grid",
                        "kind": "emptyRect",
                        "rect": {"anchor": "TopStretch", "padding": [16, 0, 16, 16], "size": [0, 500]},
                        "layoutGroup": {
                            "type": "Grid",
                            "cellSize": [180, 220],
                            "spacing": [12, 12],
                            "padding": [8, 8, 8, 8],
                            "startCorner": "UpperLeft",
                            "startAxis": "Horizontal",
                            "constraint": "FixedColumnCount",
                            "constraintCount": 3,
                            "childAlignment": "UpperCenter",
                        },
                        "contentSizeFitter": {
                            "horizontalFit": "Unconstrained",
                            "verticalFit": "PreferredSize",
                        },
                        "children": [
                            {
                                "id": "item_1",
                                "useBase": "Base_Image",
                                "rect": {"anchor": "MiddleCenter", "size": [180, 220]},
                                "props": {"color": "#334466FF"},
                            }
                        ],
                    }
                ],
                "footer": [
                    {
                        "id": "cancel_btn",
                        "useBase": "Base_Button",
                        "bindToRoot": "confirmBtn",
                        "layoutElement": {"preferredWidth": 200, "preferredHeight": 72},
                        "partOverrides": {
                            "buttonText": {"props": {"text": "CLOSE"}}
                        },
                    }
                ],
            },
        }
        res = v.validate_layout_document(layout, catalog)
        self.assertTrue(res["valid"], res["errors"])
        self.assertEqual(res["errors"], [])

    def test_all_14_anchor_presets_and_center_alias(self) -> None:
        point_presets = [
            "TopLeft",
            "TopCenter",
            "TopRight",
            "MiddleLeft",
            "MiddleCenter",
            "Center",
            "MiddleRight",
            "BottomLeft",
            "BottomCenter",
            "BottomRight",
        ]
        stretch_partial = ["TopStretch", "BottomStretch", "LeftStretch", "RightStretch"]
        children = []
        for idx, preset in enumerate(point_presets):
            children.append(
                {
                    "id": f"pt_{idx}",
                    "kind": "emptyRect",
                    "rect": {"anchor": preset, "size": [100, 50], "offset": [5, -5]},
                }
            )
        for idx, preset in enumerate(stretch_partial):
            children.append(
                {
                    "id": f"sp_{idx}",
                    "kind": "emptyRect",
                    "rect": {"anchor": preset, "padding": [8, 8, 8, 8], "size": [64, 64]},
                }
            )
        children.append(
            {
                "id": "sa",
                "kind": "emptyRect",
                "rect": {"anchor": "StretchAll", "padding": [-10, 0, -20, 0]},
            }
        )
        res = v.validate_layout_document(
            {"id": "root", "kind": "emptyRect", "children": children}
        )
        self.assertTrue(res["valid"], res["errors"])

    def test_unknown_keys_and_typo_hints(self) -> None:
        bad_layout = {
            "id": "root",
            "useBase": "Base_Popup",
            "slots": {},
            "parts": {},
            "partOverrides": {"panel": {"setProps": {"color": "#FFF"}}},
            "rect": {"anchor": "MiddleCenter", "size": [100, 100], "width": 100},
        }
        res = v.validate_layout_document(bad_layout)
        self.assertFalse(res["valid"])
        joined = "\n".join(res["errors"])
        self.assertIn("slotChildren", joined)
        self.assertIn("partOverrides", joined)
        self.assertIn("Did you mean 'props'?", joined)
        self.assertIn("Use 'size': [width, height].", joined)

        bad_patch = {
            "targetNodePath": ".",
            "props": {"a": 1},
            "slotOperations": [{"slotName": "content", "operation": "append", "node": {"id": "x", "kind": "emptyRect"}}],
        }
        res_patch = v.validate_patch_document(bad_patch)
        self.assertFalse(res_patch["valid"])
        joined_p = "\n".join(res_patch["errors"])
        self.assertIn("setProps", joined_p)
        self.assertIn("action", joined_p)

    def test_node_structural_rules_and_duplicate_ids(self) -> None:
        for bad in (
            {"kind": "emptyRect"},
            {"id": "n1"},
            {"id": "n2", "kind": "emptyRect", "useBase": "Base_Popup"},
            {"id": "n3", "kind": "Button"},
            {"id": "e1", "kind": "emptyRect", "props": {"a": 1}},
            {"id": "e2", "kind": "emptyRect", "slotChildren": {"content": []}},
            {"id": "e3", "kind": "emptyRect", "partOverrides": {"panel": {}}},
            {
                "id": "dup",
                "kind": "emptyRect",
                "children": [{"id": "DUP", "kind": "emptyRect"}],
            },
        ):
            res = v.validate_layout_document(bad)
            self.assertFalse(res["valid"], f"Expected failure for {bad}")

    def test_rect_layout_group_and_fitter_validation(self) -> None:
        bad_cases = [
            {"id": "r1", "kind": "emptyRect", "rect": {"anchor": "BadPreset", "size": [10, 10]}},
            {"id": "r2", "kind": "emptyRect", "rect": {"anchor": "MiddleCenter", "size": [-5, 10]}},
            {"id": "r3", "kind": "emptyRect", "rect": {"anchor": "StretchAll", "padding": [1, 2]}},
            {"id": "r4", "kind": "emptyRect", "rect": {"anchor": "TopStretch", "padding": [1, 2, 3, 4]}},
            {"id": "r5", "kind": "emptyRect", "rect": {"anchor": "MiddleCenter", "size": [True, 10]}},
            {
                "id": "lg1",
                "kind": "emptyRect",
                "layoutGroup": {"type": "Radial"},
            },
            {
                "id": "lg2",
                "kind": "emptyRect",
                "layoutGroup": {"type": "Grid", "constraintCount": 0, "spacing": 10},
            },
            {
                "id": "lg3",
                "kind": "emptyRect",
                "layoutGroup": {"type": "Vertical", "padding": [1, 2, 3], "spacing": [5, 5]},
            },
            {
                "id": "lg4",
                "kind": "emptyRect",
                "layoutGroup": {"type": "Grid", "startCorner": "Center", "constraint": "None"},
            },
            {
                "id": "csf1",
                "kind": "emptyRect",
                "contentSizeFitter": {"verticalFit": "Grow"},
            },
        ]
        for case in bad_cases:
            res = v.validate_layout_document(case)
            self.assertFalse(res["valid"], f"Expected failure for {case}")

    def test_grid_cell_size_conflict(self) -> None:
        conflict_layout = {
            "id": "grid_parent",
            "kind": "emptyRect",
            "layoutGroup": {
                "type": "Grid",
                "cellSize": [150, 150],
                "spacing": [10, 10],
            },
            "children": [
                {
                    "id": "bad_cell",
                    "kind": "emptyRect",
                    "rect": {"anchor": "MiddleCenter", "size": [200, 150]},
                }
            ],
        }
        res = v.validate_layout_document(conflict_layout)
        self.assertFalse(res["valid"])
        self.assertTrue(any("cellSize" in e for e in res["errors"]))

    def test_nested_part_overrides_catalog_errors(self) -> None:
        catalog = make_sample_catalog()

        # Unknown nested part on Base_Button
        layout_bad_nested_part = {
            "id": "popup",
            "useBase": "Base_Popup",
            "partOverrides": {
                "confirmButton": {
                    "partOverrides": {"nonExistentPart": {"props": {"text": "X"}}}
                }
            },
        }
        res1 = v.validate_layout_document(layout_bad_nested_part, catalog)
        self.assertFalse(res1["valid"])
        self.assertTrue(any("nonExistentPart" in e for e in res1["errors"]))

        # Disallowed rect override on Base_Button.buttonImage (allowRectOverride=False)
        layout_locked_rect = {
            "id": "popup",
            "useBase": "Base_Popup",
            "partOverrides": {
                "confirmButton": {
                    "partOverrides": {
                        "buttonImage": {
                            "rect": {"anchor": "StretchAll", "padding": [0, 0, 0, 0]}
                        }
                    }
                }
            },
        }
        res2 = v.validate_layout_document(layout_locked_rect, catalog)
        self.assertFalse(res2["valid"])
        self.assertTrue(any("allowRectOverride" in e for e in res2["errors"]))

        # Unknown prop inside nested part
        layout_bad_prop = {
            "id": "popup",
            "useBase": "Base_Popup",
            "partOverrides": {
                "confirmButton": {
                    "partOverrides": {"buttonText": {"props": {"bogusProp": 123}}}
                }
            },
        }
        res3 = v.validate_layout_document(layout_bad_prop, catalog)
        self.assertFalse(res3["valid"])
        self.assertTrue(any("bogusProp" in e for e in res3["errors"]))

    def test_catalog_checks_and_optional_reference_containers(self) -> None:
        catalog = make_sample_catalog()

        # Unknown base, unknown slot, rejected category, unknown ref container
        bad = {
            "id": "root",
            "useBase": "Base_Popup",
            "refs": {"badRef": [{"id": "x", "targetNodeId": "root"}]},
            "slotChildren": {
                "footer": [{"id": "bad_child", "useBase": "Base_Text"}],
                "missingSlot": [],
            },
        }
        res = v.validate_layout_document(bad, catalog)
        self.assertFalse(res["valid"])
        joined = "\n".join(res["errors"])
        self.assertIn("badRef", joined)
        self.assertIn("not accepted", joined)
        self.assertIn("missingSlot", joined)

        # Older catalog without referenceContainers skips container key check gracefully
        legacy_catalog = copy.deepcopy(catalog)
        legacy_catalog.pop("referenceContainers", None)
        ok_legacy = {
            "id": "root",
            "useBase": "Base_Popup",
            "refs": {"objectRef": [{"id": "self_ref", "targetNodeId": "root"}]},
        }
        res_legacy = v.validate_layout_document(ok_legacy, legacy_catalog)
        self.assertTrue(res_legacy["valid"], res_legacy["errors"])

    def test_patch_validation_rules(self) -> None:
        catalog = make_sample_catalog()

        valid_patch = {
            "targetNodeId": "shop_root",
            "setActive": True,
            "setLayoutGroup": {
                "type": "Vertical",
                "spacing": 12,
                "padding": [8, 8, 8, 8],
                "reverseArrangement": False,
            },
            "setLayoutElement": {"preferredHeight": 300, "flexibleWidth": 1},
            "setContentSizeFitter": {"verticalFit": "PreferredSize"},
            "partOverrides": {
                "confirmButton": {
                    "useBase": "Base_Button",
                    "partOverrides": {
                        "buttonText": {"props": {"text": "UPDATED"}}
                    },
                }
            },
            "slotOperations": [
                {
                    "slotName": "children",
                    "action": "replace",
                    "nodeId": "old_child",
                    "node": {"id": "new_child", "useBase": "Base_Image", "props": {"color": "#FFFFFFFF"}},
                },
                {"slotName": "content", "action": "clear"},
            ],
            "mergeRefs": {
                "objectRef": [
                    {"id": "btn", "targetNodeId": "new_child", "componentType": "Image"},
                    {"id": "obsolete", "remove": True},
                ]
            },
        }
        res = v.validate_patch_document(valid_patch, catalog)
        self.assertTrue(res["valid"], res["errors"])

        for bad_patch in (
            {"setActive": True},
            {"targetNodeId": "a", "targetNodePath": ".", "setActive": True},
            {"targetNodePath": "."},
            {"targetNodePath": ".", "slotOperations": [{"slotName": "content", "action": "nuke"}]},
            {"targetNodePath": ".", "slotOperations": [{"slotName": "content", "action": "append"}]},
            {"targetNodePath": ".", "slotOperations": [{"slotName": "content", "action": "remove"}]},
            {"targetNodePath": ".", "slotOperations": [{"slotName": "content", "action": "reorder", "nodeId": "x"}]},
            {"targetNodePath": ".", "slotOperations": [{"slotName": "content", "action": "replace", "node": {"id": "n", "kind": "emptyRect"}}]},
        ):
            r = v.validate_patch_document(bad_patch)
            self.assertFalse(r["valid"], f"Expected failure for patch {bad_patch}")

    def test_cli_json_output_and_exit_codes(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = Path(tmp)
            valid_file = tmp_path / "valid.ui.json"
            invalid_file = tmp_path / "invalid.ui.json"
            valid_file.write_text(
                json.dumps({"id": "root", "kind": "emptyRect"}), encoding="utf-8"
            )
            invalid_file.write_text(json.dumps({"kind": "emptyRect"}), encoding="utf-8")

            ok_proc = subprocess.run(
                [sys.executable, str(SCRIPTS_DIR / "validate_ui_json.py"), "--layout", str(valid_file), "--json"],
                capture_output=True,
                text=True,
                check=False,
            )
            self.assertEqual(ok_proc.returncode, 0, ok_proc.stderr)
            ok_json = json.loads(ok_proc.stdout)
            self.assertTrue(ok_json["valid"])

            err_proc = subprocess.run(
                [sys.executable, str(SCRIPTS_DIR / "validate_ui_json.py"), "--layout", str(invalid_file), "--json"],
                capture_output=True,
                text=True,
                check=False,
            )
            self.assertNotEqual(err_proc.returncode, 0)
            err_json = json.loads(err_proc.stdout)
            self.assertFalse(err_json["valid"])

    def test_real_dmcore_catalog_if_present(self) -> None:
        if not DMCORE_CATALOG_PATH.is_file():
            self.skipTest(f"Real DMCore catalog not present at {DMCORE_CATALOG_PATH}")

        catalog = json.loads(DMCORE_CATALOG_PATH.read_text(encoding="utf-8"))
        layout = {
            "id": "dmcore_shop_popup",
            "name": "ShopPopup",
            "useBase": "Base_Popup",
            "rect": {"anchor": "StretchAll", "padding": [0, 0, 0, 0]},
            "props": {
                "windowRect": {"anchor": "MiddleCenter", "size": [780, 960]},
                "showClose": True,
                "closeOnOverlay": True,
                "showUIBackground": True,
            },
            "partOverrides": {
                "panel": {
                    "props": {
                        "baseimageColor": "#1E2436F5",
                        "baseimageType": "Sliced",
                    }
                }
            },
            "refs": {
                "objectRef": [
                    {"id": "btn_buy", "targetNodeId": "buy_btn", "componentType": "Button"}
                ]
            },
            "slotChildren": {
                "top": [
                    {
                        "id": "shop_title",
                        "useBase": "Base_Text",
                        "rect": {"anchor": "StretchAll", "padding": [16, 4, 16, 4]},
                        "props": {"text": "SHOP", "fontSize": 36, "color": "#FFFFFFFF"},
                    }
                ],
                "content": [
                    {
                        "id": "scroll_list",
                        "useBase": "Base_V_Scroll",
                        "rect": {"anchor": "StretchAll", "padding": [16, 16, 16, 16]},
                        "slotChildren": {
                            "content": [
                                {
                                    "id": "row_container",
                                    "kind": "emptyRect",
                                    "rect": {"anchor": "TopStretch", "padding": [0, 0, 0, 0], "size": [0, 120]},
                                    "layoutGroup": {"type": "Horizontal", "spacing": 12, "padding": [8, 8, 8, 8]},
                                    "contentSizeFitter": {"verticalFit": "PreferredSize"},
                                    "children": [],
                                }
                            ]
                        },
                    }
                ],
                "bottom": [
                    {
                        "id": "buy_btn",
                        "useBase": "Base_Button",
                        "rect": {"anchor": "MiddleCenter", "size": [240, 72]},
                        "partOverrides": {
                            "buttonText": {
                                "props": {"text": "BUY", "fontSize": 28, "color": "#FFFFFFFF"}
                            },
                            "buttonImage": {
                                "props": {"baseimageColor": "#06D6A0FF"}
                            },
                        },
                    }
                ],
            },
        }
        res = v.validate_layout_document(layout, catalog)
        self.assertTrue(res["valid"], res["errors"])

    def test_skill_files_are_strictly_ascii(self) -> None:
        for file_path in SKILLS_ROOT.rglob("*"):
            if not file_path.is_file() or "__pycache__" in file_path.parts or file_path.suffix == ".pyc":
                continue
            text = file_path.read_text(encoding="utf-8")
            self.assertTrue(
                text.isascii(),
                f"Non-ASCII character detected in skill file: {file_path}",
            )

    def test_examples_in_reference_doc_are_valid(self) -> None:
        examples_md = (
            SKILLS_ROOT / "ui-maker" / "references" / "examples-and-troubleshooting.md"
        ).read_text(encoding="utf-8")

        import re

        blocks = re.findall(r"```json\s*(.*?)\s*```", examples_md, flags=re.DOTALL)
        self.assertGreaterEqual(len(blocks), 9)
        for idx, raw_block in enumerate(blocks):
            payload = json.loads(raw_block)
            if "targetNodeId" in payload or "targetNodePath" in payload:
                res = v.validate_patch_document(payload)
            else:
                res = v.validate_layout_document(payload)
            self.assertTrue(
                res["valid"],
                f"Reference JSON example #{idx + 1} failed validation: {res['errors']}",
            )

    def test_install_project_skill_copies_both_skills_without_pycache(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            proc = subprocess.run(
                [
                    sys.executable,
                    str(SCRIPTS_DIR / "install_project_skill.py"),
                    "--project-root",
                    tmp,
                    "--targets",
                    "all",
                    "--force",
                ],
                capture_output=True,
                text=True,
                check=False,
            )
            self.assertEqual(proc.returncode, 0, proc.stdout + proc.stderr)
            tmp_path = Path(tmp)
            for rel_root in (".agents/skills", ".agent/skills", ".claude/skills", ".gemini/skills"):
                for skill_name in ("ui-maker", "ui-maker-manifest-authoring"):
                    skill_md = tmp_path / rel_root / skill_name / "SKILL.md"
                    self.assertTrue(skill_md.is_file(), f"Missing installed {skill_md}")
                    openai_yaml = tmp_path / rel_root / skill_name / "agents" / "openai.yaml"
                    self.assertTrue(openai_yaml.is_file(), f"Missing installed {openai_yaml}")
                    pycache = tmp_path / rel_root / skill_name / "scripts" / "__pycache__"
                    self.assertFalse(pycache.exists(), f"Unexpected __pycache__ in {pycache}")

    def test_pivot_and_patch_slot_bind_to_root(self) -> None:
        self.assertTrue(
            v.validate_layout_document(
                {
                    "id": "p_ok",
                    "kind": "emptyRect",
                    "rect": {"anchor": "MiddleCenter", "size": [100, 100], "pivot": []},
                }
            )["valid"]
        )
        self.assertFalse(
            v.validate_layout_document(
                {
                    "id": "p_bad",
                    "kind": "emptyRect",
                    "rect": {"anchor": "MiddleCenter", "size": [100, 100], "pivot": [0.5]},
                }
            )["valid"]
        )
        patch_res = v.validate_patch_document(
            {
                "targetNodePath": ".",
                "slotOperations": [
                    {
                        "slotName": "footer",
                        "action": "append",
                        "node": {
                            "id": "new_btn",
                            "useBase": "Base_Button",
                            "bindToRoot": "actionBtn",
                        },
                    }
                ],
            }
        )
        self.assertTrue(patch_res["valid"], patch_res["errors"])


if __name__ == "__main__":
    unittest.main()
