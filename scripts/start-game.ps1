[CmdletBinding()]
param(
    [switch]$BuildAndDeploy,
    [switch]$Plan,
    [string]$GameDirectory,
    [string]$SteamLibraryDirectory,
    [string]$ArtifactsPath,
    [string]$PackageDirectory,
    [string]$DeployDirectory
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$python = Join-Path $repositoryRoot ".venv/Scripts/python.exe"
$toolPath = Join-Path $repositoryRoot "scripts/tool.py"

function Assert-GameStopped {
    param([string]$ExecutablePath)

    $runningGames = @(Get-CimInstance -ClassName Win32_Process -Filter "Name = 'Probably Stolen.exe'" -ErrorAction Stop)
    foreach ($runningGame in $runningGames) {
        if ([string]::IsNullOrWhiteSpace($runningGame.ExecutablePath)) {
            throw 'A Probably Stolen process is running but its installation path cannot be verified. Close it before continuing.'
        }
        $runningPath = [System.IO.Path]::GetFullPath($runningGame.ExecutablePath)
        if ([string]::Equals($runningPath, $ExecutablePath, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw 'The selected Probably Stolen installation is running. Close the game before building, deploying or launching it again.'
        }
    }
}

function Invoke-GameWorkflowStep {
    param(
        [string]$ScriptName,
        [string[]]$Arguments
    )

    $scriptPath = Join-Path $PSScriptRoot $ScriptName
    Write-Host ('> powershell.exe ' + $ScriptName + ' ' + ($Arguments -join ' '))
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $scriptPath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$ScriptName exited with code $LASTEXITCODE. The game was not launched."
    }
}

function Invoke-GameWorkflow {
    param(
        [switch]$BuildAndDeploy,
        [switch]$Plan,
        [string]$GameDirectory,
        [string]$SteamLibraryDirectory,
        [string]$ArtifactsPath,
        [string]$PackageDirectory,
        [string]$DeployDirectory
    )

    $resolverArguments = @($toolPath, 'environment', '--target', 'deploy', '--json')
    $pathArguments = @{
        '--game-dir' = $GameDirectory
        '--steam-library-dir' = $SteamLibraryDirectory
        '--artifacts-path' = $ArtifactsPath
        '--package-dir' = $PackageDirectory
        '--deploy-dir' = $DeployDirectory
    }
    foreach ($argumentName in $pathArguments.Keys) {
        if ($pathArguments[$argumentName]) {
            $resolverArguments += @($argumentName, $pathArguments[$argumentName])
        }
    }
    $resolverOutput = & $python -B @resolverArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Environment resolution exited with code $LASTEXITCODE."
    }
    $environment = ($resolverOutput -join [Environment]::NewLine) | ConvertFrom-Json
    $gameExecutable = [System.IO.Path]::GetFullPath((Join-Path $environment.game_directory 'Probably Stolen.exe'))

    Write-Host "Game executable: $gameExecutable"
    Write-Host "Working directory: $($environment.game_directory)"
    if ($BuildAndDeploy) {
        Write-Host "Package directory: $($environment.package_directory)"
        Write-Host "Deployment directory: $($environment.deploy_directory)"
        Write-Host 'Order: Release build and package validation -> owned mod deployment -> game launch.'
    }
    else {
        Write-Host 'Order: game launch using the currently installed mod files.'
    }
    if ($Plan) {
        Write-Host '[PLAN] Paths resolved. No build, deployment or process launch was performed.'
        return
    }

    Assert-GameStopped -ExecutablePath $gameExecutable
    if ($BuildAndDeploy) {
        $packageArguments = @(
            '-GameDirectory', $environment.game_directory,
            '-ArtifactsPath', $environment.output_directory,
            '-PackageDirectory', $environment.package_directory
        )
        if ($environment.steam_library_directory) {
            $packageArguments += @('-SteamLibraryDirectory', $environment.steam_library_directory)
        }
        Invoke-GameWorkflowStep -ScriptName 'package.ps1' -Arguments $packageArguments
        Invoke-GameWorkflowStep -ScriptName 'deploy.ps1' -Arguments @(
            '-Action', 'Deploy',
            '-GameDirectory', $environment.game_directory,
            '-PackageDirectory', $environment.package_directory,
            '-DeployDirectory', $environment.deploy_directory
        )
        Assert-GameStopped -ExecutablePath $gameExecutable
    }

    $gameProcess = Start-Process -FilePath $gameExecutable -WorkingDirectory $environment.game_directory -PassThru -ErrorAction Stop
    Write-Host "[PASS] Game process started (PID $($gameProcess.Id)). Confirm mod loading in the game manually."
}

if ($MyInvocation.InvocationName -ne '.') {
    Push-Location $repositoryRoot
    try {
        Invoke-GameWorkflow @PSBoundParameters
    }
    catch {
        [Console]::Error.WriteLine("[FAIL] $($_.Exception.Message)")
        exit 1
    }
    finally {
        Pop-Location
    }
}
