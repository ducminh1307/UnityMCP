from __future__ import annotations

import json
import os
from pathlib import Path

import pytest

from unity_mcp_server.discovery import discover_instances, select_instance
from unity_mcp_server.errors import AmbiguousInstanceError, InstanceNotFoundError
from unity_mcp_server.models import InstanceDescriptor


def descriptor(instance_id: str, pid: int = 100, *, kind: str = "editor") -> dict:
    return {
        "port": 38291,
        "token": "t" * 48,
        "pid": pid,
        "projectId": "project-a",
        "instanceId": instance_id,
        "kind": kind,
        "buildId": "build-1",
    }


def write_private(path: Path, value: str) -> None:
    path.write_text(value, encoding="utf-8")
    if os.name != "nt":
        path.chmod(0o600)


def test_discovery_filters_stale_and_malformed_descriptors(tmp_path) -> None:
    write_private(tmp_path / "live.json", json.dumps(descriptor("live", 10)))
    write_private(tmp_path / "stale.json", json.dumps(descriptor("stale", 20)))
    write_private(tmp_path / "interrupted.json", '{"port":')
    (tmp_path / "not-json.txt").write_text("ignored", encoding="utf-8")

    found = discover_instances(tmp_path, liveness=lambda pid: pid == 10)

    assert [item.instance_id for item in found] == ["live"]
    assert found[0].path == tmp_path / "live.json"
    assert "t" * 48 not in repr(found[0])
    assert "token" not in found[0].public_dict()


def test_discovery_deduplicates_instance_id_deterministically(tmp_path) -> None:
    write_private(tmp_path / "a.json", json.dumps(descriptor("same", 10)))
    second = descriptor("same", 11)
    second["port"] = 38292
    write_private(tmp_path / "b.json", json.dumps(second))

    found = discover_instances(tmp_path, liveness=lambda _: True)

    assert len(found) == 1
    assert found[0].port == 38291


def test_instance_selection_requires_explicit_id_when_ambiguous() -> None:
    first = InstanceDescriptor.from_dict(descriptor("editor"))
    second = InstanceDescriptor.from_dict(descriptor("player", kind="player"))

    with pytest.raises(AmbiguousInstanceError):
        select_instance([first, second])
    assert select_instance([first, second], "player") is second
    with pytest.raises(InstanceNotFoundError):
        select_instance([first, second], "missing")


def test_invalid_descriptor_fields_are_rejected() -> None:
    raw = descriptor("bad")
    raw["port"] = 0
    with pytest.raises(Exception, match="port"):
        InstanceDescriptor.from_dict(raw)


@pytest.mark.skipif(os.name == "nt", reason="POSIX permission bits are not enforced on Windows")
def test_discovery_rejects_group_or_other_readable_descriptor(tmp_path) -> None:
    insecure = tmp_path / "insecure.json"
    insecure.write_text(json.dumps(descriptor("insecure", 10)), encoding="utf-8")
    insecure.chmod(0o644)

    assert discover_instances(tmp_path, liveness=lambda _: True) == []
