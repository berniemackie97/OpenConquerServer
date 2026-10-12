#!/usr/bin/env python3
"""Rebuild the reviewed world-transition candidate catalog from the pinned evidence export."""

import hashlib
import json
import os
import pathlib
import tempfile
import sys

SOURCE_SHA256 = "d728c4ec78fb75266b90ebce99f847aebd7ccdeb283d20ad9066f64747ff4ab3"
OUTPUT_SHA256 = "4b5258a9d70fd04e87e76fc9be00c4bb8464638f1f2f6d449e225d8cd9d62ea0"
STATUSES = {
    "valid": "resolved",
    "no_row_any_source": "no-source",
    "no_row_terrain_shared": "shared-terrain-unresolved",
    "target_blocked_all_sources_agree": "blocked-destination",
}


def main(source: pathlib.Path, destination: pathlib.Path) -> None:
    raw = source.read_bytes()
    if hashlib.sha256(raw).hexdigest() != SOURCE_SHA256:
        raise ValueError("Passway evidence SHA-256 does not match the reviewed source")
    data = json.loads(raw)
    rows = data["rows"]
    entries = []
    seen = set()
    for row in sorted(rows, key=lambda item: (item["map_id"], item["passway_idx"])):
        key = (row["map_id"], row["passway_idx"])
        if key in seen:
            raise ValueError(f"Duplicate native exit: {key}")
        seen.add(key)
        status = STATUSES[row["status"]]
        entry = {"mapId": row["map_id"], "passwayIndex": row["passway_idx"], "status": status}
        if status == "resolved":
            if not row["target_walkable"] or row["source"] not in ("L", "T7", "Q"):
                raise ValueError(f"Unreviewed destination: {key}")
            entry.update(source=row["source"], targetMapId=row["target_map"],
                         targetPortalIndex=row["target_portal"], targetX=row["target_x"], targetY=row["target_y"])
        entries.append(entry)
    if len(entries) != 373 or sum(e["status"] == "resolved" for e in entries) != 322:
        raise ValueError("Candidate counts no longer match the reviewed evidence")
    output = (json.dumps({"formatVersion": 1, "transitions": entries}, ensure_ascii=False, indent=2) + "\n").encode()
    if hashlib.sha256(output).hexdigest() != OUTPUT_SHA256:
        raise ValueError("Regenerated catalog differs from the pinned release")
    if destination.exists():
        if destination.read_bytes() != output:
            raise FileExistsError("Existing destination differs; refusing to overwrite")
        return
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(dir=destination.parent, prefix=".transitions-", delete=False) as staged:
            temporary = pathlib.Path(staged.name)
            staged.write(output)
            staged.flush()
            os.fsync(staged.fileno())
        os.link(temporary, destination)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit("usage: rebuild.py <passways-5517.json> <new-output-path>")
    main(pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2]))
