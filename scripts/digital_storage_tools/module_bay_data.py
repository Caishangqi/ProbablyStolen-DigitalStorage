"""Validate the explicit module-bay JSON resource index and its referenced files."""

from __future__ import annotations

import json
import unicodedata
from pathlib import Path, PurePosixPath
from typing import Any, Callable


MAXIMUM_BYTES = 1024 * 1024
MAXIMUM_DEPTH = 32
MAXIMUM_INT = 2_147_483_647
MAXIMUM_DIMENSION = 256
MAXIMUM_AREA = 65536
MAXIMUM_STAGES = 64
MAXIMUM_TOTAL_AREA = 262144
EXPECTED_PROFILE_COUNTS = {
    "digital_storage:terminal": (26, 31, 41, 59, 73, 95, 113, 139, 161, 191, 255),
    "digital_storage:vanilla_standard": (26, 31, 35),
}
BUILTIN_PROVIDERS = {"digital_storage:native_standard", "digital_storage:cell"}


class ModuleBayDataError(ValueError):
    """Raised when packaged module-bay data is malformed or incomplete."""


class _DuplicateKeyError(ValueError):
    pass


def _object_pairs(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise _DuplicateKeyError(f"duplicate JSON property: {key}")
        result[key] = value
    return result


def _load_json(path: Path, path_validator: Callable[[Path], Path]) -> Any:
    try:
        safe_path = path_validator(path)
        with safe_path.open("rb") as stream:
            data = stream.read(MAXIMUM_BYTES + 1)
    except (OSError, ValueError) as error:
        if isinstance(error, ModuleBayDataError):
            raise
        raise ModuleBayDataError(f"Could not read JSON resource {path}: {error}") from error
    if len(data) > MAXIMUM_BYTES:
        raise ModuleBayDataError(f"JSON resource exceeds the 1 MiB limit: {path}")
    try:
        text = data.decode("utf-8")
        value = json.loads(text, object_pairs_hook=_object_pairs)
    except (UnicodeError, json.JSONDecodeError, _DuplicateKeyError) as error:
        raise ModuleBayDataError(f"Invalid JSON resource {path}: {error}") from error
    _check_depth(value, 1, path)
    return value


def _check_depth(value: Any, depth: int, path: Path) -> None:
    if depth > MAXIMUM_DEPTH:
        raise ModuleBayDataError(f"JSON nesting exceeds depth {MAXIMUM_DEPTH}: {path}")
    if isinstance(value, dict):
        for child in value.values():
            _check_depth(child, depth + 1, path)
    elif isinstance(value, list):
        for child in value:
            _check_depth(child, depth + 1, path)


def _object(value: Any, label: str) -> dict[str, Any]:
    if not isinstance(value, dict):
        raise ModuleBayDataError(f"{label} must be an object")
    return value


def _array(value: Any, label: str) -> list[Any]:
    if not isinstance(value, list):
        raise ModuleBayDataError(f"{label} must be an array")
    return value


def _properties(value: dict[str, Any], required: set[str], optional: set[str] = set()) -> None:
    unknown = set(value) - required - optional
    missing = required - set(value)
    if unknown:
        raise ModuleBayDataError(f"unknown properties: {sorted(unknown)}")
    if missing:
        raise ModuleBayDataError(f"missing properties: {sorted(missing)}")


def _integer(value: Any, label: str) -> int:
    if isinstance(value, bool) or not isinstance(value, int) or not -2_147_483_648 <= value <= MAXIMUM_INT:
        raise ModuleBayDataError(f"{label} must be a signed 32-bit integer")
    return value


def _text(value: Any, label: str) -> str:
    if not isinstance(value, str) or not value.strip() or value != value.strip():
        raise ModuleBayDataError(f"{label} must be a non-empty trimmed string")
    if any(character.isspace() and character != "/" for character in value):
        raise ModuleBayDataError(f"{label} must not contain whitespace")
    return value


def _identifier(value: Any, label: str) -> str:
    value = _text(value, label)
    if value.count(":") != 1:
        raise ModuleBayDataError(f"{label} must use namespace:path syntax")
    namespace, path = value.split(":")
    if not namespace or not path or any(not part for part in path.split("/")):
        raise ModuleBayDataError(f"{label} must be a valid lowercase identifier")
    allowed = set("abcdefghijklmnopqrstuvwxyz0123456789_.-")
    if any(character not in allowed for character in namespace + path):
        raise ModuleBayDataError(f"{label} must be a valid lowercase identifier")
    return value


def _relative_json_path(value: Any, label: str) -> str:
    value = _text(value, label)
    parts = value.split("/")
    if (value.startswith("/") or "\\" in value or ":" in value or not value.endswith(".json")
            or any(part in {"", ".", ".."} for part in parts)):
        raise ModuleBayDataError(f"{label} must be a normalized relative JSON path")
    return value


def _layout(value: Any, label: str, remaining_area: int) -> tuple[int, int, int, tuple[str, ...]]:
    layout = _object(value, label)
    if "type" not in layout:
        raise ModuleBayDataError(f"missing properties: ['type']")
    layout_type = _text(layout["type"], f"{label}.type")
    if layout_type == "rectangle":
        _properties(layout, {"type", "width", "height"})
        width = _integer(layout["width"], f"{label}.width")
        height = _integer(layout["height"], f"{label}.height")
        if width <= 0 or height <= 0 or width > MAXIMUM_DIMENSION or height > MAXIMUM_DIMENSION or width * height > MAXIMUM_AREA:
            raise ModuleBayDataError(f"{label} dimensions are outside the supported range")
        if width * height > remaining_area:
            raise ModuleBayDataError(f"stage area exceeds the maximum total {MAXIMUM_TOTAL_AREA}")
        rows = tuple("0" * width for _ in range(height))
    elif layout_type == "pattern":
        _properties(layout, {"type", "rows"})
        raw_rows = _array(layout["rows"], f"{label}.rows")
        if not raw_rows or len(raw_rows) > MAXIMUM_DIMENSION or any(not isinstance(row, str) for row in raw_rows):
            raise ModuleBayDataError(f"{label}.rows must contain strings")
        rows = tuple(raw_rows)
        width = len(rows[0])
        height = len(rows)
        if width * height > remaining_area:
            raise ModuleBayDataError(f"stage area exceeds the maximum total {MAXIMUM_TOTAL_AREA}")
        if width <= 0 or width > MAXIMUM_DIMENSION or any(len(row) != width or any(character not in "01" for character in row) for row in rows):
            raise ModuleBayDataError(f"{label}.rows must be equal-width zero/one rows")
        if width * height > MAXIMUM_AREA:
            raise ModuleBayDataError(f"{label} dimensions are outside the supported range")
    else:
        raise ModuleBayDataError(f"{label}.type must be rectangle or pattern")
    available = sum(row.count("0") for row in rows)
    return width, height, available, rows


def _definition(value: Any, source: Path) -> tuple[str, list[dict[str, Any]]]:
    root = _object(value, str(source))
    _properties(root, {"format_version", "id", "stages"})
    if _integer(root["format_version"], "format_version") != 1:
        raise ModuleBayDataError(f"Unsupported format_version in {source}")
    definition_id = _identifier(root["id"], "id")
    stages = _array(root["stages"], "stages")
    if not stages:
        raise ModuleBayDataError(f"stages must not be empty: {source}")
    if len(stages) > MAXIMUM_STAGES:
        raise ModuleBayDataError(f"stages exceed the maximum count {MAXIMUM_STAGES}: {source}")
    parsed: list[dict[str, Any]] = []
    keys: set[str] = set()
    total_area = 0
    previous: set[tuple[int, int]] | None = None
    for expected_level, raw_stage in enumerate(stages):
        stage = _object(raw_stage, "stage")
        _properties(stage, {"level", "key", "layout"}, {"localization_key"})
        if _integer(stage["level"], "stage.level") != expected_level:
            raise ModuleBayDataError(f"stage levels must be contiguous from zero: {source}")
        key = _text(stage["key"], "stage.key")
        _identifier("stage:" + key, "stage.key")
        if key in keys:
            raise ModuleBayDataError(f"duplicate stage key: {key}")
        keys.add(key)
        if "localization_key" in stage:
            _identifier(stage["localization_key"], "stage.localization_key")
        width, height, available, rows = _layout(stage["layout"], "stage.layout", MAXIMUM_TOTAL_AREA - total_area)
        total_area += width * height
        if total_area > MAXIMUM_TOTAL_AREA:
            raise ModuleBayDataError(f"stage area exceeds the maximum total {MAXIMUM_TOTAL_AREA}: {source}")
        opened = {(x, y) for y, row in enumerate(rows) for x, cell in enumerate(row) if cell == "0"}
        if previous is not None and not previous <= opened:
            raise ModuleBayDataError(f"stage layout shrinks an earlier open coordinate: {source}")
        previous = opened
        parsed.append({"level": expected_level, "key": key, "width": width, "height": height,
                       "available": available, "rows": rows})
    return definition_id, parsed


def _native_item(value: Any, label: str) -> str:
    if not isinstance(value, str) or not value or value != value.strip() or any(unicodedata.category(character) == "Cc" for character in value):
        raise ModuleBayDataError(f"{label} must be a stable non-empty native item id")
    return value


def _binding(value: Any, source: Path) -> dict[str, Any]:
    binding = _object(value, "binding")
    _properties(binding, {"native_item_id", "definition_id", "provider_id"}, {"max_stage"})
    native = _native_item(binding["native_item_id"], "native_item_id")
    if native == "*":
        raise ModuleBayDataError("wildcard bindings are not supported; bind each native item id exactly")
    definition_id = _identifier(binding["definition_id"], "definition_id")
    provider_id = _identifier(binding["provider_id"], "provider_id")
    if provider_id not in BUILTIN_PROVIDERS:
        raise ModuleBayDataError(f"unknown builtin provider: {provider_id}")
    max_stage = binding.get("max_stage")
    if max_stage is not None:
        max_stage = _integer(max_stage, "max_stage")
        if max_stage < 0:
            raise ModuleBayDataError("max_stage must not be negative")
    return {"native": native, "definition": definition_id, "provider": provider_id, "max_stage": max_stage}


def validate_registry_data(resources_root: Path, path_validator: Callable[[Path], Path]) -> None:
    """Validate only files explicitly named by the packaged registry index."""
    root = path_validator(Path(resources_root))
    index_path = root / "Data" / "registries.json"
    index = _load_json(index_path, path_validator)
    _properties(index, {"format_version", "profiles", "bindings"})
    if _integer(index["format_version"], "format_version") != 1:
        raise ModuleBayDataError("Unsupported registry index format_version")
    paths: list[tuple[str, str]] = []
    for key, kind in (("profiles", "definition"), ("bindings", "bindings")):
        for entry in _array(index[key], key):
            entry = _object(entry, key + " entry")
            _properties(entry, {"path"})
            path = _relative_json_path(entry["path"], key + ".path")
            paths.append((kind, path))
    if len({path for _, path in paths}) != len(paths):
        raise ModuleBayDataError("registry index contains a duplicate path")

    definitions: dict[str, list[dict[str, Any]]] = {}
    bindings: list[dict[str, Any]] = []
    for kind, relative in paths:
        source = root / PurePosixPath(relative)
        value = _load_json(source, path_validator)
        if kind == "definition":
            definition_id, stages = _definition(value, source)
            if definition_id in definitions:
                raise ModuleBayDataError(f"duplicate definition id: {definition_id}")
            definitions[definition_id] = stages
        else:
            content = _object(value, str(source))
            _properties(content, {"format_version", "bindings"})
            if _integer(content["format_version"], "format_version") != 1:
                raise ModuleBayDataError(f"Unsupported bindings format_version in {source}")
            for item in _array(content["bindings"], "bindings"):
                bindings.append(_binding(item, source))

    seen_native: set[str] = set()
    for binding in bindings:
        if binding["native"] in seen_native:
            raise ModuleBayDataError(f"duplicate native binding: {binding['native']}")
        seen_native.add(binding["native"])
        stages = definitions.get(binding["definition"])
        if stages is None:
            raise ModuleBayDataError(f"binding references unknown definition: {binding['definition']}")
        maximum = binding["max_stage"]
        if maximum is not None and maximum >= len(stages):
            raise ModuleBayDataError(f"binding max_stage exceeds definition stages: {binding['native']}")

    required_profiles = {"digital_storage:terminal", "digital_storage:vanilla_standard"}
    missing_profiles = required_profiles - definitions.keys()
    if missing_profiles:
        raise ModuleBayDataError(f"missing builtin definitions: {sorted(missing_profiles)}")
    required_bindings = {
        "digital_storage.storage_terminal": ("digital_storage:terminal", "digital_storage:native_standard", 10),
        "digital_storage.storage_drive": ("digital_storage:vanilla_standard", "digital_storage:native_standard", 2),
        "digital_storage.storage_network_controller": ("digital_storage:vanilla_standard", "digital_storage:native_standard", 2),
        "digital_storage.item_storage_cell_1k": ("digital_storage:vanilla_standard", "digital_storage:cell", 0),
        "digital_storage.item_storage_cell_4k": ("digital_storage:vanilla_standard", "digital_storage:cell", 0),
        "digital_storage.item_storage_cell_16k": ("digital_storage:vanilla_standard", "digital_storage:cell", 1),
        "digital_storage.item_storage_cell_64k": ("digital_storage:vanilla_standard", "digital_storage:cell", 1),
        "digital_storage.item_storage_cell_256k": ("digital_storage:vanilla_standard", "digital_storage:cell", 2),
        "digital_storage.item_cell_housing": ("digital_storage:vanilla_standard", "digital_storage:cell", 2),
    }
    binding_map = {item["native"]: item for item in bindings}
    for native, expected in required_bindings.items():
        actual = binding_map.get(native)
        if actual is None or (actual["definition"], actual["provider"], actual["max_stage"]) != expected:
            raise ModuleBayDataError(f"missing or incorrect builtin binding: {native}")

    for definition_id, expected in EXPECTED_PROFILE_COUNTS.items():
        actual = tuple(stage["available"] for stage in definitions[definition_id])
        if actual != expected:
            raise ModuleBayDataError(f"published stage counts differ for {definition_id}: {actual}")
