"""Validate the installable Digital Storage package contents and manifest."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import stat
import sys
import unicodedata
import xml.etree.ElementTree as ET
from pathlib import Path, PurePosixPath
from typing import BinaryIO, Sequence

EXPECTED_ID = "digital_storage"
EXPECTED_NAME = "Digital Storage"
EXPECTED_AUTHOR = "Caizii"
# The mods menu compares only the leading digits and dots of Application.version, ignoring the letter suffix.
EXPECTED_GAME_VERSION = "0.46"
SOURCE_MANIFEST_PATH = Path(__file__).resolve().parents[2] / "packaging/manifest.xml"
LAYOUT_PATH = Path(__file__).resolve().parents[2] / "packaging/package-files.json"
INSTALLED_LAYOUT_PATH = Path(__file__).resolve().parents[2] / "packaging/installed-layouts.json"
# Every resource of a package this toolchain validated, by name and content hash. Lets replacement accept leftovers of
# unreleased local builds without listing them in packaging/, while foreign files still fail.
OWNED_RESOURCES_PATH = Path(__file__).resolve().parents[2] / "artifacts/owned-resources.json"
MANIFEST_MAX_BYTES = 1_048_576
EXPECTED_ELEMENTS = (
    "ID", "Name", "Author", "ModVersion", "Description", "GameVersions", "Prerequisites", "Assemblies", "Capabilities"
)
APPROVED_ASSEMBLY_LOAD_ORDER = (
    "ProbablyStolen.DigitalStorage.Api.Core.dll",
    "ProbablyStolen.DigitalStorage.Api.dll",
    "ProbablyStolen.DigitalStorage.Common.dll",
    "ProbablyStolen.DigitalStorage.Client.dll",
    "ProbablyStolen.DigitalStorage.Platform.dll",
    "ProbablyStolen.DigitalStorage.Compat.dll",
    "ProbablyStolen.DigitalStorage.Mod.dll",
)
LEGACY_PLATFORM_ASSEMBLY = "ProbablyStolen.DigitalStorage.Platform.ProbablyStolen.dll"
_ID_PATTERN = re.compile(r"[A-Za-z0-9_-][A-Za-z0-9_.-]{0,63}\Z")
_REPARSE_POINT = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)
_WINDOWS_RESERVED = {"CON", "PRN", "AUX", "NUL", *(f"COM{i}" for i in range(1, 10)), *(f"LPT{i}" for i in range(1, 10))}



def _is_reparse_point(status: object) -> bool:
    return bool(getattr(status, "st_file_attributes", 0) & _REPARSE_POINT)


class PackageValidationError(ValueError):
    """Raised when package contents do not match the release allowlist."""


def manifest_text(platform_assembly: str, assemblies: Sequence[str] | None = None) -> str:
    """Return the approved manifest declaring the given on-disk Platform assembly name.

    ``assemblies`` lists the DLLs of an older layout in load order; releases before the public API declared only the
    four core assemblies, so their manifests must not name the API and integration assemblies.
    """
    text = SOURCE_MANIFEST_PATH.read_text(encoding="utf-8")
    if assemblies is not None:
        # The Platform entry is written under its current name and renamed below, like the current manifest.
        canonical = (CURRENT_PLATFORM_ASSEMBLY if name == LEGACY_PLATFORM_ASSEMBLY else name for name in assemblies)
        block = "".join(f"    <Assembly>{name}</Assembly>\n" for name in canonical)
        text, count = re.subn(r"(<Assemblies>\n)(?:\s*<Assembly>[^<]*</Assembly>\n)+(\s*</Assemblies>)",
                              lambda match: match.group(1) + block + match.group(2), text)
        if count != 1:
            raise PackageValidationError("The source manifest has no assembly list to rewrite.")
    return text.replace(
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


def _validate_manifest(path: Path, expected_assemblies: Sequence[str], require_current_metadata: bool) -> str:
    try:
        with path.open("rb") as stream:
            return validate_manifest_stream(stream, expected_assemblies, require_current_metadata)
    except OSError as error:
        raise PackageValidationError(f"manifest.xml could not be read: {error}") from error


def validate_manifest_stream(
    stream: BinaryIO, expected_assemblies: Sequence[str], require_current_metadata: bool = True
) -> str:
    data = stream.read(MANIFEST_MAX_BYTES + 1)
    if len(data) > MANIFEST_MAX_BYTES:
        raise PackageValidationError("manifest.xml exceeds the 1 MiB validation limit.")
    try:
        root = ET.fromstring(data)
    except ET.ParseError as error:
        raise PackageValidationError(f"manifest.xml could not be parsed: {error}") from error

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

    version = _element_text(fields["ModVersion"], "ModVersion")
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
            "Assemblies must list exactly the product DLLs of the selected file layout in load order: "
            f"expected {list(expected_assemblies)!r}, got {list(declared)!r}."
        )
    if any("/" in item or "\\" in item or ":" in item or ".." in item for item in declared):
        raise PackageValidationError("Assembly entries must be plain DLL file names inside the package root.")
    return version


def _safe_relative_path(value: object, label: str) -> str:
    if not isinstance(value, str) or not value or "\\" in value or ":" in value:
        raise PackageValidationError(f"Unsafe {label} path: {value!r}")
    path = PurePosixPath(value)
    parts = value.split("/")
    if path.is_absolute() or any(part in ("", ".", "..") for part in parts):
        raise PackageValidationError(f"Unsafe {label} path: {value!r}")
    if unicodedata.normalize("NFKC", value) != value:
        raise PackageValidationError(f"Non-normalized {label} path: {value!r}")
    for part in parts:
        if part[-1:] in (".", " ") or any(ord(char) < 32 or char in '<>"|?*' for char in part):
            raise PackageValidationError(f"Unsafe Windows {label} path segment: {part!r}")
        stem = part.split(".", 1)[0].upper()
        if stem in _WINDOWS_RESERVED:
            raise PackageValidationError(f"Reserved Windows {label} path segment: {part!r}")
    return value


def _validate_path_set(paths: Sequence[str], label: str) -> None:
    for value in paths:
        _safe_relative_path(value, label)
    nodes: dict[str, tuple[str, bool]] = {}
    for value in paths:
        parts = value.split("/")
        for index, part in enumerate(parts):
            spelling = "/".join(parts[:index + 1])
            key = unicodedata.normalize("NFKC", spelling).casefold()
            is_file = index == len(parts) - 1
            if key in nodes:
                old_spelling, old_is_file = nodes[key]
                if old_spelling != spelling:
                    raise PackageValidationError(f"Case/normalization collision in {label}: {old_spelling!r} and {spelling!r}")
                if old_is_file or is_file:
                    raise PackageValidationError(f"File/directory collision in {label}: {value!r}")
            else:
                nodes[key] = (spelling, is_file)


def _manifest_assemblies(path: Path = SOURCE_MANIFEST_PATH) -> tuple[str, ...]:
    try:
        root = ET.parse(path).getroot()
        container = root.find("Assemblies")
        names = tuple((item.text or "").strip() for item in container if item.tag == "Assembly") if container is not None else ()
    except (OSError, ET.ParseError) as error:
        raise PackageValidationError(f"Could not read product assembly list from manifest.xml: {error}") from error
    if names != APPROVED_ASSEMBLY_LOAD_ORDER:
        raise PackageValidationError("Manifest product assemblies differ from the independently approved assembly/load order.")
    _validate_path_set(names, "manifest assembly")
    if any(not name.endswith(".dll") or "/" in name for name in names):
        raise PackageValidationError("Manifest assemblies must be product DLL file names.")
    return names


def load_layouts(path: Path = LAYOUT_PATH) -> dict[str, object]:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        raise PackageValidationError(f"Invalid package declaration: {error}") from error
    if not isinstance(data, dict) or set(data) != {"schemaVersion", "resources"} or data["schemaVersion"] != 2:
        raise PackageValidationError("Unsupported package declaration schema.")
    entries = data["resources"]
    if not isinstance(entries, list) or not entries:
        raise PackageValidationError("Package resources must be a non-empty list.")
    resource_paths: list[str] = []
    for entry in entries:
        if not isinstance(entry, dict) or set(entry) - {"path", "image"} or "path" not in entry:
            raise PackageValidationError("Each package resource must contain a path and optional image constraints.")
        resource_path = _safe_relative_path(entry["path"], "package resource")
        suffix = PurePosixPath(resource_path).suffix.lower()
        if suffix not in {".png", ".csv", ".txt", ".json"}:
            raise PackageValidationError(f"Unsupported package resource type: {resource_path}")
        resource_paths.append(resource_path)
        if suffix == ".png" and "image" not in entry:
            raise PackageValidationError(f"PNG resource requires image constraints: {resource_path}.")
        if "image" in entry:
            if not resource_path.endswith(".png"):
                raise PackageValidationError(f"Image constraints require a .png resource: {resource_path}.")
            image = entry["image"]
            if not isinstance(image, dict) or set(image) != {"source", "runtime", "interface", "visible", "sheetCells", "sheetCellSize", "openCenters"}:
                raise PackageValidationError(f"Invalid image constraints for {resource_path}.")
            for dimension in (image["source"], image["runtime"]):
                if not isinstance(dimension, list) or len(dimension) != 2 or any(type(n) is not int or n <= 0 for n in dimension):
                    raise PackageValidationError(f"Invalid image dimensions for {entry['path']}.")
            if any(type(image[key]) is not bool for key in ("interface", "visible", "openCenters")) or type(image["sheetCells"]) is not int or image["sheetCells"] < 0:
                raise PackageValidationError(f"Invalid image behavior constraints for {entry['path']}.")
            cell_size = image["sheetCellSize"]
            if cell_size is not None and (not isinstance(cell_size, list) or len(cell_size) != 2 or
                                          any(type(n) is not int or n <= 0 for n in cell_size)):
                raise PackageValidationError(f"Invalid image sheet-cell size for {entry['path']}.")
            if (image["sheetCells"] == 0) != (cell_size is None):
                raise PackageValidationError(f"Image sheet-cell count and dimensions disagree for {entry['path']}.")
            if cell_size is not None and (image["runtime"][0] % cell_size[0] or image["runtime"][1] % cell_size[1]):
                raise PackageValidationError(f"Image dimensions do not align to sheet cells for {entry['path']}.")
    _validate_path_set(resource_paths, "package resource")
    assemblies = _manifest_assemblies()
    all_paths = ("manifest.xml", *assemblies, *resource_paths)
    _validate_path_set(all_paths, "current package")
    image_names = [PurePosixPath(entry["path"]).stem for entry in entries if "image" in entry]
    if len(image_names) != len(set(image_names)):
        raise PackageValidationError("Image resource identifiers must have unique PNG stems.")
    data["assemblies"] = list(assemblies)
    data["current"] = list(all_paths)
    data["images"] = {PurePosixPath(entry["path"]).stem: {**entry["image"], "path": entry["path"]}
                      for entry in entries if "image" in entry}
    return data


def load_installed_layouts(path: Path = INSTALLED_LAYOUT_PATH) -> dict[str, object]:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as error:
        raise PackageValidationError(f"Invalid installed-layout declaration: {error}") from error
    if not isinstance(data, dict) or set(data) != {"schemaVersion", "lastReleased", "earlierLocalBuild", "layouts"} or data["schemaVersion"] != 1:
        raise PackageValidationError("Unsupported installed-layout schema.")
    layouts = data["layouts"]
    if not isinstance(layouts, list) or not layouts:
        raise PackageValidationError("Installed layouts must be a non-empty list.")
    seen_ids: set[str] = set()
    for layout in layouts:
        if not isinstance(layout, dict) or set(layout) != {"id", "files", "platformAssembly"}:
            raise PackageValidationError("Each installed layout must define id, files, and platformAssembly.")
        identity, files, platform = layout["id"], layout["files"], layout["platformAssembly"]
        if not isinstance(identity, str) or not identity or identity in seen_ids or not isinstance(platform, str):
            raise PackageValidationError("Installed layout identifiers and platform assemblies must be unique strings.")
        seen_ids.add(identity)
        if not isinstance(files, list) or not files or any(not isinstance(name, str) for name in files):
            raise PackageValidationError(f"Installed layout {identity} must list files.")
        _validate_path_set(files, f"installed layout {identity}")
        expected = tuple(name for name in files if name.endswith(".dll"))
        if not expected or platform not in expected or "manifest.xml" not in files:
            raise PackageValidationError(f"Installed layout {identity} has no valid manifest/assembly declaration.")
        approved_names = {*APPROVED_ASSEMBLY_LOAD_ORDER, LEGACY_PLATFORM_ASSEMBLY}
        if not set(expected) <= approved_names:
            raise PackageValidationError(f"Installed layout {identity} contains a non-product assembly.")
    last_released = data["lastReleased"]
    if not isinstance(last_released, str) or last_released not in seen_ids:
        raise PackageValidationError("Last released layout must identify a declared installed layout.")
    earlier = data["earlierLocalBuild"]
    if not isinstance(earlier, dict) or set(earlier) != {"requiredAssemblies", "allowResourcesFrom"}:
        raise PackageValidationError("Invalid earlier-local-build policy.")
    required = earlier["requiredAssemblies"]
    allowed = earlier["allowResourcesFrom"]
    if not isinstance(required, list) or any(not isinstance(name, str) for name in required) or \
            not isinstance(allowed, list) or any(not isinstance(name, str) for name in allowed):
        raise PackageValidationError("Earlier-local-build policy values must be string lists.")
    if tuple(required) != tuple(load_layouts()["assemblies"]):
        raise PackageValidationError("Earlier-local-build assembly requirement differs from the current product assemblies.")
    if set(allowed) != {"current", last_released}:
        raise PackageValidationError("Earlier-local-build resources must come only from current and last-released layouts.")
    return data


def _known_layouts() -> tuple[tuple[str, tuple[str, ...], str], ...]:
    data = load_installed_layouts()
    layouts = [(item["id"], tuple(item["files"]), item["platformAssembly"]) for item in data["layouts"]]
    current = tuple(load_layouts()["current"])
    layouts.insert(0, ("current", current, next(name for name in load_layouts()["assemblies"] if name.endswith("Platform.dll"))))
    return tuple(layouts)


def _earlier_build_of_current(actual_names: set[str], is_owned_leftover=lambda name: False) -> tuple[str, ...] | None:
    data = load_installed_layouts()
    current = tuple(load_layouts()["current"])
    required = {"manifest.xml", *data["earlierLocalBuild"]["requiredAssemblies"]}
    released = next(tuple(item["files"]) for item in data["layouts"] if item["id"] == data["lastReleased"])
    allowed = tuple(dict.fromkeys((*current, *(name for name in released if name not in current))))
    leftovers = tuple(sorted(actual_names - set(allowed)))
    if not required <= actual_names or not all(is_owned_leftover(name) for name in leftovers):
        return None
    return (*(name for name in allowed if name in actual_names), *leftovers)


def load_owned_resources(path: Path | None = None) -> dict[str, set[str]]:
    """Read the resource hashes of earlier validated packages; a missing or unreadable record owns nothing."""
    try:
        data = json.loads((path or OWNED_RESOURCES_PATH).read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {}
    resources = data.get("resources") if isinstance(data, dict) and data.get("schemaVersion") == 1 else None
    if not isinstance(resources, dict):
        return {}
    return {name: {value for value in hashes if isinstance(value, str)}
            for name, hashes in resources.items() if isinstance(name, str) and isinstance(hashes, list)}


def remember_owned_resources(inventory: Sequence[dict[str, object]], path: Path | None = None) -> None:
    """Add the resource files of a validated package to the record; assemblies and the manifest are never leftovers."""
    path = path or OWNED_RESOURCES_PATH
    owned = load_owned_resources(path)
    for item in inventory:
        name = str(item["name"])
        if name.endswith(".dll") or name == "manifest.xml":
            continue
        owned.setdefault(name, set()).add(str(item["sha256"]))
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = {"schemaVersion": 1, "resources": {name: sorted(hashes) for name, hashes in sorted(owned.items())}}
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    os.replace(temporary, path)


KNOWN_LAYOUTS = _known_layouts()
CURRENT_FILES = tuple(load_layouts()["current"])
EXPECTED_ASSEMBLIES = tuple(load_layouts()["assemblies"])
RESOURCE_FILES = tuple(entry["path"] for entry in load_layouts()["resources"])
_LAYOUTS_BY_ID = {identity: files for identity, files, _ in KNOWN_LAYOUTS}
_PLATFORM_BY_ID = {identity: platform for identity, _, platform in KNOWN_LAYOUTS}
CURRENT_PLATFORM_ASSEMBLY = _PLATFORM_BY_ID["current"]
LEGACY_FILES = _LAYOUTS_BY_ID["legacy5"]
PREVIOUS_FILES = _LAYOUTS_BY_ID["previous11"]
PRE_CODE_REGISTRY_CURRENT_FILES = _LAYOUTS_BY_ID["pre-code-registry-current"]
PRE_KEEP_OPEN_CURRENT_FILES = _LAYOUTS_BY_ID["pre-keep-open-current"]
PRE_STACKING_CURRENT_FILES = _LAYOUTS_BY_ID["pre-stacking-current"]
PRE_BUTTON_CURRENT_FILES = _LAYOUTS_BY_ID["pre-button-current"]
PRE_DATA_CURRENT_FILES = _LAYOUTS_BY_ID["pre-data-current"]
PREVIOUS_API_FILES = _LAYOUTS_BY_ID["previous-api"]
PREVIOUS_SEARCH_FILES = _LAYOUTS_BY_ID["previous-search"]
PREVIOUS_TIER_FILES = _LAYOUTS_BY_ID["previous21"]
RENAMED_PLATFORM_TIER_FILES = _LAYOUTS_BY_ID["previous21-renamed"]
PREVIOUS_CURRENT_FILES = _LAYOUTS_BY_ID["current13"]
RENAMED_PLATFORM_FILES = _LAYOUTS_BY_ID["old13"]
DATA_RESOURCE_FILES = tuple(name for name in _LAYOUTS_BY_ID[load_installed_layouts()["lastReleased"]]
                            if name not in CURRENT_FILES)
HISTORICAL_RESOURCE_FILES = tuple(name for name in PRE_DATA_CURRENT_FILES if name not in (*EXPECTED_ASSEMBLIES, "manifest.xml"))
TIER_RESOURCE_FILES = tuple(name for name in PREVIOUS_TIER_FILES if name not in PREVIOUS_CURRENT_FILES)
CORE_ASSEMBLIES = tuple(name for name in LEGACY_FILES if name.endswith(".dll"))
EXPECTED_FILES = set(CURRENT_FILES)


def package_directories(names: Sequence[str]) -> set[str]:
    return {str(parent) for name in names for parent in PurePosixPath(name).parents if str(parent) != "."}


def validate_package_directory(
    package_directory: Path, require_current_metadata: bool = True, allow_earlier_build: bool = False,
    owned_resources: dict[str, set[str]] | None = None
) -> list[dict[str, object]]:
    """Validate the package allowlist and return a size and SHA-256 inventory.

    ``allow_earlier_build`` also accepts an earlier local build of the current layout, and is meant only for checking
    a directory about to be replaced; removal keeps requiring a known layout. Such a build may also hold resources an
    earlier validated package had, matched by name and content against ``owned_resources`` (by default the record at
    ``OWNED_RESOURCES_PATH``), so the replacement removes them.
    """
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
    # Earlier approved layouts may hold directories the current one dropped, such as the module bay Data/.
    owned = (load_owned_resources() if owned_resources is None else owned_resources) if allow_earlier_build else {}
    allowed_directories = package_directories([*(name for _, names, _ in KNOWN_LAYOUTS for name in names), *owned])
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
        for _, candidate_names, _ in KNOWN_LAYOUTS[1:]:
            if actual_names == set(candidate_names):
                names = candidate_names
                break
        else:
            if allow_earlier_build:
                def is_owned_leftover(name: str) -> bool:
                    if name not in owned or name.endswith(".dll") or name == "manifest.xml":
                        return False
                    with (candidate / name).open("rb") as stream:
                        return str(file_inventory_entry(name, stream)["sha256"]) in owned[name]

                names = _earlier_build_of_current(actual_names, is_owned_leftover) or names
    expected_directories = package_directories(names)
    # An installation whose layout dropped a directory may keep it empty; every directory was already checked to
    # belong to an approved layout, and with the file set exact such a leftover holds no files.
    leftover_directories_allowed = allow_earlier_build and expected_directories <= actual_directories
    if actual_names != set(names) or (actual_directories != expected_directories and not leftover_directories_allowed):
        missing = sorted(set(names) - actual_names)
        extra = sorted(actual_names - set(names))
        raise PackageValidationError(f"Package file allowlist mismatch; missing={missing}, extra={extra}; directories={sorted(actual_directories)}.")

    _validate_manifest(candidate / "manifest.xml", _layout_assemblies(names), require_current_metadata)
    inventory: list[dict[str, object]] = []
    for name in names:
        with (candidate / name).open("rb") as stream:
            inventory.append(file_inventory_entry(name, stream))
    return inventory


def file_inventory_entry(name: str, stream: BinaryIO) -> dict[str, object]:
    digest = hashlib.sha256()
    size = 0
    for block in iter(lambda: stream.read(1024 * 1024), b""):
        size += len(block)
        digest.update(block)
    if size == 0:
        raise PackageValidationError(f"Package file is empty: {name}")
    return {"name": name, "size": size, "sha256": digest.hexdigest()}


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
            Path(arguments.package_dir), require_current_metadata=not arguments.check_existing,
            allow_earlier_build=arguments.check_existing,
        )
    except PackageValidationError as error:
        print(f"Package validation error: {error}", file=sys.stderr)
        return 2

    if not arguments.check_existing:
        try:
            remember_owned_resources(inventory)
        except OSError as error:
            print(f"Warning: could not record package resources: {error}", file=sys.stderr)
    print("[PASS] Package manifest and file allowlist validated.")
    for item in inventory:
        print(f"[FILE] {item['name']} size={item['size']} sha256={item['sha256']}")
    return 0

