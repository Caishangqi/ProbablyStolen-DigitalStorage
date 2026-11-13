import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]


def _runner_fixture(root):
    scripts = root / "scripts"
    scripts.mkdir(parents=True)
    shutil.copy2(ROOT / "scripts/test.ps1", scripts / "test.ps1")
    shutil.copy2(ROOT / "scripts/digital_storage_tools/platform_review.py", scripts / "platform_review.py")
    python = root / ".venv/Scripts/python.exe"
    python.parent.mkdir(parents=True)
    shutil.copy2(sys.executable, python)
    shutil.copy2(ROOT / ".venv/pyvenv.cfg", root / ".venv/pyvenv.cfg")
    (root / "DigitalStorage.Core.slnf").write_text('{"solution":{"projects":[]}}', encoding="utf-8")
    (scripts / "tool.py").write_text('''\
import json
import sys
from pathlib import Path
from platform_review import validate_api

root = Path(__file__).resolve().parents[1]
if sys.argv[1] == "environment":
    print(json.dumps({"game_directory": sys.argv[sys.argv.index("--game-dir") + 1],
                      "output_directory": str(root / "output")}))
elif sys.argv[1] == "verify-api-review":
    try:
        validate_api(root)
    except (OSError, ValueError, KeyError) as error:
        print("Api review error: " + str(error), file=sys.stderr)
        sys.exit(1)
elif sys.argv[1] == "verify-platform-review":
    print("fixture Platform review passed")
else:
    sys.exit("unexpected fixture command")
''', encoding="utf-8")
    game = root / "game"
    for relative in ("Probably Stolen.exe", "Probably Stolen_Data/Managed/Assembly-CSharp.dll",
                     "Probably Stolen_Data/Managed/UnityEngine.dll",
                     "Probably Stolen_Data/Managed/UnityEngine.CoreModule.dll"):
        path = game / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(b"fixture")
    return game


class NUnitRunnerTests(unittest.TestCase):
    def test_unlisted_test_project_is_rejected_before_build(self):
        with tempfile.TemporaryDirectory(prefix='unlisted-test-', dir=ROOT / 'tests') as directory:
            (Path(directory) / 'Unlisted.csproj').write_text('<Project/>', encoding='utf-8')
            result = subprocess.run(['powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
                                     str(ROOT / 'scripts/test.ps1'), '-Target', 'Common'], cwd=ROOT,
                                    capture_output=True, text=True, encoding='utf-8', errors='replace')
            self.assertNotEqual(result.returncode, 0)
            self.assertIn('do not match the declared inventory', result.stderr)
            self.assertNotIn('> dotnet', result.stdout)

    def test_all_requires_game_before_suites(self):
        env = os.environ.copy()
        env.pop("DIGITAL_STORAGE_GAME_DIR", None)
        env.pop("DIGITAL_STORAGE_STEAM_LIBRARY_DIR", None)
        result = subprocess.run(["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                                 str(ROOT / "scripts/test.ps1"), "-Target", "All"], cwd=ROOT, env=env,
                                capture_output=True, text=True, encoding="utf-8", errors="replace")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Environment resolution failed", result.stderr)
        self.assertNotIn("dotnet test", result.stdout)

    def test_unapproved_api_scope_fails_before_build_without_platform_review(self):
        with tempfile.TemporaryDirectory(prefix="api runner ") as directory:
            root = Path(directory)
            game = _runner_fixture(root)
            result = subprocess.run(
                ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                 str(root / "scripts/test.ps1"), "-Target", "Api", "-GameDirectory", str(game)],
                cwd=root, capture_output=True, text=True, encoding="utf-8", errors="replace")
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("Api review error", result.stderr)
            self.assertNotIn("Platform review", result.stderr)
            self.assertNotIn("> dotnet", result.stdout)

    def test_unreviewed_platform_source_fails_before_build(self):
        self._assert_unreviewed_platform_source_rejected(skip_review=False)

    def test_skipped_review_still_enforces_platform_source_inventory(self):
        result = self._assert_unreviewed_platform_source_rejected(skip_review=True)
        self.assertIn("[SKIP] Platform review gate disabled", result.stdout)

    def _assert_unreviewed_platform_source_rejected(self, skip_review):
        with tempfile.TemporaryDirectory(prefix="platform runner ") as directory:
            root = Path(directory)
            game = _runner_fixture(root)
            sources = root / "tests/Unit/ProbablyStolen.DigitalStorage.Platform.Tests"
            sources.mkdir(parents=True)
            for name in ("CanonicalFilterKeyCodecTests.cs", "ItemContentSynchronizationTests.cs",
                         "NativeItemIdentityReaderTests.cs", "UnreviewedTests.cs"):
                (sources / name).write_text("", encoding="utf-8")
            command = ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                       str(root / "scripts/test.ps1"), "-Target", "Platform", "-GameDirectory", str(game)]
            if skip_review:
                command.append("-SkipPlatformReview")
            result = subprocess.run(command, cwd=root, capture_output=True, text=True,
                                    encoding="utf-8", errors="replace")
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("fixture Platform review passed", result.stdout)
            self.assertIn("Platform test sources changed", result.stderr)
            self.assertNotIn("> dotnet", result.stdout)
            return result

    def test_failure_empty_skipped_and_bad_report_fail_closed_and_redact(self):
        with tempfile.TemporaryDirectory(prefix="runner test ") as directory:
            temp = Path(directory)
            driver = temp / "driver.ps1"
            driver.write_text(r'''
$ErrorActionPreference = 'Stop'
function dotnet {
    if ($args[0] -eq 'test') {
        $logger = $args[[array]::IndexOf($args, '--logger') + 1]
        $name = ($logger -split 'LogFileName=')[1]
        $dir = $args[[array]::IndexOf($args, '--results-directory') + 1]
        $path = Join-Path $dir $name
        if ($env:RUNNER_SCENARIO -eq 'malformed') { Set-Content -LiteralPath $path -Value 'not xml'; $global:LASTEXITCODE = 0; return }
        $total = if ($env:RUNNER_SCENARIO -eq 'zero') { 0 } else { 1 }
        $passed = if ($env:RUNNER_SCENARIO -eq 'success') { 1 } else { 0 }
        $failed = if ($env:RUNNER_SCENARIO -eq 'failure') { 1 } else { 0 }
        $outcome = if ($env:RUNNER_SCENARIO -eq 'success') { 'Passed' } else { 'Failed' }
        $summary = if ($env:RUNNER_SCENARIO -eq 'success') { 'Completed' } else { 'Failed' }
        $xml = '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><ResultSummary outcome="' + $summary + '"><Counters total="' + $total + '" executed="' + $total + '" passed="' + $passed + '" failed="' + $failed + '" error="0" timeout="0" aborted="0" inconclusive="0" passedButRunAborted="0" notRunnable="0" notExecuted="0" disconnected="0" warning="0" completed="0" inProgress="0" pending="0" /></ResultSummary><Results><UnitTestResult testId="id" executionId="exec" outcome="' + $outcome + '" computerName="' + $env:USERNAME + '" testName="' + $env:RUNNER_ROOT + '" /></Results><TestDefinitions><UnitTest id="id"><Execution id="exec"/><TestMethod className="Fixture.Tests" name="Test"/></UnitTest></TestDefinitions></TestRun>'
        Set-Content -LiteralPath $path -Value $xml
        $global:LASTEXITCODE = if ($failed) { 1 } else { 0 }
        return
    }
    $global:LASTEXITCODE = 0
}
& (Join-Path $env:RUNNER_ROOT 'scripts/test.ps1') -Target Common -ArtifactsPath $env:RUNNER_OUTPUT
exit $LASTEXITCODE
''', encoding="utf-8")
            scenarios = [(name, None) for name in ("success", "failure", "zero", "skipped", "malformed")]
            scenarios += [("success", username) for username in ("test", "Passed", "Completed", "1")]
            for scenario, username in scenarios:
                output = temp / (scenario if username is None else "overlap-" + username)
                env = os.environ.copy()
                env.update(RUNNER_ROOT=str(ROOT), RUNNER_OUTPUT=str(output), RUNNER_SCENARIO=scenario)
                if username is not None:
                    env["USERNAME"] = username
                result = subprocess.run(["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(driver)],
                                        env=env, cwd=temp, capture_output=True, text=True, encoding="utf-8", errors="replace")
                self.assertEqual(result.returncode == 0, scenario == "success", result.stdout + result.stderr)
                reports = list(output.rglob("*.trx"))
                if scenario == "malformed":
                    self.assertEqual(reports, [])
                else:
                    self.assertEqual(len(reports), 1)
                    report = reports[0].read_text(encoding="utf-8")
                    self.assertNotIn(str(ROOT).lower(), report.lower())
                    self.assertIn("[redacted]", report)
                    if username is not None:
                        import xml.etree.ElementTree as ET
                        namespace = {"v": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
                        document = ET.fromstring(report)
                        method = document.find("v:TestDefinitions/v:UnitTest/v:TestMethod", namespace)
                        self.assertEqual(method.attrib, {"className": "Fixture.Tests", "name": "Test"})
                        self.assertEqual(document.find("v:Results/v:UnitTestResult", namespace).get("outcome"), "Passed")
