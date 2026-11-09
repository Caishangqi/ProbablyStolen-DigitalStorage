import json
import sys
import tempfile
import unittest
import urllib.error
from pathlib import Path
from unittest.mock import patch

REPOSITORY = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(REPOSITORY / "scripts"))

from digital_storage_tools import decompile as decompile_game  # noqa: E402
from digital_storage_tools import extract_assets as extract_game_assets  # noqa: E402


class DecompileGameTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.game = self.root / "steamapps" / "common" / "Probably Stolen"
        managed = self.game / "Probably Stolen_Data" / "Managed"
        managed.mkdir(parents=True)
        (managed / "Assembly-CSharp.dll").write_bytes(b"game")
        (managed / "UnityEngine.dll").write_bytes(b"engine")
        manifest = self.root / "steamapps" / "appmanifest_4348910.acf"
        manifest.write_text(
            '"AppState" { "appid" "4348910" "buildid" "12345" "installdir" "Probably Stolen" }',
            encoding="utf-8",
        )
        self.archive = self.root / "repo" / ".reference" / "probably-stolen-src"
        self.repo = self.root / "repo"
        self.repo.mkdir()

    def fake_ilspy(self, command, **_kwargs):
        destination = Path(command[command.index("-o") + 1])
        destination.mkdir(parents=True)
        (destination / "Restored.cs").write_text("class Restored {}", encoding="utf-8")
        return type("Completed", (), {"returncode": 0, "stdout": "", "stderr": ""})()

    def test_archives_every_managed_dll_with_build_and_hash_provenance(self):
        with patch.object(decompile_game.subprocess, "run", side_effect=self.fake_ilspy):
            output = decompile_game.decompile(self.game, self.archive, "ilspycmd", self.repo)

        manifest = json.loads((output / "decompile-manifest.json").read_text(encoding="utf-8"))
        self.assertTrue(output.parent.name.startswith("steam-12345-asm-"))
        self.assertEqual({item["name"] for item in manifest["assemblies"]}, {"Assembly-CSharp.dll", "UnityEngine.dll"})
        self.assertEqual(len(list(output.rglob("*.cs"))), 2)

    def test_new_build_creates_new_archive_and_preserves_old_build(self):
        with patch.object(decompile_game.subprocess, "run", side_effect=self.fake_ilspy):
            first = decompile_game.decompile(self.game, self.archive, "ilspycmd", self.repo)
            appmanifest = self.root / "steamapps" / "appmanifest_4348910.acf"
            appmanifest.write_text(
                '"AppState" { "appid" "4348910" "buildid" "67890" "installdir" "Probably Stolen" }',
                encoding="utf-8",
            )
            second = decompile_game.decompile(self.game, self.archive, "ilspycmd", self.repo)

        self.assertNotEqual(first, second)
        self.assertTrue((first / "decompile-manifest.json").is_file())
        self.assertTrue((second / "decompile-manifest.json").is_file())

    def test_rejects_archive_path_outside_reference_root(self):
        with self.assertRaisesRegex(decompile_game.WorkflowError, "outside"):
            decompile_game.decompile(self.game, self.root / "outside", "ilspycmd", self.repo)

    def test_changed_secondary_assembly_creates_new_archive_and_preserves_old(self):
        with patch.object(decompile_game.subprocess, "run", side_effect=self.fake_ilspy):
            first = decompile_game.decompile(self.game, self.archive, "ilspycmd", self.repo)
            (self.game / "Probably Stolen_Data" / "Managed" / "UnityEngine.dll").write_bytes(b"changed")
            second = decompile_game.decompile(self.game, self.archive, "ilspycmd", self.repo)

        self.assertNotEqual(first, second)
        self.assertTrue(first.is_dir())
        self.assertTrue(second.is_dir())

    def test_rejects_derived_reparse_archive_component(self):
        with patch.object(decompile_game, "_is_reparse_point", side_effect=lambda path: path.name.startswith("steam-")):
            with self.assertRaisesRegex(decompile_game.WorkflowError, "linked or reparse"):
                decompile_game.decompile(self.game, self.archive, "ilspycmd", self.repo)

    def test_rejects_manifest_for_another_game(self):
        manifest = self.root / "steamapps" / "appmanifest_4348910.acf"
        manifest.write_text('"appid" "123" "buildid" "2" "installdir" "Probably Stolen"', encoding="utf-8")
        with self.assertRaisesRegex(decompile_game.WorkflowError, "invalid"):
            decompile_game._build_id(self.game)


class FakeRipperProcess:
    def __init__(self, poll_result=None):
        self.poll_result = poll_result
        self.terminated = False
        self.waited = False
        self.killed = False
        self.returncode = poll_result

    def poll(self):
        return self.poll_result if not self.terminated else 0

    def terminate(self):
        self.terminated = True
        self.returncode = 0

    def wait(self, timeout=None):
        self.waited = True
        return self.returncode

    def kill(self):
        self.killed = True
        self.returncode = -9


class ExtractAssetsSafetyTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        (self.root / ".reference").mkdir()
        self.output = self.root / ".reference" / "probably-stolen-content"
        self.outside = self.root / "outside"
        self.outside.mkdir()
        (self.outside / "keep.txt").write_text("keep", encoding="utf-8")

    def test_cleanup_removes_only_output_children_and_keeps_outside_data(self):
        self.output.mkdir()
        (self.output / "old.txt").write_text("old", encoding="utf-8")
        (self.output / "nested").mkdir()
        (self.output / "nested" / "old.txt").write_text("old", encoding="utf-8")
        extract_game_assets._clear_contents(self.output)

        self.assertEqual(list(self.output.iterdir()), [])
        self.assertEqual((self.outside / "keep.txt").read_text(encoding="utf-8"), "keep")

    def test_rejects_nested_reparse_child_without_touching_output(self):
        self.output.mkdir()
        nested = self.output / "nested"
        nested.mkdir()
        (nested / "linked").write_text("linked", encoding="utf-8")
        with patch.object(extract_game_assets, "_is_reparse_point", side_effect=lambda path: path.name == "linked"):
            with self.assertRaisesRegex(extract_game_assets.WorkflowError, "linked or reparse"):
                extract_game_assets._clear_contents(self.output)

        self.assertTrue((nested / "linked").exists())
        self.assertEqual((self.outside / "keep.txt").read_text(encoding="utf-8"), "keep")

    def test_rejects_wrong_output_directory(self):
        with self.assertRaisesRegex(extract_game_assets.WorkflowError, "outside"):
            extract_game_assets._assert_plain_path(self.root / "outside", self.root, "content output")

    def test_rejects_linked_output_directory(self):
        linked = self.root / ".reference" / "probably-stolen-content"
        linked.mkdir()
        with patch.object(extract_game_assets, "_is_reparse_point", return_value=True):
            with self.assertRaisesRegex(extract_game_assets.WorkflowError, "linked"):
                extract_game_assets._assert_plain_path(linked, self.root, "content output")

    def test_asset_ripper_http_workflow_exports_and_terminates_process(self):
        game_dir = self.root / "Probably Stolen"
        data_dir = game_dir / "Probably Stolen_Data"
        data_dir.mkdir(parents=True)
        ripper = self.root / "AssetRipper.exe"
        ripper.write_bytes(b"ripper")
        process = FakeRipperProcess()
        requests = []

        def request(url, fields=None, timeout=15):
            requests.append((url, fields, timeout))
            if fields and url.endswith("/Export/UnityProject"):
                staging = Path(fields["Path"])
                for directory in ("Assets", "Packages", "ProjectSettings"):
                    (staging / directory).mkdir()
            return (200, None, "ready") if not fields else (302, "/", "")

        def start_process(arguments, **_kwargs):
            log = Path(arguments[arguments.index("--log-path") + 1])
            log.write_text("Finished reading files\nFinished post-export", encoding="utf-8")
            return process

        with patch.object(extract_game_assets, "_assert_plain_path", return_value=self.output), \
             patch.object(extract_game_assets, "_reserve_port", return_value=43123), \
             patch.object(extract_game_assets.subprocess, "Popen", side_effect=start_process), \
             patch.object(extract_game_assets, "_request", side_effect=request):
            extract_game_assets.export_assets(game_dir, ripper, self.output)

        self.assertEqual(requests[0][0], "http://127.0.0.1:43123")
        self.assertEqual(requests[1][0], "http://127.0.0.1:43123/LoadFolder")
        self.assertEqual(requests[1][1], {"Path": str(data_dir)})
        self.assertEqual(requests[2][0], "http://127.0.0.1:43123/Export/UnityProject")
        self.assertEqual(requests[2][1]["CreateSubfolder"], "false")
        self.assertTrue(process.terminated)
        self.assertTrue(process.waited)

    def test_asset_ripper_http_failure_terminates_process_and_propagates_error(self):
        game_dir = self.root / "Probably Stolen"
        (game_dir / "Probably Stolen_Data").mkdir(parents=True)
        ripper = self.root / "AssetRipper.exe"
        ripper.write_bytes(b"ripper")
        process = FakeRipperProcess()
        calls = iter([(200, None, "ready"), urllib.error.URLError("load failed")])

        def request(*_args, **_kwargs):
            result = next(calls)
            if isinstance(result, Exception):
                raise result
            return result

        with patch.object(extract_game_assets, "_assert_plain_path", return_value=self.output), \
             patch.object(extract_game_assets, "_reserve_port", return_value=43124), \
             patch.object(extract_game_assets.subprocess, "Popen", return_value=process), \
             patch.object(extract_game_assets, "_request", side_effect=request):
            with self.assertRaises(urllib.error.URLError):
                extract_game_assets.export_assets(game_dir, ripper, self.output)

        self.assertTrue(process.terminated)
        self.assertTrue(process.waited)


if __name__ == "__main__":
    unittest.main()
