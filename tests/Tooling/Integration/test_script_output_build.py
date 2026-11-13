import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
ARCHITECTURE = ROOT / "tests/Architecture/ProbablyStolen.DigitalStorage.ArchitectureTests/ProbablyStolen.DigitalStorage.ArchitectureTests.csproj"


class ScriptArtifactsIntegrationTests(unittest.TestCase):
    def test_build_and_architecture_tests_with_external_artifacts_and_foreign_cwd(self):
        with tempfile.TemporaryDirectory(prefix="build with spaces ") as directory:
            root = Path(directory)
            output = root / "external output"
            command = ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                       str(ROOT / "scripts/build.ps1"), "-Target", "Core", "-ArtifactsPath", str(output)]
            subprocess.run(command, cwd=root, check=True, capture_output=True, text=True, encoding="utf-8", errors="replace")
            result = subprocess.run(["dotnet", "test", str(ARCHITECTURE), "--no-build", "--no-restore",
                                     "--configuration", "Debug", f"-p:ArtifactsPath={output}"],
                                    cwd=root, check=True, capture_output=True, text=True, encoding="utf-8", errors="replace")
            self.assertIn("64", result.stdout)
            self.assertTrue((output / "bin" / "ProbablyStolen.DigitalStorage.Common" / "debug" /
                             "ProbablyStolen.DigitalStorage.Common.dll").is_file())
