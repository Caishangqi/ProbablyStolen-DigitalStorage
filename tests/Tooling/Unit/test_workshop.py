import json
import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import Mock, patch

from digital_storage_tools import commands, workshop


POWERSHELL = shutil.which("powershell.exe")


WORKSHOP_WRAPPER_DRIVER = r'''
$ErrorActionPreference = 'Stop'
$wrapper = Join-Path $env:WORKSHOP_TEST_REPOSITORY 'scripts/publish-workshop.ps1'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $wrapper -PackageDirectory $env:WORKSHOP_TEST_PACKAGE
$result = @{
    exitCode = $LASTEXITCODE
    roots = @(Get-ChildItem -LiteralPath (Split-Path -Parent $env:WORKSHOP_TEST_PACKAGE) -Force |
        Where-Object { $_.Name -like '.DigitalStorage.workshop.*' } |
        ForEach-Object { $_.FullName })
}
Write-Output ('WORKSHOP_RESULT=' + ($result | ConvertTo-Json -Compress))
'''



def _typographic(text: str) -> str:
    """Mirror the writer: straight double quotes alternate between opening and closing typographic quotes."""
    characters, opening = [], True
    for character in text:
        if character == '"':
            characters.append("“" if opening else "”")
            opening = not opening
        else:
            characters.append(character)
    return "".join(characters)

class WorkshopWrapperWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        if POWERSHELL is None:
            raise unittest.SkipTest("Windows PowerShell is required for wrapper workflow tests")

    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory(prefix="workshop-wrapper-")
        self.addCleanup(self.temporary_directory.cleanup)
        self.root = Path(self.temporary_directory.name).resolve()
        self.repository = self.root / "copied repository"
        (self.repository / "scripts").mkdir(parents=True)
        (self.repository / "scripts/publish-workshop.ps1").write_text(
            (Path(__file__).resolve().parents[3] / "scripts/publish-workshop.ps1").read_text(encoding="utf-8"),
            encoding="utf-8",
        )
        shutil.copytree(
            Path(__file__).resolve().parents[3] / ".venv", self.repository / ".venv"
        )
        (self.repository / "scripts/tool.py").write_text(
            """import json, os, sys

if sys.argv[1:3] == ['environment', '--target']:
    print(json.dumps({'package_directory': os.environ['WORKSHOP_TEST_PACKAGE'], 'game_directory': 'fake-game', 'output_directory': 'fake-output', 'steam_library_directory': None}))
elif 'internal-workshop-upload' in sys.argv:
    package = sys.argv[sys.argv.index('--package-dir') + 1]
    with open(os.environ['WORKSHOP_TEST_LOG'], 'a', encoding='utf-8') as stream:
        stream.write('upload|' + package + '\\n')
    with open(os.environ['WORKSHOP_TEST_LOG'] + '.json', 'w', encoding='utf-8') as stream:
        json.dump({'arguments': sys.argv[1:]}, stream)
    if os.environ['WORKSHOP_TEST_FAILURE'] == 'upload':
        raise SystemExit(7)
else:
    raise SystemExit('unexpected fake Python command')
""",
            encoding="utf-8",
        )
        package = self.root / "shared package" / "DigitalStorage"
        package.mkdir(parents=True)
        (package / "shared.txt").write_text("shared", encoding="utf-8")
        self.package = package
        self.log = self.root / "workflow.log"

    def run_wrapper(self, failure: str = "") -> dict[str, object]:
        package_script = self.repository / "scripts/package.ps1"
        package_script.write_text(
            """param([string]$PackageDirectory)\nAdd-Content -LiteralPath $env:WORKSHOP_TEST_LOG -Value ('package|' + $PackageDirectory)\nNew-Item -ItemType Directory -Force -Path $PackageDirectory | Out-Null\nSet-Content -LiteralPath (Join-Path $PackageDirectory 'built.txt') -Value 'built'\nif ($env:WORKSHOP_TEST_FAILURE -eq 'package') { exit 9 }\n""",
            encoding="utf-8",
        )
        environment = os.environ.copy()
        environment.update(
            WORKSHOP_TEST_REPOSITORY=str(self.repository),
            WORKSHOP_TEST_PACKAGE=str(self.package),
            WORKSHOP_TEST_LOG=str(self.log),
            WORKSHOP_TEST_FAILURE=failure,
        )
        driver = self.root / "driver.ps1"
        driver.write_text(WORKSHOP_WRAPPER_DRIVER, encoding="utf-8")
        result = subprocess.run(
            [POWERSHELL, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(driver)],
            cwd=self.repository,
            env=environment,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=30,
            check=True,
        )
        line = next(line for line in result.stdout.splitlines() if line.startswith("WORKSHOP_RESULT="))
        return json.loads(line.removeprefix("WORKSHOP_RESULT="))

    def test_wrapper_uses_unique_package_and_cleans_it_after_child_outcomes(self) -> None:
        shared_before = (self.package / "shared.txt").read_text(encoding="utf-8")
        for failure in ("", "package", "upload"):
            with self.subTest(failure=failure):
                result = self.run_wrapper(failure)
                self.assertEqual(0 if not failure else 1, result["exitCode"])
                entries = self.log.read_text(encoding="utf-8").splitlines()
                package_path = next(line.split("|", 1)[1] for line in entries if line.startswith("package|"))
                upload_paths = [line.split("|", 1)[1] for line in entries if line.startswith("upload|")]
                self.assertTrue(package_path.endswith("DigitalStorage"))
                self.assertTrue(package_path.startswith(str(self.package.parent)))
                self.assertNotEqual(Path(package_path), self.package)
                self.assertEqual([] if failure in ("package",) else [package_path], upload_paths)
                self.assertFalse(Path(package_path).exists())
                self.assertEqual([], result["roots"])
                self.assertEqual(shared_before, (self.package / "shared.txt").read_text(encoding="utf-8"))
                self.log.write_text("", encoding="utf-8")


class WorkshopTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary_directory.cleanup)
        self.root = Path(self.temporary_directory.name).resolve()
        self.package_directory = self.root / "package with spaces" / "DigitalStorage"
        self.package_directory.mkdir(parents=True)
        (self.package_directory / "manifest.xml").write_text(
            """<?xml version="1.0" encoding="utf-8"?>
<Manifest>
  <ID>digital_storage</ID>
  <Name>Digital Storage "Workshop"</Name>
  <Author>Caizii</Author>
  <ModVersion>0.3.0</ModVersion>
  <Description>A storage network\\with a terminal.
Second line.</Description>
</Manifest>
""",
            encoding="utf-8",
        )
        self.steamcmd_directory = self.root / "SteamCMD"
        self.steamcmd_directory.mkdir()
        (self.steamcmd_directory / "steamcmd.exe").write_bytes(b"fake")
        self.preview_path = self.root / "preview.png"
        from PIL import Image

        Image.new("RGB", (64, 64), "white").save(self.preview_path)
        self.vdf_path = self.root / ".secrets" / "workshop.vdf"
        changelog = patch.object(workshop, "WORKSHOP_CHANGELOG_DIR", self.root / "changelog")
        changelog.start()
        self.addCleanup(changelog.stop)
        self.write_notes("Fix A.", "修复 A。")
        self.settings = workshop.WorkshopSettings(
            package_directory=self.package_directory,
            steamcmd_directory=self.steamcmd_directory,
            vdf_path=self.vdf_path,
            preview_path=self.preview_path,
            steam_username="steam user",
        )

    def write_notes(self, english: str, chinese: str) -> None:
        directory = self.root / "changelog"
        directory.mkdir(parents=True, exist_ok=True)
        (directory / "en-US.txt").write_bytes(english.encode("utf-8"))
        (directory / "zh-CN.txt").write_bytes(chinese.encode("utf-8"))

    def test_first_publish_writes_zero_id_and_manifest_metadata(self) -> None:
        with patch.object(workshop, "validate_package_directory"):
            prepared = workshop.prepare_workshop_item(self.settings)

        self.assertEqual(prepared.published_file_id, "0")
        text = self.vdf_path.read_text(encoding="utf-8")
        values = workshop._read_vdf(self.vdf_path)
        self.assertEqual(values["publishedfileid"], "0")
        self.assertEqual(values["title"], 'Digital Storage “Workshop”')
        self.assertEqual(values["description"],
                         _typographic(workshop.WORKSHOP_LOCALE_PATHS["en-US"].read_text(encoding="utf-8").strip()))
        self.assertEqual(values["changenote"], "[h2]0.3.0[/h2]\nFix A.\n[hr][/hr]\n修复 A。")
        self.assertEqual(values["visibility"], "0")

    def test_chinese_locale_uses_checked_in_workshop_description(self) -> None:
        arguments = SimpleNamespace(
            package_dir=str(self.package_directory), steamcmd_dir=str(self.steamcmd_directory),
            vdf_path=str(self.vdf_path), preview=str(self.preview_path), steam_username="steam user",
            visibility="2", locale="zh-CN",
        )
        settings = workshop.settings_from_arguments(arguments, {}, repository_root=self.root)
        with patch.object(workshop, "validate_package_directory"):
            prepared = workshop.prepare_workshop_item(settings)

        self.assertEqual(prepared.metadata.description,
                         workshop.WORKSHOP_LOCALE_PATHS["zh-CN"].read_text(encoding="utf-8").strip())
        self.assertEqual(workshop._read_vdf(self.vdf_path)["description"],
                         _typographic(prepared.metadata.description))
        self.assertEqual(prepared.metadata.change_note, "[h2]0.3.0[/h2]\n修复 A。\n[hr][/hr]\nFix A.")

    def test_missing_or_empty_change_note_is_rejected_before_steamcmd(self) -> None:
        notes = self.root / "changelog"
        for name, action, message in (
            ("zh-CN.txt", lambda path: path.unlink(), r"change note \(zh-CN\) is missing"),
            ("en-US.txt", lambda path: path.write_text(" \n", encoding="utf-8"), "change note must not be empty"),
        ):
            with self.subTest(name=name):
                self.write_notes("Fix A.", "修复 A。")
                action(notes / name)
                runner = Mock()
                with patch.object(workshop, "validate_package_directory"), \
                        self.assertRaisesRegex(workshop.WorkshopValidationError, message):
                    workshop.publish_workshop(self.settings, runner=runner)
                runner.assert_not_called()
                self.assertFalse(Path(str(self.vdf_path) + ".pending").exists())

    def test_multiline_vdf_fields_keep_real_line_breaks(self) -> None:
        english, chinese = '[list]\n[*]Pinyin search\n[/list]', '[list]\n[*]拼音搜索\n[/list]'
        note = '[h2]0.3.0[/h2]\n' + english + '\n[hr][/hr]\n' + chinese
        for separator in ("\n", "\r\n", "\r"):
            with self.subTest(separator=repr(separator)):
                self.write_notes(english.replace("\n", separator), chinese.replace("\n", separator))
                with patch.object(workshop, "validate_package_directory"):
                    workshop.prepare_workshop_item(self.settings)
                raw = self.vdf_path.read_text(encoding="utf-8")
                self.assertIn('"changenote"    "' + note + '"', raw)
                self.assertIn('"description"    "[h1]Digital Storage[/h1]\n\n', raw)
                self.assertNotIn(r"\n", raw)
                self.assertNotIn(r"\r", raw)
                self.assertEqual(note, workshop._read_vdf(self.vdf_path)["changenote"])

    def test_text_fields_are_written_without_escape_sequences(self) -> None:
        note = '[*]物品按"数量 × 占地格数"计费，"Pending migration" 提示'
        self.write_notes("Fix A.", note)
        with patch.object(workshop, "validate_package_directory"):
            workshop.prepare_workshop_item(self.settings)
        raw = self.vdf_path.read_text(encoding="utf-8")
        # SteamCMD does not read escapes, so no backslash may appear in any text field.
        self.assertNotIn("\\", raw[raw.index('"title"'):])
        self.assertIn('[hr][/hr]\n[*]物品按“数量 × 占地格数”计费，“Pending migration” 提示"', raw)
        self.assertTrue(workshop._read_vdf(self.vdf_path)["changenote"].endswith(_typographic(note)))

    def test_backslash_in_text_is_rejected_before_steamcmd(self) -> None:
        self.write_notes("Path C:\\Mods", "修复 A。")
        with patch.object(workshop, "validate_package_directory"), \
                self.assertRaisesRegex(workshop.WorkshopValidationError, "changenote must not contain a backslash"):
            workshop.prepare_workshop_item(self.settings)

    def test_text_over_steam_byte_limit_is_rejected_before_steamcmd(self) -> None:
        # The heading (15 bytes), separator (11) and Chinese note (1) leave 7973 bytes for the English note: 2657 CJK
        # characters and two straight quotes fit as written, but not once the quotes become three-byte typographic
        # quotes, so the limit must be checked on the rendered UTF-8 text.
        for english, accepted in (("材" * 2657 + "ab", True), ('"' + "材" * 2657 + '"', False)):
            with self.subTest(accepted=accepted):
                self.write_notes(english, "x")
                runner = Mock(return_value=subprocess.CompletedProcess([], 1))
                with patch.object(workshop, "validate_package_directory"), \
                        patch.object(workshop, "_localized_description", return_value="Short description."):
                    if accepted:
                        workshop.prepare_workshop_item(self.settings)
                        continue
                    with self.assertRaisesRegex(workshop.WorkshopValidationError,
                                                "changenote is 8004 UTF-8 bytes; Steam accepts at most 8000"):
                        workshop.publish_workshop(self.settings, runner=runner)
                runner.assert_not_called()
                self.assertFalse(Path(str(self.vdf_path) + ".pending").exists())

    def test_old_release_manifest_is_rejected_before_steamcmd(self) -> None:
        manifest = self.package_directory / "manifest.xml"
        original = manifest.read_text(encoding="utf-8")
        manifest.write_text(original.replace("<ModVersion>0.3.0</ModVersion>", "<ModVersion>0.1.0</ModVersion>"),
                            encoding="utf-8")
        runner = Mock()
        with patch.object(workshop, "validate_package_directory"), self.assertRaisesRegex(
            workshop.WorkshopValidationError, "at least 0.3.0"
        ):
            workshop.publish_workshop(self.settings, runner=runner)
        runner.assert_not_called()
        self.assertFalse(Path(str(self.vdf_path) + ".pending").exists())

    def test_vdf_inside_contentfolder_is_rejected_before_steamcmd(self) -> None:
        settings = workshop.WorkshopSettings(
            package_directory=self.package_directory, steamcmd_directory=self.steamcmd_directory,
            vdf_path=self.package_directory / "workshop.vdf", preview_path=self.preview_path,
            steam_username="steam user",
        )
        runner = Mock()
        with self.assertRaisesRegex(workshop.WorkshopValidationError, "outside the package contentfolder"):
            workshop.publish_workshop(settings, runner=runner)
        runner.assert_not_called()
        self.assertFalse(settings.vdf_path.exists())

    def test_pending_marker_inside_contentfolder_is_rejected_before_steamcmd(self) -> None:
        vdf = self.root / "marker.vdf"
        package = Path(str(vdf) + ".pending")
        package.mkdir()
        settings = workshop.WorkshopSettings(package, self.steamcmd_directory, vdf,
                                             self.preview_path, "steam user")
        runner = Mock()
        with self.assertRaisesRegex(workshop.WorkshopValidationError, "pending marker must be outside"):
            workshop.publish_workshop(settings, runner=runner)
        runner.assert_not_called()
        self.assertFalse(vdf.exists())

    def test_pending_marker_blocks_before_reading_vdf(self) -> None:
        pending = Path(str(self.vdf_path) + ".pending")
        pending.parent.mkdir(parents=True)
        pending.write_text("uncertain", encoding="utf-8")
        self.vdf_path.write_text('"workshopitem" { "publishedfileid" "broken" }', encoding="utf-8")
        runner = Mock()
        with patch.object(workshop, "_read_vdf", side_effect=AssertionError("VDF must not be read")), \
             self.assertRaisesRegex(workshop.WorkshopValidationError, "uncertain outcome"):
            workshop.publish_workshop(self.settings, runner=runner)
        runner.assert_not_called()

    def test_second_publish_is_blocked_while_first_runner_owns_marker(self) -> None:
        calls = []

        def runner(command, **kwargs):
            before = self.vdf_path.read_text(encoding="utf-8")
            nested = Mock()
            with patch.object(workshop, "validate_package_directory"), self.assertRaisesRegex(
                workshop.WorkshopValidationError, "uncertain outcome"
            ):
                workshop.publish_workshop(self.settings, runner=nested)
            nested.assert_not_called()
            self.assertEqual(self.vdf_path.read_text(encoding="utf-8"), before)
            calls.append(command)
            self.vdf_path.write_text('"workshopitem" { "publishedfileid" "123456789" }', encoding="utf-8")
            return SimpleNamespace(returncode=0)

        with patch.object(workshop, "validate_package_directory"), patch("builtins.print"):
            self.assertEqual(workshop.publish_workshop(self.settings, runner=runner), 0)
        self.assertEqual(len(calls), 1)

    def test_pending_marker_is_acquired_before_preflight_reads(self) -> None:
        pending = Path(str(self.vdf_path) + ".pending")
        observed = []

        def validate(_package, **_kwargs):
            observed.append(pending.exists())
            raise workshop.PackageValidationError("invalid package")

        with patch.object(workshop, "validate_package_directory", side_effect=validate), self.assertRaisesRegex(
            workshop.WorkshopValidationError, "Package validation failed"
        ):
            workshop.publish_workshop(self.settings, runner=Mock())
        self.assertEqual([True], observed)
        self.assertFalse(pending.exists())

    def test_marker_write_failure_removes_only_owned_marker(self) -> None:
        pending = Path(str(self.vdf_path) + ".pending")

        class FailingMarker:
            def __enter__(self):
                return self

            def __exit__(self, *args):
                return False

            def write(self, _content):
                raise OSError("injected marker write failure")

            def flush(self):
                raise AssertionError("flush must not run after write failure")

            def fileno(self):
                return 1

        def open_marker(path, *_args, **_kwargs):
            Path(path).touch()
            return FailingMarker()

        runner = Mock()
        with patch.object(Path, "open", side_effect=open_marker), self.assertRaisesRegex(
            OSError, "injected marker write failure"
        ):
            workshop.publish_workshop(self.settings, runner=runner)
        runner.assert_not_called()
        self.assertFalse(pending.exists())

    def test_marker_flush_failure_removes_only_owned_marker(self) -> None:
        pending = Path(str(self.vdf_path) + ".pending")

        class FailingMarker:
            def __enter__(self):
                return self

            def __exit__(self, *args):
                return False

            def write(self, _content):
                return None

            def flush(self):
                raise OSError("injected marker flush failure")

            def fileno(self):
                return 1

        def open_marker(path, *_args, **_kwargs):
            Path(path).touch()
            return FailingMarker()

        runner = Mock()
        with patch.object(Path, "open", side_effect=open_marker), self.assertRaisesRegex(
            OSError, "injected marker flush failure"
        ):
            workshop.publish_workshop(self.settings, runner=runner)
        runner.assert_not_called()
        self.assertFalse(pending.exists())

    def test_marker_fsync_failure_removes_only_owned_marker(self) -> None:
        pending = Path(str(self.vdf_path) + ".pending")

        class Marker:
            def __enter__(self):
                return self

            def __exit__(self, *args):
                return False

            def write(self, _content):
                return None

            def flush(self):
                return None

            def fileno(self):
                return 1

        def open_marker(path, *_args, **_kwargs):
            Path(path).touch()
            return Marker()

        runner = Mock()
        with patch.object(Path, "open", side_effect=open_marker), patch.object(
            workshop.os, "fsync", side_effect=OSError("injected marker fsync failure")
        ), self.assertRaisesRegex(OSError, "injected marker fsync failure"):
            workshop.publish_workshop(self.settings, runner=runner)
        runner.assert_not_called()
        self.assertFalse(pending.exists())

        self.vdf_path.parent.mkdir(parents=True, exist_ok=True)
        self.vdf_path.write_bytes(b'"workshopitem" { \xff }')
        with patch.object(workshop, "validate_package_directory"), self.assertRaisesRegex(
            workshop.WorkshopValidationError, "Could not read Workshop VDF"
        ):
            workshop.publish_workshop(self.settings, runner=Mock())
        self.assertFalse(Path(str(self.vdf_path) + ".pending").exists())

    def test_invalid_utf8_locale_is_validation_error_and_clears_marker(self) -> None:
        invalid_locale = self.root / "invalid-locale.txt"
        invalid_locale.write_bytes(b"\xff")
        with patch.dict(workshop.WORKSHOP_LOCALE_PATHS, {"en-US": invalid_locale}), \
             patch.object(workshop, "validate_package_directory"), \
             self.assertRaisesRegex(workshop.WorkshopValidationError, "Could not read Workshop description"):
            workshop.publish_workshop(self.settings, runner=Mock())
        self.assertFalse(Path(str(self.vdf_path) + ".pending").exists())

        self.vdf_path.parent.mkdir(exist_ok=True)
        self.vdf_path.write_text(
            '"workshopitem"\n{\n    "publishedfileid" "987654321"\n}\n',
            encoding="utf-8",
        )
        with patch.object(workshop, "validate_package_directory"):
            prepared = workshop.prepare_workshop_item(self.settings)

        self.assertEqual(prepared.published_file_id, "987654321")
        self.assertIn('"publishedfileid"    "987654321"', self.vdf_path.read_text(encoding="utf-8"))

    def test_public_workflow_lock_brackets_package_and_upload(self) -> None:
        script = (workshop.ROOT / "scripts" / "publish-workshop.ps1").read_text(encoding="utf-8")
        lock = script.index("[System.IO.File]::Open")
        package = script.index("Write-Host '> powershell.exe package.ps1'")
        upload = script.index("internal-workshop-upload")
        release = script.index("$workflowLock.Dispose()")
        self.assertLess(lock, package)
        self.assertLess(package, upload)
        self.assertLess(upload, release)

    @unittest.skipUnless(os.name == "nt", "Windows filesystem alias test")
    def test_short_name_alias_inside_package_is_rejected(self) -> None:
        import ctypes

        get_short_path = ctypes.windll.kernel32.GetShortPathNameW
        get_short_path.argtypes = (ctypes.c_wchar_p, ctypes.c_wchar_p, ctypes.c_uint32)
        get_short_path.restype = ctypes.c_uint32
        buffer = ctypes.create_unicode_buffer(32768)
        length = get_short_path(str(self.package_directory), buffer, len(buffer))
        if not length or buffer.value.casefold() == str(self.package_directory).casefold():
            self.skipTest("No distinct 8.3 alias is available")
        settings = workshop.WorkshopSettings(
            package_directory=self.package_directory, steamcmd_directory=self.steamcmd_directory,
            vdf_path=Path(buffer.value) / "workshop.vdf", preview_path=self.preview_path,
            steam_username="steam user",
        )
        with self.assertRaisesRegex(workshop.WorkshopValidationError, "outside the package contentfolder"):
            workshop.publish_workshop(settings, runner=Mock())

    def test_publish_invokes_steamcmd_without_password(self) -> None:
        calls = []

        def runner(command, **kwargs):
            calls.append((command, kwargs))
            Path(command[4]).write_text(
                '"workshopitem"\n{\n    "publishedfileid" "123456789"\n}\n',
                encoding="utf-8",
            )
            return SimpleNamespace(returncode=0)

        with patch.object(workshop, "validate_package_directory"), patch("builtins.print") as output:
            result = workshop.publish_workshop(self.settings, runner=runner)

        self.assertEqual(result, 0)
        output.assert_any_call("[INFO] Published file ID: 123456789")
        self.assertEqual(len(calls), 1)
        command, kwargs = calls[0]
        self.assertEqual(command[0], str(self.steamcmd_directory / "steamcmd.exe"))
        self.assertEqual(command[1:], [
            "+login",
            "steam user",
            "+workshop_build_item",
            str(self.vdf_path),
            "+quit",
        ])
        self.assertEqual(kwargs, {"cwd": str(self.steamcmd_directory), "check": False})
        self.assertNotIn("password", " ".join(command).casefold())

    def test_package_failure_prevents_steamcmd(self) -> None:
        runner = Mock()
        with patch.object(
            workshop,
            "validate_package_directory",
            side_effect=workshop.PackageValidationError("invalid package"),
        ), self.assertRaisesRegex(workshop.WorkshopValidationError, "Package validation failed"):
            workshop.publish_workshop(self.settings, runner=runner)
        runner.assert_not_called()

    def test_invalid_preview_is_rejected_before_steamcmd(self) -> None:
        self.preview_path.write_bytes(b"x" * (workshop.MAX_PREVIEW_BYTES + 1))
        runner = Mock()
        with patch.object(workshop, "validate_package_directory"), self.assertRaisesRegex(
            workshop.WorkshopValidationError, "no larger than 1 MiB"
        ):
            workshop.publish_workshop(self.settings, runner=runner)
        runner.assert_not_called()

    def test_invalid_vdf_never_starts_steamcmd(self) -> None:
        self.vdf_path.parent.mkdir()
        for text in ('"workshopitem" { "publishedfileid" "1" "publishedfileid" "2" }',
                     '"workshopitem" { "publishedfileid" "1" } trailing',
                     '"workshopitem" { "publishedfileid" "broken" }'):
            with self.subTest(text=text):
                self.vdf_path.write_text(text, encoding="utf-8")
                runner = Mock()
                with patch.object(workshop, "validate_package_directory"), self.assertRaises(workshop.WorkshopValidationError):
                    workshop.publish_workshop(self.settings, runner=runner)
                runner.assert_not_called()

    def test_nonzero_exit_and_missing_returned_id_fail(self) -> None:
        with patch.object(workshop, "validate_package_directory"):
            with self.assertRaisesRegex(workshop.WorkshopValidationError, "exited with code 7"):
                workshop.publish_workshop(self.settings, runner=Mock(return_value=SimpleNamespace(returncode=7)))
            with self.assertRaisesRegex(workshop.WorkshopValidationError, "uncertain outcome"):
                workshop.publish_workshop(self.settings, runner=Mock())
            Path(str(self.vdf_path) + ".pending").unlink()
            with self.assertRaisesRegex(workshop.WorkshopValidationError, "check Steam"):
                workshop.publish_workshop(self.settings, runner=Mock(return_value=SimpleNamespace(returncode=0)))
        self.assertTrue(self.vdf_path.exists())

    def test_changed_id_blocks_retry_after_success_or_failure(self) -> None:
        for code in (0, 7):
            with self.subTest(exit_code=code):
                self.vdf_path.parent.mkdir(exist_ok=True)
                self.vdf_path.write_text('"workshopitem" { "publishedfileid" "123" }', encoding="utf-8")
                Path(str(self.vdf_path) + ".pending").unlink(missing_ok=True)
                def change_id(command, **kwargs):
                    self.vdf_path.write_text('"workshopitem" { "publishedfileid" "456" }', encoding="utf-8")
                    return SimpleNamespace(returncode=code)
                runner = Mock()
                with patch.object(workshop, "validate_package_directory"):
                    with self.assertRaises(workshop.WorkshopValidationError):
                        workshop.publish_workshop(self.settings, runner=change_id)
                    with self.assertRaisesRegex(workshop.WorkshopValidationError, "uncertain outcome"):
                        workshop.publish_workshop(self.settings, runner=runner)
                runner.assert_not_called()
                self.assertEqual(workshop._published_file_id(self.vdf_path), "456")

    def test_steamcmd_executable_reparse_is_rejected(self) -> None:
        executable = self.steamcmd_directory / "steamcmd.exe"
        original = Path.lstat

        def lstat(path, *args, **kwargs):
            if path == executable:
                return SimpleNamespace(st_mode=0o100644, st_file_attributes=0x400)
            return original(path, *args, **kwargs)

        with patch.object(Path, "lstat", lstat), self.assertRaisesRegex(workshop.WorkshopValidationError, "reparse"):
            workshop.prepare_workshop_item(self.settings)

    def test_settings_use_environment_and_public_visibility_by_default(self) -> None:
        arguments = SimpleNamespace(
            package_dir=str(self.package_directory),
            steamcmd_dir=None,
            vdf_path=None,
            preview=None,
            steam_username=None,
            visibility=None,
        )
        settings = workshop.settings_from_arguments(
            arguments,
            {
                "DIGITAL_STORAGE_STEAMCMD_DIR": str(self.steamcmd_directory),
                "DIGITAL_STORAGE_STEAM_USERNAME": "steam user",
                "DIGITAL_STORAGE_WORKSHOP_PREVIEW": str(self.preview_path),
            },
            repository_root=self.root,
        )
        self.assertEqual(settings.visibility, "0")
        self.assertEqual(settings.vdf_path, self.root / ".secrets" / "workshop.vdf")

    def test_visibility_argument_overrides_toml(self) -> None:
        arguments = workshop._arguments([
            "--package-dir", str(self.package_directory), "--steamcmd-dir", str(self.steamcmd_directory),
            "--preview", str(self.preview_path), "--steam-username", "test user", "--visibility", "2",
        ])
        settings = workshop.settings_from_arguments(arguments, {"DIGITAL_STORAGE_WORKSHOP_VISIBILITY": "0"}, self.root)
        self.assertEqual("2", settings.visibility)

    def test_invalid_visibility_is_rejected(self) -> None:
        arguments = SimpleNamespace(
            package_dir=str(self.package_directory),
            steamcmd_dir=str(self.steamcmd_directory),
            vdf_path=str(self.vdf_path),
            preview=str(self.preview_path),
            steam_username="steam user",
            visibility="3",
        )
        with self.assertRaisesRegex(workshop.WorkshopValidationError, "visibility must be 0, 1, or 2"):
            workshop.settings_from_arguments(arguments, {}, repository_root=self.root)


if __name__ == "__main__":
    unittest.main()
