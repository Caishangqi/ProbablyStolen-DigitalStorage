"""Explicitly prepare or read-only validate the mod's runtime PNG assets."""

from __future__ import annotations

import argparse
import io
import os
import re
import stat
import sys
import tempfile
from pathlib import Path
from typing import Sequence


REPOSITORY = Path(__file__).resolve().parents[2]


class ContentValidationError(ValueError):
    """A required content input or runtime resource is invalid."""


def _resource_contract():
    from .package import PackageValidationError, load_layouts
    try:
        return load_layouts()
    except PackageValidationError as error:
        raise ContentValidationError(str(error)) from error


def _image_contracts() -> dict[str, dict[str, object]]:
    return IMAGE_CONTRACTS


def _dimensions(name: str) -> tuple[tuple[int, int], tuple[int, int]]:
    image = _image_contracts()[name]
    return tuple(image["source"]), tuple(image["runtime"])


def _resource_path(name: str) -> Path:
    contract = _image_contracts()[name]
    return Path(contract["path"])


_SOURCE_PATH_OVERRIDES = {
    # The runtime icon is prepared from the check-mark drawing; the older sketch in rawassets/ is left untouched.
    "button_io_finish_done": Path("io-port-artwork/button_io_finish_done.png"),
}


def _as_name(item: str) -> str:
    images = _image_contracts()
    name = Path(item).stem if item.lower().endswith(".png") else item
    if name not in images:
        raise ContentValidationError(f"Undeclared image resource: {item}")
    if item.lower().endswith(".png") and Path(item).as_posix() != _resource_path(name).as_posix():
        raise ContentValidationError(f"Image resource path does not match declaration: {item}")
    return name


IMAGE_CONTRACTS = _resource_contract()["images"]
ASSETS = {name: _dimensions(name) for name in IMAGE_CONTRACTS}
BUTTON_ASSETS = tuple(name for name, image in _image_contracts().items() if image["interface"] and image["runtime"] == [16, 16])
OPEN_CENTER_ASSETS = frozenset(name for name, image in _image_contracts().items() if image["openCenters"])
PICTURE_ASSETS = tuple(name for name, image in _image_contracts().items() if image["visible"] and name not in BUTTON_ASSETS)
INTERFACE_ASSETS = frozenset(name for name, image in _image_contracts().items() if image["interface"])



def image_api():
    try:
        import PIL
        from PIL import Image
    except ImportError as error:
        raise ContentValidationError(
            "Asset checks require Pillow. Run explicitly: mise run setup"
        ) from error
    if PIL.__version__ != "12.0.0":
        raise ContentValidationError(f"Expected Pillow 12.0.0, found {PIL.__version__}; run mise run setup.")
    return Image


def plain_path(path: Path) -> Path:
    """Reject links/reparse points in every existing path component before resolution."""
    path = Path(os.path.abspath(path))
    for part in (*reversed(path.parents), path):
        try:
            status = part.lstat()
        except FileNotFoundError:
            continue
        if stat.S_ISLNK(status.st_mode) or getattr(status, "st_file_attributes", 0) & 0x400:
            raise ContentValidationError(f"Links and reparse points are not accepted: {part}")
    return path


def validate_write_root(source: Path, output: Path) -> Path:
    source, output = plain_path(source), plain_path(output)
    if output == source or output.is_relative_to(source) or source.is_relative_to(output):
        raise ContentValidationError("Runtime output must be separate from source assets.")
    allowed = REPOSITORY / "packaging" / "resources"
    temporary = Path(tempfile.gettempdir()).resolve()
    if output.is_relative_to(REPOSITORY):
        if output != allowed:
            raise ContentValidationError("Repository writes are restricted to packaging/resources.")
    elif not output.is_relative_to(temporary) or output == temporary:
        raise ContentValidationError("External output must be an isolated directory under the system temporary directory.")
    return output


def source_png(source: Path, name: str):
    Image = image_api()
    path = plain_path(source / _SOURCE_PATH_OVERRIDES.get(name, Path(_resource_path(name).name)))
    try:
        data = path.read_bytes()
        with Image.open(io.BytesIO(data)) as opened:
            if opened.format != "PNG" or getattr(opened, "n_frames", 1) != 1:
                raise ContentValidationError(f"Expected a single PNG: {path}")
            if opened.size != _dimensions(name)[0]:
                raise ContentValidationError(f"Unexpected source dimensions for {name}: {opened.size}")
            rgba = opened.convert("RGBA")
            if name in BUTTON_ASSETS:
                validate_button_image(rgba, name)
    except (OSError, ValueError) as error:
        if isinstance(error, ContentValidationError):
            raise
        raise ContentValidationError(f"Could not read source {name}: {error}") from error
    return data, rgba


def validate_button_image(image, name: str) -> None:
    if image.getchannel("A").getbbox() is None:
        raise ContentValidationError(f"Image has no visible pixels: {name}")


def expected_png(source: Path, name: str) -> bytes:
    data, rgba = source_png(source, name)
    if _dimensions(name)[0] == _dimensions(name)[1]:
        return data
    padded = image_api().new("RGBA", _dimensions(name)[1], (0, 0, 0, 0))
    padded.paste(rgba, (0, 0))
    stream = io.BytesIO()
    padded.save(stream, format="PNG")
    return stream.getvalue()


def runtime_png(output: Path, name: str):
    path = plain_path(output / _resource_path(name))
    try:
        data = path.read_bytes()
        with image_api().open(io.BytesIO(data)) as opened:
            if opened.format != "PNG" or getattr(opened, "n_frames", 1) != 1:
                raise ContentValidationError(f"Expected a single runtime PNG: {name}")
            if opened.size != _dimensions(name)[1]:
                raise ContentValidationError(f"Runtime dimensions mismatch for {name}: {opened.size}")
            return data, opened.convert("RGBA")
    except (OSError, ValueError) as error:
        if isinstance(error, ContentValidationError):
            raise
        raise ContentValidationError(f"Could not read runtime {name}: {error}") from error


def check_image(output: Path, name: str) -> None:
    """Validate a committed runtime PNG on its own; art source files are not part of the repository."""
    _, actual = runtime_png(output, name)
    if name in BUTTON_ASSETS or name in PICTURE_ASSETS:
        validate_button_image(actual, name)
        sheet_cells = _image_contracts()[name]["sheetCells"]
        if sheet_cells:
            cell_width, cell_height = _image_contracts()[name]["sheetCellSize"]
            for cell in range(sheet_cells):
                validate_button_image(actual.crop((cell * cell_width, 0, cell * cell_width + cell_width, cell_height)),
                                      f"{name} cell {cell + 1}")
        return
    width, height = _dimensions(name)[0]
    for y in range(actual.height):
        for x in range(actual.width):
            if (x >= width or y >= height) and actual.getpixel((x, y)) != (0, 0, 0, 0):
                raise ContentValidationError(f"Padding must be transparent RGBA zero: {name}")
    for y in range(8, actual.height, 16):
        for x in range(8, actual.width, 16):
            if name not in OPEN_CENTER_ASSETS and actual.getpixel((x, y))[3] == 0:
                raise ContentValidationError(f"Transparent occupied grid center at {x},{y}: {name}")


def check_matches_source(source: Path, output: Path, name: str) -> None:
    source_data, original = source_png(source, name)
    actual_data, actual = runtime_png(output, name)
    width, height = original.size
    if actual.crop((0, 0, width, height)).tobytes() != original.tobytes():
        raise ContentValidationError(f"Source pixels changed: {name}")
    if _dimensions(name)[0] == _dimensions(name)[1] and actual_data != source_data:
        raise ContentValidationError(f"Unchanged asset must retain original bytes: {name}")


def prepare_assets(source: Path, output: Path, names: Sequence[str] | None = None) -> None:
    names = tuple(ASSETS) if names is None else tuple(_as_name(item) for item in names)
    output = validate_write_root(source, output)
    if not names or len(set(names)) != len(names) or any(name not in ASSETS for name in names):
        raise ContentValidationError("Choose unique known asset names.")
    # Preflight all inputs and destination paths before writing any image.
    prepared = {name: expected_png(source, name) for name in names}
    paths = {name: plain_path(output / _resource_path(name)) for name in names}
    for path in paths.values():
        if path.exists() and not path.is_file():
            raise ContentValidationError(f"Runtime destination is not a file: {path}")
    for name, data in prepared.items():
        path = paths[name]
        path.parent.mkdir(parents=True, exist_ok=True)
        temporary_name = None
        try:
            with tempfile.NamedTemporaryFile(dir=path.parent, delete=False) as stream:
                temporary_name = stream.name
                stream.write(data)
            os.replace(temporary_name, path)
        finally:
            if temporary_name and Path(temporary_name).exists():
                Path(temporary_name).unlink()
        check_matches_source(source, output, name)
        check_image(output, name)


def check_assets(output: Path, names: Sequence[str] | None = None) -> None:
    names = tuple(ASSETS) if names is None else tuple(_as_name(item) for item in names)
    for name in names:
        check_image(output, name)


def check_resource_tree(output: Path) -> None:
    output = plain_path(output)
    if not output.is_dir():
        raise ContentValidationError(f"Runtime resource directory is missing: {output}")
    contract = _resource_contract()
    expected = {entry["path"] for entry in contract["resources"]}
    actual: set[str] = set()
    for path in output.rglob("*"):
        if path.is_symlink() or getattr(path.lstat(), "st_file_attributes", 0) & 0x400:
            raise ContentValidationError(f"Links/reparse points are not accepted in runtime resources: {path}")
        if path.is_file():
            actual.add(path.relative_to(output).as_posix())
    missing, unexpected = sorted(expected - actual), sorted(actual - expected)
    if missing or unexpected:
        raise ContentValidationError(f"Runtime resource path mismatch; missing={missing}, unexpected={unexpected}")


def read_localization(path: Path) -> dict[str, str]:
    try:
        data = plain_path(path).read_bytes()
        if data.startswith(b"\xef\xbb\xbf"):
            raise ContentValidationError(f"Localization must not have a UTF-8 BOM: {path}")
        lines = data.decode("utf-8").splitlines()
    except (OSError, UnicodeError) as error:
        raise ContentValidationError(f"Could not read localization {path}: {error}") from error
    values: dict[str, str] = {}
    for line in lines:
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        if "," not in line:
            raise ContentValidationError(f"Localization rows must occupy one line with a comma: {path}")
        key, raw = line.split(",", 1)
        key, raw = key.strip(), raw.strip()
        if not re.fullmatch(r"[a-z][a-z0-9_]*", key) or key in values:
            raise ContentValidationError(f"Invalid or duplicate localization key: {key}")
        if raw.startswith('"'):
            if len(raw) < 2 or not raw.endswith('"') or re.search(r'(?<!")"(?!")', raw[1:-1].replace('""', '')):
                raise ContentValidationError(f"Invalid CSV quoting: {key}")
            raw = raw[1:-1].replace('""', '"')
        elif '"' in raw:
            raise ContentValidationError(f"Quote the entire CSV value and escape quotes: {key}")
        value = raw.replace("\\n", "\n")
        if not value.strip():
            raise ContentValidationError(f"Empty localization value: {key}")
        values[key] = value
    return values


def format_parameters(value: str) -> list[int]:
    remaining = value.replace("{{", "").replace("}}", "")
    parameters = sorted(int(index) for index in re.findall(r"\{([0-9]+)\}", remaining))
    without_parameters = re.sub(r"\{[0-9]+\}", "", remaining)
    if "{" in without_parameters or "}" in without_parameters:
        raise ContentValidationError("Only numbered localization placeholders such as {0} are supported.")
    return parameters


def check_content(project: Path, output: Path) -> None:
    platform = project / "src" / "ProbablyStolen.DigitalStorage.Platform"
    classes = {
        "terminal": ("Content/TerminalItem.cs", "TerminalItem", "storage_terminal"),
        "drive": ("Content/DriveItem.cs", "DriveItem", "storage_drive"),
        "controller": ("Content/ControllerItem.cs", "ControllerItem", "storage_network_controller"),
        "item_storage_cell_1k": ("Content/StorageCellItem.cs", "StorageCellItem", "item_storage_cell_1k"),
        "item_cell_housing": ("Content/MaterialItem.cs", "MaterialItem", "item_cell_housing"),
        "cell_component_1k": ("Content/StorageComponentFactory.cs", "StorageComponentFactory", "cell_component_1k"),
        "workbench": ("Content/CellWorkbenchItem.cs", "CellWorkbenchItem", "cell_workbench"),
    }
    keys: set[str] = set()
    ids = set()
    for name, (relative_path, class_name, item_id) in classes.items():
        relative = Path(relative_path)
        text = plain_path(platform / relative).read_text(encoding="utf-8")
        if not re.search(r"\binternal sealed class " + class_name + r"\b", text):
            raise ContentValidationError(f"Content class/file mismatch: {relative}")
        declarations = re.findall(r"\bconst\s+(string|int)\s+(\w+)\s*=\s*([^;]+);", text)
        values = {}
        for kind, key, literal in declarations:
            literal = literal.strip()
            if key in values or not re.fullmatch(r'"[a-z0-9_.]+"' if kind == "string" else r"[1-9][0-9]*", literal):
                raise ContentValidationError(f"Unsupported or duplicate constant declaration: {class_name}.{key}")
            values[key] = literal[1:-1] if kind == "string" else int(literal)
        expected = {"ItemId": "digital_storage." + item_id, "SpriteKey": name,
                    "Width": ASSETS[name][1][0] // 16, "Height": ASSETS[name][1][1] // 16,
                    "NameKey": name + "_name", "DescriptionKey": name + "_desc"}
        if values != expected or values["ItemId"] in ids:
            raise ContentValidationError(f"Content declaration contract mismatch: {class_name}")
        ids.add(values["ItemId"])
        keys.update((values["NameKey"], values["DescriptionKey"]))
    io_port_source = plain_path(platform / "Content/IoPortItem.cs").read_text(encoding="utf-8")
    io_port_identity = plain_path(platform / "Content/IoPortItem.Identity.cs").read_text(encoding="utf-8")
    io_port_contract = {
        "ItemId": '"digital_storage.io_port"', "SpriteKey": '"io_port"', "Width": "3", "Height": "3",
        "NameKey": '"io_port_name"', "DescriptionKey": '"io_port_desc"',
    }
    declarations = dict(re.findall(r"\bconst\s+(?:string|int)\s+(\w+)\s*=\s*([^;]+);", io_port_source))
    identity = re.search(r'\bconst\s+string\s+ItemId\s*=\s*("[a-z0-9_.]+");', io_port_identity)
    declarations["ItemId"] = identity.group(1) if identity else None
    if {key: declarations.get(key) for key in io_port_contract} != io_port_contract:
        raise ContentValidationError("Content declaration contract mismatch: IoPortItem")
    keys.update(("io_port_name", "io_port_desc"))
    catalog = plain_path(project / "src/ProbablyStolen.DigitalStorage.Common/Domain/Storage/StorageCellTier.cs").read_text(encoding="utf-8")
    declarations = re.findall(r'new StorageCellTier\("([a-z0-9]+)", (\d+), (\d+), (\d+)\)', catalog)
    expected_tiers = [("1k", "1", "100", "50"), ("4k", "4", "200", "25"),
                      ("16k", "16", "400", "15"), ("64k", "64", "800", "8"),
                      ("256k", "256", "1600", "2")]
    if declarations != expected_tiers:
        raise ContentValidationError("Storage tier catalog contract mismatch")
    tiers = sorted({name.removeprefix("item_storage_cell_") for name in ASSETS
                    if name.startswith("item_storage_cell_")}, key=lambda value: int(value.removesuffix("k")))
    for tier in tiers[1:]:
        for prefix in ("item_storage_cell_", "cell_component_"):
            keys.update((prefix + tier + "_name", prefix + tier + "_desc"))
    keys.update(("component_panel", "storage_pages_panel", "modules_panel", "cells_panel"))
    io_port_keys = (
        "io_port_name", "io_port_desc", "io_direction_cell_to_network", "io_direction_network_to_cell",
        "io_bank_input", "io_bank_output", "io_finish_button", "io_finish_empty", "io_finish_full", "io_finish_done",
        "io_start", "io_pause", "io_cancel", "io_status_idle", "io_status_working", "io_status_paused", "io_status_completed",
        "io_status_no_eligible_content", "io_status_no_controller", "io_status_controller_conflict",
        "io_status_unpowered", "io_status_operation_fee_unavailable", "io_status_waiting_for_capacity",
        "io_status_storage_unavailable", "io_status_unsupported_energy_source", "io_status_output_full",
        "io_status_faulted", "io_status_settings_unavailable", "io_progress",
        "io_tooltip_rate", "io_tooltip_fee", "io_tooltip_state", "config_section_io_port",
        "network_power_io_port_name", "network_power_io_port_desc", "io_port_rate_name", "io_port_rate_desc",
        "io_port_fee_name", "io_port_fee_desc",
    )
    keys.update(io_port_keys)
    keys.update((
        "module_equal_distribution_desc",
        "module_equal_distribution_name",
        "module_stack_display_desc",
        "module_stack_display_name",
        "module_quality_common",
        "module_quality_epic",
        "module_quality_rare",
        "module_type_limit_desc",
        "module_type_limit_effect",
        "module_type_limit_invalid",
        "module_type_limit_name",
        "module_refusal_notinworkbench",
        "module_refusal_limitreached",
        "module_refusal_typesexceedlimit",
        "module_refusal_bytesexceedcapacity",
        "module_refusal_cellunavailable",
        "module_refusal_housingonly",
        "module_refusal_restoring",
        "partition_add_condition",
        "partition_all_conditions",
        "partition_back",
        "partition_not_saved",
        "partition_choose",
        "partition_conflict",
        "partition_duplicate_found",
        "partition_empty_list_note",
        "partition_failed",
        "partition_full",
        "partition_invalid_draft",
        "partition_issue_duplicateruleid",
        "partition_issue_invalidslot",
        "partition_issue_invalidvalue",
        "partition_issue_missingvalue",
        "partition_issue_noconditions",
        "partition_issue_unknownfield",
        "partition_issue_unknownmode",
        "partition_issue_unsupportedoperator",
        "partition_mode_blacklist",
        "partition_mode_button",
        "partition_mode_whitelist",
        "partition_op_contains",
        "partition_op_eq",
        "partition_op_ge",
        "partition_op_gt",
        "partition_op_is",
        "partition_op_isnot",
        "partition_op_le",
        "partition_op_lt",
        "partition_op_ne",
        "partition_op_no",
        "partition_op_notcontains",
        "partition_op_range",
        "partition_op_yes",
        "partition_rule_name",
        "partition_sample_attributes",
        "partition_sample_unsupported",
        "partition_search",
        "partition_status",
        "partition_summary_and",
        "partition_summary_contains",
        "partition_summary_empty",
        "partition_summary_eq",
        "partition_summary_ge",
        "partition_summary_gt",
        "partition_summary_is",
        "partition_summary_isnot",
        "partition_summary_label",
        "partition_summary_le",
        "partition_summary_lt",
        "partition_summary_ne",
        "partition_summary_no",
        "partition_summary_notcontains",
        "partition_summary_range",
        "partition_summary_unknown",
        "partition_summary_yes",
        "partition_title",
        "partition_unknown_value",
        "partition_repaired",
        "partition_upper_placeholder",
        "partition_value_legality_critical",
        "partition_value_legality_high",
        "partition_value_legality_low",
        "partition_value_legality_mid",
        "partition_value_legality_none",
        "partition_value_placeholder",
        "partition_warning_covered",
        "partition_warning_nevermatches",
        "workbench_actions_panel",
        "workbench_cell_panel",
        "workbench_configure",
        "workbench_reject_not_cell",
        "workbench_reject_not_owned",
        "workbench_reject_migrate",
        "workbench_reject_schema",
        "workbench_reject_unavailable",
        "workbench_status_empty",
    ))
    keys.update((
        "network_tooltip_state", "network_tooltip_drive_slots",
        "network_tooltip_device_power", "network_tooltip_total_power",
        "network_status_online", "network_status_unpowered",
        "network_status_no_controller", "network_status_controller_conflict",
        "network_power_controller_name", "network_power_controller_desc",
        "network_power_drive_name", "network_power_drive_desc",
        "network_power_terminal_name", "network_power_terminal_desc",
        "terminal_storage_title", "terminal_storage_search_placeholder", "terminal_storage_loading",
        "terminal_storage_drag_hint", "terminal_storage_drag_pending", "terminal_storage_drag_refused",
        "terminal_drag_reason_not_owned", "terminal_drag_reason_whole_stack", "terminal_drag_reason_unsupported", "terminal_drag_reason_offline", "terminal_drag_reason_store_disabled", "terminal_drag_reason_no_cell", "terminal_drag_reason_type_limit", "terminal_drag_reason_too_large", "terminal_drag_reason_full", "terminal_drag_reason_capacity", "terminal_drag_reason_quota", "terminal_drag_reason_read_only", "terminal_drag_reason_partition_unknown", "terminal_drag_reason_partition_invalid", "terminal_storage_whole_stack_only", "terminal_diagnostics_name", "terminal_diagnostics_desc",
        "debug_item_identity_log_name", "debug_item_identity_log_desc",
        "config_section_logs", "log_category_storage", "log_category_transfer", "log_category_comp", "log_category_temp", "log_category_enabled",
        "log_level_error", "log_level_warning", "log_level_info", "log_level_verbose",
        "terminal_storage_offline", "terminal_storage_empty",
        "terminal_storage_invalid_amount", "terminal_storage_no_items", "terminal_storage_insufficient_power",
        "terminal_storage_target_rejected", "terminal_storage_unsupported_item",
        "terminal_storage_commit_failed", "terminal_storage_extracted",
        "terminal_storage_inserted", "terminal_storage_capacity_rejected", "storage_cell_not_empty",
        "storage_cell_bytes_used", "storage_cell_types_used",
        "terminal_storage_unavailable", "terminal_storage_recovery_pending",
        "storage_restore_failed_title", "storage_restore_failed_body", "storage_restore_unavailable",
        "terminal_storage_inserted_partial", "terminal_storage_reject_no_cell", "terminal_storage_reject_type_limit",
        "terminal_storage_reject_too_large", "terminal_storage_reject_no_space", "terminal_storage_reject_not_owned",
        "terminal_storage_unsupported_power", "terminal_storage_unique_split",
        "cell_storage_partition_invalid", "cell_storage_unavailable", "cell_storage_bytes", "cell_storage_types",
        "cell_storage_partition_include", "cell_storage_partition_exclude", "cell_storage_schema",
        "cell_storage_type_modules", "cell_storage_equal_distribution",
        "cell_storage_expansion", "cell_storage_migration_refused", "cell_storage_page_protected",
        "cell_storage_migration_failed", "cell_storage_compaction_blocked", "cell_storage_locked",
        "cell_storage_schema_too_new", "cell_storage_rollback_failed", "cell_storage_type_limit_invalid",
        "terminal_storage_reject_capacity", "terminal_storage_reject_quota", "terminal_storage_reject_read_only",
        "terminal_storage_reject_partition_unknown", "terminal_storage_reject_partition_invalid",
        "partition_field_item", "partition_field_type", "partition_field_legality", "partition_field_stolen",
        "partition_field_heat", "partition_field_feature", "partition_unit_percent",
        "terminal_button_modules_tip", "terminal_button_sort_tip", "terminal_sort_name", "terminal_sort_amount",
        "terminal_button_direction_tip", "terminal_direction_asc", "terminal_direction_desc", "terminal_button_size_tip",
        "terminal_size_small", "terminal_size_medium", "terminal_size_tall", "terminal_size_full",
        "terminal_dock_title", "terminal_dock_actions_title", "terminal_button_current_tip",
        "terminal_button_functions_tip", "terminal_view_storage", "terminal_view_diagnostics",
        "terminal_button_diagnostics_rail_tip",
        "terminal_button_diagnostics_picker_tip", "terminal_back",
        "terminal_modules_unavailable", "terminal_page_unavailable", "terminal_diagnostics_view_text",
        "terminal_diagnostics_screen_text", "terminal_test_view_name", "terminal_test_view_desc",
        "workbench_info_mode", "workbench_info_rules", "workbench_info_types", "workbench_info_count",
        "partition_condition_count", "partition_remove_condition_tip", "partition_search_value",
        "partition_clear",
        "partition_hint_title", "partition_hint_left", "partition_hint_right", "partition_hint_middle",
        "config_section_terminal_view", "terminal_view_sort_name", "terminal_view_sort_desc",
        "terminal_view_direction_name", "terminal_view_direction_desc", "terminal_view_size_name", "terminal_view_size_desc", "partition_view_size_name",
        "partition_view_size_desc", "terminal_keep_open_name", "terminal_keep_open_desc",
        "terminal_hint_title", "terminal_hint_left", "terminal_hint_right", "terminal_hint_shift",
        "terminal_hint_drag", "terminal_hint_drag_amount",
        "config_section_power", "config_section_debug",
        "config_section_terminal_search",
        "terminal_search_keyboard_name", "terminal_search_keyboard_desc",
        "terminal_search_pinyin_mode_name", "terminal_search_pinyin_mode_desc",
        "terminal_search_pinyin_mode_option_auto", "terminal_search_pinyin_mode_option_on",
        "terminal_search_pinyin_mode_option_off",
        "terminal_search_zh2z_name", "terminal_search_zh2z_desc",
        "terminal_search_sh2s_name", "terminal_search_sh2s_desc",
        "terminal_search_ch2c_name", "terminal_search_ch2c_desc",
        "terminal_search_ang2an_name", "terminal_search_ang2an_desc",
        "terminal_search_ing2in_name", "terminal_search_ing2in_desc",
        "terminal_search_eng2en_name", "terminal_search_eng2en_desc",
        "terminal_search_u2v_name", "terminal_search_u2v_desc",
        "terminal_search_tags_name", "terminal_search_tags_desc",
        "terminal_search_types_name", "terminal_search_types_desc",
        "terminal_search_descriptions_name", "terminal_search_descriptions_desc",
        "terminal_search_identifiers_name", "terminal_search_identifiers_desc",
        "terminal_search_highlighting_name", "terminal_search_highlighting_desc",
        "debug_show_cell_pages_name", "debug_show_cell_pages_desc",
        "priority_configure", "priority_title", "priority_insert_rule", "priority_extract_rule",
        "network_tab_digital_storage", "network_card_digital_storage",
        "network_digital_storage_starter_title", "network_digital_storage_starter_desc",
        "network_digital_storage_advanced_title", "network_digital_storage_advanced_desc",
        "network_digital_storage_expert_title", "network_digital_storage_expert_desc",
        "data_scientist_name", "data_scientist_greet_1", "data_scientist_greet_2",
        "integration_customerstocking_section", "integration_state_incompatible", "integration_state_not_running",
        "integration_state_failed", "integration_state_failed_label", "integration_state_not_running_label",
        "integration_itemdetails_section", "integration_itemdetails_label", "integration_itemdetails_tooltip", "tooltip_scroll_hint",
        "integration_state_unverified", "integration_state_active", "integration_state_active_unverified", "integration_state_disabled",
        "integration_proficiency_section", "integration_proficiency_label", "integration_proficiency_tooltip",
        "integration_client_demand_section", "integration_client_demand_label", "integration_client_demand_tooltip",
        "integration_client_demand_normal", "integration_client_demand_normal_desc",
        "integration_client_demand_stolen", "integration_client_demand_stolen_desc",
        "integration_client_demand_contraband", "integration_client_demand_contraband_desc",
        "integration_customerstocking_label", "integration_customerstocking_tooltip",
        "integration_dsstocking_section", "integration_dsstocking_label", "integration_dsstocking_tooltip",
        "integration_dsstocking_stacks", "integration_dsstocking_stacks_desc",
        "terminal_button_stacking_tip",
        "terminal_keep_open_tip", "terminal_keep_open_enabled", "terminal_keep_open_disabled",
        "terminal_extract_destination_name", "terminal_extract_destination_desc", "terminal_extract_destination_tip",
        "terminal_extract_warehouse", "terminal_extract_counter",
        "terminal_extract_warehouse_first", "terminal_extract_counter_first",
        "terminal_slot_size_name", "terminal_slot_size_desc", "terminal_icon_margin_name", "terminal_icon_margin_desc",
        "terminal_dock_button_size_name", "terminal_dock_button_size_desc", "display_settings_dock_button_size",
        "size_tier_default", "size_tier_small", "size_tier_medium", "size_tier_large",
        "icon_margin_compact", "icon_margin_standard", "icon_margin_loose",
        "terminal_button_display_settings_tip", "display_settings_slot_size", "display_settings_icon_margin",
        "display_settings_reduced", "display_settings_note",
        "terminal_tooltip_total_units",
        "stacking_marker_color", "stacking_marker_placeholder", "stacking_marker_color_tip", "stacking_marker_invalid",
        "stacking_info_title", "stacking_info_kinds", "stacking_info_rule",
        "stacking_info_selection", "stacking_info_selection_unavailable", "stacking_info_selection_unknown",
        "stacking_info_difference", "stacking_info_tag_unavailable", "stacking_info_selection_unknown_parts",
        "stacking_info_identity_unreadable", "stacking_info_identity_unreadable_reason",
        "stacking_info_identity_item_types", "stacking_info_identity_name", "stacking_info_identity_short_description",
        "stacking_info_identity_flavor_text", "stacking_info_identity_sprite", "stacking_info_identity_examine_window",
        "stacking_info_identity_more",
        "stacking_conditions", "stacking_rule_count",
        "stacking_config_unreadable",
        "stacking_saved_immediately",
        "stacking_save_failed",
        "stacking_remove_rule_tip",
        "stacking_add_rule",
        "stacking_clear_rules",
        "stacking_search", "stacking_rule_search", "stacking_rule_search_empty",
        "stacking_field_absent",
        "stacking_mode_match",
        "stacking_mode_ignore",
        "stacking_note_match_alone",
        "stacking_note_derived_alone",
        "stacking_note_value_still_compared",
        "stacking_note_container_first_only",
        "stacking_field_appraised_value",
        "stacking_field_appraised_value_desc",
        "stacking_field_price_composition",
        "stacking_field_price_composition_desc",
        "stacking_field_container_content",
        "stacking_field_container_content_desc",
        "stacking_field_feature",
        "stacking_field_feature_desc",
        "stacking_quick_add_tip", "stacking_quick_append_tip", "stacking_quick_edit_tip",
        "stacking_quick_delete_tip", "stacking_quick_merged", "stacking_quick_still_separate",
        "stacking_quick_rule_off", "stacking_quick_nothing", "stacking_quick_deleted", "stacking_quick_delete_none",
        "stacking_draft_hint",
        "stacking_rules_empty", "stacking_apply", "stacking_item_search", "stacking_item_rules",
        "stacking_item_rule_count", "stacking_item_search_empty", "stacking_items_empty", "stacking_item_absent",
        "stacking_item_on", "stacking_item_off", "stacking_item_switch_tip", "stacking_item_edit_tip",
        "stacking_item_delete_tip", "stacking_all_on", "stacking_all_off", "stacking_all_delete",
        "stacking_item_add", "stacking_item_has_rule", "stacking_advanced", "stacking_advanced_tip",
        "stacking_quick_hidden", "stacking_info_hidden_feature", "stacking_field_hidden",
        "stacking_quick_global_add_tip", "stacking_quick_global_delete_tip", "stacking_quick_global_overridden",
        "stacking_quick_global_deleted", "stacking_quick_global_delete_none", "stacking_global_rules",
        "stacking_globals_empty", "stacking_global_add", "stacking_global_switch_tip", "stacking_global_delete_tip",
        "terminal_search_tip_title", "terminal_search_tip_tags", "terminal_search_tip_types",
        "terminal_search_tip_descriptions", "terminal_search_tip_identifier", "terminal_search_tip_negate",
        "terminal_search_tip_or", "terminal_search_tip_quote", "terminal_search_tip_pinyin",
        "terminal_search_tip_clear", "stacking_item_search_tip", "stacking_rule_search_tip",
        "stacking_info_item_rules", "stacking_info_global_rules", "stacking_info_item_off",
        "stacking_info_difference_overridden", "stacking_rule_overrides_global", "stacking_rule_same_as_global",
        "stacking_item_overrides", "stacking_global_overridden_by",
    ))
    text_resources = [entry["path"] for entry in _resource_contract()["resources"]
                      if Path(entry["path"]).suffix == ".txt"]
    dictionaries = [name for name in text_resources if Path(name).parent.name == "Search"]
    licenses = [name for name in text_resources if Path(name).parent.name == "Licenses"]
    if len(dictionaries) != 1 or not licenses:
        raise ContentValidationError("The resource declaration must contain one search dictionary and at least one license.")
    for resource in text_resources:
        path = plain_path(output / resource)
        if not path.is_file() or path.stat().st_size == 0:
            raise ContentValidationError(f"Required text resource is missing or empty: {resource}")
    dictionary = (output / dictionaries[0]).read_text(encoding="utf-8")
    if "材: cai2" not in dictionary or "重: zhong4, chong2" not in dictionary:
        raise ContentValidationError("Pinyin dictionary content is invalid")
    languages = [read_localization(output / "Localization" / (language + ".csv")) for language in ("en", "zh")]
    for language in languages:
        if set(language) != keys:
            raise ContentValidationError(f"Localization key set must contain the required content keys: {sorted(keys ^ set(language))}")
    for key in keys:
        if format_parameters(languages[0][key]) != format_parameters(languages[1][key]):
            raise ContentValidationError(f"Localization format parameters differ: {key}")
    io_port_parameters = dict.fromkeys(io_port_keys, ())
    io_port_parameters.update({
        "io_progress": (0,), "io_finish_button": (0,), "io_tooltip_rate": (0,), "io_tooltip_fee": (0,),
        "io_tooltip_state": (0, 1), "workbench_info_count": (0, 1),
        "modules_panel": (), "terminal_dock_title": (), "partition_unknown_value": (),
        "partition_mode_whitelist": (), "partition_mode_blacklist": (),
    })
    for key, parameters in io_port_parameters.items():
        if tuple(format_parameters(languages[0][key])) != parameters:
            raise ContentValidationError(f"Localization format parameters do not match the IO Port caller: {key}")


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--write", action="store_true")
    mode.add_argument("--check", action="store_true")
    parser.add_argument("--source-dir", type=Path, help="art source folder; required with --write")
    parser.add_argument("--output-dir", type=Path, default=REPOSITORY / "packaging" / "resources")
    parser.add_argument("--items", nargs="+", help="declared image identifiers or runtime PNG paths")
    args = parser.parse_args(argv)
    if args.write and args.source_dir is None:
        parser.error("--write requires --source-dir pointing at the local art source folder")
    try:
        selected = None if args.items is None else tuple(_as_name(item) for item in args.items)
        if args.write:
            prepare_assets(args.source_dir, args.output_dir, selected)
        check_assets(args.output_dir, selected)
        if args.check and selected is None:
            check_resource_tree(args.output_dir)
            check_content(REPOSITORY, args.output_dir)
    except (ContentValidationError, OSError) as error:
        print(f"Content error: {error}", file=sys.stderr)
        return 1
    print(f"[PASS] {len(ASSETS) if selected is None else len(selected)} runtime PNGs validated.")
    return 0


