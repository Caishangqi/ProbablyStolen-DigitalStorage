"""Safely deploy or remove only the owned Digital Storage mod directory."""

from __future__ import annotations

import argparse
import os
import json
import subprocess
import shutil
import stat
import sys
import uuid
from pathlib import Path
from typing import Sequence

from .package import (
    CURRENT_FILES,
    CURRENT_PLATFORM_ASSEMBLY,
    KNOWN_LAYOUTS,
    LEGACY_FILES,
    LEGACY_PLATFORM_ASSEMBLY,
    PREVIOUS_FILES,
    RENAMED_PLATFORM_FILES,
    RESOURCE_FILES,
    EXPECTED_ASSEMBLIES,
    EXPECTED_FILES,
    manifest_text,
    package_directories,
    PackageValidationError,
    validate_package_directory,
)


GAME_FILES = (
    Path("Probably Stolen.exe"),
    Path("Probably Stolen_Data/Managed/Assembly-CSharp.dll"),
    Path("Probably Stolen_Data/Managed/UnityEngine.dll"),
    Path("Probably Stolen_Data/Managed/UnityEngine.CoreModule.dll"),
)
_REPARSE_POINT = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)


class DeploymentError(ValueError):
    """Raised when a deployment target or existing package is not owned."""


def _running_game_paths() -> list[str | None]:
    command = "$p = @(Get-CimInstance -ClassName Win32_Process -Filter \"Name = 'Probably Stolen.exe'\" -ErrorAction Stop | ForEach-Object { $_.ExecutablePath }); ConvertTo-Json -InputObject $p -Compress"
    try:
        result = subprocess.run(["powershell.exe", "-NoProfile", "-NonInteractive", "-Command", command],
                                capture_output=True, text=True, check=True)
        paths = json.loads(result.stdout)
        if not isinstance(paths, list) or any(value is not None and not isinstance(value, str) for value in paths):
            raise ValueError("invalid process list")
        return paths
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        raise DeploymentError(f"Could not verify game processes: {error}") from error


def _assert_game_stopped(game_directory: Path) -> None:
    try:
        expected = os.path.normcase(str((Path(game_directory).resolve(strict=True) / "Probably Stolen.exe").resolve(strict=True)))
    except (OSError, RuntimeError) as error:
        raise DeploymentError(f"Could not resolve the selected game executable: {error}") from error
    for path in _running_game_paths():
        if not path or not path.strip():
            raise DeploymentError("A game process has an unverifiable executable path; close it before continuing.")
        try:
            actual = os.path.normcase(str(Path(path).resolve(strict=True)))
        except (OSError, RuntimeError) as error:
            raise DeploymentError("A game process has an unverifiable executable path; close it before continuing.") from error
        if actual == expected:
            raise DeploymentError("The selected game is running; close it before deployment or removal.")


def _is_reparse_point(status: object) -> bool:
    return bool(getattr(status, "st_file_attributes", 0) & _REPARSE_POINT)


def _existing_plain_directory(path: Path, label: str) -> bool:
    try:
        status = path.lstat()
    except FileNotFoundError:
        return False
    except OSError as error:
        raise DeploymentError(f"Could not inspect {label} {path}: {error}") from error

    if stat.S_ISLNK(status.st_mode) or _is_reparse_point(status):
        raise DeploymentError(f"{label} must not be a symbolic link or reparse point: {path}")
    if not stat.S_ISDIR(status.st_mode):
        raise DeploymentError(f"{label} is not a directory: {path}")
    return True


def _validate_game(game_directory: Path) -> Path:
    try:
        game = Path(game_directory).resolve(strict=True)
    except OSError as error:
        raise DeploymentError(f"Probably Stolen installation does not exist: {game_directory}") from error
    if not game.is_dir():
        raise DeploymentError(f"Probably Stolen installation is not a directory: {game}")

    missing = [str(game / relative) for relative in GAME_FILES if not (game / relative).is_file()]
    if missing:
        raise DeploymentError("Required game files are missing: " + ", ".join(missing))
    return game


def _validate_target(game_directory: Path, deploy_directory: Path) -> tuple[Path, Path]:
    game = _validate_game(game_directory)
    mods = game / "Mods"
    target = mods / "DigitalStorage"
    _existing_plain_directory(mods, "Mods directory")
    _existing_plain_directory(target, "Digital Storage directory")

    supplied = Path(deploy_directory).resolve(strict=False)
    expected = target.resolve(strict=False)
    normalized_supplied = os.path.normcase(os.path.abspath(os.path.normpath(str(supplied))))
    normalized_expected = os.path.normcase(os.path.abspath(os.path.normpath(str(expected))))
    if normalized_supplied != normalized_expected:
        raise DeploymentError(
            f"Deployment target must be exactly the game Mods/DigitalStorage directory: {expected}"
        )
    return target, mods


def _validate_package_source(package_directory: Path, game_directory: Path) -> tuple[Path, list[dict[str, object]]]:
    try:
        validate_package_directory(Path(package_directory), require_current_metadata=True)
        package = Path(package_directory).resolve(strict=True)
        game = Path(game_directory).resolve(strict=True)
    except (OSError, PackageValidationError) as error:
        raise DeploymentError(f"The package is not valid for deployment: {error}") from error
    if package == game or package.is_relative_to(game):
        raise DeploymentError("The package source must be outside the game installation.")
    try:
        inventory = validate_package_directory(package, require_current_metadata=True)
    except PackageValidationError as error:
        raise DeploymentError(f"The package is not valid for deployment: {error}") from error
    return package, inventory


def _inventory_by_name(inventory: list[dict[str, object]]) -> dict[str, dict[str, object]]:
    return {str(item["name"]): item for item in inventory}


def plan_deployment(
    package_directory: Path, game_directory: Path, deploy_directory: Path
) -> list[dict[str, str]]:
    """Validate ownership and report the file changes without writing anything."""
    target, _ = _validate_target(game_directory, deploy_directory)
    package, source_inventory = _validate_package_source(package_directory, game_directory)
    existing_inventory: dict[str, dict[str, object]] = {}
    if target.exists():
        try:
            existing_inventory = _inventory_by_name(
                validate_package_directory(target, require_current_metadata=False)
            )
        except PackageValidationError as error:
            raise DeploymentError(f"Existing Digital Storage directory is not safely owned: {error}") from error

    source_by_name = _inventory_by_name(source_inventory)
    changes: list[dict[str, str]] = []
    for name in source_by_name:
        source_hash = str(source_by_name[name]["sha256"])
        existing = existing_inventory.get(name)
        if existing is None:
            action = "Add"
        elif str(existing["sha256"]) == source_hash:
            action = "Unchanged"
        else:
            action = "Update"
        changes.append({"action": action, "name": name, "sha256": source_hash})
    for name, existing in existing_inventory.items():
        if name not in source_by_name:
            changes.append({"action": "Remove", "name": name, "sha256": str(existing["sha256"])})
    return changes


def _ensure_resource_directories(root: Path, names: Sequence[str], created: list[Path] | None = None) -> list[Path]:
    created = [] if created is None else created
    for name in sorted(package_directories(names), key=lambda value: (value.count("/"), value)):
        path = root / name
        if not _existing_plain_directory(path, "Resource directory"):
            path.mkdir()
            created.append(path)
    return created


def _clean_attempt_tree(directory: Path, names: Sequence[str]) -> None:
    """Clean only a self-created staging/backup tree, allowing known partial files."""
    if not _existing_plain_directory(directory, "Attempt directory"):
        return
    allowed_dirs = package_directories(names)
    files: list[Path] = []
    directories: list[Path] = []
    def inspect(parent: Path) -> None:
        for child in parent.iterdir():
            relative = child.relative_to(directory).as_posix()
            status = child.lstat()
            if stat.S_ISLNK(status.st_mode) or _is_reparse_point(status):
                raise DeploymentError(f"Refusing to follow an attempt-tree link: {child}")
            if stat.S_ISDIR(status.st_mode) and relative in allowed_dirs:
                directories.append(child)
                inspect(child)
            elif stat.S_ISREG(status.st_mode) and relative in names:
                files.append(child)
            else:
                raise DeploymentError(f"Unknown attempt-tree entry retained: {child}")
    inspect(directory)
    for path in files:
        path.unlink()
    for path in sorted(directories, key=lambda value: len(value.parts), reverse=True):
        path.rmdir()
    directory.rmdir()


def deploy_package(
    package_directory: Path, game_directory: Path, deploy_directory: Path
) -> list[dict[str, str]]:
    """Install a validated tree with checked backup/rollback; not crash-atomic."""
    _assert_game_stopped(game_directory)
    changes = plan_deployment(package_directory, game_directory, deploy_directory)
    if all(change["action"] == "Unchanged" for change in changes):
        return changes
    target, mods = _validate_target(game_directory, deploy_directory)
    package, source_inventory = _validate_package_source(package_directory, game_directory)
    source_names = [str(item["name"]) for item in source_inventory]
    target_existed = target.exists()
    old_inventory = validate_package_directory(target, False) if target_existed else []
    old_names = [str(item["name"]) for item in old_inventory]
    mods_existed = _existing_plain_directory(mods, "Mods directory")
    if not mods_existed:
        mods.mkdir()
    staging = mods / (".DigitalStorage.staging." + uuid.uuid4().hex)
    backup = mods / (".DigitalStorage.backup." + uuid.uuid4().hex)
    created_directories: list[Path] = []
    target_created = False
    attempted: list[str] = []
    backup_ready = False
    try:
        staging.mkdir()
        _ensure_resource_directories(staging, source_names)
        for name in source_names:
            shutil.copyfile(package / name, staging / name)
        if validate_package_directory(staging) != source_inventory:
            raise DeploymentError("Staged content changed from the validated source inventory.")
        if target_existed:
            if validate_package_directory(target, False) != old_inventory:
                raise DeploymentError("Installed files changed while staging; no replacement performed.")
            backup.mkdir()
            _ensure_resource_directories(backup, old_names)
            for name in old_names:
                shutil.copyfile(target / name, backup / name)
            if validate_package_directory(backup, False) != old_inventory:
                raise DeploymentError("Backup does not match the original installation.")
            backup_ready = True
        else:
            target.mkdir()
            target_created = True
        _ensure_resource_directories(target, source_names, created_directories)
        for name in [value for value in source_names if value != "manifest.xml"]:
            attempted.append(name)
            os.replace(staging / name, target / name)
        for name in old_names:
            if name not in source_names:
                attempted.append(name)
                (target / name).unlink()
        attempted.append("manifest.xml")
        os.replace(staging / "manifest.xml", target / "manifest.xml")
        if validate_package_directory(target) != source_inventory:
            raise DeploymentError("Installed files do not match the staged package.")
    except Exception as error:
        try:
            if attempted:
                if target_existed and not backup_ready:
                    raise DeploymentError("A complete backup is unavailable.")
                for name in old_names:
                    if name in attempted:
                        shutil.copyfile(backup / name, staging / name)
                        os.replace(staging / name, target / name)
                for name in reversed(attempted):
                    if name not in old_names:
                        path = target / name
                        if path.exists():
                            status = path.lstat()
                            if not stat.S_ISREG(status.st_mode) or _is_reparse_point(status):
                                raise DeploymentError(f"Unexpected new file retained during rollback: {path}")
                            path.unlink()
            for directory in reversed(created_directories):
                directory.rmdir()
            if target_created:
                target.rmdir()
            if target_existed and validate_package_directory(target, False) != old_inventory:
                raise DeploymentError("Rollback did not restore the old inventory.")
        except Exception as rollback_error:
            raise DeploymentError(
                f"Deployment failed ({error}); rollback needs attention ({rollback_error}); "
                f"backup retained at {backup}; staging retained at {staging}. Do not launch."
            ) from rollback_error
        try:
            _clean_attempt_tree(staging, source_names)
            _clean_attempt_tree(backup, old_names)
            if not mods_existed and mods.exists() and not any(mods.iterdir()):
                mods.rmdir()
        except Exception as cleanup_error:
            raise DeploymentError(f"Deployment failed ({error}); cleanup needs attention ({cleanup_error}); backup path: {backup}.") from cleanup_error
        if isinstance(error, PermissionError):
            raise DeploymentError(f"Could not replace installed files; close Probably Stolen and verify write permissions before retrying: {error}") from error
        raise DeploymentError(f"Could not deploy the package: {error}") from error
    try:
        _clean_attempt_tree(staging, source_names)
        _clean_attempt_tree(backup, old_names)
    except Exception as error:
        raise DeploymentError(f"Installed package validated, but cleanup failed ({error}); backup path: {backup}. Do not launch.") from error
    return changes


def plan_removal(game_directory: Path, deploy_directory: Path) -> list[str]:
    """Validate the target and return the exact files an uninstall would remove."""
    target, _ = _validate_target(game_directory, deploy_directory)
    if not target.exists():
        return []
    try:
        inventory = validate_package_directory(target, require_current_metadata=False)
    except PackageValidationError as error:
        raise DeploymentError(f"Refusing to remove an unowned or incomplete package: {error}") from error
    return [str(item["name"]) for item in inventory]


def remove_deployment(game_directory: Path, deploy_directory: Path) -> list[str]:
    """Remove the identified complete layout and its known empty directories."""
    _assert_game_stopped(game_directory)
    files = plan_removal(game_directory, deploy_directory)
    if not files:
        return []
    target, _ = _validate_target(game_directory, deploy_directory)
    for name in files:
        (target / name).unlink()
    for name in sorted(package_directories(files), key=lambda value: value.count("/"), reverse=True):
        (target / name).rmdir()
    target.rmdir()
    return files


def _arguments(argv: Sequence[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--action", choices=("deploy", "remove"), required=True)
    parser.add_argument("--game-dir", required=True)
    parser.add_argument("--deploy-dir", required=True)
    parser.add_argument("--package-dir")
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    arguments = _arguments(argv)

    try:
        if arguments.action == "deploy":
            if not arguments.package_dir:
                raise DeploymentError("Deploy requires --package-dir.")
            changes = plan_deployment(
                Path(arguments.package_dir), Path(arguments.game_dir), Path(arguments.deploy_dir)
            )
            print(f"[TARGET] {Path(arguments.deploy_dir).resolve(strict=False)}")
            for change in changes:
                print(f"[PLAN] {change['action']} {change['name']} sha256={change['sha256']}")
            deploy_package(Path(arguments.package_dir), Path(arguments.game_dir), Path(arguments.deploy_dir))
            print("[PASS] Digital Storage package deployed.")
        else:
            files = plan_removal(Path(arguments.game_dir), Path(arguments.deploy_dir))
            print(f"[TARGET] {Path(arguments.deploy_dir).resolve(strict=False)}")
            if not files:
                print("[PASS] Digital Storage is not installed; no files changed.")
                return 0
            for name in files:
                print(f"[PLAN] Remove {name}")
            remove_deployment(Path(arguments.game_dir), Path(arguments.deploy_dir))
            print("[PASS] Digital Storage package removed.")
    except (DeploymentError, OSError) as error:
        print(f"Deployment error: {error}", file=sys.stderr)
        return 2
    return 0


