[CmdletBinding()]
param(
    [string]$GameDirectory,
    [string]$SteamLibraryDirectory,
    [string]$ArtifactsPath,
    [string]$PackageDirectory
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$python = Join-Path $repositoryRoot ".venv/Scripts/python.exe"
$toolPath = Join-Path $repositoryRoot "scripts/tool.py"
$manifestPath = Join-Path $repositoryRoot 'packaging/manifest.xml'
$productAssemblies = @(
    'ProbablyStolen.DigitalStorage.Common.dll',
    'ProbablyStolen.DigitalStorage.Client.dll',
    'ProbablyStolen.DigitalStorage.Platform.dll',
    'ProbablyStolen.DigitalStorage.Mod.dll'
)
$stagingDirectory = $null
$backupDirectory = $null
$destinationCommitted = $false
$backupMoved = $false

function Invoke-PythonPackageCheck {
    param(
        [string]$Directory,
        [switch]$CheckExisting
    )

    $arguments = @($toolPath, 'verify-package', '--package-dir', $Directory)
    if ($CheckExisting) {
        $arguments += '--check-existing'
    }
    Write-Host ('> python -B ' + ($arguments -join ' '))
    & $python -B @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Package validation exited with code $LASTEXITCODE."
    }
}

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
    $env:DIGITAL_STORAGE_PACKAGE_DIR = $environment.package_directory
    $env:DIGITAL_STORAGE_GAME_DIR = $environment.game_directory

    $destination = [System.IO.Path]::GetFullPath($environment.package_directory)
    if (Test-Path -LiteralPath $destination) {
        $existing = Get-Item -LiteralPath $destination -Force
        if (-not $existing.PSIsContainer -or ($existing.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
            throw "Existing package path is not a plain directory: $destination"
        }
        if (@(Get-ChildItem -LiteralPath $destination -Force).Count -gt 0) {
            Invoke-PythonPackageCheck -Directory $destination -CheckExisting
        }
    }

    Write-Host "Game directory: $env:DIGITAL_STORAGE_GAME_DIR"
    Write-Host "Package directory: $destination"
    $buildScript = Join-Path $repositoryRoot 'scripts/build.ps1'
    $buildArguments = @(
        '-Target', 'Game',
        '-Configuration', 'Release',
        '-GameDirectory', $environment.game_directory,
        '-ArtifactsPath', $environment.output_directory
    )
    if ($environment.steam_library_directory) {
        $buildArguments += @('-SteamLibraryDirectory', $environment.steam_library_directory)
    }
    Write-Host '> powershell.exe build.ps1 -Target Game -Configuration Release'
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $buildScript @buildArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Release build exited with code $LASTEXITCODE."
    }

    $packageParent = Split-Path -Parent $destination
    [System.IO.Directory]::CreateDirectory($packageParent) | Out-Null
    $stagingDirectory = Join-Path $packageParent ('.DigitalStorage.staging.' + [guid]::NewGuid().ToString('N'))
    [System.IO.Directory]::CreateDirectory($stagingDirectory) | Out-Null
    Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $stagingDirectory 'manifest.xml')

    foreach ($assembly in $productAssemblies) {
        $assemblyName = [System.IO.Path]::GetFileNameWithoutExtension($assembly)
        $project = Join-Path $repositoryRoot "src/$assemblyName/$assemblyName.csproj"
        $escapedOutput = $environment.output_directory.Replace('%', '%25').Replace(';', '%3B').Replace(',', '%2C')
        $sourcePath = & dotnet msbuild $project '-getProperty:TargetPath' '-p:Configuration=Release' "-p:ArtifactsPath=$escapedOutput"
        if ($LASTEXITCODE -ne 0 -or $sourcePath -is [array] -or [string]::IsNullOrWhiteSpace($sourcePath)) {
            throw "Could not query TargetPath for $assemblyName."
        }
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "Release product assembly is missing: $sourcePath"
        }
        $actualName = [System.Reflection.AssemblyName]::GetAssemblyName($sourcePath).Name
        if ($actualName -cne $assemblyName) {
            throw "Assembly identity mismatch: expected '$assemblyName', found '$actualName' in $sourcePath"
        }
        Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $stagingDirectory $assembly)
    }

    & $python -B $toolPath verify-layout
    if ($LASTEXITCODE -ne 0) { throw 'The package layout is invalid.' }
    $layout = Get-Content -LiteralPath (Join-Path $repositoryRoot 'packaging/package-files.json') -Raw | ConvertFrom-Json
    foreach ($relativePath in $layout.current) {
        if ($relativePath -eq 'manifest.xml' -or $relativePath -in $productAssemblies) { continue }
        $sourcePath = Join-Path $repositoryRoot ('packaging/resources/' + $relativePath)
        $targetPath = Join-Path $stagingDirectory $relativePath
        [System.IO.Directory]::CreateDirectory((Split-Path -Parent $targetPath)) | Out-Null
        Copy-Item -LiteralPath $sourcePath -Destination $targetPath
    }

    Invoke-PythonPackageCheck -Directory $stagingDirectory

    if (Test-Path -LiteralPath $destination) {
        $backupDirectory = Join-Path $packageParent ('.DigitalStorage.backup.' + [guid]::NewGuid().ToString('N'))
        Move-Item -LiteralPath $destination -Destination $backupDirectory
        $backupMoved = $true
    }
    Move-Item -LiteralPath $stagingDirectory -Destination $destination
    $stagingDirectory = $null
    $destinationCommitted = $true
    Invoke-PythonPackageCheck -Directory $destination

    if ($backupMoved -and (Test-Path -LiteralPath $backupDirectory)) {
        try {
            Remove-Item -LiteralPath $backupDirectory -Recurse -Force
            $backupMoved = $false
            $backupDirectory = $null
        }
        catch {
            Write-Warning "The validated prior package remains at '$backupDirectory': $($_.Exception.Message)"
        }
    }

    Write-Host "[PASS] Package created at $destination"
}
catch {
    if ($destinationCommitted -and (Test-Path -LiteralPath $destination)) {
        Remove-Item -LiteralPath $destination -Recurse -Force
    }
    if ($backupMoved -and $backupDirectory -and (Test-Path -LiteralPath $backupDirectory) -and -not (Test-Path -LiteralPath $destination)) {
        Move-Item -LiteralPath $backupDirectory -Destination $destination
    }
    [Console]::Error.WriteLine("[FAIL] $($_.Exception.Message)")
    exit 1
}
finally {
    if ($stagingDirectory -and (Test-Path -LiteralPath $stagingDirectory)) {
        Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
    }
    Pop-Location
}
