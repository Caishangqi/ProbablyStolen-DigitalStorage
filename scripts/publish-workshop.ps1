[CmdletBinding()]
param(
    [string]$GameDirectory,
    [string]$SteamLibraryDirectory,
    [string]$ArtifactsPath,
    [string]$PackageDirectory,
    [string]$SteamCmdDirectory,
    [string]$WorkshopVdf,
    [string]$WorkshopPreview,
    [string]$SteamUsername,
    [string]$Visibility,
    [string]$ChangeNote,
    [ValidateSet('en-US', 'zh-CN')]
    [string]$Locale
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$python = Join-Path $repositoryRoot '.venv/Scripts/python.exe'
$toolPath = Join-Path $repositoryRoot 'scripts/tool.py'
$packageScript = Join-Path $repositoryRoot 'scripts/package.ps1'
$workflowLock = $null
$workshopPackageRoot = $null
$workshopPackageDirectory = $null

Push-Location $repositoryRoot
try {
    $resolverArguments = @($toolPath, 'environment', '--target', 'package', '--json')
    if ($GameDirectory) { $resolverArguments += @('--game-dir', $GameDirectory) }
    if ($SteamLibraryDirectory) { $resolverArguments += @('--steam-library-dir', $SteamLibraryDirectory) }
    if ($ArtifactsPath) { $resolverArguments += @('--artifacts-path', $ArtifactsPath) }
    if ($PackageDirectory) { $resolverArguments += @('--package-dir', $PackageDirectory) }

    Write-Host ('> python -B ' + ($resolverArguments -join ' '))
    $resolverOutput = & $python -B @resolverArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Environment resolution exited with code $LASTEXITCODE."
    }
    $environment = ($resolverOutput -join [Environment]::NewLine) | ConvertFrom-Json
    $resolvedPackageDirectory = [System.IO.Path]::GetFullPath($environment.package_directory)
    $packageParent = Split-Path -Parent $resolvedPackageDirectory
    [System.IO.Directory]::CreateDirectory($packageParent) | Out-Null
    $workshopPackageRoot = Join-Path $packageParent ('.DigitalStorage.workshop.' + [guid]::NewGuid().ToString('N'))
    $workshopPackageDirectory = Join-Path $workshopPackageRoot 'DigitalStorage'
    [System.IO.Directory]::CreateDirectory($workshopPackageDirectory) | Out-Null
    $lockPath = Join-Path $packageParent '.DigitalStorage.publish.lock'
    try {
        $workflowLock = [System.IO.File]::Open($lockPath, [System.IO.FileMode]::OpenOrCreate,
            [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    }
    catch [System.IO.IOException] {
        throw "Another Workshop publish workflow owns the package lock: $lockPath"
    }

    $packageArguments = @('-PackageDirectory', $workshopPackageDirectory)
    if ($GameDirectory) { $packageArguments += @('-GameDirectory', $GameDirectory) }
    if ($SteamLibraryDirectory) { $packageArguments += @('-SteamLibraryDirectory', $SteamLibraryDirectory) }
    if ($ArtifactsPath) { $packageArguments += @('-ArtifactsPath', $ArtifactsPath) }

    Write-Host '> powershell.exe package.ps1'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $packageScript @packageArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Package build exited with code $LASTEXITCODE."
    }

    $uploadArguments = @($toolPath, 'internal-workshop-upload', '--package-dir', $workshopPackageDirectory)
    if ($SteamCmdDirectory) { $uploadArguments += @('--steamcmd-dir', $SteamCmdDirectory) }
    if ($WorkshopVdf) { $uploadArguments += @('--vdf-path', $WorkshopVdf) }
    if ($WorkshopPreview) { $uploadArguments += @('--preview', $WorkshopPreview) }
    if ($SteamUsername) { $uploadArguments += @('--steam-username', $SteamUsername) }
    if ($Visibility) { $uploadArguments += @('--visibility', $Visibility) }
    if ($ChangeNote) { $uploadArguments += @('--change-note', $ChangeNote) }
    if ($Locale) { $uploadArguments += @('--locale', $Locale) }

    Write-Host ('> python -B ' + ($uploadArguments -join ' '))
    & $python -B @uploadArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Workshop publishing exited with code $LASTEXITCODE."
    }

    Write-Host '[PASS] Workshop publish workflow completed.'
}
catch {
    [Console]::Error.WriteLine("[FAIL] $($_.Exception.Message)")
    exit 1
}
finally {
    if ($workflowLock) { $workflowLock.Dispose() }
    if ($workshopPackageRoot -and (Test-Path -LiteralPath $workshopPackageRoot)) {
        $rootItem = Get-Item -LiteralPath $workshopPackageRoot -Force
        if ($rootItem.PSIsContainer -and -not ($rootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
            Remove-Item -LiteralPath $workshopPackageRoot -Recurse -Force
        }
    }
    Pop-Location
}
