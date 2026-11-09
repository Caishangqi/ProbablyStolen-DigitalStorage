[CmdletBinding()]
param(
    [ValidateSet('Deploy', 'Remove')]
    [string]$Action = 'Deploy',
    [string]$GameDirectory,
    [string]$SteamLibraryDirectory,
    [string]$PackageDirectory,
    [string]$DeployDirectory
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$python = Join-Path $repositoryRoot ".venv/Scripts/python.exe"
$toolPath = Join-Path $repositoryRoot "scripts/tool.py"

Push-Location $repositoryRoot
try {
    $resolverArguments = @($toolPath, 'environment', '--target', 'deploy', '--json')
    if ($GameDirectory) { $resolverArguments += @('--game-dir', $GameDirectory) }
    if ($SteamLibraryDirectory) { $resolverArguments += @('--steam-library-dir', $SteamLibraryDirectory) }
    if ($PackageDirectory) { $resolverArguments += @('--package-dir', $PackageDirectory) }
    if ($DeployDirectory) { $resolverArguments += @('--deploy-dir', $DeployDirectory) }

    Write-Host ('> python -B ' + ($resolverArguments -join ' '))
    $resolverOutput = & $python -B @resolverArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Environment resolution exited with code $LASTEXITCODE."
    }
    $environment = ($resolverOutput -join [Environment]::NewLine) | ConvertFrom-Json
    $gameExecutable = [System.IO.Path]::GetFullPath((Join-Path $environment.game_directory 'Probably Stolen.exe'))
    $runningGames = @(Get-CimInstance -ClassName Win32_Process -Filter "Name = 'Probably Stolen.exe'" -ErrorAction Stop)
    foreach ($runningGame in $runningGames) {
        if ([string]::IsNullOrWhiteSpace($runningGame.ExecutablePath)) {
            throw 'A Probably Stolen process is running but its installation path cannot be verified. Close it before deployment or removal.'
        }
        $runningPath = [System.IO.Path]::GetFullPath($runningGame.ExecutablePath)
        if ([string]::Equals($runningPath, $gameExecutable, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Probably Stolen is running from '$gameExecutable'. Close it before deployment or removal."
        }
    }

    $arguments = @(
        $toolPath, 'internal-deployment',
        '--action', $Action.ToLowerInvariant(),
        '--game-dir', $environment.game_directory,
        '--deploy-dir', $environment.deploy_directory
    )
    if ($Action -eq 'Deploy') {
        $arguments += @('--package-dir', $environment.package_directory)
    }

    Write-Host ('> python -B ' + ($arguments -join ' '))
    & $python -B @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "The $Action operation exited with code $LASTEXITCODE."
    }
}
catch {
    [Console]::Error.WriteLine("[FAIL] $($_.Exception.Message)")
    exit 1
}
finally {
    Pop-Location
}
