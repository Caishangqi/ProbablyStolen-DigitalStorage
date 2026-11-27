"""Local game assembly snapshots, one per Steam branch and build, and the binding check that runs against each.

``snapshot-game`` copies the managed DLLs of the installed game into ``game-refs/<branch>-<buildid>/`` with a
manifest of their hashes. Steam installs one branch at a time, so a snapshot keeps a build available for compiling
and checking after the install switches branch. The root ``.gitignore`` admits only listed top-level entries, so
``game-refs/`` is never committed; snapshots are not packaged or published.

``check-game-bindings`` builds the binding tests against every snapshot and runs them. They look up each patch target
and required member without patching or running game code. No command here starts the game or touches saves.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

from .decompile import GAME_ENV, WorkflowError, _build_id, _reject_reparse_components, _sha256

SNAPSHOT_ROOT = "game-refs"
MANAGED = Path("Probably Stolen_Data") / "Managed"
MANIFEST = "snapshot-manifest.json"
PUBLIC_BRANCH = "public"
BINDING_TESTS = Path("tests/Compatibility/ProbablyStolen.DigitalStorage.GameBindingTests/ProbablyStolen.DigitalStorage.GameBindingTests.csproj")
_BRANCH = re.compile(r"^[A-Za-z0-9_.-]{1,64}$")


def installed_branch(game_dir: Path) -> str:
    """Returns the Steam branch the installed files belong to; ``public`` when no beta is mounted."""
    manifest = game_dir.parent.parent / "appmanifest_4348910.acf"
    text = manifest.read_text(encoding="utf-8", errors="strict")
    # MountedConfig describes the files on disk; UserConfig is the branch the player selected, which may still be
    # downloading.
    mounted = re.search(r'"MountedConfig"\s*\{([^{}]*)\}', text)
    beta = re.search(r'"BetaKey"\s+"([^"]*)"', mounted.group(1)) if mounted else None
    branch = beta.group(1).strip() if beta and beta.group(1).strip() else PUBLIC_BRANCH
    if not _BRANCH.fullmatch(branch):
        raise WorkflowError(f"Steam branch name is not usable as a directory name: {branch!r}")
    return branch


def installed_build(game_dir: Path) -> tuple[str, str]:
    """Returns the installed Steam branch and build id."""
    return installed_branch(game_dir), _build_id(game_dir)


PACKAGE_BUILD = "package-build.json"


def record_package_build(game_dir: Path, package_dir: Path, output_dir: Path, assemblies: list[str]) -> Path:
    """Records which installed game build the package's assemblies were compiled against."""
    game_dir = game_dir.resolve(strict=True)
    package_dir = package_dir.resolve(strict=True)
    branch, build_id = installed_build(game_dir)
    record = {
        "schemaVersion": 1,
        "steamBranch": branch,
        "steamBuildId": build_id,
        "assemblyCSharpSha256": _sha256(game_dir / MANAGED / "Assembly-CSharp.dll"),
        "packageDirectory": str(package_dir),
        "assemblies": {name: _sha256(package_dir / name) for name in assemblies},
    }
    output_dir.mkdir(parents=True, exist_ok=True)
    path = output_dir / PACKAGE_BUILD
    path.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
    return path


def read_package_build(output_dir: Path, package_dir: Path, assemblies: list[str]) -> tuple[str, str]:
    """Returns the branch and build the package was compiled against; refuses a record of another package."""
    path = output_dir / PACKAGE_BUILD
    if not path.is_file():
        raise WorkflowError(f"No package build record at {path}; create the package with mise run package first.")
    record = json.loads(path.read_text(encoding="utf-8"))
    branch, build_id = record.get("steamBranch"), record.get("steamBuildId")
    if (not isinstance(branch, str) or not _BRANCH.fullmatch(branch) or not isinstance(build_id, str)
            or not build_id.isdigit()):
        raise WorkflowError(f"The package build record is invalid: {path}")
    current = {name: _sha256(package_dir / name) for name in assemblies}
    if record.get("assemblies") != current:
        raise WorkflowError("The package assemblies changed since they were built; run mise run package again.")
    return branch, build_id


def _assemblies(managed: Path) -> list[dict[str, str]]:
    return [{"name": dll.name, "sha256": _sha256(dll)} for dll in sorted(managed.glob("*.dll")) if dll.is_file()]


def snapshot(game_dir: Path, repository_root: Path) -> Path:
    """Copies the installed game's managed DLLs into ``game-refs/<branch>-<buildid>/``; returns that directory."""
    game_dir = game_dir.resolve(strict=True)
    repository_root = repository_root.resolve(strict=True)
    managed = game_dir / MANAGED
    if not (managed / "Assembly-CSharp.dll").is_file():
        raise WorkflowError(f"Game Managed directory is missing Assembly-CSharp.dll: {managed}")
    branch, build_id = installed_build(game_dir)
    assemblies = _assemblies(managed)
    root = repository_root / SNAPSHOT_ROOT
    target = root / f"{branch}-{build_id}"
    _reject_reparse_components(root, repository_root)
    _reject_reparse_components(target, repository_root)
    manifest_path = target / MANIFEST
    if manifest_path.is_file():
        previous = json.loads(manifest_path.read_text(encoding="utf-8"))
        if previous.get("assemblies") == assemblies:
            return target
        raise WorkflowError(f"The snapshot for {branch} build {build_id} holds different DLLs: {target}")
    if target.exists():
        raise WorkflowError(f"Refusing to overwrite an incomplete snapshot: {target}")

    root.mkdir(exist_ok=True)
    temporary = Path(tempfile.mkdtemp(prefix=f".{branch}-{build_id}-", dir=root))
    try:
        destination = temporary / MANAGED
        destination.mkdir(parents=True)
        for entry in assemblies:
            shutil.copy2(managed / entry["name"], destination / entry["name"])
        if _assemblies(destination) != assemblies:
            raise WorkflowError("The game files changed while they were copied; try again when Steam is idle.")
        metadata = {
            "schemaVersion": 1,
            "game": "Probably Stolen",
            "steamAppId": "4348910",
            "steamBranch": branch,
            "steamBuildId": build_id,
            "assemblyCSharpSha256": next(entry["sha256"] for entry in assemblies if entry["name"] == "Assembly-CSharp.dll"),
            "managedDirectory": MANAGED.as_posix(),
            "contentFingerprint": hashlib.sha256(
                json.dumps(assemblies, separators=(",", ":"), sort_keys=True).encode("utf-8")).hexdigest()[:12],
            "assemblies": assemblies,
            "boundary": "Copied managed DLLs for compiling and binding checks only; not packaged, published or committed.",
        }
        (temporary / MANIFEST).write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
        temporary.rename(target)
        return target
    except Exception:
        shutil.rmtree(temporary, ignore_errors=True)
        raise


def snapshots(repository_root: Path) -> list[Path]:
    """Lists complete snapshots, oldest build first within each branch name order."""
    root = repository_root / SNAPSHOT_ROOT
    if not root.is_dir():
        return []
    return sorted(path for path in root.iterdir()
                  if path.is_dir() and not path.name.startswith(".") and (path / MANIFEST).is_file())


def check_bindings(repository_root: Path) -> int:
    """Builds and runs the binding tests against every snapshot; returns a process exit code."""
    found = snapshots(repository_root)
    if not found:
        print(f"No game snapshots in {SNAPSHOT_ROOT}/; run mise run snapshot-game on each branch first.", file=sys.stderr)
        return 2
    env = {key: value for key, value in os.environ.items() if key.casefold() != "artifactspath"}
    failed = []
    for path in found:
        print(f"== {path.name}", flush=True)
        artifacts = repository_root / "artifacts" / "game-bindings" / path.name
        result = subprocess.run(
            ["dotnet", "test", str(repository_root / BINDING_TESTS), "-c", "Debug",
             "-p:RestoreLockedMode=true", f"-p:DigitalStorageGameDirectory={path}", f"-p:ArtifactsPath={artifacts}",
             "--logger", "console;verbosity=normal"],
            cwd=repository_root, env=env, check=False)
        if result.returncode:
            failed.append(path.name)
    for path in found:
        print(f"[{'FAIL' if path.name in failed else 'PASS'}] {path.name}")
    return 1 if failed else 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=("snapshot", "check", "record-package"))
    parser.add_argument("--game-dir", type=Path, help=f"Game installation root; defaults to {GAME_ENV}.")
    parser.add_argument("--package-dir", type=Path, help="record-package: the package directory.")
    parser.add_argument("--output-dir", type=Path, help="record-package: the artifacts directory for the record.")
    args = parser.parse_args(argv)
    repository_root = Path(__file__).resolve().parents[2]
    try:
        if args.action == "check":
            return check_bindings(repository_root)
        game_dir = args.game_dir or (Path(os.environ[GAME_ENV]) if os.environ.get(GAME_ENV) else None)
        if game_dir is None:
            raise WorkflowError(f"Set {GAME_ENV} or pass --game-dir.")
        if args.action == "record-package":
            if args.package_dir is None or args.output_dir is None:
                raise WorkflowError("record-package requires --package-dir and --output-dir.")
            from .package import load_layouts
            branch, build_id = installed_build(game_dir.resolve(strict=True))
            path = record_package_build(game_dir, args.package_dir, args.output_dir, load_layouts()["assemblies"])
            print(f"Package built against {branch} build {build_id}: {path}")
            return 0
        target = snapshot(game_dir, repository_root)
        print(f"Game snapshot: {target}")
        return 0
    except (WorkflowError, OSError, ValueError, KeyError) as error:
        print(f"Game snapshot error: {error}", file=sys.stderr)
        return 1
