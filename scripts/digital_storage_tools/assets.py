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
# Source dimensions are deliberate: the 47px drive gains transparent right/bottom edges.
ASSETS = {
    "terminal": ((48, 48), (48, 48)),
    "drive": ((47, 47), (48, 48)),
    "controller": ((32, 48), (32, 48)),
    "item_storage_cell_1k": ((16, 32), (16, 32)),
    "item_cell_housing": ((16, 32), (16, 32)),
    "cell_component_1k": ((16, 32), (16, 32)),
}
TIERS = ("1k", "4k", "16k", "64k", "256k")
for tier in TIERS[1:]:
    for prefix in ("item_storage_cell_", "cell_component_"):
        ASSETS[prefix + tier] = ((16, 32), (16, 32))
ASSETS["workbench"] = ((48, 48), (48, 48))
# Three workbench corner cells are transparent, so their centres are exempt from the opacity check.
OPEN_CENTER_ASSETS = frozenset({"workbench"})
for module in ("module_type_expansion_common", "module_type_expansion_rare", "module_type_expansion_epic",
               "module_equal_distribution"):
    ASSETS[module] = ((32, 32), (32, 32))


class ContentValidationError(ValueError):
    """A required content input or runtime resource is invalid."""


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
    path = plain_path(source / (name + ".png"))
    try:
        data = path.read_bytes()
        with Image.open(io.BytesIO(data)) as opened:
            if opened.format != "PNG" or getattr(opened, "n_frames", 1) != 1:
                raise ContentValidationError(f"Expected a single PNG: {path}")
            if opened.size != ASSETS[name][0]:
                raise ContentValidationError(f"Unexpected source dimensions for {name}: {opened.size}")
            rgba = opened.convert("RGBA")
    except (OSError, ValueError) as error:
        if isinstance(error, ContentValidationError):
            raise
        raise ContentValidationError(f"Could not read source {name}: {error}") from error
    return data, rgba


def expected_png(source: Path, name: str) -> bytes:
    data, rgba = source_png(source, name)
    if ASSETS[name][0] == ASSETS[name][1]:
        return data
    padded = image_api().new("RGBA", ASSETS[name][1], (0, 0, 0, 0))
    padded.paste(rgba, (0, 0))
    stream = io.BytesIO()
    padded.save(stream, format="PNG")
    return stream.getvalue()


def runtime_png(output: Path, name: str):
    path = plain_path(output / "Textures" / "Items" / (name + ".png"))
    try:
        data = path.read_bytes()
        with image_api().open(io.BytesIO(data)) as opened:
            if opened.format != "PNG" or getattr(opened, "n_frames", 1) != 1:
                raise ContentValidationError(f"Expected a single runtime PNG: {name}")
            if opened.size != ASSETS[name][1]:
                raise ContentValidationError(f"Runtime dimensions mismatch for {name}: {opened.size}")
            return data, opened.convert("RGBA")
    except (OSError, ValueError) as error:
        if isinstance(error, ContentValidationError):
            raise
        raise ContentValidationError(f"Could not read runtime {name}: {error}") from error


def check_image(output: Path, name: str) -> None:
    """Validate a committed runtime PNG on its own; art source files are not part of the repository."""
    _, actual = runtime_png(output, name)
    width, height = ASSETS[name][0]
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
    if ASSETS[name][0] == ASSETS[name][1] and actual_data != source_data:
        raise ContentValidationError(f"Unchanged asset must retain original bytes: {name}")


def prepare_assets(source: Path, output: Path, names: Sequence[str] = tuple(ASSETS)) -> None:
    output = validate_write_root(source, output)
    if not names or len(set(names)) != len(names) or any(name not in ASSETS for name in names):
        raise ContentValidationError("Choose unique known asset names.")
    # Preflight all inputs and destination paths before writing any image.
    prepared = {name: expected_png(source, name) for name in names}
    paths = {name: plain_path(output / "Textures" / "Items" / (name + ".png")) for name in names}
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


def check_assets(output: Path, names: Sequence[str] = tuple(ASSETS)) -> None:
    for name in names:
        check_image(output, name)


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
    parameters = sorted(int(index) for index in re.findall(r"\{(\d+)\}", remaining))
    if "{" in re.sub(r"\{\d+\}", "", remaining) or "}" in re.sub(r"\{\d+\}", "", remaining):
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
    catalog = plain_path(project / "src/ProbablyStolen.DigitalStorage.Common/Domain/Storage/StorageCellTier.cs").read_text(encoding="utf-8")
    declarations = re.findall(r'new StorageCellTier\("([a-z0-9]+)", (\d+), (\d+), (\d+)\)', catalog)
    expected_tiers = [("1k", "1", "250", "50"), ("4k", "4", "750", "25"),
                      ("16k", "16", "2250", "15"), ("64k", "64", "6750", "8"),
                      ("256k", "256", "20250", "2")]
    if declarations != expected_tiers:
        raise ContentValidationError("Storage tier catalog contract mismatch")
    for tier in TIERS[1:]:
        for prefix in ("item_storage_cell_", "cell_component_"):
            keys.update((prefix + tier + "_name", prefix + tier + "_desc"))
    keys.update(("component_panel", "storage_pages_panel", "modules_panel", "cells_panel"))
    keys.update((
        "module_equal_distribution_desc",
        "module_equal_distribution_name",
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
        "partition_clear",
        "partition_conflict",
        "partition_duplicate",
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
        "partition_open",
        "partition_remove",
        "partition_remove_condition",
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
        "partition_undo",
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
        "network_tooltip_state", "network_tooltip_drive_slots", "network_module_stats_scope",
        "network_status_online", "network_status_unpowered",
        "network_status_no_controller", "network_status_controller_conflict",
        "network_power_controller_name", "network_power_controller_desc",
        "network_power_drive_name", "network_power_drive_desc",
        "network_power_terminal_name", "network_power_terminal_desc",
        "terminal_storage_title", "terminal_storage_search_placeholder", "terminal_storage_loading",
        "terminal_storage_drag_hint", "terminal_storage_drag_rejected", "terminal_storage_whole_stack_only", "terminal_diagnostics_name", "terminal_diagnostics_desc",
        "terminal_storage_offline", "terminal_storage_empty",
        "terminal_storage_invalid_amount", "terminal_storage_no_items", "terminal_storage_insufficient_power",
        "terminal_storage_target_rejected", "terminal_storage_unsupported_item",
        "terminal_storage_commit_failed", "terminal_storage_extracted",
        "terminal_storage_inserted", "terminal_storage_capacity_rejected", "storage_cell_not_empty",
        "storage_cell_bytes_used", "storage_cell_types_used",
        "terminal_storage_unavailable", "terminal_storage_recovery_pending",
        "storage_restore_failed_title", "storage_restore_failed_body", "storage_restore_unavailable",
        "terminal_storage_inserted_partial", "terminal_storage_reject_no_cell", "terminal_storage_reject_type_limit",
        "terminal_storage_reject_too_large", "terminal_storage_reject_no_space",
        "terminal_storage_unsupported_power", "terminal_storage_unique_split",
        "cell_storage_partition_invalid", "cell_storage_unavailable", "cell_storage_bytes", "cell_storage_types",
        "cell_storage_partition_include", "cell_storage_partition_exclude", "cell_storage_schema",
        "cell_storage_type_modules", "cell_storage_equal_distribution",
        "cell_storage_expansion", "cell_storage_migration_refused",
        "cell_storage_migration_failed", "cell_storage_compaction_blocked", "cell_storage_locked",
        "cell_storage_schema_too_new", "cell_storage_rollback_failed", "cell_storage_type_limit_invalid",
        "terminal_storage_reject_capacity", "terminal_storage_reject_quota", "terminal_storage_reject_read_only",
        "terminal_storage_reject_partition_unknown", "terminal_storage_reject_partition_invalid",
        "partition_field_item", "partition_field_type", "partition_field_legality", "partition_field_stolen",
        "partition_field_heat", "partition_field_feature", "partition_unit_percent",
        "terminal_button_modules", "terminal_button_modules_tip", "terminal_button_sort_name",
        "terminal_button_sort_amount", "terminal_button_sort_tip", "terminal_sort_name", "terminal_sort_amount",
        "terminal_button_direction_asc", "terminal_button_direction_desc", "terminal_button_direction_tip",
        "terminal_direction_asc", "terminal_direction_desc", "terminal_button_size_small", "terminal_button_size_medium",
        "terminal_button_size_tall", "terminal_button_size_full", "terminal_button_size_tip",
        "terminal_size_small", "terminal_size_medium", "terminal_size_tall", "terminal_size_full",
        "workbench_info_mode", "workbench_info_rules", "workbench_info_types", "workbench_info_count",
        "partition_condition_count", "partition_remove_condition_tip", "partition_search_value",
        "partition_tool_whitelist", "partition_tool_blacklist", "partition_tool_open", "partition_tool_duplicate",
        "partition_tool_remove", "partition_tool_clear", "partition_tool_undo",
        "config_section_terminal_view", "terminal_view_sort_name", "terminal_view_sort_desc",
        "terminal_view_direction_name", "terminal_view_direction_desc", "terminal_view_size_name", "terminal_view_size_desc", "partition_view_size_name",
        "partition_view_size_desc",
        "terminal_hint_title", "terminal_hint_left", "terminal_hint_right", "terminal_hint_shift",
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
        "priority_configure", "priority_title", "priority_desc", "priority_invalid",
        "config_section_vendors", "upper_engineer_daily_chance_name", "upper_engineer_daily_chance_desc",
        "upper_engineer_name", "upper_engineer_greet_1", "upper_engineer_greet_2", "upper_engineer_wrong_item",
        "upper_engineer_right_item", "upper_engineer_repeat", "upper_engineer_accept",
    ))
    if (platform / "Diagnostics" / "ItemCommands.cs").exists():
        raise ContentValidationError("Removed custom command source must not be present.")
    for resource in ("Search/pinyin-dictionary.txt", "Licenses/PininSharp-LICENSE.txt",
                     "Licenses/pinyin-data-LICENSE.txt", "Licenses/Rime-Terra-Pinyin-LICENSE.txt"):
        path = plain_path(output / resource)
        if not path.is_file() or path.stat().st_size == 0:
            raise ContentValidationError(f"Required search resource is missing or empty: {resource}")
    dictionary = (output / "Search/pinyin-dictionary.txt").read_text(encoding="utf-8")
    if "材: cai2" not in dictionary or "重: zhong4, chong2" not in dictionary:
        raise ContentValidationError("Pinyin dictionary content is invalid")
    languages = [read_localization(output / "Localization" / (language + ".csv")) for language in ("en", "zh")]
    for language in languages:
        if set(language) != keys:
            raise ContentValidationError(f"Localization key set must contain the required content keys: {sorted(keys ^ set(language))}")
    for key in keys:
        if format_parameters(languages[0][key]) != format_parameters(languages[1][key]):
            raise ContentValidationError(f"Localization format parameters differ: {key}")


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--write", action="store_true")
    mode.add_argument("--check", action="store_true")
    parser.add_argument("--source-dir", type=Path, help="art source folder; required with --write")
    parser.add_argument("--output-dir", type=Path, default=REPOSITORY / "packaging" / "resources")
    parser.add_argument("--items", choices=tuple(ASSETS), nargs="+", default=tuple(ASSETS))
    args = parser.parse_args(argv)
    if args.write and args.source_dir is None:
        parser.error("--write requires --source-dir pointing at the local art source folder")
    try:
        if args.write:
            prepare_assets(args.source_dir, args.output_dir, args.items)
        check_assets(args.output_dir, args.items)
        if args.check and tuple(args.items) == tuple(ASSETS):
            check_content(REPOSITORY, args.output_dir)
    except (ContentValidationError, OSError) as error:
        print(f"Content error: {error}", file=sys.stderr)
        return 1
    print(f"[PASS] {len(args.items)} runtime PNGs validated.")
    return 0


