"""Validate the installable Digital Storage package contents and manifest."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import stat
import sys
import xml.etree.ElementTree as ET
from pathlib import Path, PurePosixPath
from typing import Sequence


EXPECTED_ID = "digital_storage"
EXPECTED_NAME = "Digital Storage"
EXPECTED_AUTHOR = "Caizii"
# The mods menu compares only the leading digits and dots of Application.version (for example "0.46D"),
# so "0.46" matches every 0.46 letter release.
EXPECTED_GAME_VERSION = "0.46"
CURRENT_PLATFORM_ASSEMBLY = "ProbablyStolen.DigitalStorage.Platform.dll"
EXPECTED_ASSEMBLIES = (
    "ProbablyStolen.DigitalStorage.Common.dll",
    "ProbablyStolen.DigitalStorage.Client.dll",
    CURRENT_PLATFORM_ASSEMBLY,
    "ProbablyStolen.DigitalStorage.Mod.dll",
)
# Known 5-file and 11-file layouts were installed before the Platform assembly was renamed, so they keep
# the old on-disk DLL name for safe replacement and removal. The current layout never lists both names.
LEGACY_PLATFORM_ASSEMBLY = "ProbablyStolen.DigitalStorage.Platform.ProbablyStolen.dll"
LEGACY_FILES = (
    "manifest.xml",
    "ProbablyStolen.DigitalStorage.Common.dll",
    "ProbablyStolen.DigitalStorage.Client.dll",
    LEGACY_PLATFORM_ASSEMBLY,
    "ProbablyStolen.DigitalStorage.Mod.dll",
)
PREVIOUS_FILES = (*LEGACY_FILES,
    "Textures/Items/storage_terminal.png", "Textures/Items/storage_drive.png",
    "Textures/Items/storage_cell.png", "Textures/Items/storage_network_controller.png",
    "Localization/en.csv", "Localization/zh.csv")
LEGACY_RESOURCE_FILES = (
    "Textures/Items/terminal.png", "Textures/Items/drive.png", "Textures/Items/controller.png",
    "Textures/Items/item_storage_cell_1k.png", "Textures/Items/item_cell_housing.png",
    "Textures/Items/cell_component_1k.png", "Localization/en.csv", "Localization/zh.csv",
)
RESOURCE_FILES = (*LEGACY_RESOURCE_FILES,
    *(f"Textures/Items/{prefix}_{tier}.png" for tier in ("4k", "16k", "64k", "256k")
      for prefix in ("item_storage_cell", "cell_component")),
    "Search/pinyin-dictionary.txt",
    "Licenses/PininSharp-LICENSE.txt",
    "Licenses/pinyin-data-LICENSE.txt",
    "Licenses/Rime-Terra-Pinyin-LICENSE.txt",
)
CURRENT_FILES = ("manifest.xml", *EXPECTED_ASSEMBLIES, *RESOURCE_FILES)
# An installation made before the Platform assembly rename has the current 13-file layout with the old
# Platform DLL name. Deployment must still recognize it so upgrade and removal stay safe.
RENAMED_PLATFORM_ASSEMBLIES = tuple(
    LEGACY_PLATFORM_ASSEMBLY if name == CURRENT_PLATFORM_ASSEMBLY else name
    for name in EXPECTED_ASSEMBLIES
)
RENAMED_PLATFORM_FILES = ("manifest.xml", *RENAMED_PLATFORM_ASSEMBLIES, *LEGACY_RESOURCE_FILES)
PREVIOUS_CURRENT_FILES = ("manifest.xml", *EXPECTED_ASSEMBLIES, *LEGACY_RESOURCE_FILES)
TIER_RESOURCE_FILES = tuple(
    f"Textures/Items/{prefix}_{tier}.png"
    for tier in ("4k", "16k", "64k", "256k")
    for prefix in ("item_storage_cell", "cell_component")
)
PREVIOUS_TIER_FILES = ("manifest.xml", *EXPECTED_ASSEMBLIES, *LEGACY_RESOURCE_FILES, *TIER_RESOURCE_FILES)
RENAMED_PLATFORM_TIER_FILES = ("manifest.xml", *RENAMED_PLATFORM_ASSEMBLIES, *LEGACY_RESOURCE_FILES, *TIER_RESOURCE_FILES)
# Each known on-disk layout pins the single Platform DLL name that its manifest must declare.
KNOWN_LAYOUTS = (
    ("current21", CURRENT_FILES, CURRENT_PLATFORM_ASSEMBLY),
    ("previous21", PREVIOUS_TIER_FILES, CURRENT_PLATFORM_ASSEMBLY),
    ("previous21-renamed", RENAMED_PLATFORM_TIER_FILES, LEGACY_PLATFORM_ASSEMBLY),
    ("current13", PREVIOUS_CURRENT_FILES, CURRENT_PLATFORM_ASSEMBLY),
    ("old13", RENAMED_PLATFORM_FILES, LEGACY_PLATFORM_ASSEMBLY),
    ("previous11", PREVIOUS_FILES, LEGACY_PLATFORM_ASSEMBLY),
    ("legacy5", LEGACY_FILES, LEGACY_PLATFORM_ASSEMBLY),
)
EXPECTED_FILES = set(CURRENT_FILES)
SOURCE_MANIFEST_PATH = Path(__file__).resolve().parents[2] / "packaging/manifest.xml"
LAYOUT_PATH = Path(__file__).resolve().parents[2] / "packaging/package-files.json"
EXPECTED_ELEMENTS = (
    "ID",
    "Name",
    "Author",
    "ModVersion",
    "Description",
    "GameVersions",
    "Prerequisites",
    "Assemblies",
    "Capabilities",
)
_ID_PATTERN = re.compile(r"[A-Za-z0-9_-][A-Za-z0-9_.-]{0,63}\Z")
_REPARSE_POINT = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)


def _is_reparse_point(status: object) -> bool:
    return bool(getattr(status, "st_file_attributes", 0) & _REPARSE_POINT)


class PackageValidationError(ValueError):
    """Raised when package contents do not match the release allowlist."""


def manifest_text(platform_assembly: str) -> str:
    """Return the approved manifest declaring the given on-disk Platform assembly name."""
    return SOURCE_MANIFEST_PATH.read_text(encoding="utf-8").replace(
        f"<Assembly>{CURRENT_PLATFORM_ASSEMBLY}</Assembly>",
        f"<Assembly>{platform_assembly}</Assembly>",
    )


def _element_text(element: ET.Element, label: str) -> str:
    if element.attrib or list(element):
        raise PackageValidationError(f"{label} must contain text only.")
    value = (element.text or "").strip()
    if not value:
        raise PackageValidationError(f"{label} must not be empty.")
    return value


def _layout_assemblies(names: Sequence[str]) -> tuple[str, ...]:
    """Return the assembly tuple a manifest must declare for the selected file layout."""
    return tuple(name for name in names if name.endswith(".dll"))


def _validate_manifest(
    path: Path, expected_assemblies: Sequence[str], require_current_metadata: bool
) -> None:
    try:
        if path.stat().st_size > 1_048_576:
            raise PackageValidationError("manifest.xml exceeds the 1 MiB validation limit.")
        document = ET.parse(path)
    except (OSError, ET.ParseError) as error:
        raise PackageValidationError(f"manifest.xml could not be parsed: {error}") from error

    root = document.getroot()
    if root.tag != "Manifest" or root.attrib:
        raise PackageValidationError("manifest.xml must have a plain Manifest root element.")

    children = list(root)
    names = tuple(child.tag for child in children)
    if names != EXPECTED_ELEMENTS:
        raise PackageValidationError(
            "Manifest fields must appear once in the approved order: " + ", ".join(EXPECTED_ELEMENTS)
        )
    fields = dict(zip(names, children, strict=True))

    identity = _element_text(fields["ID"], "ID")
    if not _ID_PATTERN.fullmatch(identity):
        raise PackageValidationError("ID contains characters or a prefix rejected by the game loader.")
    if identity != EXPECTED_ID:
        raise PackageValidationError(f"Expected package ID {EXPECTED_ID!r}, got {identity!r}.")

    _element_text(fields["ModVersion"], "ModVersion")
    _element_text(fields["Description"], "Description")
    expected_values = {
        "Name": EXPECTED_NAME,
        "Author": EXPECTED_AUTHOR,
    }
    for name, expected in expected_values.items():
        value = _element_text(fields[name], name)
        if require_current_metadata and value != expected:
            raise PackageValidationError(f"Expected {name} {expected!r}, got {value!r}.")
        if not require_current_metadata and name == "Name" and value != EXPECTED_NAME:
            raise PackageValidationError(f"Expected package name {EXPECTED_NAME!r}, got {value!r}.")

    game_versions = fields["GameVersions"]
    if game_versions.attrib or len(game_versions) == 0:
        raise PackageValidationError("GameVersions must contain at least one Version element.")
    versions = list(game_versions)
    if any(version.tag != "Version" for version in versions):
        raise PackageValidationError("GameVersions may contain only Version elements.")
    version_values = [_element_text(version, "GameVersions/Version") for version in versions]
    if require_current_metadata and version_values != [EXPECTED_GAME_VERSION]:
        raise PackageValidationError(
            f"Expected GameVersions [{EXPECTED_GAME_VERSION!r}], got {version_values!r}."
        )

    for container_name in ("Prerequisites", "Capabilities"):
        container = fields[container_name]
        if container.attrib or list(container) or (container.text or "").strip():
            raise PackageValidationError(f"{container_name} must remain empty for this mod version.")

    assemblies = fields["Assemblies"]
    if assemblies.attrib:
        raise PackageValidationError("Assemblies must not have attributes.")
    assembly_elements = list(assemblies)
    if any(element.tag != "Assembly" for element in assembly_elements):
        raise PackageValidationError("Assemblies may contain only Assembly elements.")
    declared = tuple(_element_text(element, "Assemblies/Assembly") for element in assembly_elements)
    if declared != tuple(expected_assemblies):
        raise PackageValidationError(
            "Assemblies must list exactly the four product DLLs of the selected file layout in load order: "
            f"expected {list(expected_assemblies)!r}, got {list(declared)!r}."
        )
    if any("/" in item or "\\" in item or ":" in item or ".." in item for item in declared):
        raise PackageValidationError("Assembly entries must be plain DLL file names inside the package root.")


def load_layouts(path: Path = LAYOUT_PATH) -> dict[str, object]:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        raise PackageValidationError(f"Invalid package layout: {error}") from error
    if not isinstance(data, dict) or set(data) != {"schemaVersion", "current", "legacy", "previous"} or data["schemaVersion"] != 1:
        raise PackageValidationError("Unsupported package layout schema.")
    for key, expected in (("current", CURRENT_FILES), ("legacy", LEGACY_FILES), ("previous", PREVIOUS_FILES)):
        names = data[key]
        if not isinstance(names, list) or any(not isinstance(name, str) for name in names):
            raise PackageValidationError("Package layout must contain lists of relative paths.")
        for name in names:
            if not name or "\\" in name or ":" in name or PurePosixPath(name).is_absolute() or \
                    any(part in {"", ".", ".."} for part in name.split("/")):
                raise PackageValidationError(f"Unsafe package layout path: {name}")
        if tuple(names) != expected:
            raise PackageValidationError(f"Package layout {key} must match the approved exact file set/order.")
    return data


def package_directories(names: Sequence[str]) -> set[str]:
    return {str(parent) for name in names for parent in PurePosixPath(name).parents if str(parent) != "."}


def validate_package_directory(package_directory: Path, require_current_metadata: bool = True) -> list[dict[str, object]]:
    """Validate the package allowlist and return a size and SHA-256 inventory."""
    layouts = load_layouts()
    candidate = Path(os.path.abspath(package_directory))
    for parent in reversed(candidate.parents):
        try:
            status = parent.lstat()
        except OSError as error:
            raise PackageValidationError(f"Could not inspect package parent: {parent}") from error
        if stat.S_ISLNK(status.st_mode) or _is_reparse_point(status):
            raise PackageValidationError(f"Package parents must not be links or reparse points: {parent}")
    try:
        root_status = candidate.lstat()
    except OSError as error:
        raise PackageValidationError(f"Package directory does not exist: {candidate}") from error
    if stat.S_ISLNK(root_status.st_mode) or _is_reparse_point(root_status):
        raise PackageValidationError("Package directory must not be a symbolic link or reparse point.")
    if not stat.S_ISDIR(root_status.st_mode):
        raise PackageValidationError(f"Package path is not a directory: {candidate}")

    actual_names: set[str] = set()
    actual_directories: set[str] = set()
    allowed_directories = package_directories(CURRENT_FILES)
    case_names: set[str] = set()

    def visit(directory: Path) -> None:
        for entry in directory.iterdir():
            name = entry.relative_to(candidate).as_posix()
            status = entry.lstat()
            if name.casefold() in case_names:
                raise PackageValidationError(f"Case collision in package: {name}")
            case_names.add(name.casefold())
            if stat.S_ISLNK(status.st_mode) or _is_reparse_point(status):
                raise PackageValidationError(f"Package entries must not be links or reparse points: {name}")
            if stat.S_ISDIR(status.st_mode):
                if name not in allowed_directories:
                    raise PackageValidationError(f"Unknown package directory; only approved directories and regular files are allowed: {name}")
                actual_directories.add(name)
                visit(entry)
            elif stat.S_ISREG(status.st_mode):
                actual_names.add(name)
            else:
                raise PackageValidationError(f"Package entries must be regular files: {name}")

    try:
        visit(candidate)
    except OSError as error:
        raise PackageValidationError(f"Could not inspect package tree: {error}") from error
    names = layouts["current"]
    if not require_current_metadata:
        for candidate_names in (PREVIOUS_TIER_FILES, RENAMED_PLATFORM_TIER_FILES, PREVIOUS_CURRENT_FILES, RENAMED_PLATFORM_FILES, layouts["legacy"], layouts["previous"]):
            if actual_names == set(candidate_names):
                names = candidate_names
                break
    if actual_names != set(names) or actual_directories != package_directories(names):
        missing = sorted(set(names) - actual_names)
        extra = sorted(actual_names - set(names))
        raise PackageValidationError(f"Package file allowlist mismatch; missing={missing}, extra={extra}; directories={sorted(actual_directories)}.")

    _validate_manifest(candidate / "manifest.xml", _layout_assemblies(names), require_current_metadata)
    inventory: list[dict[str, object]] = []
    for name in names:
        path = candidate / name
        digest = hashlib.sha256()
        size = 0
        with path.open("rb") as stream:
            for block in iter(lambda: stream.read(1024 * 1024), b""):
                size += len(block)
                digest.update(block)
        if size == 0:
            raise PackageValidationError(f"Package file is empty: {name}")
        inventory.append({"name": name, "size": size, "sha256": digest.hexdigest()})
    return inventory


def _arguments(argv: Sequence[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--package-dir", required=True)
    parser.add_argument(
        "--check-existing",
        action="store_true",
        help="Check ownership and package boundaries before replacing an earlier local package.",
    )
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    arguments = _arguments(argv)
    try:
        inventory = validate_package_directory(
            Path(arguments.package_dir), require_current_metadata=not arguments.check_existing
        )
    except PackageValidationError as error:
        print(f"Package validation error: {error}", file=sys.stderr)
        return 2

    print("[PASS] Package manifest and file allowlist validated.")
    for item in inventory:
        print(f"[FILE] {item['name']} size={item['size']} sha256={item['sha256']}")
    return 0


