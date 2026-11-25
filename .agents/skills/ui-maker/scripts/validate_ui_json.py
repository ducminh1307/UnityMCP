#!/usr/bin/env python3
"""Validate declarative UI Maker layout and patch JSON documents."""

from __future__ import annotations

import argparse
import json
import math
import sys
from pathlib import Path
from typing import Any


POINT_ANCHOR_PRESETS = {
    "topleft",
    "topcenter",
    "topright",
    "middleleft",
    "middlecenter",
    "center",
    "middleright",
    "bottomleft",
    "bottomcenter",
    "bottomright",
}

STRETCH_ANCHOR_PRESETS = {
    "stretchall",
    "topstretch",
    "bottomstretch",
    "leftstretch",
    "rightstretch",
}

ALL_ANCHOR_PRESETS = POINT_ANCHOR_PRESETS | STRETCH_ANCHOR_PRESETS

LAYOUT_GROUP_TYPES = {"horizontal", "vertical", "grid"}
TEXT_ANCHOR_VALUES = {
    "upperleft",
    "uppercenter",
    "upperright",
    "middleleft",
    "middlecenter",
    "middleright",
    "lowerleft",
    "lowercenter",
    "lowerright",
}
GRID_CORNERS = {"upperleft", "upperright", "lowerleft", "lowerright"}
GRID_AXES = {"horizontal", "vertical"}
GRID_CONSTRAINTS = {"flexible", "fixedcolumncount", "fixedrowcount"}
FIT_MODES = {"unconstrained", "minsize", "preferredsize"}
SLOT_ACTIONS = {"append", "insert", "replace", "remove", "reorder", "clear"}

NODE_KEYS = {
    "id",
    "name",
    "active",
    "kind",
    "useBase",
    "rect",
    "layoutGroup",
    "layoutElement",
    "contentSizeFitter",
    "props",
    "partOverrides",
    "rootScript",
    "bindToRoot",
    "bindToParent",
    "slotChildren",
    "children",
    "refs",
}

NODE_HINTS = {
    "slots": "Did you mean 'slotChildren'?",
    "parts": "Did you mean 'partOverrides'?",
    "properties": "Did you mean 'props'?",
    "type": "Did you mean 'kind' ('emptyRect') or 'useBase'?",
    "script": "Did you mean 'rootScript'?",
    "setprops": "Did you mean 'props'?",
    "setrect": "Did you mean 'rect'?",
    "setactive": "Did you mean 'active'?",
    "setrefs": "Did you mean 'refs'?",
}

PART_OVERRIDE_KEYS = {
    "useBase",
    "targetPath",
    "active",
    "rect",
    "props",
    "partOverrides",
}

PART_OVERRIDE_HINTS = {
    "setprops": "Did you mean 'props'?",
    "setrect": "Did you mean 'rect'?",
    "setactive": "Did you mean 'active'?",
    "parts": "Did you mean 'partOverrides'?",
}

RECT_KEYS = {"anchor", "size", "padding", "offset", "pivot"}

RECT_HINTS = {
    "width": "Use 'size': [width, height].",
    "height": "Use 'size': [width, height].",
    "x": "Use 'offset': [x, y].",
    "y": "Use 'offset': [x, y].",
    "position": "Use 'offset': [x, y].",
    "anchoredposition": "Use 'offset': [x, y].",
    "sizedelta": "Use 'size': [width, height].",
}

LAYOUT_GROUP_KEYS = {
    "type",
    "spacing",
    "cellSize",
    "padding",
    "childAlignment",
    "childControlWidth",
    "childControlHeight",
    "childForceExpandWidth",
    "childForceExpandHeight",
    "reverseArrangement",
    "childScaleWidth",
    "childScaleHeight",
    "startCorner",
    "startAxis",
    "constraint",
    "constraintCount",
}

LAYOUT_GROUP_HINTS = {
    "alignment": "Did you mean 'childAlignment'?",
}

LAYOUT_ELEMENT_KEYS = {
    "ignoreLayout",
    "minWidth",
    "minHeight",
    "preferredWidth",
    "preferredHeight",
    "flexibleWidth",
    "flexibleHeight",
    "layoutPriority",
}

LAYOUT_ELEMENT_HINTS = {
    "width": "Did you mean 'preferredWidth' or 'minWidth'?",
    "height": "Did you mean 'preferredHeight' or 'minHeight'?",
}

CONTENT_SIZE_FITTER_KEYS = {"horizontalFit", "verticalFit"}

CONTENT_SIZE_FITTER_HINTS = {
    "horizontal": "Did you mean 'horizontalFit'?",
    "vertical": "Did you mean 'verticalFit'?",
}

ROOT_SCRIPT_KEYS = {"fullTypeName", "fields"}

ROOT_SCRIPT_HINTS = {
    "type": "Did you mean 'fullTypeName'?",
    "typename": "Did you mean 'fullTypeName'?",
    "scripttype": "Did you mean 'fullTypeName'?",
}

PARENT_BINDING_KEYS = {"field", "scriptType", "nodeId"}

PARENT_BINDING_HINTS = {
    "fieldname": "Did you mean 'field'?",
    "targetnodeid": "Did you mean 'nodeId'?",
}

REF_ENTRY_KEYS = {
    "id",
    "targetNodeId",
    "targetPath",
    "assetPath",
    "legacyKey",
    "componentType",
    "remove",
}

REF_ENTRY_HINTS = {
    "key": "Did you mean 'id' (or 'legacyKey')?",
    "nodeid": "Did you mean 'targetNodeId'?",
}

PATCH_KEYS = {
    "targetNodeId",
    "targetNodePath",
    "setActive",
    "setProps",
    "setRect",
    "setLayoutGroup",
    "setLayoutElement",
    "setContentSizeFitter",
    "partOverrides",
    "slotOperations",
    "setRefs",
    "mergeRefs",
}

PATCH_HINTS = {
    "props": "Did you mean 'setProps'?",
    "rect": "Did you mean 'setRect'?",
    "active": "Did you mean 'setActive'?",
    "refs": "Did you mean 'setRefs' (or 'mergeRefs')?",
    "slots": "Did you mean 'slotOperations'?",
    "slotchildren": "Did you mean 'slotOperations'?",
    "layoutgroup": "Did you mean 'setLayoutGroup'?",
    "layoutelement": "Did you mean 'setLayoutElement'?",
    "contentsizefitter": "Did you mean 'setContentSizeFitter'?",
    "parts": "Did you mean 'partOverrides'?",
}

SLOT_OP_KEYS = {"slotName", "action", "index", "node", "nodeId"}

SLOT_OP_HINTS = {
    "operation": "Did you mean 'action'?",
    "targetnodeid": "Did you mean 'nodeId'?",
    "id": "Did you mean 'nodeId'?",
    "slot": "Did you mean 'slotName'?",
}


def _is_number(value: Any) -> bool:
    return (
        isinstance(value, (int, float))
        and not isinstance(value, bool)
        and math.isfinite(float(value))
    )


def _is_int(value: Any) -> bool:
    return isinstance(value, int) and not isinstance(value, bool)


def _is_non_empty_str(value: Any) -> bool:
    return isinstance(value, str) and bool(value.strip())


def _normalize_object(
    obj: Any,
    canonical_keys: set[str],
    hints: dict[str, str],
    context_label: str,
    errors: list[str],
    warnings: list[str],
) -> dict[str, Any] | None:
    if not isinstance(obj, dict):
        errors.append(f"{context_label} must be a JSON object.")
        return None

    lower_map = {k.lower(): k for k in canonical_keys}
    normalized: dict[str, Any] = {}
    for raw_key, value in obj.items():
        if not isinstance(raw_key, str):
            errors.append(f"{context_label}: key must be a string.")
            continue
        low = raw_key.lower()
        canonical = lower_map.get(low)
        if canonical is None:
            hint = hints.get(low)
            suffix = f" {hint}" if hint else ""
            errors.append(f"{context_label}: unknown key '{raw_key}'.{suffix}")
            continue
        if raw_key != canonical:
            warnings.append(
                f"{context_label}: key '{raw_key}' should use canonical casing '{canonical}'."
            )
        normalized[canonical] = value
    return normalized


class CatalogIndex:
    """Case-insensitive view over ui_catalog.json."""

    def __init__(self, raw_catalog: dict[str, Any]) -> None:
        self.raw = raw_catalog
        prefabs = raw_catalog.get("prefabs") or {}
        scripts = raw_catalog.get("scripts") or {}
        ref_containers = raw_catalog.get("referenceContainers")

        self.prefabs: dict[str, dict[str, Any]] = {
            k.lower(): v for k, v in prefabs.items() if isinstance(k, str) and isinstance(v, dict)
        }
        self.scripts: dict[str, dict[str, Any]] = {
            k.lower(): v for k, v in scripts.items() if isinstance(k, str) and isinstance(v, dict)
        }
        self.has_reference_containers = isinstance(ref_containers, dict) and len(ref_containers) > 0
        self.reference_containers: dict[str, dict[str, Any]] = (
            {
                k.lower(): v
                for k, v in ref_containers.items()
                if isinstance(k, str) and isinstance(v, dict)
            }
            if isinstance(ref_containers, dict)
            else {}
        )

    def get_prefab(self, base_id: str) -> dict[str, Any] | None:
        return self.prefabs.get(base_id.strip().lower())

    def get_script(self, full_type_name: str) -> dict[str, Any] | None:
        return self.scripts.get(full_type_name.strip().lower())

    def get_ref_container(self, key: str) -> dict[str, Any] | None:
        return self.reference_containers.get(key.strip().lower())


def _ci_lookup(mapping: dict[str, Any] | None, key: str) -> tuple[bool, Any]:
    if not isinstance(mapping, dict):
        return False, None
    if key in mapping:
        return True, mapping[key]
    low = key.lower()
    for k, v in mapping.items():
        if isinstance(k, str) and k.lower() == low:
            return True, v
    return False, None


def validate_rect(
    rect_obj: Any,
    context_label: str,
    errors: list[str],
    warnings: list[str],
) -> dict[str, Any] | None:
    rect = _normalize_object(rect_obj, RECT_KEYS, RECT_HINTS, context_label, errors, warnings)
    if rect is None:
        return None

    if "anchor" in rect:
        anchor_raw = rect["anchor"]
        if not _is_non_empty_str(anchor_raw):
            errors.append(
                f"{context_label}: 'anchor' must be a non-empty string preset."
            )
            return rect
        anchor_norm = anchor_raw.strip().lower()
    else:
        anchor_norm = "middlecenter"

    if anchor_norm not in ALL_ANCHOR_PRESETS:
        errors.append(
            f"{context_label}: invalid semantic anchor preset '{rect.get('anchor')}'."
        )
        return rect

    size = rect.get("size")
    if size is not None:
        if not isinstance(size, list) or not all(_is_number(v) for v in size):
            errors.append(f"{context_label}: 'size' must be an array of finite numbers.")
            size = None
        else:
            for v in size:
                if float(v) < 0.0:
                    errors.append(
                        f"{context_label}: size dimensions must be finite and non-negative. Got: {size}."
                    )
                    break

    padding = rect.get("padding")
    if padding is not None:
        if not isinstance(padding, list) or not all(_is_number(v) for v in padding):
            errors.append(f"{context_label}: 'padding' must be an array of finite numbers.")
            padding = None
        elif len(padding) != 4:
            errors.append(
                f"{context_label}: 'padding' must contain 4 numbers [left, bottom, right, top]. Got length {len(padding)}."
            )

    offset = rect.get("offset")
    if offset is not None:
        if not isinstance(offset, list) or not all(_is_number(v) for v in offset):
            errors.append(f"{context_label}: 'offset' must be an array of finite numbers.")
        elif len(offset) != 2:
            warnings.append(
                f"{context_label}: 'offset' should have 2 elements [x, y]. Got length {len(offset)}."
            )

    pivot = rect.get("pivot")
    if pivot is not None:
        if not isinstance(pivot, list) or not all(_is_number(v) for v in pivot):
            errors.append(f"{context_label}: 'pivot' must be an array of finite numbers.")
        elif len(pivot) > 0 and len(pivot) != 2:
            errors.append(
                f"{context_label}: 'pivot' must contain 2 numbers [x, y]. Got length {len(pivot)}."
            )

    if anchor_norm == "stretchall":
        if padding is None or len(padding) < 4:
            errors.append(
                f"{context_label}: preset 'StretchAll' requires 4 padding values [left, bottom, right, top]."
            )
    elif anchor_norm in {"topstretch", "bottomstretch"}:
        if padding is None or len(padding) < 4:
            errors.append(
                f"{context_label}: preset '{rect.get('anchor')}' requires 4 padding values [left, bottom, right, top]."
            )
        if size is None or len(size) < 1:
            errors.append(
                f"{context_label}: preset '{rect.get('anchor')}' requires 'size' to specify height."
            )
    elif anchor_norm in {"leftstretch", "rightstretch"}:
        if padding is None or len(padding) < 4:
            errors.append(
                f"{context_label}: preset '{rect.get('anchor')}' requires 4 padding values [left, bottom, right, top]."
            )
        if size is None or len(size) < 1:
            errors.append(
                f"{context_label}: preset '{rect.get('anchor')}' requires 'size' to specify width."
            )
    else:
        if size is None or len(size) < 2:
            errors.append(
                f"{context_label}: point preset '{rect.get('anchor', 'MiddleCenter')}' requires 'size' [width, height]."
            )

    return rect


def validate_layout_group(
    lg_obj: Any,
    context_label: str,
    errors: list[str],
    warnings: list[str],
) -> dict[str, Any] | None:
    lg = _normalize_object(
        lg_obj, LAYOUT_GROUP_KEYS, LAYOUT_GROUP_HINTS, context_label, errors, warnings
    )
    if lg is None:
        return None

    effective_type = "vertical"
    if "type" in lg and lg["type"] is not None:
        raw_type = lg["type"]
        if not _is_non_empty_str(raw_type) or raw_type.strip().lower() not in LAYOUT_GROUP_TYPES:
            errors.append(
                f"{context_label}: invalid layoutGroup type '{raw_type}'. Allowed: Horizontal, Vertical, Grid."
            )
        else:
            effective_type = raw_type.strip().lower()

    if lg.get("childAlignment") is not None:
        val = lg["childAlignment"]
        if not _is_non_empty_str(val) or val.strip().lower() not in TEXT_ANCHOR_VALUES:
            errors.append(
                f"{context_label}: invalid childAlignment '{val}'. Expected Unity TextAnchor value."
            )

    if lg.get("startCorner") is not None:
        val = lg["startCorner"]
        if not _is_non_empty_str(val) or val.strip().lower() not in GRID_CORNERS:
            errors.append(
                f"{context_label}: invalid startCorner '{val}'. Allowed: UpperLeft, UpperRight, LowerLeft, LowerRight."
            )

    if lg.get("startAxis") is not None:
        val = lg["startAxis"]
        if not _is_non_empty_str(val) or val.strip().lower() not in GRID_AXES:
            errors.append(
                f"{context_label}: invalid startAxis '{val}'. Allowed: Horizontal, Vertical."
            )

    if lg.get("constraint") is not None:
        val = lg["constraint"]
        if not _is_non_empty_str(val) or val.strip().lower() not in GRID_CONSTRAINTS:
            errors.append(
                f"{context_label}: invalid constraint '{val}'. Allowed: Flexible, FixedColumnCount, FixedRowCount."
            )

    if lg.get("constraintCount") is not None:
        cc = lg["constraintCount"]
        if not _is_int(cc) or cc <= 0:
            errors.append(
                f"{context_label}: 'constraintCount' must be a positive integer (> 0)."
            )

    if lg.get("cellSize") is not None:
        cs = lg["cellSize"]
        if (
            not isinstance(cs, list)
            or len(cs) < 2
            or not all(_is_number(v) and float(v) >= 0.0 for v in cs[:2])
        ):
            errors.append(
                f"{context_label}: 'cellSize' must be [width, height] of finite non-negative numbers."
            )

    if lg.get("padding") is not None:
        pad = lg["padding"]
        if not isinstance(pad, list) or len(pad) != 4 or not all(_is_int(v) for v in pad):
            errors.append(
                f"{context_label}: 'padding' must be 4 integers [left, right, top, bottom]."
            )

    if lg.get("spacing") is not None:
        sp = lg["spacing"]
        if effective_type == "grid":
            if (
                not isinstance(sp, list)
                or len(sp) < 2
                or not all(_is_number(v) for v in sp[:2])
            ):
                errors.append(
                    f"{context_label}: Grid 'spacing' must be a 2-number array [x, y]."
                )
        else:
            if not _is_number(sp):
                errors.append(
                    f"{context_label}: {effective_type.title()} 'spacing' must be a finite number."
                )

    for bool_key in (
        "childControlWidth",
        "childControlHeight",
        "childForceExpandWidth",
        "childForceExpandHeight",
        "reverseArrangement",
        "childScaleWidth",
        "childScaleHeight",
    ):
        if lg.get(bool_key) is not None and not isinstance(lg[bool_key], bool):
            errors.append(f"{context_label}: '{bool_key}' must be a boolean.")

    return lg


def validate_layout_element(
    le_obj: Any,
    context_label: str,
    errors: list[str],
    warnings: list[str],
) -> dict[str, Any] | None:
    le = _normalize_object(
        le_obj, LAYOUT_ELEMENT_KEYS, LAYOUT_ELEMENT_HINTS, context_label, errors, warnings
    )
    if le is None:
        return None

    if le.get("ignoreLayout") is not None and not isinstance(le["ignoreLayout"], bool):
        errors.append(f"{context_label}: 'ignoreLayout' must be a boolean.")

    for num_key in (
        "minWidth",
        "minHeight",
        "preferredWidth",
        "preferredHeight",
        "flexibleWidth",
        "flexibleHeight",
    ):
        if le.get(num_key) is not None and not _is_number(le[num_key]):
            errors.append(f"{context_label}: '{num_key}' must be a finite number.")

    if le.get("layoutPriority") is not None and not _is_int(le["layoutPriority"]):
        errors.append(f"{context_label}: 'layoutPriority' must be an integer.")

    return le


def validate_content_size_fitter(
    csf_obj: Any,
    context_label: str,
    errors: list[str],
    warnings: list[str],
) -> dict[str, Any] | None:
    csf = _normalize_object(
        csf_obj,
        CONTENT_SIZE_FITTER_KEYS,
        CONTENT_SIZE_FITTER_HINTS,
        context_label,
        errors,
        warnings,
    )
    if csf is None:
        return None

    for key in ("horizontalFit", "verticalFit"):
        if csf.get(key) is not None:
            val = csf[key]
            if not _is_non_empty_str(val) or val.strip().lower() not in FIT_MODES:
                errors.append(
                    f"{context_label}: invalid '{key}' value '{val}'. Allowed: Unconstrained, MinSize, PreferredSize."
                )

    return csf


def validate_part_overrides_structural(
    parts_obj: Any,
    context_label: str,
    errors: list[str],
    warnings: list[str],
) -> dict[str, dict[str, Any]] | None:
    if not isinstance(parts_obj, dict):
        errors.append(f"{context_label}: 'partOverrides' must be an object.")
        return None

    normalized_parts: dict[str, dict[str, Any]] = {}
    for part_name, override_raw in parts_obj.items():
        if not _is_non_empty_str(part_name):
            errors.append(f"{context_label}: partOverrides contains an empty part name.")
            continue
        if override_raw is None:
            continue

        part_label = f"{context_label}.partOverrides['{part_name}']"
        override = _normalize_object(
            override_raw,
            PART_OVERRIDE_KEYS,
            PART_OVERRIDE_HINTS,
            part_label,
            errors,
            warnings,
        )
        if override is None:
            continue

        if override.get("active") is not None and not isinstance(override["active"], bool):
            errors.append(f"{part_label}: 'active' must be a boolean.")
        if override.get("useBase") is not None and not isinstance(override["useBase"], str):
            errors.append(f"{part_label}: 'useBase' must be a string.")
        if override.get("targetPath") is not None and not isinstance(override["targetPath"], str):
            errors.append(f"{part_label}: 'targetPath' must be a string.")
        if override.get("props") is not None and not isinstance(override["props"], dict):
            errors.append(f"{part_label}: 'props' must be an object.")
        if override.get("rect") is not None:
            override["rect"] = validate_rect(override["rect"], f"{part_label}.rect", errors, warnings)
        if override.get("partOverrides") is not None:
            override["partOverrides"] = validate_part_overrides_structural(
                override["partOverrides"], part_label, errors, warnings
            )

        normalized_parts[part_name] = override
    return normalized_parts


def validate_part_overrides_catalog(
    parts_map: dict[str, dict[str, Any]],
    owner_prefab: dict[str, Any],
    owner_base_id: str,
    context_label: str,
    catalog: CatalogIndex,
    errors: list[str],
) -> None:
    declared_parts = owner_prefab.get("parts") or {}
    for part_name, override in parts_map.items():
        found_part, part_contract = _ci_lookup(declared_parts, part_name)
        if not found_part or not isinstance(part_contract, dict):
            errors.append(
                f"{context_label}: part '{part_name}' is not exposed in Base Prefab '{owner_base_id}' manifest."
            )
            continue

        if override is None:
            continue

        part_label = f"{context_label} part '{part_name}'"
        part_base_id = part_contract.get("baseId")
        if override.get("useBase") and part_base_id:
            if override["useBase"].strip().lower() != str(part_base_id).strip().lower():
                errors.append(
                    f"{part_label}: 'useBase' '{override['useBase']}' does not match manifest baseId '{part_base_id}'."
                )

        if override.get("rect") is not None and not part_contract.get("allowRectOverride", True):
            errors.append(
                f"{part_label}: does not allow rect overrides (allowRectOverride is false)."
            )

        if not _is_non_empty_str(part_base_id):
            if override.get("props") or override.get("partOverrides"):
                errors.append(f"{part_label}: missing 'baseId' in catalog.")
            continue

        part_prefab = catalog.get_prefab(part_base_id)
        if part_prefab is None:
            errors.append(
                f"{part_label}: base prefab '{part_base_id}' not found in catalog."
            )
            continue

        props = override.get("props")
        if isinstance(props, dict) and props:
            declared_props = part_prefab.get("props") or {}
            for prop_name in props:
                has_prop, _ = _ci_lookup(declared_props, prop_name)
                if not has_prop:
                    errors.append(
                        f"{part_label}: property '{prop_name}' is not exposed in Base Prefab '{part_base_id}' manifest."
                    )

        nested_parts = override.get("partOverrides")
        if isinstance(nested_parts, dict) and nested_parts:
            validate_part_overrides_catalog(
                nested_parts, part_prefab, part_base_id, part_label, catalog, errors
            )


def validate_refs_map(
    refs_obj: Any,
    context_label: str,
    is_merge: bool,
    catalog: CatalogIndex | None,
    all_node_ids: set[str] | None,
    errors: list[str],
    warnings: list[str],
) -> None:
    if not isinstance(refs_obj, dict):
        errors.append(f"{context_label} must be an object mapping containerKey -> entry list.")
        return

    for container_key, entries in refs_obj.items():
        if not _is_non_empty_str(container_key):
            errors.append(f"{context_label}: empty reference container key.")
            continue

        if not isinstance(entries, list):
            errors.append(f"{context_label}['{container_key}'] must be an array.")
            continue

        rule = None
        if catalog is not None and catalog.has_reference_containers:
            rule = catalog.get_ref_container(container_key)
            if rule is None:
                errors.append(
                    f"{context_label}: unknown reference container '{container_key}' (not in catalog.referenceContainers)."
                )

        for idx, entry_raw in enumerate(entries):
            entry_label = f"{context_label}['{container_key}'][{idx}]"
            entry = _normalize_object(
                entry_raw, REF_ENTRY_KEYS, REF_ENTRY_HINTS, entry_label, errors, warnings
            )
            if entry is None:
                continue

            if not _is_non_empty_str(entry.get("id")) and not _is_non_empty_str(entry.get("legacyKey")):
                errors.append(f"{entry_label}: must specify 'id' (or 'legacyKey').")

            if entry.get("remove") is not None and not isinstance(entry["remove"], bool):
                errors.append(f"{entry_label}: 'remove' must be a boolean.")
            elif entry.get("remove") is True and not is_merge:
                warnings.append(
                    f"{entry_label}: 'remove: true' is ignored outside 'mergeRefs'."
                )

            for str_key in ("targetNodeId", "targetPath", "assetPath", "legacyKey", "componentType"):
                if entry.get(str_key) is not None and not isinstance(entry[str_key], str):
                    errors.append(f"{entry_label}: '{str_key}' must be a string.")

            is_removing = entry.get("remove") is True
            target_node_id = entry.get("targetNodeId")
            if all_node_ids is not None and _is_non_empty_str(target_node_id) and not is_removing:
                if target_node_id.strip().lower() not in all_node_ids:
                    errors.append(
                        f"{entry_label}: references unknown targetNodeId '{target_node_id}'."
                    )

            if rule is not None:
                target_kind = str(rule.get("targetKind", "")).strip().lower()
                if target_kind in {"assetobject", "1"} and _is_non_empty_str(target_node_id):
                    warnings.append(
                        f"{entry_label}: container '{container_key}' targets AssetObject; 'targetNodeId' is unexpected."
                    )


def _collect_node_ids(node_obj: Any, ids: set[str]) -> None:
    if not isinstance(node_obj, dict):
        return
    node_id = None
    for k, v in node_obj.items():
        if isinstance(k, str) and k.lower() == "id" and _is_non_empty_str(v):
            node_id = v.strip().lower()
            break
    if node_id:
        ids.add(node_id)

    for k, v in node_obj.items():
        if not isinstance(k, str):
            continue
        low = k.lower()
        if low == "children" and isinstance(v, list):
            for child in v:
                _collect_node_ids(child, ids)
        elif low == "slotchildren" and isinstance(v, dict):
            for slot_list in v.values():
                if isinstance(slot_list, list):
                    for child in slot_list:
                        _collect_node_ids(child, ids)


def _extract_grid_cell_size(node: dict[str, Any]) -> tuple[float, float] | None:
    lg = node.get("layoutGroup")
    if isinstance(lg, dict):
        lg_type = lg.get("type")
        if isinstance(lg_type, str) and lg_type.strip().lower() == "grid":
            cs = lg.get("cellSize")
            if isinstance(cs, list) and len(cs) >= 2 and _is_number(cs[0]) and _is_number(cs[1]):
                return float(cs[0]), float(cs[1])

    if _is_non_empty_str(node.get("useBase")) and isinstance(node.get("props"), dict):
        has_cs, cs = _ci_lookup(node["props"], "cellSize")
        if has_cs and isinstance(cs, list) and len(cs) >= 2 and _is_number(cs[0]) and _is_number(cs[1]):
            return float(cs[0]), float(cs[1])

    return None


def _get_prefab_category(base_id: str | None, catalog: CatalogIndex) -> str:
    if _is_non_empty_str(base_id):
        entry = catalog.get_prefab(base_id)
        if entry is not None and _is_non_empty_str(entry.get("category")):
            return entry["category"].strip()
    return "Common"


def _accepts_category(accepted: list[str] | None, candidate: str) -> bool:
    if not accepted:
        return True
    cand_low = candidate.strip().lower()
    for item in accepted:
        if not isinstance(item, str):
            continue
        token = item.strip().lower()
        if token in {"*", "all"} or token == cand_low:
            return True
    return False


def _validate_node_recursive(
    node_obj: Any,
    parent_norm: dict[str, Any] | None,
    has_root_script: bool,
    context_label: str,
    seen_ids: set[str],
    all_node_ids: set[str],
    catalog: CatalogIndex | None,
    errors: list[str],
    warnings: list[str],
) -> None:
    node = _normalize_object(node_obj, NODE_KEYS, NODE_HINTS, context_label, errors, warnings)
    if node is None:
        return

    raw_id = node.get("id")
    if not _is_non_empty_str(raw_id):
        errors.append(f"{context_label}: 'id' is required and cannot be empty.")
        node_label = context_label
    else:
        node_id = raw_id.strip()
        node_label = f"Node '{node_id}'"
        low_id = node_id.lower()
        if low_id in seen_ids:
            errors.append(f"Duplicate node id '{node_id}' in hierarchy.")
        else:
            seen_ids.add(low_id)

    kind = node.get("kind")
    use_base = node.get("useBase")
    if _is_non_empty_str(kind) and kind.strip().lower() != "emptyrect":
        errors.append(
            f"{node_label}: invalid kind '{kind}'. Only 'emptyRect' is supported (use 'useBase' for Base Prefabs)."
        )

    is_empty_rect = isinstance(kind, str) and kind.strip().lower() == "emptyrect"
    has_use_base = _is_non_empty_str(use_base)

    if not has_use_base and not is_empty_rect:
        errors.append(
            f"{node_label} must specify either 'useBase' or '\"kind\": \"emptyRect\"'."
        )
    elif is_empty_rect and has_use_base:
        errors.append(
            f"{node_label} cannot specify both 'useBase' and 'kind: emptyRect'."
        )

    if node.get("active") is not None and not isinstance(node["active"], bool):
        errors.append(f"{node_label}: 'active' must be a boolean.")
    if node.get("name") is not None and not isinstance(node["name"], str):
        errors.append(f"{node_label}: 'name' must be a string.")

    norm_rect = None
    if node.get("rect") is not None:
        norm_rect = validate_rect(node["rect"], f"{node_label}.rect", errors, warnings)

    if node.get("layoutGroup") is not None:
        node["layoutGroup"] = validate_layout_group(
            node["layoutGroup"], f"{node_label}.layoutGroup", errors, warnings
        )
        if has_use_base and not is_empty_rect:
            warnings.append(
                f"{node_label}: 'layoutGroup' on a useBase node is ignored by UILayoutBuilder; place layoutGroup on an emptyRect container."
            )

    norm_le = None
    if node.get("layoutElement") is not None:
        norm_le = validate_layout_element(
            node["layoutElement"], f"{node_label}.layoutElement", errors, warnings
        )

    norm_csf = None
    if node.get("contentSizeFitter") is not None:
        norm_csf = validate_content_size_fitter(
            node["contentSizeFitter"], f"{node_label}.contentSizeFitter", errors, warnings
        )

    if norm_csf is not None and norm_rect is not None:
        anchor_val = str(norm_rect.get("anchor", "MiddleCenter")).strip().lower()
        h_fit = str(norm_csf.get("horizontalFit", "Unconstrained")).strip().lower()
        v_fit = str(norm_csf.get("verticalFit", "Unconstrained")).strip().lower()
        if anchor_val == "stretchall" and (h_fit != "unconstrained" or v_fit != "unconstrained"):
            warnings.append(
                f"{node_label}: ContentSizeFitter with '{h_fit}/{v_fit}' conflicts with StretchAll; use TopStretch, LeftStretch, or a point anchor."
            )

    props = node.get("props")
    if props is not None and not isinstance(props, dict):
        errors.append(f"{node_label}: 'props' must be an object.")
        props = None

    part_overrides = node.get("partOverrides")
    if part_overrides is not None:
        part_overrides = validate_part_overrides_structural(
            part_overrides, node_label, errors, warnings
        )
        node["partOverrides"] = part_overrides

    slot_children = node.get("slotChildren")
    if slot_children is not None and not isinstance(slot_children, dict):
        errors.append(f"{node_label}: 'slotChildren' must be an object.")
        slot_children = None

    children = node.get("children")
    if children is not None and not isinstance(children, list):
        errors.append(f"{node_label}: 'children' must be an array.")
        children = None

    if is_empty_rect:
        if props and len(props) > 0:
            errors.append(
                f"emptyRect {node_label} cannot have 'props'. Visual components must come from Base Prefabs."
            )
        if slot_children and len(slot_children) > 0:
            errors.append(
                f"emptyRect {node_label} cannot have 'slotChildren'. Use 'children' instead."
            )
        if part_overrides and len(part_overrides) > 0:
            errors.append(f"emptyRect {node_label} cannot have 'partOverrides'.")
    else:
        if children and len(children) > 0:
            warnings.append(
                f"{node_label}: useBase nodes ignore 'children'; place child nodes inside 'slotChildren'."
            )

    if node.get("rootScript") is not None:
        rs = _normalize_object(
            node["rootScript"],
            ROOT_SCRIPT_KEYS,
            ROOT_SCRIPT_HINTS,
            f"{node_label}.rootScript",
            errors,
            warnings,
        )
        if rs is not None:
            ftn = rs.get("fullTypeName")
            if not _is_non_empty_str(ftn):
                errors.append(f"{node_label}.rootScript: 'fullTypeName' is required.")
            elif catalog is not None and catalog.get_script(ftn) is None:
                errors.append(
                    f"{node_label}: rootScript '{ftn}' is not registered as [AIManagedScript] in catalog."
                )
            if rs.get("fields") is not None and not isinstance(rs["fields"], dict):
                errors.append(f"{node_label}.rootScript: 'fields' must be an object.")

    if node.get("bindToRoot") is not None:
        if not _is_non_empty_str(node["bindToRoot"]):
            errors.append(f"{node_label}: 'bindToRoot' must be a non-empty string.")
        elif not has_root_script:
            errors.append(
                f"{node_label}: specifies bindToRoot='{node['bindToRoot']}' but root has no 'rootScript'."
            )

    if node.get("bindToParent") is not None:
        bp = _normalize_object(
            node["bindToParent"],
            PARENT_BINDING_KEYS,
            PARENT_BINDING_HINTS,
            f"{node_label}.bindToParent",
            errors,
            warnings,
        )
        if bp is not None and not _is_non_empty_str(bp.get("field")):
            errors.append(f"{node_label}.bindToParent: 'field' is required.")

    if node.get("refs") is not None:
        validate_refs_map(
            node["refs"],
            f"{node_label}.refs",
            is_merge=False,
            catalog=catalog,
            all_node_ids=all_node_ids,
            errors=errors,
            warnings=warnings,
        )

    # Parent GridLayoutGroup conflict check
    if parent_norm is not None:
        cell_size = _extract_grid_cell_size(parent_norm)
        if cell_size is not None:
            grid_w, grid_h = cell_size
            if norm_rect is not None and isinstance(norm_rect.get("size"), list) and len(norm_rect["size"]) >= 2:
                cw = float(norm_rect["size"][0])
                ch = float(norm_rect["size"][1])
                if abs(cw - grid_w) > 0.01 or abs(ch - grid_h) > 0.01:
                    errors.append(
                        f"Child {node_label} specifies rect.size [{cw}, {ch}] which conflicts with parent GridLayoutGroup.cellSize [{grid_w}, {grid_h}]. In GridLayoutGroup, cell dimensions are governed strictly by parent cellSize."
                    )
            if norm_le is not None and (
                norm_le.get("preferredWidth") is not None or norm_le.get("preferredHeight") is not None
            ):
                warnings.append(
                    f"Child {node_label} specifies layoutElement preferredWidth/preferredHeight, which GridLayoutGroup ignores."
                )

    # Catalog checks for useBase
    if catalog is not None and has_use_base and not is_empty_rect:
        prefab_entry = catalog.get_prefab(use_base)
        if prefab_entry is None:
            errors.append(f"{node_label}: Base Prefab '{use_base}' not found in catalog.")
        else:
            if props:
                declared_props = prefab_entry.get("props") or {}
                for prop_name in props:
                    has_prop, _ = _ci_lookup(declared_props, prop_name)
                    if not has_prop:
                        errors.append(
                            f"{node_label}: property '{prop_name}' is not exposed in Base Prefab '{use_base}' manifest."
                        )

            if part_overrides:
                validate_part_overrides_catalog(
                    part_overrides, prefab_entry, use_base, node_label, catalog, errors
                )

            if slot_children:
                declared_slots = prefab_entry.get("slots") or {}
                for slot_name, child_list in slot_children.items():
                    has_slot, slot_contract = _ci_lookup(declared_slots, slot_name)
                    if not has_slot or not isinstance(slot_contract, dict):
                        errors.append(
                            f"{node_label}: slot '{slot_name}' is not defined on Base Prefab '{use_base}'."
                        )
                        continue
                    if not isinstance(child_list, list):
                        errors.append(
                            f"{node_label}.slotChildren['{slot_name}'] must be an array."
                        )
                        continue
                    accepted = slot_contract.get("acceptedCategories")
                    for child_raw in child_list:
                        if not isinstance(child_raw, dict):
                            continue
                        c_kind = None
                        c_base = None
                        c_id = "?"
                        for k, v in child_raw.items():
                            if not isinstance(k, str):
                                continue
                            low = k.lower()
                            if low == "kind":
                                c_kind = v
                            elif low == "usebase":
                                c_base = v
                            elif low == "id" and v:
                                c_id = str(v)
                        c_is_empty = isinstance(c_kind, str) and c_kind.strip().lower() == "emptyrect"
                        child_cat = "Container" if c_is_empty else _get_prefab_category(c_base, catalog)
                        if not _accepts_category(accepted, child_cat):
                            errors.append(
                                f"{node_label} slot '{slot_name}': child '{c_id}' category '{child_cat}' is not accepted by slot (allowed: {accepted})."
                            )

    if children:
        for idx, child in enumerate(children):
            _validate_node_recursive(
                child,
                node,
                has_root_script,
                f"{node_label}.children[{idx}]",
                seen_ids,
                all_node_ids,
                catalog,
                errors,
                warnings,
            )

    if slot_children:
        for slot_name, child_list in slot_children.items():
            if not isinstance(child_list, list):
                if catalog is None:
                    errors.append(f"{node_label}.slotChildren['{slot_name}'] must be an array.")
                continue
            for idx, child in enumerate(child_list):
                _validate_node_recursive(
                    child,
                    node,
                    has_root_script,
                    f"{node_label}.slotChildren['{slot_name}'][{idx}]",
                    seen_ids,
                    all_node_ids,
                    catalog,
                    errors,
                    warnings,
                )


def validate_layout_document(
    data: Any, catalog_data: dict[str, Any] | None = None
) -> dict[str, Any]:
    errors: list[str] = []
    warnings: list[str] = []

    if not isinstance(data, dict):
        return {
            "valid": False,
            "mode": "layout",
            "errors": ["Root layout JSON must be an object."],
            "warnings": [],
        }

    catalog = CatalogIndex(catalog_data) if isinstance(catalog_data, dict) else None
    all_node_ids: set[str] = set()
    _collect_node_ids(data, all_node_ids)

    has_root_script = False
    for k, v in data.items():
        if isinstance(k, str) and k.lower() == "rootscript" and isinstance(v, dict):
            has_root_script = _is_non_empty_str(v.get("fullTypeName"))
            break

    seen_ids: set[str] = set()
    _validate_node_recursive(
        data,
        None,
        has_root_script,
        "Root",
        seen_ids,
        all_node_ids,
        catalog,
        errors,
        warnings,
    )

    return {
        "valid": len(errors) == 0,
        "mode": "layout",
        "errors": errors,
        "warnings": warnings,
    }


def _validate_patch_part_overrides_catalog(
    parts_map: dict[str, dict[str, Any]],
    context_label: str,
    catalog: CatalogIndex,
    errors: list[str],
) -> None:
    for part_name, override in parts_map.items():
        if not isinstance(override, dict):
            continue
        part_label = f"{context_label} part '{part_name}'"
        part_base_id = override.get("useBase")
        if _is_non_empty_str(part_base_id):
            part_prefab = catalog.get_prefab(part_base_id)
            if part_prefab is None:
                errors.append(f"{part_label}: base prefab '{part_base_id}' not found in catalog.")
                continue
            props = override.get("props")
            if isinstance(props, dict):
                declared_props = part_prefab.get("props") or {}
                for prop_name in props:
                    has_prop, _ = _ci_lookup(declared_props, prop_name)
                    if not has_prop:
                        errors.append(
                            f"{part_label}: property '{prop_name}' is not exposed in Base Prefab '{part_base_id}' manifest."
                        )
            nested = override.get("partOverrides")
            if isinstance(nested, dict) and nested:
                validate_part_overrides_catalog(
                    nested, part_prefab, part_base_id, part_label, catalog, errors
                )
        elif isinstance(override.get("partOverrides"), dict):
            _validate_patch_part_overrides_catalog(
                override["partOverrides"], part_label, catalog, errors
            )


def validate_patch_document(
    data: Any, catalog_data: dict[str, Any] | None = None
) -> dict[str, Any]:
    errors: list[str] = []
    warnings: list[str] = []

    if not isinstance(data, dict):
        return {
            "valid": False,
            "mode": "patch",
            "errors": ["Patch JSON must be an object."],
            "warnings": [],
        }

    catalog = CatalogIndex(catalog_data) if isinstance(catalog_data, dict) else None
    patch = _normalize_object(data, PATCH_KEYS, PATCH_HINTS, "Patch", errors, warnings)
    if patch is None:
        return {"valid": False, "mode": "patch", "errors": errors, "warnings": warnings}

    has_node_id = _is_non_empty_str(patch.get("targetNodeId"))
    has_node_path = _is_non_empty_str(patch.get("targetNodePath"))

    if not has_node_id and not has_node_path:
        errors.append("Patch must specify either 'targetNodeId' or 'targetNodePath'.")
    elif has_node_id and has_node_path:
        errors.append(
            "Patch cannot specify both 'targetNodeId' and 'targetNodePath'; choose exactly one."
        )

    set_active = patch.get("setActive")
    if set_active is not None and not isinstance(set_active, bool):
        errors.append("Patch: 'setActive' must be a boolean.")

    set_props = patch.get("setProps")
    if set_props is not None and not isinstance(set_props, dict):
        errors.append("Patch: 'setProps' must be an object.")
        set_props = None

    set_rect = patch.get("setRect")
    if set_rect is not None:
        validate_rect(set_rect, "Patch.setRect", errors, warnings)

    set_lg = patch.get("setLayoutGroup")
    if set_lg is not None:
        validate_layout_group(set_lg, "Patch.setLayoutGroup", errors, warnings)

    set_le = patch.get("setLayoutElement")
    if set_le is not None:
        validate_layout_element(set_le, "Patch.setLayoutElement", errors, warnings)

    set_csf = patch.get("setContentSizeFitter")
    if set_csf is not None:
        validate_content_size_fitter(set_csf, "Patch.setContentSizeFitter", errors, warnings)

    part_overrides = patch.get("partOverrides")
    if part_overrides is not None:
        part_overrides = validate_part_overrides_structural(
            part_overrides, "Patch", errors, warnings
        )
        if catalog is not None and part_overrides:
            _validate_patch_part_overrides_catalog(part_overrides, "Patch", catalog, errors)

    slot_ops = patch.get("slotOperations")
    if slot_ops is not None and not isinstance(slot_ops, list):
        errors.append("Patch: 'slotOperations' must be an array.")
        slot_ops = None

    set_refs = patch.get("setRefs")
    if set_refs is not None:
        validate_refs_map(
            set_refs,
            "Patch.setRefs",
            is_merge=False,
            catalog=catalog,
            all_node_ids=None,
            errors=errors,
            warnings=warnings,
        )

    merge_refs = patch.get("mergeRefs")
    if merge_refs is not None:
        validate_refs_map(
            merge_refs,
            "Patch.mergeRefs",
            is_merge=True,
            catalog=catalog,
            all_node_ids=None,
            errors=errors,
            warnings=warnings,
        )

    has_mutation = (
        (set_active is not None)
        or (isinstance(set_props, dict) and len(set_props) > 0)
        or (set_rect is not None)
        or (set_lg is not None)
        or (set_le is not None)
        or (set_csf is not None)
        or (isinstance(part_overrides, dict) and len(part_overrides) > 0)
        or (isinstance(slot_ops, list) and len(slot_ops) > 0)
        or (isinstance(set_refs, dict) and len(set_refs) > 0)
        or (isinstance(merge_refs, dict) and len(merge_refs) > 0)
    )

    if not has_mutation:
        errors.append(
            "Patch must include at least one change in 'setActive', 'setProps', 'setRect', 'setLayoutGroup', 'setLayoutElement', 'setContentSizeFitter', 'partOverrides', 'slotOperations', 'setRefs', or 'mergeRefs'."
        )

    if isinstance(slot_ops, list):
        seen_op_node_ids: set[str] = set()
        for idx, op_raw in enumerate(slot_ops):
            op_label = f"Patch.slotOperations[{idx}]"
            op = _normalize_object(
                op_raw, SLOT_OP_KEYS, SLOT_OP_HINTS, op_label, errors, warnings
            )
            if op is None:
                continue

            slot_name = op.get("slotName")
            if not _is_non_empty_str(slot_name):
                errors.append(f"{op_label}: 'slotName' is required.")

            raw_action = op.get("action")
            if not _is_non_empty_str(raw_action):
                errors.append(f"{op_label}: 'action' is required.")
                continue

            action = raw_action.strip().lower()
            if action not in SLOT_ACTIONS:
                errors.append(
                    f"{op_label}: invalid slot action '{raw_action}'. Allowed: append, insert, replace, remove, reorder, clear."
                )
                continue

            index = op.get("index")
            if index is not None and (not _is_int(index) or index < 0):
                errors.append(f"{op_label}: 'index' must be a non-negative integer.")

            node_id = op.get("nodeId")
            if node_id is not None and not isinstance(node_id, str):
                errors.append(f"{op_label}: 'nodeId' must be a string.")

            op_node = op.get("node")

            if action in {"append", "insert", "replace"}:
                if op_node is None:
                    errors.append(
                        f"{op_label}: slot action '{action}' requires a valid 'node'."
                    )
                else:
                    op_node_ids: set[str] = set()
                    _collect_node_ids(op_node, op_node_ids)
                    # Inserted/replaced slot subtrees can bindToRoot against the target scene/prefab root.
                    _validate_node_recursive(
                        op_node,
                        None,
                        True,
                        f"{op_label}.node",
                        seen_op_node_ids,
                        op_node_ids,
                        catalog,
                        errors,
                        warnings,
                    )

            if action == "replace" and not _is_non_empty_str(node_id) and index is None:
                errors.append(
                    f"{op_label}: slot action 'replace' requires 'nodeId' or 'index'."
                )
            elif action == "remove":
                if not _is_non_empty_str(node_id):
                    errors.append(f"{op_label}: slot action 'remove' requires 'nodeId'.")
                else:
                    seen_op_node_ids.discard(node_id.strip().lower())
            elif action == "reorder":
                if not _is_non_empty_str(node_id):
                    errors.append(f"{op_label}: slot action 'reorder' requires 'nodeId'.")
                if index is None:
                    errors.append(f"{op_label}: slot action 'reorder' requires 'index'.")

    return {
        "valid": len(errors) == 0,
        "mode": "patch",
        "errors": errors,
        "warnings": warnings,
    }


def _load_json_file(path: Path) -> Any:
    text = path.read_text(encoding="utf-8")
    return json.loads(text)


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Validate UI Maker layout (.ui.json) or patch JSON files."
    )
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--layout", type=Path, help="Path to a UILayoutNode JSON file.")
    group.add_argument("--patch", type=Path, help="Path to a UIPatchDocument JSON file.")
    parser.add_argument(
        "--catalog",
        type=Path,
        default=None,
        help="Optional path to ui_catalog.json for catalog-aware validation.",
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="Emit validation result as JSON.",
    )
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    target_path = args.layout if args.layout is not None else args.patch
    mode = "layout" if args.layout is not None else "patch"

    try:
        payload = _load_json_file(target_path)
    except (OSError, json.JSONDecodeError) as exc:
        result = {
            "valid": False,
            "mode": mode,
            "errors": [f"Failed to read JSON file '{target_path}': {exc}"],
            "warnings": [],
        }
        if args.json:
            print(json.dumps(result, indent=2))
        else:
            print(f"ERROR: {result['errors'][0]}", file=sys.stderr)
        return 1

    catalog_data = None
    if args.catalog is not None:
        try:
            catalog_data = _load_json_file(args.catalog)
        except (OSError, json.JSONDecodeError) as exc:
            result = {
                "valid": False,
                "mode": mode,
                "errors": [f"Failed to read catalog JSON '{args.catalog}': {exc}"],
                "warnings": [],
            }
            if args.json:
                print(json.dumps(result, indent=2))
            else:
                print(f"ERROR: {result['errors'][0]}", file=sys.stderr)
            return 1

    result = (
        validate_layout_document(payload, catalog_data)
        if mode == "layout"
        else validate_patch_document(payload, catalog_data)
    )

    if args.json:
        print(json.dumps(result, indent=2))
    else:
        for warn in result["warnings"]:
            print(f"WARNING: {warn}")
        for err in result["errors"]:
            print(f"ERROR: {err}", file=sys.stderr)
        if result["valid"]:
            print(f"OK: {mode} JSON '{target_path}' is valid.")

    return 0 if result["valid"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
