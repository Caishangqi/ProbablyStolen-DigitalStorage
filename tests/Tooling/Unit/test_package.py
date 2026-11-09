"""Validate the installable Digital Storage package contents and manifest."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import stat
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path, PurePosixPath
from types import SimpleNamespace
from typing import Sequence
from unittest.mock import patch


from digital_storage_tools.package import *
from digital_storage_tools.package import _REPARSE_POINT

class PackageValidationTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.package_directory = Path(self.temporary_directory.name) / "DigitalStorage"
        self.package_directory.mkdir()
        self._write_valid_package()

    def tearDown(self) -> None:
        self.temporary_directory.cleanup()

    def _write_valid_package(self) -> None:
        self._write_layout(CURRENT_FILES, CURRENT_PLATFORM_ASSEMBLY)

    def _clear_layout(self) -> None:
        for path in sorted(self.package_directory.rglob("*"), key=lambda value: len(value.parts), reverse=True):
            if path.is_file():
                path.unlink()
            else:
                path.rmdir()

    def _write_layout(self, names: Sequence[str], platform_assembly: str) -> None:
        """Write a synthetic known layout whose manifest names that layout's Platform assembly."""
        self._clear_layout()
        for name in names:
            if name == "manifest.xml":
                continue
            path = self.package_directory / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(f"synthetic {name}".encode("ascii"))
        (self.package_directory / "manifest.xml").write_bytes(
            manifest_text(platform_assembly).encode("utf-8")
        )

    def _replace_manifest_text(self, old: str, new: str) -> None:
        path = self.package_directory / "manifest.xml"
        path.write_text(path.read_text(encoding="utf-8").replace(old, new), encoding="utf-8")

    def test_valid_package_returns_hash_inventory(self) -> None:
        inventory = validate_package_directory(self.package_directory)
        self.assertEqual([item["name"] for item in inventory], list(CURRENT_FILES))
        self.assertTrue(all(len(str(item["sha256"])) == 64 for item in inventory))

    def test_existing_package_allows_old_version_for_safe_replacement(self) -> None:
        self._replace_manifest_text("<ModVersion>0.1.0</ModVersion>", "<ModVersion>0.0.9</ModVersion>")
        inventory = validate_package_directory(self.package_directory, require_current_metadata=False)
        self.assertEqual(len(inventory), len(CURRENT_FILES))
        with self.assertRaisesRegex(PackageValidationError, "ModVersion"):
            validate_package_directory(self.package_directory)

    def test_known_layouts_require_their_own_manifest_platform_assembly(self) -> None:
        for label, names, platform_assembly in KNOWN_LAYOUTS:
            other_assembly = (
                LEGACY_PLATFORM_ASSEMBLY if platform_assembly == CURRENT_PLATFORM_ASSEMBLY else CURRENT_PLATFORM_ASSEMBLY
            )
            with self.subTest(layout=label, direction="matching"):
                self._write_layout(names, platform_assembly)
                inventory = validate_package_directory(self.package_directory, require_current_metadata=False)
                self.assertEqual([item["name"] for item in inventory], list(names))
            with self.subTest(layout=label, direction="mismatched"):
                self._write_layout(names, other_assembly)
                with self.assertRaisesRegex(PackageValidationError, "load order"):
                    validate_package_directory(self.package_directory, require_current_metadata=False)

    def test_manifest_listing_both_platform_names_is_rejected(self) -> None:
        path = self.package_directory / "manifest.xml"
        text = path.read_text(encoding="utf-8").replace(
            f"<Assembly>{CURRENT_PLATFORM_ASSEMBLY}</Assembly>",
            f"<Assembly>{CURRENT_PLATFORM_ASSEMBLY}</Assembly><Assembly>{LEGACY_PLATFORM_ASSEMBLY}</Assembly>",
        )
        path.write_text(text, encoding="utf-8")
        with self.assertRaisesRegex(PackageValidationError, "load order"):
            validate_package_directory(self.package_directory)

    def test_renamed_platform_layout_is_accepted_only_for_existing_packages(self) -> None:
        self._write_layout(RENAMED_PLATFORM_FILES, LEGACY_PLATFORM_ASSEMBLY)
        inventory = validate_package_directory(self.package_directory, require_current_metadata=False)
        self.assertEqual([item["name"] for item in inventory], list(RENAMED_PLATFORM_FILES))
        with self.assertRaisesRegex(PackageValidationError, "allowlist mismatch"):
            validate_package_directory(self.package_directory)

    def test_legacy_is_accepted_only_for_existing_complete_packages(self) -> None:
        self._write_layout(LEGACY_FILES, LEGACY_PLATFORM_ASSEMBLY)
        self.assertEqual(len(validate_package_directory(self.package_directory, False)), 5)
        with self.assertRaisesRegex(PackageValidationError, "allowlist mismatch"):
            validate_package_directory(self.package_directory)
        (self.package_directory / "Localization").mkdir()
        with self.assertRaisesRegex(PackageValidationError, "allowlist mismatch"):
            validate_package_directory(self.package_directory, False)

    def test_nested_missing_empty_unknown_and_reparse_resources_are_rejected(self) -> None:
        path = self.package_directory / RESOURCE_FILES[0]
        path.unlink()
        with self.assertRaisesRegex(PackageValidationError, "allowlist mismatch"):
            validate_package_directory(self.package_directory, False)
        path.write_bytes(b"")
        with self.assertRaisesRegex(PackageValidationError, "empty"):
            validate_package_directory(self.package_directory)
        path.write_bytes(b"image")
        foreign = path.parent / "unknown.txt"
        foreign.write_bytes(b"foreign")
        with self.assertRaisesRegex(PackageValidationError, "extra=.*unknown.txt"):
            validate_package_directory(self.package_directory)
        foreign.unlink()
        original = Path.lstat
        def reparse(p, *args, **kwargs):
            if p == path.parent:
                return SimpleNamespace(st_mode=stat.S_IFDIR, st_file_attributes=_REPARSE_POINT)
            return original(p, *args, **kwargs)
        with patch.object(Path, "lstat", new=reparse), self.assertRaisesRegex(PackageValidationError, "reparse"):
            validate_package_directory(self.package_directory)

    def test_layout_rejects_traversal_backslashes_case_and_unknown_paths(self) -> None:
        path = Path(self.temporary_directory.name) / "layout.json"
        valid = load_layouts()
        for name in ("../foreign", "/absolute", "Textures\\foreign.png", "Localization/EN.csv", "./manifest.xml"):
            with self.subTest(name=name):
                data = dict(valid, current=[*CURRENT_FILES[:-1], name])
                path.write_text(json.dumps(data), encoding="utf-8")
                with self.assertRaises(PackageValidationError):
                    load_layouts(path)

    def test_wrong_case_resource_name_is_rejected(self) -> None:
        path = self.package_directory / "Localization/en.csv"
        interim = path.with_name("temp.csv")
        path.rename(interim)
        interim.rename(path.with_name("EN.csv"))
        with self.assertRaisesRegex(PackageValidationError, "allowlist mismatch"):
            validate_package_directory(self.package_directory)

    def test_rejects_missing_product_dll(self) -> None:
        (self.package_directory / EXPECTED_ASSEMBLIES[-1]).unlink()
        with self.assertRaisesRegex(PackageValidationError, "allowlist mismatch"):
            validate_package_directory(self.package_directory)

    def test_rejects_unlisted_game_dll(self) -> None:
        (self.package_directory / "Assembly-CSharp.dll").write_bytes(b"forbidden")
        with self.assertRaisesRegex(PackageValidationError, "extra=.*Assembly-CSharp.dll"):
            validate_package_directory(self.package_directory)

    def test_rejects_unlisted_test_runner(self) -> None:
        (self.package_directory / "nunit.framework.dll").write_bytes(b"forbidden")
        with self.assertRaisesRegex(PackageValidationError, "nunit.framework.dll"):
            validate_package_directory(self.package_directory)

    def test_rejects_nested_directories(self) -> None:
        (self.package_directory / "nested").mkdir()
        with self.assertRaisesRegex(PackageValidationError, "regular files"):
            validate_package_directory(self.package_directory)

    def test_rejects_malformed_xml(self) -> None:
        (self.package_directory / "manifest.xml").write_text("<Manifest>", encoding="utf-8")
        with self.assertRaisesRegex(PackageValidationError, "could not be parsed"):
            validate_package_directory(self.package_directory)

    def test_rejects_unconfirmed_or_mismatched_author(self) -> None:
        self._replace_manifest_text("<Author>Caizii</Author>", "<Author>Unknown</Author>")
        with self.assertRaisesRegex(PackageValidationError, "Author"):
            validate_package_directory(self.package_directory)

    def test_rejects_wrong_identity(self) -> None:
        self._replace_manifest_text("<ID>digital_storage</ID>", "<ID>other_mod</ID>")
        with self.assertRaisesRegex(PackageValidationError, "Expected package ID"):
            validate_package_directory(self.package_directory)

    def test_rejects_invalid_loader_id_characters(self) -> None:
        self._replace_manifest_text("<ID>digital_storage</ID>", "<ID>../digital_storage</ID>")
        with self.assertRaisesRegex(PackageValidationError, "rejected by the game loader"):
            validate_package_directory(self.package_directory)

    def test_rejects_assembly_order_or_path_changes(self) -> None:
        self._replace_manifest_text(
            "<Assembly>ProbablyStolen.DigitalStorage.Common.dll</Assembly>",
            "<Assembly>../ProbablyStolen.DigitalStorage.Common.dll</Assembly>",
        )
        with self.assertRaisesRegex(PackageValidationError, "load order"):
            validate_package_directory(self.package_directory)

    def test_rejects_missing_capabilities_element(self) -> None:
        self._replace_manifest_text("  <Capabilities />\n", "")
        with self.assertRaisesRegex(PackageValidationError, "approved order"):
            validate_package_directory(self.package_directory)

    def test_rejects_declared_capabilities_for_this_package(self) -> None:
        self._replace_manifest_text("<Capabilities />", "<Capabilities><Capability>files</Capability></Capabilities>")
        with self.assertRaisesRegex(PackageValidationError, "must remain empty"):
            validate_package_directory(self.package_directory)

    def test_rejects_empty_release_file(self) -> None:
        (self.package_directory / EXPECTED_ASSEMBLIES[0]).write_bytes(b"")
        with self.assertRaisesRegex(PackageValidationError, "is empty"):
            validate_package_directory(self.package_directory)

    def test_rejects_package_directory_reparse_point(self) -> None:
        original_lstat = Path.lstat

        def reparse_directory(path: Path, *args: object, **kwargs: object) -> object:
            if path == self.package_directory:
                return SimpleNamespace(st_mode=stat.S_IFDIR, st_file_attributes=_REPARSE_POINT)
            return original_lstat(path, *args, **kwargs)

        with patch.object(Path, "lstat", new=reparse_directory):
            with self.assertRaisesRegex(PackageValidationError, "symbolic link or reparse point"):
                validate_package_directory(self.package_directory)

    def test_rejects_reparse_file_entries(self) -> None:
        original_lstat = Path.lstat
        target = self.package_directory / EXPECTED_ASSEMBLIES[0]

        def reparse_file(path: Path, *args: object, **kwargs: object) -> object:
            if path == target:
                return SimpleNamespace(st_mode=stat.S_IFREG, st_file_attributes=_REPARSE_POINT)
            return original_lstat(path, *args, **kwargs)

        with patch.object(Path, "lstat", new=reparse_file):
            with self.assertRaisesRegex(PackageValidationError, "links or reparse points"):
                validate_package_directory(self.package_directory)


