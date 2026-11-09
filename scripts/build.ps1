[CmdletBinding()]
param(
    [ValidateSet('Core', 'Game')]
    [string]$Target = 'Core',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$GameDirectory,
    [string]$SteamLibraryDirectory,
    [string]$ArtifactsPath
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$python = Join-Path $repositoryRoot ".venv/Scripts/python.exe"
$toolPath = Join-Path $repositoryRoot "scripts/tool.py"

function Invoke-DotNet {
    param([string[]]$Arguments)
    $escapedOutput = $environment.output_directory.Replace('%', '%25').Replace(';', '%3B').Replace(',', '%2C')
    $Arguments += "-p:ArtifactsPath=$escapedOutput"
    Write-Host ('> dotnet ' + ($Arguments -join ' '))
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet exited with code $LASTEXITCODE."
    }
}

Push-Location $repositoryRoot
try {
    $resolverArguments = @($toolPath, 'environment', '--target', $(if ($Target -eq 'Game') { 'game' } else { 'core' }), '--json')
    if ($GameDirectory) { $resolverArguments += @('--game-dir', $GameDirectory) }
    if ($SteamLibraryDirectory) { $resolverArguments += @('--steam-library-dir', $SteamLibraryDirectory) }
    if ($ArtifactsPath) { $resolverArguments += @('--artifacts-path', $ArtifactsPath) }

    Write-Host ('> python -B ' + ($resolverArguments -join ' '))
    $resolverOutput = & $python -B @resolverArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Environment resolution exited with code $LASTEXITCODE."
    }
    $environment = ($resolverOutput -join [Environment]::NewLine) | ConvertFrom-Json
    if ($Target -eq 'Game') {
        $env:DIGITAL_STORAGE_GAME_DIR = $environment.game_directory
        Write-Host "Game directory: $env:DIGITAL_STORAGE_GAME_DIR"
    }
    Write-Host "Artifacts path: $($environment.output_directory)"
    if ($Target -eq 'Game') {
        & $python -B $toolPath assets --check
        if ($LASTEXITCODE -ne 0) {
            throw "Read-only content validation exited with code $LASTEXITCODE."
        }
    }

    $solution = if ($Target -eq 'Game') { 'DigitalStorage.sln' } else { 'DigitalStorage.Core.slnf' }
    Invoke-DotNet @('restore', $solution, '--locked-mode')
    Invoke-DotNet @('build', $solution, '--configuration', $Configuration, '--no-restore')

    Write-Host "[PASS] $Target build completed in $Configuration configuration."
}
catch {
    [Console]::Error.WriteLine("[FAIL] $($_.Exception.Message)")
    exit 1
}
finally {
    Pop-Location
}
