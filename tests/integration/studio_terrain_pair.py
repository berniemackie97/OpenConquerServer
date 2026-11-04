"""Verify the Studio CLI and server content tool as separate executable processes."""

import pathlib
import subprocess
import sys
import tempfile
import uuid


def run(tool, *arguments, expected=0):
    result = subprocess.run(
        ["dotnet", str(tool), *map(str, arguments)],
        capture_output=True,
        text=True,
        timeout=120,
        check=False,
    )
    if result.returncode != expected:
        raise AssertionError(
            f"{tool.name} returned {result.returncode}; expected {expected}\n"
            f"{result.stdout}\n{result.stderr}"
        )
    return result.stdout.strip()


def files(root):
    result = {}
    for folder, extension in (("terrains", ".ocbt"), ("collisions", ".ocmc")):
        matches = list((root / folder).iterdir())
        if len(matches) != 1 or matches[0].suffix != extension:
            raise AssertionError(f"Unexpected {folder} output: {matches}")
        result[folder] = matches[0].read_bytes()
        if not result[folder]:
            raise AssertionError(f"{folder} output is empty")
    if sorted(p.name for p in root.iterdir()) != ["collisions", "terrains"]:
        raise AssertionError("Unexpected files in the published terrain pair")
    return result


def main():
    if len(sys.argv) != 3:
        raise SystemExit("usage: studio_terrain_pair.py <studio-tool.dll> <server-tool.dll>")

    studio, server = map(lambda path: pathlib.Path(path).resolve(), sys.argv[1:])
    if not studio.is_file() or not server.is_file():
        raise FileNotFoundError("Both compiled tool assemblies must exist")

    with tempfile.TemporaryDirectory(prefix="studio-terrain-pair-") as temporary:
        root = pathlib.Path(temporary)
        project = root / "project"
        project.mkdir()

        run(studio, "create", project, "Integrated world")
        resource = run(studio, "new-terrain", project, "Field", 900001, 3, 2, 12, 0, -30)
        uuid.UUID(resource)
        run(studio, "terrain-cell", project, resource, 2, 1, 65535, 5, -32768)

        first = root / "first"
        second = root / "second"
        source = root / "source.json"
        direct = root / "direct"

        run(studio, "build-terrain", project, resource, server, first)
        run(studio, "build-terrain", project, resource, server, second)
        run(studio, "export-base-terrain", project, resource, source)
        run(server, "build-authored-terrain", source, direct)

        first_bytes = files(first)
        if first_bytes != files(second) or first_bytes != files(direct):
            raise AssertionError("Studio and server produced different terrain/collision bytes")

        for folder, extension in (("terrains", ".ocbt"), ("collisions", ".ocmc")):
            generated = first / folder / f"900001{extension}"
            if not generated.is_file():
                raise AssertionError(f"Missing {generated}")

        run(studio, "build-terrain", project, resource, server, first, expected=1)
        if first_bytes != files(first):
            raise AssertionError("Repeated publication modified an existing terrain pair")

        run(studio, "build-terrain", project, resource, root / "missing.dll", root / "rejected", expected=1)
        if (root / "rejected").exists():
            raise AssertionError("Tool failure published an incomplete terrain pair")

        if list(root.glob(".studio-terrain-*")):
            raise AssertionError("The Studio compiler left staging files after publication")

    print("Studio-to-server terrain/collision integration passed")


if __name__ == "__main__":
    main()
