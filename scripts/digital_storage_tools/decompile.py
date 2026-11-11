"""Decompile all managed game assemblies into a game-version-scoped local archive."""

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

GAME_ENV = "DIGITAL_STORAGE_GAME_DIR"
ILSPYCMD_ENV = "DIGITAL_STORAGE_ILSPYCMD"
APP_ID = "4348910"


class WorkflowError(RuntimeError):
    pass


def _is_reparse_point(path: Path) -> bool:
    try:
        status = path.lstat()
    except FileNotFoundError:
        return False
    return path.is_symlink() or bool(getattr(status, "st_file_attributes", 0) & 0x400)


def _reject_reparse_components(path: Path, root: Path) -> None:
    try:
        relative = path.absolute().relative_to(root.absolute())
    except ValueError as error:
        raise WorkflowError(f"Path is outside the repository: {path}") from error
    cursor = root
    for part in relative.parts:
        cursor = cursor / part
        if _is_reparse_point(cursor):
            raise WorkflowError(f"Refusing a linked or reparse archive path: {cursor}")


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _build_id(game_dir: Path) -> str:
    manifest = game_dir.parent.parent / "appmanifest_4348910.acf"
    if not manifest.is_file():
        raise WorkflowError(f"Steam appmanifest not found next to the game installation: {manifest}")
    text = manifest.read_text(encoding="utf-8", errors="strict")
    app_id = re.search(r'"appid"\s+"(\d+)"', text)
    build_id = re.search(r'"buildid"\s+"(\d+)"', text)
    install_dir = re.search(r'"installdir"\s+"([^"]+)"', text)
    if not app_id or app_id.group(1) != APP_ID or not build_id or not install_dir:
        raise WorkflowError(f"Steam appmanifest is invalid or does not describe app {APP_ID}: {manifest}")
    if Path(install_dir.group(1)).name.casefold() != game_dir.name.casefold():
        raise WorkflowError(f"Steam appmanifest install directory does not match {game_dir}")
    return build_id.group(1)


def decompile(
    game_dir: Path,
    archive_root: Path,
    ilspycmd: str | None = None,
    repository_root: Path | None = None,
) -> Path:
    game_dir = game_dir.resolve(strict=True)
    repository_root = Path(__file__).resolve().parents[2] if repository_root is None else repository_root.resolve(strict=True)
    managed_dir = game_dir / "Probably Stolen_Data" / "Managed"
    assembly = managed_dir / "Assembly-CSharp.dll"
    if not assembly.is_file() or not managed_dir.is_dir():
        raise WorkflowError(f"Game Managed directory is missing expected assemblies: {managed_dir}")
    assemblies = sorted(p for p in managed_dir.glob("*.dll") if p.is_file())
    if not assemblies:
        raise WorkflowError(f"No managed DLLs found in {managed_dir}")
    build_id = _build_id(game_dir)
    assembly_hash = _sha256(assembly)
    current_assemblies = [{"name": p.name, "sha256": _sha256(p)} for p in assemblies]
    content_fingerprint = hashlib.sha256(
        json.dumps(current_assemblies, separators=(",", ":"), sort_keys=True).encode("utf-8")
    ).hexdigest()[:12]
    supplied_archive_root = archive_root.absolute()
    expected_root = (repository_root / ".reference" / "probably-stolen-src").resolve(strict=False)
    _reject_reparse_components(supplied_archive_root, repository_root)
    archive_root = supplied_archive_root.resolve(strict=False)
    if os.path.normcase(str(archive_root)) != os.path.normcase(str(expected_root)):
        raise WorkflowError(f"Refusing archive path outside the approved source archive: {archive_root}")
    archive_root.mkdir(parents=True, exist_ok=True)
    build_dir = archive_root / f"steam-{build_id}-asm-{assembly_hash[:8]}"
    output_dir = build_dir / f"all-managed-dlls-{content_fingerprint}"
    _reject_reparse_components(build_dir, repository_root)
    _reject_reparse_components(output_dir, repository_root)
    legacy_output_dir = build_dir / "all-managed-dlls"
    manifest_path = output_dir / "decompile-manifest.json"
    legacy_manifest_path = legacy_output_dir / "decompile-manifest.json"
    if manifest_path.is_file():
        previous = json.loads(manifest_path.read_text(encoding="utf-8"))
        if previous.get("assemblies") == current_assemblies and previous.get("steamBuildId") == build_id:
            return output_dir
        raise WorkflowError(f"Existing archive metadata does not match the current build: {output_dir}")

    if output_dir.exists():
        raise WorkflowError(f"Refusing to overwrite incomplete existing output: {output_dir}")
    executable = ilspycmd or os.environ.get(ILSPYCMD_ENV) or shutil.which("ilspycmd")
    if not executable:
        raise WorkflowError(f"ilspycmd not found; install it or set {ILSPYCMD_ENV}")

    build_dir.mkdir(parents=True, exist_ok=True)
    temporary = Path(tempfile.mkdtemp(prefix=".all-managed-dlls-", dir=build_dir))
    try:
        for index, dll in enumerate(assemblies):
            destination = temporary / f"{dll.stem}-{_sha256(dll)[:8]}"
            result = subprocess.run(
                [executable, "--disable-updatecheck", "-p", "--nested-directories", "-o", str(destination), str(dll)],
                check=False,
                capture_output=True,
                text=True,
            )
            if result.returncode != 0:
                raise WorkflowError(f"ILSpy failed for {dll.name}: {result.stderr.strip() or result.stdout.strip()}")
            if not any(destination.rglob("*.cs")):
                raise WorkflowError(f"ILSpy produced no C# files for {dll.name}")
            print(f"[{index + 1}/{len(assemblies)}] Decompiled {dll.name}")
        metadata = {
            "schemaVersion": 1,
            "game": "Probably Stolen",
            "steamAppId": APP_ID,
            "steamBuildId": build_id,
            "assemblyCSharpSha256": assembly_hash,
            "managedDirectory": "Probably Stolen_Data/Managed",
            "contentFingerprint": content_fingerprint,
            "assemblies": current_assemblies,
            "decompiler": executable,
            "boundary": "Static ILSpy decompilation only; no game code or save was executed or accessed.",
        }
        (temporary / "decompile-manifest.json").write_text(json.dumps(metadata, indent=2) + "\n", encoding="utf-8")
        temporary.rename(output_dir)
        return output_dir
    except Exception:
        shutil.rmtree(temporary, ignore_errors=True)
        raise


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game-dir", default=os.environ.get(GAME_ENV))
    parser.add_argument("--archive-root", default=str(Path(__file__).resolve().parents[2] / ".reference" / "probably-stolen-src"))
    parser.add_argument("--ilspycmd")
    args = parser.parse_args(argv)
    if not args.game_dir:
        parser.error(f"Set {GAME_ENV} in mise.local.toml or pass --game-dir")
    try:
        print(f"Source archive: {decompile(Path(args.game_dir), Path(args.archive_root), args.ilspycmd)}")
        return 0
    except (OSError, ValueError, WorkflowError, subprocess.SubprocessError) as error:
        print(f"Decompile error: {error}", file=sys.stderr)
        return 2


