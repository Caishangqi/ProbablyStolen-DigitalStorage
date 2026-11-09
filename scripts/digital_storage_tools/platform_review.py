"""Validate reviewed Platform test inputs and test project inventory."""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
MANIFEST = ROOT / "tests/Unit/ProbablyStolen.DigitalStorage.Platform.Tests/reviewed-inputs.json"
PROJECTS = ("src/ProbablyStolen.DigitalStorage.Common", "src/ProbablyStolen.DigitalStorage.Client", "src/ProbablyStolen.DigitalStorage.Platform", "tests/Unit/ProbablyStolen.DigitalStorage.Platform.Tests")
CONFIG = ("Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json",
          "scripts/test.ps1", "scripts/tool.py", "scripts/digital_storage_tools/cli.py",
          "scripts/digital_storage_tools/platform_review.py", "scripts/digital_storage_tools/trx.py")


AUTO_IMPORT_NAMES = {
    "directory.build.props", "directory.build.targets", "directory.packages.props",
    "directory.packages.targets", "directory.build.rsp", "msbuild.rsp", "global.json", "nuget.config",
}


def input_hashes(root: Path = ROOT) -> dict[str, str]:
    paths = [root / path for path in CONFIG]
    for project in PROJECTS:
        project_path = root / project
        for ancestor in (project_path, *project_path.parents):
            if not ancestor.is_relative_to(root):
                break
            if ancestor.is_symlink() or ancestor.is_junction():
                raise ValueError("Linked review input ancestor")
            for candidate in ancestor.iterdir():
                if candidate.name.casefold() in AUTO_IMPORT_NAMES:
                    paths.append(candidate)
        for directory, names, files in os.walk(root / project, followlinks=False):
            for name in list(names):
                path = Path(directory) / name
                status = path.lstat()
                if path.is_symlink() or getattr(status, 'st_file_attributes', 0) & 0x400:
                    raise ValueError('Linked review input directory')
                if name.lower() in ('bin', 'obj'):
                    names.remove(name)
            for name in files:
                if name.casefold() != 'reviewed-inputs.json' and name.casefold().endswith(('.cs', '.csproj', '.props', '.targets', '.json')):
                    paths.append(Path(directory) / name)
    output = {}
    for path in paths:
        status = path.lstat()
        if path.is_symlink() or getattr(status, 'st_file_attributes', 0) & 0x400:
            raise ValueError('Linked review input file')
        data = path.read_bytes().replace(b'\r\n', b'\n')
        output[path.relative_to(root).as_posix()] = hashlib.sha256(data).hexdigest()
    return output


def _is_test_boundary_input(path: str) -> bool:
    # Platform test sources, their project and every MSBuild auto-import decide which code the test host can run.
    return path.startswith(PROJECTS[-1] + "/") or path.rsplit("/", 1)[-1].casefold() in AUTO_IMPORT_NAMES


def validate(root: Path = ROOT, tests_only: bool = False) -> dict:
    """Compare current inputs with the reviewed manifest.

    ``tests_only`` drops product sources and tooling scripts from the comparison but still pins the Platform test
    project and auto-imported build configuration. Product code reached by the approved tests is not reviewed in
    this mode, so a product change can still alter what those tests execute.
    """
    manifest = json.loads((root / MANIFEST.relative_to(ROOT)).read_text(encoding='utf-8'))
    expected, actual = manifest['inputs'], input_hashes(root)
    if tests_only:
        expected = {key: value for key, value in expected.items() if _is_test_boundary_input(key)}
        actual = {key: value for key, value in actual.items() if _is_test_boundary_input(key)}
        if expected != actual:
            raise ValueError('Platform test boundary inputs changed; independent review is required')
    elif expected != actual:
        raise ValueError('Platform reviewed inputs changed; independent review is required')
    return manifest
