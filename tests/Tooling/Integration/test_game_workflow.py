"""Verify the development game workflow without starting or modifying a real game."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[3]
POWERSHELL = shutil.which("powershell.exe")
DRIVER = r"""
$ErrorActionPreference = 'Stop'
. (Join-Path $env:WORKFLOW_TEST_REPOSITORY 'scripts/start-game.ps1')
$script:workflowEvents = New-Object 'System.Collections.Generic.List[string]'
$script:workflowArguments = @{}
$script:processChecks = 0

function Get-CimInstance {
    param($ClassName, $Filter, $ErrorAction)
    $script:processChecks++
    switch ($env:WORKFLOW_TEST_RUNNING) {
        'selected' { return [pscustomobject]@{ ExecutablePath = (Join-Path $env:WORKFLOW_TEST_GAME 'Probably Stolen.exe') } }
        'unknown' { return [pscustomobject]@{ ExecutablePath = $null } }
        'other' { return [pscustomobject]@{ ExecutablePath = (Join-Path $env:WORKFLOW_TEST_ROOT 'other/Probably Stolen.exe') } }
        'after-deploy' {
            if ($script:processChecks -gt 1) {
                return [pscustomobject]@{ ExecutablePath = (Join-Path $env:WORKFLOW_TEST_GAME 'Probably Stolen.exe') }
            }
        }
    }
}

function Invoke-GameWorkflowStep {
    param([string]$ScriptName, [string[]]$Arguments)
    $script:workflowEvents.Add($ScriptName)
    $script:workflowArguments[$ScriptName] = $Arguments
    if ($env:WORKFLOW_TEST_FAILURE -eq $ScriptName) {
        throw "Injected failure in $ScriptName."
    }
    if ($ScriptName -eq 'package.ps1' -and $env:WORKFLOW_TEST_VALIDATE_PACKAGE) {
        & (Join-Path $env:WORKFLOW_TEST_REPOSITORY '.venv/Scripts/python.exe') -B (Join-Path $env:WORKFLOW_TEST_REPOSITORY 'scripts/tool.py') verify-package --package-dir $env:WORKFLOW_TEST_VALIDATE_PACKAGE
        if ($LASTEXITCODE -ne 0) { throw 'Actual package validation failed.' }
    }
    if ($ScriptName -eq 'package.ps1' -and $env:WORKFLOW_TEST_RESOURCE_OUTPUT) {
        & (Join-Path $env:WORKFLOW_TEST_REPOSITORY '.venv/Scripts/python.exe') -B (Join-Path $env:WORKFLOW_TEST_REPOSITORY 'scripts/tool.py') assets --check --output-dir $env:WORKFLOW_TEST_RESOURCE_OUTPUT
        if ($LASTEXITCODE -ne 0) { throw 'Actual resource validation failed.' }
    }
}

function Start-Process {
    param($FilePath, $WorkingDirectory, [switch]$PassThru, $ErrorAction)
    $script:workflowEvents.Add('launch')
    $script:workflowArguments['launch'] = @($FilePath, $WorkingDirectory)
    if ($env:WORKFLOW_TEST_FAILURE -eq 'launch') {
        throw 'Injected process launch failure.'
    }
    return [pscustomobject]@{ Id = 12345 }
}

$failed = $false
$message = ''
try {
    Invoke-GameWorkflow -GameDirectory $env:WORKFLOW_TEST_GAME `
        -ArtifactsPath (Join-Path $env:WORKFLOW_TEST_ROOT 'build outputs') `
        -PackageDirectory (Join-Path $env:WORKFLOW_TEST_ROOT 'package outputs/DigitalStorage') `
        -BuildAndDeploy:($env:WORKFLOW_TEST_BUILD -eq '1') `
        -Plan:($env:WORKFLOW_TEST_PLAN -eq '1')
}
catch {
    $failed = $true
    $message = $_.Exception.Message
}
$result = @{
    failed = $failed
    message = $message
    events = @($script:workflowEvents.ToArray())
    arguments = $script:workflowArguments
}
Write-Output ('WORKFLOW_RESULT=' + ($result | ConvertTo-Json -Depth 5 -Compress))
"""


class GameWorkflowTests(unittest.TestCase):
    """Exercise ordering, process checks and failure propagation with isolated inputs."""

    @classmethod
    def setUpClass(cls):
        if POWERSHELL is None:
            raise RuntimeError("Game workflow tests require Windows PowerShell (powershell.exe).")

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="digital-storage-workflow-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()
        self.game = self.root / "Game With Spaces"
        for relative in (
            "Probably Stolen.exe",
            "Probably Stolen_Data/Managed/Assembly-CSharp.dll",
            "Probably Stolen_Data/Managed/UnityEngine.dll",
            "Probably Stolen_Data/Managed/UnityEngine.CoreModule.dll",
            "UnrelatedData/preserve.txt",
            "Mods/OtherMod/preserve.txt",
        ):
            path = self.game / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_bytes(b"untouched test input")
        self.driver = self.root / "workflow driver.ps1"
        self.driver.write_text(DRIVER, encoding="utf-8")

    def environment(self, **values: str) -> dict[str, str]:
        environment = os.environ.copy()
        for name in tuple(environment):
            if name.startswith("DIGITAL_STORAGE_"):
                environment.pop(name)
        environment.update(
            WORKFLOW_TEST_REPOSITORY=str(REPOSITORY_ROOT),
            WORKFLOW_TEST_ROOT=str(self.root),
            WORKFLOW_TEST_GAME=str(self.game),
            WORKFLOW_TEST_BUILD="0",
            WORKFLOW_TEST_PLAN="0",
            WORKFLOW_TEST_RUNNING="",
            WORKFLOW_TEST_FAILURE="",
            WORKFLOW_TEST_VALIDATE_PACKAGE="",
            WORKFLOW_TEST_RESOURCE_OUTPUT="",
        )
        environment.update(values)
        return environment

    def run_workflow(self, **values: str) -> dict[str, object]:
        before = self.snapshot()
        result = subprocess.run(
            [POWERSHELL, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(self.driver)],
            cwd=REPOSITORY_ROOT,
            env=self.environment(**values),
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=30,
            check=True,
        )
        self.assertEqual(before, self.snapshot(), "The isolated game files must remain unchanged.")
        payload = next(line for line in result.stdout.splitlines() if line.startswith("WORKFLOW_RESULT="))
        return json.loads(payload.removeprefix("WORKFLOW_RESULT="))

    def snapshot(self) -> dict[str, bytes]:
        return {str(path.relative_to(self.game)): path.read_bytes() for path in self.game.rglob("*") if path.is_file()}

    def test_launch_only_does_not_build_or_deploy(self):
        result = self.run_workflow()
        self.assertFalse(result["failed"])
        self.assertEqual(["launch"], result["events"])
        self.assertEqual(
            [str(self.game / "Probably Stolen.exe"), str(self.game)], result["arguments"]["launch"]
        )

    def test_build_deploy_and_launch_run_in_order(self):
        result = self.run_workflow(WORKFLOW_TEST_BUILD="1")
        self.assertFalse(result["failed"])
        self.assertEqual(["package.ps1", "deploy.ps1", "launch"], result["events"])
        self.assertIn(str(self.game), result["arguments"]["package.ps1"])
        self.assertIn(str(self.game / "Mods/DigitalStorage"), result["arguments"]["deploy.ps1"])

    def test_package_failure_prevents_deployment_and_launch(self):
        result = self.run_workflow(WORKFLOW_TEST_BUILD="1", WORKFLOW_TEST_FAILURE="package.ps1")
        self.assertTrue(result["failed"])
        self.assertEqual(["package.ps1"], result["events"])

    def test_deployment_failure_prevents_launch(self):
        result = self.run_workflow(WORKFLOW_TEST_BUILD="1", WORKFLOW_TEST_FAILURE="deploy.ps1")
        self.assertTrue(result["failed"])
        self.assertEqual(["package.ps1", "deploy.ps1"], result["events"])

    def test_selected_running_game_prevents_all_actions(self):
        result = self.run_workflow(WORKFLOW_TEST_BUILD="1", WORKFLOW_TEST_RUNNING="selected")
        self.assertTrue(result["failed"])
        self.assertEqual([], result["events"])

    def test_unverifiable_running_game_prevents_all_actions(self):
        result = self.run_workflow(WORKFLOW_TEST_BUILD="1", WORKFLOW_TEST_RUNNING="unknown")
        self.assertTrue(result["failed"])
        self.assertEqual([], result["events"])

    def test_other_game_installation_does_not_block_selected_launch(self):
        result = self.run_workflow(WORKFLOW_TEST_RUNNING="other")
        self.assertFalse(result["failed"])
        self.assertEqual(["launch"], result["events"])

    def test_game_started_during_build_prevents_duplicate_launch(self):
        result = self.run_workflow(WORKFLOW_TEST_BUILD="1", WORKFLOW_TEST_RUNNING="after-deploy")
        self.assertTrue(result["failed"])
        self.assertEqual(["package.ps1", "deploy.ps1"], result["events"])

    def test_plan_does_not_build_deploy_or_launch(self):
        result = self.run_workflow(WORKFLOW_TEST_BUILD="1", WORKFLOW_TEST_PLAN="1")
        self.assertFalse(result["failed"])
        self.assertEqual([], result["events"])

    def test_missing_game_assembly_prevents_all_actions(self):
        (self.game / "Probably Stolen_Data/Managed/Assembly-CSharp.dll").unlink()
        result = self.run_workflow(WORKFLOW_TEST_BUILD="1")
        self.assertTrue(result["failed"])
        self.assertEqual([], result["events"])

    def test_plan_resolves_without_optional_console_sdk(self):
        for name in ("QFSW.QC.dll", "QFSW.QC.Suggestors.dll", "QFSW.QC.ScanRules.dll"):
            self.assertFalse((self.game / "Probably Stolen_Data/Managed" / name).exists())
        result = self.run_workflow(WORKFLOW_TEST_BUILD="1", WORKFLOW_TEST_PLAN="1")
        self.assertFalse(result["failed"])
        self.assertEqual([], result["events"])

    def test_eleven_file_package_and_actual_layout_failure_propagation(self):
        from digital_storage_tools.package import CURRENT_FILES
        package = self.root / "candidate package"
        for name in CURRENT_FILES:
            path = package / name
            path.parent.mkdir(parents=True, exist_ok=True)
            if name == "manifest.xml":
                shutil.copyfile(REPOSITORY_ROOT / "packaging/manifest.xml", path)
            else:
                path.write_bytes(b"synthetic workflow package file")
        result = self.run_workflow(WORKFLOW_TEST_BUILD="1", WORKFLOW_TEST_VALIDATE_PACKAGE=str(package))
        self.assertFalse(result["failed"])
        self.assertEqual(["package.ps1", "deploy.ps1", "launch"], result["events"])
        (package / "Localization/en.csv").unlink()
        result = self.run_workflow(WORKFLOW_TEST_BUILD="1", WORKFLOW_TEST_VALIDATE_PACKAGE=str(package))
        self.assertTrue(result["failed"])
        self.assertEqual(["package.ps1"], result["events"])

    def test_actual_resource_failure_prevents_deployment_and_launch(self):
        result = self.run_workflow(WORKFLOW_TEST_BUILD="1", WORKFLOW_TEST_RESOURCE_OUTPUT=str(self.root / "missing resources"))
        self.assertTrue(result["failed"])
        self.assertEqual(["package.ps1"], result["events"])

    def test_process_launch_failure_is_reported(self):
        result = self.run_workflow(WORKFLOW_TEST_FAILURE="launch")
        self.assertTrue(result["failed"])
        self.assertEqual(["launch"], result["events"])

    def test_child_process_failure_is_not_treated_as_success(self):
        self.driver.write_text(
            r"""
$ErrorActionPreference = 'Stop'
. (Join-Path $env:WORKFLOW_TEST_REPOSITORY 'scripts/start-game.ps1')
function powershell.exe { $global:LASTEXITCODE = 17 }
try {
    Invoke-GameWorkflowStep -ScriptName 'package.ps1' -Arguments @('-GameDirectory', $env:WORKFLOW_TEST_GAME)
    exit 0
}
catch {
    Write-Output $_.Exception.Message
    exit 17
}
""",
            encoding="utf-8",
        )
        result = subprocess.run(
            [POWERSHELL, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(self.driver)],
            cwd=REPOSITORY_ROOT,
            env=self.environment(),
            capture_output=True,
            text=True,
            timeout=30,
            check=False,
        )
        self.assertEqual(17, result.returncode)
        self.assertIn("exited with code 17", result.stdout)

    def test_entry_point_returns_nonzero_for_invalid_game_path(self):
        result = subprocess.run(
            [
                POWERSHELL, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                str(REPOSITORY_ROOT / "scripts/start-game.ps1"),
                "-GameDirectory", str(self.root / "absent game"), "-BuildAndDeploy",
            ],
            cwd=REPOSITORY_ROOT,
            env=self.environment(),
            capture_output=True,
            text=True,
            timeout=30,
            check=False,
        )
        self.assertNotEqual(0, result.returncode)
        self.assertIn("[FAIL]", result.stderr)


if __name__ == "__main__":
    unittest.main()
