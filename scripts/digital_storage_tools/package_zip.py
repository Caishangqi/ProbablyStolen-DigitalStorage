"""Create and verify the ZIP distributed to mod users."""

from __future__ import annotations

import argparse
import os
import re
import stat
import sys
import tempfile
from pathlib import Path
from typing import Sequence
from zipfile import BadZipFile, ZIP_STORED, ZipFile

from .assets import ContentValidationError, plain_path
from .commands import local_environment
from .decompile import WorkflowError
from .environment import parse_arguments, resolve_environment
from .game_refs import read_package_build
from .package import PackageValidationError, file_inventory_entry, load_layouts, validate_manifest_stream, validate_package_directory


ARCHIVE_ROOT = "DigitalStorage"
_VERSION = re.compile(r"[A-Za-z0-9][A-Za-z0-9_.+-]{0,63}\Z")


_BUILD_SUFFIX = re.compile(r"-(?P<branch>[A-Za-z0-9_.]{1,64})-(?P<build>[0-9]{1,20})\Z")


def _archive_name(version: str, build: tuple[str, str]) -> str:
    """Names the ZIP after the mod version and the Steam branch and build its assemblies were compiled against."""
    if not _VERSION.fullmatch(version) or version.endswith("."):
        raise PackageValidationError(f"ModVersion is not safe for a ZIP filename: {version!r}")
    branch, build_id = build
    if not _BUILD_SUFFIX.fullmatch(f"-{branch}-{build_id}"):
        raise PackageValidationError(f"Steam branch or build is not safe for a ZIP filename: {branch!r} {build_id!r}")
    return f"DigitalStorage-v{version}-{branch}-{build_id}.zip"


def _check_archive_name(name: str, version: str) -> None:
    prefix = f"DigitalStorage-v{version}"
    if not (name.startswith(prefix) and name.endswith(".zip") and _BUILD_SUFFIX.fullmatch(name[len(prefix):-4])):
        raise PackageValidationError(
            f"ZIP filename must match ModVersion and the build: expected '{prefix}-<branch>-<buildid>.zip'.")


def verify_package_zip(path: Path) -> list[dict[str, object]]:
    """Check member paths and payloads directly without extracting the archive."""
    path = plain_path(path)
    if not path.is_file():
        raise PackageValidationError(f"Package ZIP is not a regular file: {path}")
    layout = load_layouts()
    expected = {f"{ARCHIVE_ROOT}/{name}": name for name in layout["current"]}
    try:
        with ZipFile(path) as archive:
            entries = {}
            for member in archive.infolist():
                name = member.orig_filename
                if name in entries:
                    raise PackageValidationError(f"Duplicate ZIP member: {name!r}")
                kind = stat.S_IFMT(member.external_attr >> 16)
                if (name != member.filename or member.is_dir() or kind not in (0, stat.S_IFREG)
                        or member.external_attr & 0x410):
                    raise PackageValidationError(f"ZIP members must be regular files: {name!r}")
                if member.flag_bits & 1 or member.compress_type != ZIP_STORED:
                    raise PackageValidationError(f"ZIP members must be stored without compression or encryption: {name!r}")
                if member.compress_size != member.file_size:
                    raise PackageValidationError(f"Stored ZIP member sizes differ: {name!r}")
                entries[name] = member
            if entries.keys() != expected.keys():
                missing = sorted(expected.keys() - entries.keys())
                unexpected = sorted(entries.keys() - expected.keys())
                raise PackageValidationError(f"ZIP file allowlist mismatch; missing={missing}, unexpected={unexpected}.")

            with archive.open(entries[f"{ARCHIVE_ROOT}/manifest.xml"]) as stream:
                version = validate_manifest_stream(stream, layout["assemblies"])
            _check_archive_name(path.name, version)

            inventory = []
            for member_name, relative in expected.items():
                member = entries[member_name]
                with archive.open(member) as stream:
                    item = file_inventory_entry(relative, stream)
                if item["size"] != member.file_size:
                    raise PackageValidationError(f"ZIP member size mismatch: {member_name!r}")
                inventory.append(item)
            return inventory
    except (BadZipFile, EOFError, NotImplementedError, RuntimeError, UnicodeDecodeError) as error:
        raise PackageValidationError(f"Package ZIP could not be read: {error}") from error


def create_package_zip(package_directory: Path, output_directory: Path, build: tuple[str, str]) -> Path:
    """Archive a validated package and publish the checked ZIP under its version and build filename."""
    package_directory = plain_path(package_directory)
    output_directory = plain_path(output_directory)
    if output_directory.is_relative_to(package_directory):
        raise PackageValidationError("ZIP output must be outside the package directory.")
    source = validate_package_directory(package_directory)
    with (package_directory / "manifest.xml").open("rb") as stream:
        version = validate_manifest_stream(stream, load_layouts()["assemblies"])
    destination = output_directory / _archive_name(version, build)
    output_directory.mkdir(parents=True, exist_ok=True)
    if os.path.lexists(destination):
        raise PackageValidationError(f"ZIP already exists: {destination}")
    with tempfile.TemporaryDirectory(prefix=".package-zip-", dir=output_directory) as temporary_directory:
        temporary = Path(temporary_directory) / destination.name
        with ZipFile(temporary, mode="x", compression=ZIP_STORED) as archive:
            for entry in source:
                name = str(entry["name"])
                archive.write(plain_path(package_directory / name), f"{ARCHIVE_ROOT}/{name}")
        if verify_package_zip(temporary) != source:
            raise PackageValidationError("ZIP payload differs from the validated source package.")
        try:
            if os.name == "nt":
                os.rename(temporary, destination)
            else:
                os.link(temporary, destination)
        except FileExistsError as error:
            raise PackageValidationError(f"ZIP already exists: {destination}") from error
    return destination


def create_main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Create a versioned ZIP from a validated package directory.")
    parser.add_argument("--package-dir", help="Override the package directory from the project environment.")
    args = parser.parse_args(argv)
    try:
        environment_args = ["--package-dir", args.package_dir] if args.package_dir else []
        environment = resolve_environment(parse_arguments(environment_args), environ=local_environment(),
                                          for_release_archive=True)
        build = read_package_build(environment.output_directory, environment.package_directory,
                                   load_layouts()["assemblies"])
        output = create_package_zip(environment.package_directory, environment.output_directory / "release", build)
    except (ValueError, OSError, WorkflowError) as error:
        print(f"Package ZIP error: {error}", file=sys.stderr)
        return 2
    print(f"[PASS] Package ZIP created and verified: {output}")
    return 0


def verify_main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Verify the ZIP against the current package declaration.")
    parser.add_argument("--archive", type=Path, required=True)
    args = parser.parse_args(argv)
    try:
        inventory = verify_package_zip(args.archive)
    except (ContentValidationError, PackageValidationError, OSError) as error:
        print(f"Package ZIP error: {error}", file=sys.stderr)
        return 2
    print(f"[PASS] Package ZIP validated ({len(inventory)} files): {args.archive}")
    return 0
