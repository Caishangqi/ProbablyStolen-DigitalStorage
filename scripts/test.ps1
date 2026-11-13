[CmdletBinding()]
param(
    [ValidateSet('All', 'Core', 'Platform', 'ApiCore', 'Api', 'Common', 'Client', 'Integration', 'Architecture')]
    [string]$Target = 'All',
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [string]$ArtifactsPath,
    [string]$GameDirectory,
    [string]$SteamLibraryDirectory,
    # Skips review of product sources and tooling scripts and the approved Platform test list. The Platform test
    # project, its sources and auto-imported MSBuild files stay hash-pinned and the source inventory is still
    # enforced; product code reached by the approved tests is not reviewed while the switch is set.
    [switch]$SkipPlatformReview
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

function Protect-TestReport {
    param([string]$Path)

    $report = New-Object System.Xml.XmlDocument
    $report.PreserveWhitespace = $true
    $report.Load($Path)
    foreach ($result in $report.SelectNodes("//*[local-name()='UnitTestResult']")) {
        if ($result.HasAttribute('computerName')) {
            $result.SetAttribute('computerName', '[redacted]')
        }
    }

    $sensitivePaths = @($repositoryRoot, $repositoryRoot.Replace('\', '/'))
    if (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
        $sensitivePaths += @($env:USERPROFILE, $env:USERPROFILE.Replace('\', '/'))
    }
    $sensitiveValues = @($sensitivePaths)
    if (-not [string]::IsNullOrWhiteSpace($env:COMPUTERNAME)) {
        $sensitiveValues += $env:COMPUTERNAME
    }
    if (-not [string]::IsNullOrWhiteSpace($env:USERNAME)) {
        $sensitiveValues += $env:USERNAME
    }
    if (-not [string]::IsNullOrWhiteSpace($env:USERDOMAIN)) {
        $sensitiveValues += $env:USERDOMAIN
    }
    foreach ($sensitiveValue in ($sensitiveValues | Sort-Object Length -Descending)) {
        $isSensitivePath = $sensitivePaths -contains $sensitiveValue
        foreach ($attribute in $report.SelectNodes('//@*')) {
            if ($attribute.NamespaceURI -eq 'http://www.w3.org/2000/xmlns/' -or
                $attribute.OwnerElement.LocalName -eq 'Counters' -or
                $attribute.LocalName -in @('id', 'testId', 'executionId', 'testListId', 'testType', 'outcome',
                    'duration', 'startTime', 'endTime', 'creation', 'queuing', 'start', 'finish', 'relativeResultsDirectory')) {
                continue
            }
            $isTestIdentity = $attribute.OwnerElement.LocalName -in @('UnitTest', 'TestMethod', 'UnitTestResult') -and
                $attribute.LocalName -in @('className', 'name', 'testName')
            if ($isTestIdentity -and -not $isSensitivePath) { continue }
            $attribute.Value = [regex]::Replace(
                $attribute.Value,
                [regex]::Escape($sensitiveValue),
                '[redacted]',
                [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
            )
        }
        foreach ($textNode in $report.SelectNodes('//text()')) {
            $textNode.Value = [regex]::Replace(
                $textNode.Value,
                [regex]::Escape($sensitiveValue),
                '[redacted]',
                [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
            )
        }
    }
    $report.Save($Path)
}

Push-Location $repositoryRoot
try {
    $gameRequired = $Target -in @('Platform', 'Api', 'All')
    $resolverArguments = @($toolPath, 'environment', '--target', $(if ($gameRequired) { 'game' } else { 'core' }), '--json')
    if ($ArtifactsPath) { $resolverArguments += @('--artifacts-path', $ArtifactsPath) }
    if ($GameDirectory) { $resolverArguments += @('--game-dir', $GameDirectory) }
    if ($SteamLibraryDirectory) { $resolverArguments += @('--steam-library-dir', $SteamLibraryDirectory) }
    $resolverOutput = & $python -B @resolverArguments
    if ($LASTEXITCODE -ne 0) { throw 'Environment resolution failed.' }
    $environment = ($resolverOutput -join [Environment]::NewLine) | ConvertFrom-Json
    if ($gameRequired) { $env:DIGITAL_STORAGE_GAME_DIR = $environment.game_directory }

    $core = (Get-Content -LiteralPath (Join-Path $repositoryRoot 'DigitalStorage.Core.slnf') -Raw | ConvertFrom-Json).solution.projects
    $testProjects = @($core | Where-Object { $_ -like 'tests/*' })
    $platform = 'tests/Unit/ProbablyStolen.DigitalStorage.Platform.Tests/ProbablyStolen.DigitalStorage.Platform.Tests.csproj'
    $api = 'tests/Unit/ProbablyStolen.DigitalStorage.Api.Tests/ProbablyStolen.DigitalStorage.Api.Tests.csproj'
    if ($Target -in @('Api', 'All')) {
        & $python -B $toolPath verify-api-review
        if ($LASTEXITCODE -ne 0) { throw 'Api reviewed inputs changed; independent review is required.' }
    }
    if ($Target -in @('Platform', 'All')) {
        if ($SkipPlatformReview) {
            Write-Host '[SKIP] Platform review gate disabled by -SkipPlatformReview: product-source hashes and approved Platform tests are not enforced; test boundary inputs remain pinned.'
            & $python -B $toolPath verify-platform-review --tests-only
            if ($LASTEXITCODE -ne 0) { throw 'Platform test boundary inputs changed; independent review is required.' }
        }
        else {
            & $python -B $toolPath verify-platform-review
            if ($LASTEXITCODE -ne 0) { throw 'Platform reviewed inputs changed; independent review is required.' }
        }
        $platformDirectory = Split-Path -Parent (Join-Path $repositoryRoot $platform)
        $reviewedSources = @('CanonicalFilterKeyCodecTests.cs', 'ItemContentSynchronizationTests.cs', 'NativeItemIdentityReaderTests.cs')
        $platformSources = @(Get-ChildItem -LiteralPath $platformDirectory -File -Filter '*.cs' -Recurse |
            Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
            ForEach-Object { $_.FullName.Substring($platformDirectory.Length).TrimStart([char[]]'\/').Replace('\', '/') })
        if ($platformSources.Count -ne $reviewedSources.Count -or @(Compare-Object $reviewedSources $platformSources).Count -ne 0) {
            throw 'Platform test sources changed; review their game-method execution boundary before running this scope.'
        }
    }
    $solutionList = & dotnet.exe sln (Join-Path $repositoryRoot 'DigitalStorage.sln') list
    if ($LASTEXITCODE -ne 0) { throw 'Could not inspect solution inventory.' }
    $solutionTests = @($solutionList | Where-Object { $_ -match '^tests[\\/].*\.csproj$' } | ForEach-Object { $_.Replace('\', '/') })
    $declaredTests = @($testProjects) + @($platform, $api)
    $diskTests = @(& $python -B $toolPath inventory-tests)
    if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate test projects safely.' }
    if ($diskTests.Count -ne $declaredTests.Count -or @(Compare-Object $diskTests $declaredTests).Count -ne 0) {
        throw 'Test projects on disk do not match the declared inventory.'
    }
    if ($declaredTests.Count -ne $solutionTests.Count -or @(Compare-Object $declaredTests $solutionTests).Count -ne 0) {
        throw 'Core filter and Platform test inventory do not match the solution.'
    }
    $projects = @(switch ($Target) {
        'Core' { $testProjects }
        'All' { $testProjects; $platform; $api }
        'Platform' { $platform }
        'Api' { $api }
        'ApiCore' { @($testProjects | Where-Object { [System.IO.Path]::GetFileName($_) -eq 'ProbablyStolen.DigitalStorage.Api.Core.Tests.csproj' }) }
        default { @($testProjects | Where-Object { [System.IO.Path]::GetFileName($_) -eq "ProbablyStolen.DigitalStorage.$Target.Tests.csproj" -or [System.IO.Path]::GetFileName($_) -eq "ProbablyStolen.DigitalStorage.${Target}Tests.csproj" }) }
    })
    if (($Target -notin @('Core', 'All')) -and $projects.Count -ne 1) { throw "Invalid test inventory for $Target." }
    if ($Target -eq 'All') {
        foreach ($suite in @('test-tools-unit', 'test-tools-integration')) {
            & $python -B $toolPath $suite
            if ($LASTEXITCODE -ne 0) { throw "Tooling suite $suite failed." }
        }
    }
    if ($Target -in @('Core', 'All')) {
        $buildScript = Join-Path $repositoryRoot 'scripts/build.ps1'
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $buildScript -Target Core -Configuration $Configuration -ArtifactsPath $environment.output_directory
        if ($LASTEXITCODE -ne 0) { throw 'Core build failed.' }
    }
    else {
        foreach ($project in $projects) {
            Invoke-DotNet @('restore', $project, '--locked-mode')
            Invoke-DotNet @('build', $project, '--configuration', $Configuration, '--no-restore')
        }
        if ($Target -eq 'Architecture') {
            foreach ($project in @($core | Where-Object { $_ -like 'src/*' })) {
                Invoke-DotNet @('restore', $project, '--locked-mode')
                Invoke-DotNet @('build', $project, '--configuration', $Configuration, '--no-restore')
            }
        }
    }
    if ($Target -eq 'All') {
        foreach ($project in @($platform, $api)) {
            Invoke-DotNet @('restore', $project, '--locked-mode')
            Invoke-DotNet @('build', $project, '--configuration', $Configuration, '--no-restore')
        }
    }

    $resultsDirectory = Join-Path $environment.output_directory "test-results/$Configuration"
    New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null
    $totalTests = 0
    $passedTests = 0
    $failedTests = 0
    $skippedTests = 0
    $runId = [guid]::NewGuid().ToString('N')

    foreach ($project in $projects) {
        $projectName = [System.IO.Path]::GetFileNameWithoutExtension($project)
        $trxName = "$projectName-$Configuration-$runId.trx"
        $testArguments = @(
            'test', $project, '--no-build', '--no-restore', '--configuration', $Configuration,
            '--logger', "trx;LogFileName=$trxName", '--results-directory', $resultsDirectory
        )
        $trxPath = Join-Path $resultsDirectory $trxName
        $testFailure = $null
        try {
            Invoke-DotNet $testArguments
        }
        catch {
            $testFailure = $_
        }
        finally {
            if (Test-Path -LiteralPath $trxPath) {
                try { Protect-TestReport -Path $trxPath }
                catch {
                    Remove-Item -LiteralPath $trxPath -Force
                    throw 'Test report could not be sanitized and was removed.'
                }
            }
        }
        if ($testFailure) { throw $testFailure }

        if (-not (Test-Path $trxPath)) {
            throw "Test result file was not created: $trxPath"
        }
        $validationArgs = @($toolPath, 'validate-trx', $trxPath)
        if ($project -eq $api) { $validationArgs += '--api' }
        if ($project -eq $platform -and -not $SkipPlatformReview) { $validationArgs += '--platform' }
        $projectTotal = & $python -B @validationArgs
        if ($LASTEXITCODE -ne 0) { throw "$projectName has an invalid or unsuccessful test report." }
        $projectTotal = [int]$projectTotal
        $projectPassed = $projectTotal
        $projectFailed = 0
        $projectSkipped = 0
        Write-Host "[PASS] $projectName total=$projectTotal passed=$projectPassed failed=0 skipped=0"

        $totalTests += $projectTotal
        $passedTests += $projectPassed
        $failedTests += $projectFailed
        $skippedTests += $projectSkipped
    }

    if ($failedTests -gt 0 -or $totalTests -ne ($passedTests + $failedTests + $skippedTests)) {
        throw "Test summary is incomplete or has failures: total=$totalTests passed=$passedTests failed=$failedTests skipped=$skippedTests"
    }
    Write-Host "[PASS] Automated tests: total=$totalTests passed=$passedTests failed=$failedTests skipped=$skippedTests."
}
catch {
    [Console]::Error.WriteLine("[FAIL] $($_.Exception.Message)")
    exit 1
}
finally {
    Pop-Location
}
