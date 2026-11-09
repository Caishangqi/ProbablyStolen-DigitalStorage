import os
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]


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

    def test_unreviewed_platform_source_fails_before_building_or_testing(self):
        self._assert_unreviewed_platform_source_rejected(skip_review=False)

    def test_skipped_review_still_enforces_platform_source_inventory(self):
        result = self._assert_unreviewed_platform_source_rejected(skip_review=True)
        self.assertIn("[SKIP] Platform review gate disabled", result.stdout)
        self.assertNotIn("reviewed inputs changed", result.stderr)

    def _assert_unreviewed_platform_source_rejected(self, skip_review):
        with tempfile.TemporaryDirectory(prefix="platform scope ") as directory:
            temp = Path(directory)
            game = temp / "game"
            for relative in (
                "Probably Stolen.exe",
                "Probably Stolen_Data/Managed/Assembly-CSharp.dll",
                "Probably Stolen_Data/Managed/UnityEngine.dll",
                "Probably Stolen_Data/Managed/UnityEngine.CoreModule.dll",
            ):
                target = game / relative
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(b"fixture")
            driver = temp / "driver.ps1"
            driver.write_text(r'''
$ErrorActionPreference = 'Stop'
function Get-ChildItem {
    [CmdletBinding()]
    param([string]$LiteralPath, [switch]$File, [string]$Filter, [switch]$Recurse)
    if ($LiteralPath -eq $env:PLATFORM_SOURCE_ROOT) {
        foreach ($name in @('CanonicalFilterKeyCodecTests.cs', 'NativeItemIdentityReaderTests.cs', 'UnreviewedTests.cs')) {
            [pscustomobject]@{ FullName = (Join-Path $LiteralPath $name) }
        }
        return
    }
    Microsoft.PowerShell.Management\Get-ChildItem @PSBoundParameters
}
$skip = $env:RUNNER_SKIP_REVIEW -eq '1'
& (Join-Path $env:RUNNER_ROOT 'scripts/test.ps1') -Target Platform -GameDirectory $env:RUNNER_GAME -ArtifactsPath $env:RUNNER_OUTPUT -SkipPlatformReview:$skip
exit $LASTEXITCODE
''', encoding="utf-8")
            env = os.environ.copy()
            env.pop("DIGITAL_STORAGE_STEAM_LIBRARY_DIR", None)
            env.update(
                RUNNER_ROOT=str(ROOT), RUNNER_GAME=str(game), RUNNER_OUTPUT=str(temp / "output"),
                PLATFORM_SOURCE_ROOT=str(ROOT / "tests/Unit/ProbablyStolen.DigitalStorage.Platform.Tests"),
                RUNNER_SKIP_REVIEW="1" if skip_review else "0",
            )
            result = subprocess.run(
                ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(driver)],
                cwd=temp, env=env, capture_output=True, text=True, encoding="utf-8", errors="replace",
            )
            self.assertNotEqual(result.returncode, 0)
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
