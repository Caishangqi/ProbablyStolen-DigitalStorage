"""Safely deploy or remove only the owned Digital Storage mod directory."""

from __future__ import annotations

import argparse
import os
import shutil
import stat
import sys
import tempfile
import unittest
import uuid
from pathlib import Path
from types import SimpleNamespace
from typing import Sequence
from unittest.mock import patch

from digital_storage_tools.deployment import *
from digital_storage_tools.deployment import _REPARSE_POINT

class DeploymentTests(unittest.TestCase):
    def setUp(self) -> None:
        patcher = patch('digital_storage_tools.deployment._running_game_paths', return_value=[])
        self.running_game_paths = patcher.start()
        self.addCleanup(patcher.stop)
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary_directory.name).resolve()
        self.game = self.root / "Probably Stolen"
        self.game.mkdir()
        (self.game / "Probably Stolen.exe").write_bytes(b"game executable placeholder")
        managed = self.game / "Probably Stolen_Data" / "Managed"
        managed.mkdir(parents=True)
        for name in (
            "Assembly-CSharp.dll",
            "UnityEngine.dll",
            "UnityEngine.CoreModule.dll",
        ):
            (managed / name).write_bytes(b"game assembly placeholder")

        self.mods = self.game / "Mods"
        self.mods.mkdir()
        other_mod = self.mods / "OtherMod"
        other_mod.mkdir()
        (other_mod / "owned-by-someone-else.txt").write_text("preserve", encoding="utf-8")
        self.save_marker = self.game / "UnrelatedData" / "preserve.txt"
        self.save_marker.parent.mkdir()
        self.save_marker.write_text("preserve", encoding="utf-8")

        self.package = self.root / "package"
        self.package.mkdir()
        for index, name in enumerate(EXPECTED_ASSEMBLIES, start=1):
            (self.package / name).write_bytes(f"release assembly {index}".encode("ascii"))
        (self.package / "manifest.xml").write_bytes(
            manifest_text(CURRENT_PLATFORM_ASSEMBLY).encode("utf-8")
        )
        for name in RESOURCE_FILES:
            path = self.package / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"synthetic runtime resource")
        self.target = self.mods / "DigitalStorage"

    def test_direct_cli_refuses_unknown_running_process_without_writes(self):
        self.running_game_paths.return_value = [None]
        self.assertEqual(main(['--action', 'deploy', '--game-dir', str(self.game), '--deploy-dir', str(self.target), '--package-dir', str(self.package)]), 2)
        self.assertFalse(self.target.exists())

    def test_running_or_unknown_game_refuses_mutations(self):
        for path in (str(self.game / 'Probably Stolen.exe'), None):
            with self.subTest(path=path):
                self.running_game_paths.return_value = [path]
                with self.assertRaises(DeploymentError):
                    deploy_package(self.package, self.game, self.target)
                with self.assertRaises(DeploymentError):
                    remove_deployment(self.game, self.target)
                self.assertFalse(self.target.exists())

    def test_installation_alias_resolves_before_comparing_running_process(self):
        alias = self.root / "Game Alias"
        original_resolve = Path.resolve

        def resolve_alias(path, *args, **kwargs):
            if path == alias:
                return self.game
            return original_resolve(path, *args, **kwargs)

        self.running_game_paths.return_value = [str(self.game / "Probably Stolen.exe")]
        with patch.object(Path, "resolve", resolve_alias):
            for operation in (
                lambda: deploy_package(self.package, alias, self.target),
                lambda: remove_deployment(alias, self.target),
            ):
                with self.assertRaisesRegex(DeploymentError, "selected game is running"):
                    operation()
                self.assertFalse(self.target.exists())

    def test_nonexistent_process_path_fails_closed(self):
        self.running_game_paths.return_value = [str(self.root / "unresolved" / "Probably Stolen.exe")]
        with self.assertRaisesRegex(DeploymentError, "unverifiable executable path"):
            deploy_package(self.package, self.game, self.target)
        self.assertFalse(self.target.exists())

    def tearDown(self) -> None:
        self.temporary_directory.cleanup()

    def _install_layout(self, names: Sequence[str], platform_assembly: str) -> dict[str, bytes]:
        """Install a synthetic known layout with the genuine manifest for that layout."""
        self.target.mkdir(parents=True)
        installed: dict[str, bytes] = {}
        for name in names:
            path = self.target / name
            path.parent.mkdir(parents=True, exist_ok=True)
            if name == "manifest.xml":
                data = manifest_text(platform_assembly, [n for n in names if n.endswith(".dll")]).encode("utf-8")
            elif name == LEGACY_PLATFORM_ASSEMBLY:
                data = (self.package / CURRENT_PLATFORM_ASSEMBLY).read_bytes()
            elif name in EXPECTED_ASSEMBLIES or (self.package / name).exists():
                data = (self.package / name).read_bytes()
            else:
                data = b"previous image"
            path.write_bytes(data)
            installed[name] = data
        return installed

    def _install_initial_package(self, legacy: bool = False) -> None:
        if legacy:
            self._install_layout(LEGACY_FILES, LEGACY_PLATFORM_ASSEMBLY)
        else:
            self._install_layout(CURRENT_FILES, CURRENT_PLATFORM_ASSEMBLY)

    def installed_files(self) -> set[str]:
        return {path.relative_to(self.target).as_posix() for path in self.target.rglob("*") if path.is_file()}

    def installed_contents(self) -> dict[str, bytes]:
        return {
            path.relative_to(self.target).as_posix(): path.read_bytes()
            for path in self.target.rglob("*")
            if path.is_file()
        }

    def test_deploy_adds_only_package_files_and_preserves_other_data(self) -> None:
        changes = deploy_package(self.package, self.game, self.target)
        self.assertEqual({change["action"] for change in changes}, {"Add"})
        self.assertEqual(self.installed_files(), EXPECTED_FILES)
        self.assertEqual((self.mods / "OtherMod" / "owned-by-someone-else.txt").read_text(encoding="utf-8"), "preserve")
        self.assertEqual(self.save_marker.read_text(encoding="utf-8"), "preserve")

    def test_redeploy_is_idempotent(self) -> None:
        deploy_package(self.package, self.game, self.target)
        changes = deploy_package(self.package, self.game, self.target)
        self.assertEqual({change["action"] for change in changes}, {"Unchanged"})
        self.assertEqual(self.installed_files(), EXPECTED_FILES)

    def test_update_replaces_only_changed_owned_files(self) -> None:
        self._install_initial_package()
        changed_name = EXPECTED_ASSEMBLIES[0]
        (self.package / changed_name).write_bytes(b"updated release assembly")
        changes = deploy_package(self.package, self.game, self.target)
        self.assertEqual(changes[1]["action"], "Update")
        self.assertEqual((self.target / changed_name).read_bytes(), b"updated release assembly")
        self.assertEqual((self.mods / "OtherMod" / "owned-by-someone-else.txt").read_text(encoding="utf-8"), "preserve")

    def test_locked_update_rolls_back_and_reports_game_lock(self) -> None:
        self._install_initial_package()
        original = {name: (self.target / name).read_bytes() for name in CURRENT_FILES}
        (self.package / EXPECTED_ASSEMBLIES[0]).write_bytes(b"updated release assembly")
        real_replace = os.replace
        attempts = 0

        def fail_second_replace(source: object, destination: object) -> None:
            nonlocal attempts
            attempts += 1
            if attempts == 2:
                raise PermissionError("test file lock")
            real_replace(source, destination)

        with patch.object(os, "replace", side_effect=fail_second_replace):
            with self.assertRaisesRegex(DeploymentError, "close Probably Stolen"):
                deploy_package(self.package, self.game, self.target)

        self.assertEqual(
            {name: (self.target / name).read_bytes() for name in original},
            original,
        )
        self.assertEqual(self.installed_files(), EXPECTED_FILES)

    def test_legacy_upgrade_adds_current_resources_and_replaces_manifest_last(self) -> None:
        self._install_initial_package(legacy=True)
        real_replace = os.replace
        destinations = []
        def capture(source, destination):
            destinations.append(Path(destination).relative_to(self.target).as_posix())
            real_replace(source, destination)
        with patch.object(os, "replace", side_effect=capture):
            changes = deploy_package(self.package, self.game, self.target)
        self.assertEqual(destinations[-1], "manifest.xml")
        added = {change["name"] for change in changes if change["action"] == "Add"}
        self.assertEqual(added, set(CURRENT_FILES) - set(LEGACY_FILES))
        self.assertIn("ProbablyStolen.DigitalStorage.Platform.dll", added)
        removed = {change["name"] for change in changes if change["action"] == "Remove"}
        self.assertEqual(removed, set(LEGACY_FILES) - set(CURRENT_FILES))
        self.assertEqual(removed, {LEGACY_PLATFORM_ASSEMBLY})
        self.assertEqual(self.installed_files(), EXPECTED_FILES)
        self.assertFalse(any(path.name.startswith(".DigitalStorage") for path in self.mods.iterdir()))

    def _install_renamed_platform_package(self) -> dict[str, bytes]:
        # The shipped 13-file layout predates the Platform assembly rename and keeps its old manifest.
        return self._install_layout(RENAMED_PLATFORM_FILES, LEGACY_PLATFORM_ASSEMBLY)

    def test_renamed_platform_layout_upgrades_to_current_names(self) -> None:
        self._install_renamed_platform_package()
        changes = deploy_package(self.package, self.game, self.target)
        removed = {entry["name"] for entry in changes if entry["action"] == "Remove"}
        added = {entry["name"] for entry in changes if entry["action"] == "Add"}
        self.assertEqual(removed, {LEGACY_PLATFORM_ASSEMBLY})
        self.assertEqual(added, set(CURRENT_FILES) - set(RENAMED_PLATFORM_FILES))
        self.assertEqual(self.installed_files(), EXPECTED_FILES)
        self.assertFalse((self.target / LEGACY_PLATFORM_ASSEMBLY).exists())
        self.assertTrue((self.target / "ProbablyStolen.DigitalStorage.Platform.dll").is_file())
        self.assertFalse(any(path.name.startswith(".DigitalStorage") for path in self.mods.iterdir()))

    def test_mismatched_manifest_platform_assembly_is_rejected_before_mutation(self) -> None:
        for label, names, platform_assembly in KNOWN_LAYOUTS:
            other_assembly = (
                LEGACY_PLATFORM_ASSEMBLY if platform_assembly == CURRENT_PLATFORM_ASSEMBLY else CURRENT_PLATFORM_ASSEMBLY
            )
            with self.subTest(layout=label):
                if self.target.exists():
                    shutil.rmtree(self.target)
                self._install_layout(names, platform_assembly)
                manifest = self.target / "manifest.xml"
                manifest.write_bytes(manifest_text(other_assembly, [n for n in names if n.endswith(".dll")]).encode("utf-8"))
                before = self.installed_contents()
                with self.assertRaisesRegex(DeploymentError, "not safely owned"):
                    plan_deployment(self.package, self.game, self.target)
                with self.assertRaisesRegex(DeploymentError, "not safely owned"):
                    deploy_package(self.package, self.game, self.target)
                with self.assertRaisesRegex(DeploymentError, "unowned or incomplete"):
                    plan_removal(self.game, self.target)
                with self.assertRaisesRegex(DeploymentError, "unowned or incomplete"):
                    remove_deployment(self.game, self.target)
                self.assertEqual(self.installed_contents(), before)
                self.assertFalse(any(path.name.startswith(".DigitalStorage") for path in self.mods.iterdir()))

    def test_old_and_new_platform_dlls_together_are_rejected_before_mutation(self) -> None:
        self._install_layout(CURRENT_FILES, CURRENT_PLATFORM_ASSEMBLY)
        (self.target / LEGACY_PLATFORM_ASSEMBLY).write_bytes(b"duplicate platform assembly")
        before = self.installed_contents()
        with self.assertRaisesRegex(DeploymentError, "not safely owned"):
            deploy_package(self.package, self.game, self.target)
        with self.assertRaisesRegex(DeploymentError, "unowned or incomplete"):
            remove_deployment(self.game, self.target)
        self.assertEqual(self.installed_contents(), before)

    def _install_previous_package(self) -> dict[str, bytes]:
        return self._install_layout(PREVIOUS_FILES, LEGACY_PLATFORM_ASSEMBLY)

    def test_previous_resource_names_are_removed_only_after_complete_backup(self) -> None:
        self._install_previous_package()
        changes = deploy_package(self.package, self.game, self.target)
        removed = {entry["name"] for entry in changes if entry["action"] == "Remove"}
        self.assertEqual(removed, set(PREVIOUS_FILES) - set(CURRENT_FILES))
        self.assertEqual(self.installed_files(), set(CURRENT_FILES))
        self.assertFalse(any(path.name.startswith(".DigitalStorage") for path in self.mods.iterdir()))

    def test_renamed_platform_layout_failure_restores_exact_old_inventory(self) -> None:
        original = self._install_renamed_platform_package()
        real_replace = os.replace
        failed = False

        def fail_manifest_once(source, destination):
            nonlocal failed
            if Path(destination) == self.target / "manifest.xml" and not failed:
                failed = True
                raise PermissionError("injected final manifest lock")
            real_replace(source, destination)

        with patch.object(os, "replace", side_effect=fail_manifest_once), self.assertRaisesRegex(DeploymentError, "close Probably Stolen"):
            deploy_package(self.package, self.game, self.target)
        self.assertEqual(self.installed_files(), set(RENAMED_PLATFORM_FILES))
        self.assertEqual(self.installed_contents(), original)
        self.assertFalse(any(path.name.startswith(".DigitalStorage") for path in self.mods.iterdir()))

    def test_failure_after_previous_resources_removed_restores_exact_old_inventory(self) -> None:
        original = self._install_previous_package()
        real_replace = os.replace
        failed = False

        def fail_manifest_once(source, destination):
            nonlocal failed
            if Path(destination) == self.target / "manifest.xml" and not failed:
                failed = True
                self.assertFalse((self.target / "Textures/Items/storage_cell.png").exists())
                raise PermissionError("injected final manifest lock")
            real_replace(source, destination)

        with patch.object(os, "replace", side_effect=fail_manifest_once), self.assertRaises(DeploymentError):
            deploy_package(self.package, self.game, self.target)
        self.assertEqual(self.installed_files(), set(PREVIOUS_FILES))
        self.assertEqual({name: (self.target / name).read_bytes() for name in PREVIOUS_FILES}, original)
        self.assertFalse(any(path.name.startswith(".DigitalStorage") for path in self.mods.iterdir()))

    def test_nested_resource_update_and_noop_inventory(self) -> None:
        self._install_initial_package()
        name = RESOURCE_FILES[0]
        (self.package / name).write_bytes(b"new image")
        changes = deploy_package(self.package, self.game, self.target)
        self.assertEqual([entry["name"] for entry in changes if entry["action"] == "Update"], [name])
        self.assertEqual((self.target / name).read_bytes(), b"new image")
        self.assertTrue(all(entry["action"] == "Unchanged" for entry in plan_deployment(self.package, self.game, self.target)))

    def test_legacy_upgrade_failures_restore_exact_old_layout(self) -> None:
        self._install_initial_package(legacy=True)
        original = {name: (self.target / name).read_bytes() for name in LEGACY_FILES}
        real_replace = os.replace
        for failure_at in (1, 5, 10, 11):
            with self.subTest(failure_at=failure_at):
                attempts = 0
                def fail_once(source, destination):
                    nonlocal attempts
                    attempts += 1
                    if attempts == failure_at:
                        raise PermissionError("injected lock")
                    real_replace(source, destination)
                with patch.object(os, "replace", side_effect=fail_once), self.assertRaisesRegex(DeploymentError, "close Probably Stolen"):
                    deploy_package(self.package, self.game, self.target)
                self.assertEqual(self.installed_files(), set(LEGACY_FILES))
                self.assertEqual({name: (self.target / name).read_bytes() for name in LEGACY_FILES}, original)
                self.assertFalse(any(path.name.startswith(".DigitalStorage") for path in self.mods.iterdir()))

    def test_partial_staging_copy_does_not_create_target(self) -> None:
        real_copy = shutil.copyfile
        def fail_resource(source, destination):
            if str(source).endswith(".png"):
                raise PermissionError("injected copy failure")
            return real_copy(source, destination)
        with patch.object(shutil, "copyfile", side_effect=fail_resource), self.assertRaises(DeploymentError):
            deploy_package(self.package, self.game, self.target)
        self.assertFalse(self.target.exists())
        self.assertFalse(any(path.name.startswith(".DigitalStorage") for path in self.mods.iterdir()))

    def test_new_install_failure_removes_only_new_files_and_directories(self) -> None:
        real_replace = os.replace
        count = 0
        def fail_after_resources(source, destination):
            nonlocal count
            count += 1
            if count == 9:
                raise PermissionError("injected install failure")
            real_replace(source, destination)
        with patch.object(os, "replace", side_effect=fail_after_resources), self.assertRaises(DeploymentError):
            deploy_package(self.package, self.game, self.target)
        self.assertFalse(self.target.exists())
        self.assertEqual(self.save_marker.read_text(), "preserve")

    def test_directory_and_backup_failures_leave_legacy_target_unchanged(self) -> None:
        self._install_initial_package(legacy=True)
        original = validate_package_directory(self.target, False)
        real_mkdir = Path.mkdir
        def fail_target_directory(path, *args, **kwargs):
            if path == self.target / "Textures":
                raise PermissionError("injected directory failure")
            return real_mkdir(path, *args, **kwargs)
        with patch.object(Path, "mkdir", new=fail_target_directory), self.assertRaises(DeploymentError):
            deploy_package(self.package, self.game, self.target)
        self.assertEqual(validate_package_directory(self.target, False), original)
        real_copy = shutil.copyfile
        def fail_backup(source, destination):
            # Common is in every layout, so the legacy target has a copy to back up.
            if ".DigitalStorage.backup." in str(destination) and str(destination).endswith("ProbablyStolen.DigitalStorage.Common.dll"):
                raise PermissionError("injected backup failure")
            return real_copy(source, destination)
        with patch.object(shutil, "copyfile", side_effect=fail_backup), self.assertRaises(DeploymentError):
            deploy_package(self.package, self.game, self.target)
        self.assertEqual(validate_package_directory(self.target, False), original)
        self.assertFalse(any(path.name.startswith(".DigitalStorage") for path in self.mods.iterdir()))

    def test_rollback_failure_retains_complete_backup_and_blocks_success(self) -> None:
        self._install_initial_package()
        original = validate_package_directory(self.target, False)
        (self.package / EXPECTED_ASSEMBLIES[0]).write_bytes(b"changed")
        real_replace = os.replace
        count = 0
        def fail_install_and_restore(source, destination):
            nonlocal count
            count += 1
            if count >= 2:
                raise PermissionError("persistent lock")
            real_replace(source, destination)
        with patch.object(os, "replace", side_effect=fail_install_and_restore), self.assertRaisesRegex(DeploymentError, "backup retained.*Do not launch"):
            deploy_package(self.package, self.game, self.target)
        backups = list(self.mods.glob(".DigitalStorage.backup.*"))
        self.assertEqual(len(backups), 1)
        self.assertEqual(validate_package_directory(backups[0], False), original)

    def test_rejects_unknown_existing_file_without_changes(self) -> None:
        deploy_package(self.package, self.game, self.target)
        unowned = self.target / "user-note.txt"
        unowned.write_text("preserve", encoding="utf-8")
        original_hashes = {
            path.name: path.read_bytes()
            for path in self.target.iterdir()
            if path.is_file() and path.name in EXPECTED_FILES
        }
        with self.assertRaisesRegex(DeploymentError, "not safely owned"):
            deploy_package(self.package, self.game, self.target)
        self.assertEqual(unowned.read_text(encoding="utf-8"), "preserve")
        for name, contents in original_hashes.items():
            self.assertEqual((self.target / name).read_bytes(), contents)

    def test_rejects_foreign_existing_mod_identity(self) -> None:
        self._install_initial_package()
        manifest = self.target / "manifest.xml"
        manifest.write_text(manifest.read_text(encoding="utf-8").replace("digital_storage", "other_mod"), encoding="utf-8")
        original_assembly = (self.target / EXPECTED_ASSEMBLIES[0]).read_bytes()
        with self.assertRaisesRegex(DeploymentError, "not safely owned"):
            deploy_package(self.package, self.game, self.target)
        self.assertEqual((self.target / EXPECTED_ASSEMBLIES[0]).read_bytes(), original_assembly)
        self.assertIn("other_mod", manifest.read_text(encoding="utf-8"))

    def test_rejects_deployment_path_outside_owned_mod_directory(self) -> None:
        with self.assertRaisesRegex(DeploymentError, "exactly the game Mods/DigitalStorage"):
            deploy_package(self.package, self.game, self.game / "UnrelatedData")
        self.assertFalse((self.game / "UnrelatedData" / "manifest.xml").exists())

    def test_rejects_package_inside_game_tree(self) -> None:
        package_inside_game = self.game / "package"
        package_inside_game.mkdir()
        for name in CURRENT_FILES:
            path = package_inside_game / name
            path.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(self.package / name, path)
        with self.assertRaisesRegex(DeploymentError, "outside the game installation"):
            deploy_package(package_inside_game, self.game, self.target)
        self.assertFalse(self.target.exists())

    def test_rejects_invalid_source_package_without_writing(self) -> None:
        (self.package / "unknown.dll").write_bytes(b"unlisted")
        with self.assertRaisesRegex(DeploymentError, "not valid for deployment"):
            deploy_package(self.package, self.game, self.target)
        self.assertFalse(self.target.exists())
        self.assertEqual((self.mods / "OtherMod" / "owned-by-someone-else.txt").read_text(encoding="utf-8"), "preserve")

    def test_remove_deletes_only_owned_package_files(self) -> None:
        self._install_renamed_platform_package()
        self.assertEqual(set(remove_deployment(self.game, self.target)), set(RENAMED_PLATFORM_FILES))
        self.assertFalse(self.target.exists())
        self.assertTrue((self.mods / "OtherMod" / "owned-by-someone-else.txt").is_file())
        for legacy in (True, False):
            with self.subTest(legacy=legacy):
                self._install_initial_package(legacy=legacy)
                removed = remove_deployment(self.game, self.target)
                self.assertEqual(set(removed), set(LEGACY_FILES if legacy else CURRENT_FILES))
                self.assertFalse(self.target.exists())
                self.assertTrue(self.mods.is_dir())
                self.assertTrue((self.mods / "OtherMod" / "owned-by-someone-else.txt").is_file())
                self.assertEqual(self.save_marker.read_text(encoding="utf-8"), "preserve")

    def test_remove_rejects_unknown_nested_entry_before_deleting_anything(self) -> None:
        self._install_initial_package()
        note = self.target / "Textures/Items/player-note.txt"
        note.write_text("preserve", encoding="utf-8")
        before = {name: (self.target / name).read_bytes() for name in CURRENT_FILES}
        with self.assertRaisesRegex(DeploymentError, "unowned or incomplete"):
            remove_deployment(self.game, self.target)
        self.assertEqual(note.read_text(), "preserve")
        self.assertEqual({name: (self.target / name).read_bytes() for name in CURRENT_FILES}, before)

    def test_remove_rejects_incomplete_layout_and_nested_reparse_directory(self) -> None:
        self._install_initial_package()
        path = self.target / RESOURCE_FILES[0]
        saved = path.read_bytes()
        path.unlink()
        with self.assertRaisesRegex(DeploymentError, "unowned or incomplete"):
            remove_deployment(self.game, self.target)
        path.write_bytes(saved)
        before = validate_package_directory(self.target, False)
        original_lstat = Path.lstat
        def reparse(p, *args, **kwargs):
            if p == path.parent:
                return SimpleNamespace(st_mode=stat.S_IFDIR, st_file_attributes=_REPARSE_POINT)
            return original_lstat(p, *args, **kwargs)
        with patch.object(Path, "lstat", new=reparse), self.assertRaisesRegex(DeploymentError, "reparse"):
            remove_deployment(self.game, self.target)
        self.assertEqual(validate_package_directory(self.target, False), before)

    def test_remove_missing_mod_is_idempotent(self) -> None:
        self.assertEqual(remove_deployment(self.game, self.target), [])
        self.assertTrue(self.mods.is_dir())
        self.assertTrue((self.mods / "OtherMod").is_dir())

    def test_remove_refuses_unknown_file_and_preserves_all_contents(self) -> None:
        self._install_initial_package()
        unowned = self.target / "user-note.txt"
        unowned.write_text("preserve", encoding="utf-8")
        before = {path.name: path.read_bytes() for path in self.target.iterdir() if path.is_file()}
        with self.assertRaisesRegex(DeploymentError, "unowned or incomplete package"):
            remove_deployment(self.game, self.target)
        self.assertEqual({path.name: path.read_bytes() for path in self.target.iterdir() if path.is_file()}, before)

    def test_remove_refuses_foreign_identity(self) -> None:
        self._install_initial_package()
        manifest = self.target / "manifest.xml"
        manifest.write_text(manifest.read_text(encoding="utf-8").replace("digital_storage", "other_mod"), encoding="utf-8")
        before = {path.name: path.read_bytes() for path in self.target.iterdir() if path.is_file()}
        with self.assertRaisesRegex(DeploymentError, "unowned or incomplete package"):
            remove_deployment(self.game, self.target)
        self.assertEqual({path.name: path.read_bytes() for path in self.target.iterdir() if path.is_file()}, before)

    def test_rejects_reparse_mods_directory(self) -> None:
        original_lstat = Path.lstat

        def reparse_directory(path: Path, *args: object, **kwargs: object) -> object:
            if path == self.mods:
                return SimpleNamespace(st_mode=stat.S_IFDIR, st_file_attributes=_REPARSE_POINT)
            return original_lstat(path, *args, **kwargs)

        with patch.object(Path, "lstat", new=reparse_directory):
            with self.assertRaisesRegex(DeploymentError, "Mods directory must not be"):
                deploy_package(self.package, self.game, self.target)
        self.assertFalse(self.target.exists())

    def test_rejects_reparse_mod_directory(self) -> None:
        original_lstat = Path.lstat

        def reparse_directory(path: Path, *args: object, **kwargs: object) -> object:
            if path == self.target:
                return SimpleNamespace(st_mode=stat.S_IFDIR, st_file_attributes=_REPARSE_POINT)
            return original_lstat(path, *args, **kwargs)

        with patch.object(Path, "lstat", new=reparse_directory):
            with self.assertRaisesRegex(DeploymentError, "Digital Storage directory must not be"):
                plan_removal(self.game, self.target)


