import os
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

from digital_storage_tools import commands


class CommandTests(unittest.TestCase):
    def test_deploy_and_launch_use_guarded_workflows_and_forward_args(self):
        for command, script in (("deploy", "deploy.ps1"), ("remove", "deploy.ps1"), ("launch", "start-game.ps1")):
            with self.subTest(command=command), patch.object(commands.subprocess, "run", return_value=SimpleNamespace(returncode=19)) as run:
                self.assertEqual(commands.run(command, ["-GameDirectory", "a path"]), 19)
                argv = run.call_args.args[0]
                self.assertEqual(Path(argv[5]).name, script)
                self.assertEqual(argv[-2:], ["-GameDirectory", "a path"])

    def test_check_ci_uses_mise_and_propagates_missing_tool_failure(self):
        with patch.object(commands.subprocess, "run", return_value=SimpleNamespace(returncode=127)) as run:
            self.assertEqual(commands.run("check-ci", []), 127)
            self.assertEqual(run.call_args.args[0], ["mise", "exec", "--", "actionlint"])

    def test_ci_core_stops_at_first_failure_without_side_effect_commands(self):
        calls = []
        def fake_run(argv, **kwargs):
            calls.append(argv[-1])
            return SimpleNamespace(returncode=13 if argv[-1] == "check-format" else 0)
        with patch.object(commands.subprocess, "run", side_effect=fake_run):
            self.assertEqual(commands.run("ci-core", []), 13)
        self.assertEqual(calls, ["check-source", "check-assets", "check-ci", "check-format"])

    def test_format_rejects_protected_paths_before_dotnet(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(commands, "ROOT", Path(directory).resolve()):
            root = Path(directory).resolve()
            for path in (root / ".reference" / "output", root / "game" / "Mods"):
                with self.subTest(path=path), patch.dict(os.environ, {"ArtifactsPath": str(path), "DIGITAL_STORAGE_GAME_DIR": str(root / "game")}, clear=True), patch.object(commands.subprocess, "run") as run:
                    with self.assertRaises(ValueError):
                        commands.run("check-format", [])
                    run.assert_not_called()
            with patch.dict(os.environ, {"ArtifactsPath": str(root / "external output")}, clear=True), patch.object(commands.subprocess, "run", return_value=SimpleNamespace(returncode=0)) as run:
                self.assertEqual(commands.run("check-format", []), 0)
                self.assertIn(f'-p:ArtifactsPath={root / "external output"}', run.call_args_list[0].args[0])
                self.assertTrue(all(call.kwargs['env']['ArtifactsPath'] == str(root / 'external output') for call in run.call_args_list))

    def test_local_values_are_strings_and_environment_takes_precedence(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(commands, "ROOT", Path(directory)):
            (Path(directory) / "mise.local.toml").write_text('[env]\nDIGITAL_STORAGE_GAME_DIR = "local path"\n', encoding="utf-8")
            with patch.dict(os.environ, {"DIGITAL_STORAGE_GAME_DIR": "env path"}):
                self.assertEqual(commands.local_environment()["DIGITAL_STORAGE_GAME_DIR"], "env path")
            (Path(directory) / "mise.local.toml").write_text('[env]\nDIGITAL_STORAGE_GAME_DIR = 123\n', encoding="utf-8")
            with self.assertRaises(ValueError):
                commands.local_environment()
