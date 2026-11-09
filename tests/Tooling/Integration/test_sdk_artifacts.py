import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
PROJECT = ROOT / "src/ProbablyStolen.DigitalStorage.Common/ProbablyStolen.DigitalStorage.Common.csproj"


class SdkArtifactsTests(unittest.TestCase):
    def query(self, *args):
        result = subprocess.run(["dotnet", "msbuild", str(PROJECT), "-getProperty:ArtifactsPath", "-getProperty:TargetPath", *args],
                                cwd=ROOT, check=True, capture_output=True, text=True)
        return json.loads(result.stdout)["Properties"]

    def test_sdk_default_and_native_override(self):
        default = self.query()
        self.assertEqual(Path(default["ArtifactsPath"]), ROOT / "artifacts")
        self.assertTrue(Path(default["TargetPath"]).is_relative_to(ROOT / "artifacts" / "bin"))
        with tempfile.TemporaryDirectory(prefix="artifacts path ") as directory:
            output = Path(directory) / "external output"
            override = self.query(f"-p:ArtifactsPath={output}")
            self.assertEqual(Path(override["ArtifactsPath"]), output)
            self.assertTrue(Path(override["TargetPath"]).is_relative_to(output))

    def test_solution_filter_inventory_matches_game_independent_projects(self):
        solution = subprocess.run(["dotnet", "sln", str(ROOT / "DigitalStorage.sln"), "list"],
                                  check=True, capture_output=True, text=True, encoding="utf-8", errors="replace")
        projects = {line.strip().replace("\\", "/") for line in solution.stdout.splitlines() if line.strip().endswith(".csproj")}
        selected = set(json.loads((ROOT / "DigitalStorage.Core.slnf").read_text(encoding="utf-8"))["solution"]["projects"])
        self.assertEqual(len(projects), 9)
        self.assertEqual(len(selected), 6)
        self.assertTrue(selected < projects)
        self.assertEqual({Path(p).stem for p in projects - selected}, {
            "ProbablyStolen.DigitalStorage.Platform", "ProbablyStolen.DigitalStorage.Mod",
            "ProbablyStolen.DigitalStorage.Platform.Tests"})

    def test_tool_rejects_protected_and_escaping_paths(self):
        tool = ROOT / "scripts/tool.py"
        for path in (str(ROOT / ".reference" / "protected"), "../outside"):
            with self.subTest(path=path):
                result = subprocess.run([sys.executable, "-B", str(tool), "environment", "--artifacts-path", path],
                                        cwd=ROOT, capture_output=True, text=True)
                self.assertNotEqual(result.returncode, 0)
