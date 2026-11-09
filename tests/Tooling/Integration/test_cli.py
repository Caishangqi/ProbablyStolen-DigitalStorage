import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]


class CliIntegrationTests(unittest.TestCase):
    def test_environment_command_from_foreign_directory_with_spaces(self):
        with tempfile.TemporaryDirectory(prefix="tool cwd ") as cwd:
            result = subprocess.run(
                [sys.executable, "-B", str(ROOT / "scripts/tool.py"), "environment", "--target", "core", "--json"],
                cwd=cwd, capture_output=True, text=True, check=True,
            )
        self.assertEqual(Path(json.loads(result.stdout)["repository_root"]), ROOT)

    def test_unknown_command_fails(self):
        result = subprocess.run(
            [sys.executable, "-B", str(ROOT / "scripts/tool.py"), "unknown"],
            capture_output=True, text=True,
        )
        self.assertNotEqual(result.returncode, 0)
