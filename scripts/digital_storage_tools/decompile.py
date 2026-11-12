"""Decompile managed game assemblies, or a third-party mod, into version-scoped local research archives.

Game mode archives every managed game DLL under ``.reference/probably-stolen-src``. Mod mode (``--mod-dir``)
archives only the assemblies listed in the mod's ``manifest.xml`` under ``.reference/probably-stolen-mod-<id>-src``;
``--with-game`` additionally refreshes the game archive. Neither mode executes game or mod code or touches saves.
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
import xml.etree.ElementTree as ElementTree
from pathlib import Path

GAME_ENV = "DIGITAL_STORAGE_GAME_DIR"
ILSPYCMD_ENV = "DIGITAL_STORAGE_ILSPYCMD"
APP_ID = "4348910"
MOD_ARCHIVE_NAME = re.compile(r"^probably-stolen-mod-[a-z0-9][a-z0-9._-]*-src$")
MOD_ID = re.compile(r"^[a-z0-9][a-z0-9._-]{0,63}$")
MOD_TEXT_SUFFIXES = frozenset({".xml", ".md", ".txt", ".csv", ".json"})
MOD_TEXT_LIMIT = 1024 * 1024


class WorkflowError(RuntimeError):
    pass


def _is_reparse_point(path: Path) -> bool:
    try:
        status = path.lstat()
    except FileNotFoundError:
        return False
    return path.is_symlink() or bool(getattr(status, "st_file_attributes", 0) & 0x400)


def _reject_reparse_components(path: Path, root: Path) -> None:
    """Rejects links below ``root`` (already resolved) without resolving ``path`` itself.

    ``path`` may spell the repository differently, e.g. through an 8.3 short TEMP name, so the walk starts at the
    first ancestor that resolves to ``root`` and checks each component below it as supplied.
    """
    path = path.absolute()
    for cursor in reversed(path.parents):
        if os.path.normcase(str(cursor.resolve(strict=False))) == os.path.normcase(str(root)):
            break
    else:
        raise WorkflowError(f"Path is outside the repository: {path}")
    for part in path.relative_to(cursor).parts:
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


def _ilspy_executable(ilspycmd: str | None) -> str:
    executable = ilspycmd or os.environ.get(ILSPYCMD_ENV) or shutil.which("ilspycmd")
    if not executable:
        raise WorkflowError(f"ilspycmd not found; install it or set {ILSPYCMD_ENV}")
    return executable


def _run_ilspy(executable: str, dll: Path, destination: Path, references: tuple[Path, ...]) -> None:
    command = [executable, "--disable-updatecheck", "-p", "--nested-directories", "-o", str(destination)]
    for reference in references:
        command += ["-r", str(reference)]
    result = subprocess.run([*command, str(dll)], check=False, capture_output=True, text=True)
    if result.returncode != 0:
        raise WorkflowError(f"ILSpy failed for {dll.name}: {result.stderr.strip() or result.stdout.strip()}")
    if not any(destination.rglob("*.cs")):
        raise WorkflowError(f"ILSpy produced no C# files for {dll.name}")


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
    executable = _ilspy_executable(ilspycmd)

    build_dir.mkdir(parents=True, exist_ok=True)
    temporary = Path(tempfile.mkdtemp(prefix=".all-managed-dlls-", dir=build_dir))
    try:
        for index, dll in enumerate(assemblies):
            _run_ilspy(executable, dll, temporary / f"{dll.stem}-{_sha256(dll)[:8]}", ())
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


def read_mod_manifest(mod_dir: Path) -> dict:
    """Reads the identity fields and assembly list of a Probably Stolen mod ``manifest.xml``."""
    manifest = mod_dir / "manifest.xml"
    if not manifest.is_file():
        raise WorkflowError(f"Mod manifest.xml not found: {manifest}")
    try:
        root = ElementTree.parse(manifest).getroot()
    except ElementTree.ParseError as error:
        raise WorkflowError(f"Mod manifest.xml is not valid XML: {error}") from error
    mod_id = (root.findtext("ID") or "").strip().casefold()
    if not MOD_ID.fullmatch(mod_id):
        raise WorkflowError(f"Mod ID is missing or unsafe for an archive name: {mod_id!r}")
    assemblies = [(node.text or "").strip() for node in root.findall("Assemblies/Assembly")]
    if not assemblies:
        raise WorkflowError("Mod manifest.xml lists no assemblies; nothing to decompile")
    for name in assemblies:
        if not name or Path(name).name != name or not name.casefold().endswith(".dll"):
            raise WorkflowError(f"Mod assembly entry must be a plain DLL file name: {name!r}")
        if not (mod_dir / name).is_file():
            raise WorkflowError(f"Mod assembly listed in manifest.xml is missing: {name}")
    return {
        "id": mod_id,
        "name": (root.findtext("Name") or "").strip(),
        "author": (root.findtext("Author") or "").strip(),
        "version": (root.findtext("ModVersion") or "").strip(),
        "gameVersions": [(node.text or "").strip() for node in root.findall("GameVersions/Version")],
        "assemblies": assemblies,
    }


def _workshop_id(mod_dir: Path) -> str | None:
    # Steam keeps subscribed items at steamapps/workshop/content/<app id>/<published file id>.
    return mod_dir.name if mod_dir.parent.name == APP_ID and mod_dir.name.isdigit() else None


def _mod_archive_root(repository_root: Path, mod_id: str, output: Path | None) -> Path:
    reference_root = (repository_root / ".reference").resolve(strict=False)
    supplied = (output or reference_root / f"probably-stolen-mod-{mod_id}-src").absolute()
    _reject_reparse_components(supplied, repository_root)
    resolved = supplied.resolve(strict=False)
    if os.path.normcase(str(resolved.parent)) != os.path.normcase(str(reference_root)) \
            or not MOD_ARCHIVE_NAME.fullmatch(resolved.name):
        raise WorkflowError(f"Refusing mod archive path outside .reference/probably-stolen-mod-<id>-src: {resolved}")
    return resolved


def _copy_mod_text_files(mod_dir: Path, destination: Path) -> list[str]:
    """Copies small descriptive files (manifest, README, localization) next to the decompiled source."""
    copied = []
    for source in sorted(mod_dir.rglob("*")):
        relative = source.relative_to(mod_dir)
        if any(_is_reparse_point(mod_dir.joinpath(*relative.parts[:i + 1])) for i in range(len(relative.parts))):
            continue
        if not source.is_file() or source.suffix.casefold() not in MOD_TEXT_SUFFIXES \
                or source.stat().st_size > MOD_TEXT_LIMIT:
            continue
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, target)
        copied.append(relative.as_posix())
    return copied


def decompile_mod(
    mod_dir: Path,
    output: Path | None = None,
    game_dir: Path | None = None,
    include_game: bool = False,
    ilspycmd: str | None = None,
    repository_root: Path | None = None,
) -> Path:
    """Decompiles the assemblies a third-party mod declares into a mod-version-scoped archive.

    Game DLLs are passed to ILSpy only as references so mod code resolves game types; ``include_game`` also runs
    the game-mode archive. The archive is research material: third-party code is not copied into the product.
    """
    mod_dir = mod_dir.resolve(strict=True)
    repository_root = Path(__file__).resolve().parents[2] if repository_root is None else repository_root.resolve(strict=True)
    if include_game and game_dir is None:
        raise WorkflowError(f"--with-game requires the game directory ({GAME_ENV} or --game-dir)")
    mod = read_mod_manifest(mod_dir)
    archive_root = _mod_archive_root(repository_root, mod["id"], output)

    references: tuple[Path, ...] = (mod_dir,)
    game_archive = None
    if game_dir is not None:
        game_dir = game_dir.resolve(strict=True)
        managed_dir = game_dir / "Probably Stolen_Data" / "Managed"
        if not managed_dir.is_dir():
            raise WorkflowError(f"Game Managed directory not found: {managed_dir}")
        references += (managed_dir,)
        if include_game:
            game_archive = decompile(game_dir, repository_root / ".reference" / "probably-stolen-src", ilspycmd,
                                     repository_root)

    assemblies = [{"name": name, "sha256": _sha256(mod_dir / name)} for name in mod["assemblies"]]
    fingerprint = hashlib.sha256(
        json.dumps(assemblies, separators=(",", ":"), sort_keys=True).encode("utf-8")).hexdigest()[:12]
    version = re.sub(r"[^0-9A-Za-z._-]", "_", mod["version"]) or "unversioned"
    output_dir = archive_root / f"v{version}-{fingerprint}"
    _reject_reparse_components(output_dir, repository_root)
    manifest_path = output_dir / "decompile-manifest.json"
    if manifest_path.is_file():
        previous = json.loads(manifest_path.read_text(encoding="utf-8"))
        if previous.get("assemblies") == assemblies and previous.get("mod", {}).get("id") == mod["id"]:
            return output_dir
        raise WorkflowError(f"Existing archive metadata does not match the current mod: {output_dir}")
    if output_dir.exists():
        raise WorkflowError(f"Refusing to overwrite incomplete existing output: {output_dir}")
    executable = _ilspy_executable(ilspycmd)

    archive_root.mkdir(parents=True, exist_ok=True)
    temporary = Path(tempfile.mkdtemp(prefix=".mod-", dir=archive_root))
    try:
        for index, item in enumerate(assemblies):
            dll = mod_dir / item["name"]
            _run_ilspy(executable, dll, temporary / f"{dll.stem}-{item['sha256'][:8]}", references)
            print(f"[{index + 1}/{len(assemblies)}] Decompiled {dll.name}")
        metadata = {
            "schemaVersion": 1,
            "kind": "probably-stolen-mod",
            "mod": {key: mod[key] for key in ("id", "name", "author", "version", "gameVersions")},
            "steamAppId": APP_ID,
            "workshopId": _workshop_id(mod_dir),
            "contentFingerprint": fingerprint,
            "assemblies": assemblies,
            "copiedFiles": _copy_mod_text_files(mod_dir, temporary / "mod-files"),
            "gameReferenceDirectory": str(references[-1]) if game_dir is not None else None,
            "gameArchive": str(game_archive) if game_archive is not None else None,
            "decompiler": executable,
            "boundary": "Static ILSpy decompilation only; no game or mod code or save was executed or accessed.",
            "license": "Third-party mod; local research only. Do not copy its code or assets into this project.",
        }
        (temporary / "decompile-manifest.json").write_text(
            json.dumps(metadata, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
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
    parser.add_argument("--mod-dir", help="Mod folder containing manifest.xml; switches to mod mode.")
    parser.add_argument("--with-game", action="store_true", help="Mod mode: also archive the game's managed DLLs.")
    parser.add_argument("--output", help="Mod mode: archive root .reference/probably-stolen-mod-<id>-src.")
    args = parser.parse_args(argv)
    if args.mod_dir is None and (args.with_game or args.output):
        parser.error("--with-game and --output require --mod-dir")
    if args.mod_dir is None and not args.game_dir:
        parser.error(f"Set {GAME_ENV} in mise.local.toml or pass --game-dir")
    try:
        if args.mod_dir is not None:
            archive = decompile_mod(Path(args.mod_dir), Path(args.output) if args.output else None,
                                    Path(args.game_dir) if args.game_dir else None, args.with_game, args.ilspycmd)
            print(f"Mod source archive: {archive}")
        else:
            print(f"Source archive: {decompile(Path(args.game_dir), Path(args.archive_root), args.ilspycmd)}")
        return 0
    except (OSError, ValueError, WorkflowError, subprocess.SubprocessError) as error:
        print(f"Decompile error: {error}", file=sys.stderr)
        return 2


