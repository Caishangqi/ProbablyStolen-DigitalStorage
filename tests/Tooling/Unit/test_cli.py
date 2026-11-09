import importlib.metadata
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

from digital_storage_tools import cli


class BootstrapTests(unittest.TestCase):
    def setUp(self):
        root = Path(cli.__file__).resolve().parents[2]
        patches = (
            patch.object(cli.sys, "prefix", str(root / ".venv")),
            patch.object(cli.sys, "version_info", (3, 13, 7)),
            patch.object(cli.importlib.metadata, "version", return_value="12.0.0"),
            patch.object(cli.shutil, "which", return_value="mise.exe"),
            patch.object(cli.subprocess, "run", return_value=SimpleNamespace(returncode=0)),
        )
        for replacement in patches:
            replacement.start()
            self.addCleanup(replacement.stop)

    def test_prepared_environment_is_accepted(self):
        cli.check_environment()

    def test_non_project_environment_is_rejected(self):
        with patch.object(cli.sys, "prefix", "not-the-project-venv"):
            with self.assertRaisesRegex(RuntimeError, "repository .venv"):
                cli.check_environment()

    def test_wrong_python_version_is_rejected(self):
        with patch.object(cli.sys, "version_info", (3, 13, 6)):
            with self.assertRaisesRegex(RuntimeError, "Python 3.13.7"):
                cli.check_environment()

    def test_missing_or_wrong_pillow_is_rejected(self):
        for value in ("11.0.0", importlib.metadata.PackageNotFoundError("Pillow")):
            with self.subTest(value=value), patch.object(cli.importlib.metadata, "version", side_effect=value if isinstance(value, Exception) else None, return_value=value):
                with self.assertRaisesRegex(RuntimeError, "Pillow 12.0.0"):
                    cli.check_environment()

    def test_lock_mismatch_is_rejected(self):
        with patch.object(cli.subprocess, "run") as run:
            run.return_value.returncode = 1
            with self.assertRaisesRegex(RuntimeError, "environment or lock"):
                cli.check_environment()

    def test_arguments_are_forwarded_to_module(self):
        with patch("digital_storage_tools.environment.main", return_value=7) as run:
            self.assertEqual(cli.main(["environment", "--target", "core"]), 7)
            run.assert_called_once_with(["--target", "core"])

    def test_command_help_is_forwarded_to_the_selected_tool(self):
        with patch("digital_storage_tools.environment.main", return_value=0) as run:
            self.assertEqual(cli.main(["environment", "--help"]), 0)
            run.assert_called_once_with(["--help"])

    def test_missing_mise_fails_before_running_a_tool(self):
        with patch.object(cli.shutil, "which", return_value=None):
            with self.assertRaisesRegex(RuntimeError, "mise is required"):
                cli.check_environment()
