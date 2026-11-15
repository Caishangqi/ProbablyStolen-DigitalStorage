"""Validate module-bay JSON with isolated, non-save fixtures."""

from __future__ import annotations

import json
import shutil
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "scripts"))

from digital_storage_tools.assets import plain_path  # noqa: E402
from digital_storage_tools.module_bay_data import (  # noqa: E402
    ModuleBayDataError,
    MAXIMUM_BYTES,
    _load_json,
    validate_registry_data,
)


class ModuleBayDataValidationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory(prefix="module-bay-data-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()
        self.data = self.root / "Data"
        self.data.mkdir()

    def write_fixture(self, definition: dict, bindings: dict | None = None, index: dict | None = None) -> None:
        (self.data / "bay.json").write_text(json.dumps(definition), encoding="utf-8")
        if bindings is not None:
            (self.data / "bindings.json").write_text(json.dumps(bindings), encoding="utf-8")
        index = index or {
            "format_version": 1,
            "profiles": [{"path": "Data/bay.json"}],
            "bindings": [{"path": "Data/bindings.json"}],
        }
        (self.root / "Data/registries.json").write_text(json.dumps(index), encoding="utf-8")

    def valid_definition(self) -> dict:
        return {
            "format_version": 1,
            "id": "fixture:bay",
            "stages": [
                {"level": 0, "key": "base", "layout": {"type": "rectangle", "width": 1, "height": 1}},
            ],
        }

    def valid_bindings(self) -> dict:
        return {
            "format_version": 1,
            "bindings": [{
                "native_item_id": "fixture.machine",
                "definition_id": "fixture:bay",
                "provider_id": "digital_storage:native_standard",
                "max_stage": 0,
            }],
        }

    def test_published_data_passes_and_only_indexed_files_are_read(self):
        resources = ROOT / "packaging/resources"
        validate_registry_data(resources, plain_path)
        self.assertTrue((resources / "Data/module_bays/terminal.json").is_file())

    def test_missing_data_directory_is_rejected(self):
        with self.assertRaisesRegex(ModuleBayDataError, "Could not read JSON resource"):
            validate_registry_data(self.root, plain_path)

    def test_layout_dimension_stage_and_cumulative_limits_are_rejected(self):
        cases = [
            {"type": "rectangle", "width": 2_147_483_647, "height": 1},
            {"type": "rectangle", "width": 257, "height": 1},
        ]
        for layout in cases:
            with self.subTest(layout=layout):
                definition = self.valid_definition()
                definition["stages"][0]["layout"] = layout
                self.write_fixture(definition, self.valid_bindings())
                with self.assertRaises(ModuleBayDataError):
                    validate_registry_data(self.root, plain_path)
        definition = self.valid_definition()
        definition["stages"] = [
            {"level": level, "key": f"stage_{level}", "layout": {"type": "rectangle", "width": 256, "height": 256}}
            for level in range(65)
        ]
        self.write_fixture(definition, self.valid_bindings())
        with self.assertRaisesRegex(ModuleBayDataError, "maximum count"):
            validate_registry_data(self.root, plain_path)
        definition["stages"] = [
            {"level": 0, "key": "base", "layout": {"type": "rectangle", "width": 256, "height": 256}},
            {"level": 1, "key": "next", "layout": {"type": "rectangle", "width": 256, "height": 256}},
            {"level": 2, "key": "last", "layout": {"type": "rectangle", "width": 256, "height": 256}},
            {"level": 3, "key": "final", "layout": {"type": "rectangle", "width": 256, "height": 256}},
            {"level": 4, "key": "overflow", "layout": {"type": "rectangle", "width": 256, "height": 256}},
        ]
        self.write_fixture(definition, self.valid_bindings())
        with self.assertRaisesRegex(ModuleBayDataError, "maximum total"):
            validate_registry_data(self.root, plain_path)

    def test_empty_index_is_rejected(self):
        (self.data / "registries.json").write_text(
            json.dumps({"format_version": 1, "profiles": [], "bindings": []}), encoding="utf-8"
        )
        with self.assertRaisesRegex(ModuleBayDataError, "missing builtin definitions"):
            validate_registry_data(self.root, plain_path)

    def test_missing_indexed_file_is_rejected(self):
        self.write_fixture(self.valid_definition(), self.valid_bindings())
        (self.data / "bindings.json").unlink()
        with self.assertRaisesRegex(ModuleBayDataError, "Could not read JSON resource"):
            validate_registry_data(self.root, plain_path)

    def test_bad_fields_and_duplicate_properties_are_rejected(self):
        definition = self.valid_definition()
        definition["unexpected"] = True
        self.write_fixture(definition, self.valid_bindings())
        with self.assertRaisesRegex(ModuleBayDataError, "unknown properties"):
            validate_registry_data(self.root, plain_path)
        (self.data / "bay.json").write_text(
            '{"format_version":1,"id":"fixture:bay","id":"fixture:other","stages":[]}', encoding="utf-8"
        )
        with self.assertRaisesRegex(ModuleBayDataError, "duplicate JSON property"):
            validate_registry_data(self.root, plain_path)

    def test_shrinking_stage_is_rejected(self):
        definition = self.valid_definition()
        definition["stages"].append({
            "level": 1,
            "key": "next",
            "layout": {"type": "pattern", "rows": ["1"]},
        })
        self.write_fixture(definition, self.valid_bindings())
        with self.assertRaisesRegex(ModuleBayDataError, "shrinks"):
            validate_registry_data(self.root, plain_path)

    def test_wrong_binding_and_duplicate_index_path_are_rejected(self):
        bindings = self.valid_bindings()
        bindings["bindings"][0]["provider_id"] = "fixture:unknown"
        self.write_fixture(self.valid_definition(), bindings)
        with self.assertRaisesRegex(ModuleBayDataError, "unknown builtin provider"):
            validate_registry_data(self.root, plain_path)
        duplicate_index = {
            "format_version": 1,
            "profiles": [{"path": "Data/bay.json"}],
            "bindings": [{"path": "Data/bay.json"}],
        }
        self.write_fixture(self.valid_definition(), self.valid_bindings(), duplicate_index)
        with self.assertRaisesRegex(ModuleBayDataError, "duplicate path"):
            validate_registry_data(self.root, plain_path)

    def test_unknown_binding_property_is_rejected(self):
        bindings = self.valid_bindings()
        bindings["bindings"][0]["extra"] = True
        self.write_fixture(self.valid_definition(), bindings)
        with self.assertRaisesRegex(ModuleBayDataError, "unknown properties"):
            validate_registry_data(self.root, plain_path)

    def test_native_item_id_rejects_c1_control_characters(self):
        bindings = self.valid_bindings()
        bindings["bindings"][0]["native_item_id"] = "fixture\x85machine"
        self.write_fixture(self.valid_definition(), bindings)
        with self.assertRaisesRegex(ModuleBayDataError, "native item id"):
            validate_registry_data(self.root, plain_path)

    def test_wildcard_binding_is_rejected(self):
        bindings = self.valid_bindings()
        bindings["bindings"][0]["native_item_id"] = "*"
        self.write_fixture(self.valid_definition(), bindings)
        with self.assertRaisesRegex(ModuleBayDataError, "wildcard"):
            validate_registry_data(self.root, plain_path)

    def test_reparse_point_is_rejected_before_reading(self):
        self.write_fixture(self.valid_definition(), self.valid_bindings())
        original = Path.lstat
        def reparse(path, *args, **kwargs):
            status = original(path, *args, **kwargs)
            if path == self.data / "registries.json":
                return SimpleNamespace(st_mode=status.st_mode, st_file_attributes=0x400)
            return status
        with patch("digital_storage_tools.assets.Path.lstat", new=reparse):
            with self.assertRaisesRegex(ModuleBayDataError, "Could not read JSON resource"):
                validate_registry_data(self.root, plain_path)
    def test_sparse_oversized_resource_is_read_with_a_fixed_bound(self):
        path = self.data / "large.json"
        with path.open("wb") as stream:
            stream.truncate(256 * 1024 * 1024)
        original = Path.open
        reads = []
        class Reader:
            def __init__(self, stream): self.stream = stream
            def __enter__(self): self.stream.__enter__(); return self
            def __exit__(self, *args): return self.stream.__exit__(*args)
            def read(self, size): reads.append(size); return self.stream.read(size)
        def open_spy(candidate, *args, **kwargs):
            stream = original(candidate, *args, **kwargs)
            return Reader(stream) if candidate == path else stream
        with patch.object(Path, "open", new=open_spy):
            with self.assertRaisesRegex(ModuleBayDataError, "exceeds the 1 MiB limit"):
                _load_json(path, plain_path)
        self.assertEqual(reads, [MAXIMUM_BYTES + 1])


if __name__ == "__main__":
    unittest.main()
