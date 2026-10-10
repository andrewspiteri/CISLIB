param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$languageRoot = Split-Path $PSScriptRoot -Parent
$repositoryRoot = Split-Path (Split-Path $languageRoot -Parent) -Parent
$policy = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'toolchain.json') | ConvertFrom-Json
$runRoot = Join-Path $repositoryRoot ('.artifacts/csharp/toolchain/' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [guid]::NewGuid().ToString('N'))
$sourceRoot = Join-Path $runRoot 'source'
$cache = Join-Path $runRoot 'packages'
$commands = [System.Collections.Generic.List[object]]::new()
$environmentNames = @('DOTNET_CLI_HOME', 'NUGET_PACKAGES', 'NUGET_HTTP_CACHE_PATH', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_NOLOGO', 'DOTNET_GENERATE_ASPNET_CERTIFICATE', 'TESTINGPLATFORM_TELEMETRY_OPTOUT', 'VSTEST_TELEMETRY_OPTEDOUT')
$previousEnvironment = @{}
New-Item -ItemType Directory -Path (Join-Path $runRoot 'logs') -Force | Out-Null

function Invoke-Logged {
    param([string] $Name, [string[]] $Arguments, [int] $ExpectedExit = 0, [string] $Executable = 'dotnet')
    $log = Join-Path $runRoot "logs/$Name.log"
    & $Executable @Arguments 2>&1 | Tee-Object -FilePath $log | Out-Host
    $actualExit = $LASTEXITCODE
    $commands.Add([ordered]@{ name = $Name; executable = $Executable; arguments = $Arguments; workingDirectory = (Get-Location).Path; expectedExit = $ExpectedExit; exitCode = $actualExit; log = "logs/$Name.log" })
    $commands | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $runRoot 'commands.json') -Encoding utf8
    if ($actualExit -ne $ExpectedExit) { throw "$Name returned $actualExit; expected $ExpectedExit. See $log" }
}

function Get-ToolArguments {
    param([object] $Tool)
    $entry = Join-Path $cache ($Tool.id.ToLowerInvariant() + '/' + $Tool.version + '/tools/net8.0/any/' + $Tool.entryPoint)
    if (-not (Test-Path -LiteralPath $entry)) { throw "Fresh restore did not materialize $entry" }
    return @('exec', '--fx-version', $policy.toolHostRuntime, '--roll-forward', 'Disable', $entry)
}

function Assert-PassedTests {
    param([string] $Path)
    [xml] $trx = Get-Content -Raw -LiteralPath $Path
    $counters = $trx.SelectSingleNode("//*[local-name()='ResultSummary']/*[local-name()='Counters']")
    if ($null -eq $counters -or [int] $counters.total -le 0 -or $counters.total -ne $counters.passed) { throw "Missing, failed or skipped tests: $Path" }
    return [int] $counters.total
}

Push-Location $repositoryRoot
try {
    $files = @(& git ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE -ne 0 -or $files.Count -eq 0) { throw 'Unable to enumerate repository source.' }
    $sourceInventory = foreach ($relative in ($files | Sort-Object -Unique)) {
        $original = Join-Path $repositoryRoot $relative
        $copy = Join-Path $sourceRoot $relative
        New-Item -ItemType Directory -Path (Split-Path $copy -Parent) -Force | Out-Null
        Copy-Item -LiteralPath $original -Destination $copy
        [ordered]@{ path = $relative; sha256 = (Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    $sourceInventory | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runRoot 'source-inventory.json') -Encoding utf8
    foreach ($name in $environmentNames) { $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name) }
    $env:DOTNET_CLI_HOME = Join-Path $runRoot 'dotnet-home'
    $env:NUGET_PACKAGES = $cache
    $env:NUGET_HTTP_CACHE_PATH = Join-Path $runRoot 'http-cache'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $env:TESTINGPLATFORM_TELEMETRY_OPTOUT = '1'
    $env:VSTEST_TELEMETRY_OPTEDOUT = '1'
    $isolatedLanguage = Join-Path $sourceRoot 'implementations/csharp'
    Set-Location $isolatedLanguage
    $sdk = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $sdk -ne $policy.sdk) { throw "Required SDK $($policy.sdk); observed $sdk" }
    Invoke-Logged 'dotnet-info' @('--info')
    Invoke-Logged 'runtimes' @('--list-runtimes')
    Invoke-Logged 'tool-restore' @('tool', 'restore', '--tool-manifest', '.config/dotnet-tools.json', '--configfile', 'eng/Tools.NuGet.config', '--no-http-cache')
    $strykerArguments = Get-ToolArguments $policy.tools[0]
    $apiArguments = Get-ToolArguments $policy.tools[1]
    Invoke-Logged 'stryker-help' ($strykerArguments + @('--help'))
    Invoke-Logged 'apicompat-version' ($apiArguments + @('--version'))
    Invoke-Logged 'locked-restore' @('restore', 'CisLib.slnx', '--locked-mode', '--configfile', 'NuGet.config', '--no-http-cache')
    & (Join-Path $isolatedLanguage 'eng/record-toolchain-inputs.ps1') -RunRoot $runRoot -LanguageRoot $isolatedLanguage -Cache $cache
    # Developer verification includes formatting, real unit/architecture tests and the packed dependency gate.
    Invoke-Logged 'developer-verification' @('-NoProfile', '-File', 'eng/verify.ps1') -Executable 'pwsh'
    Invoke-Logged 'exact-runtime-host' @('exec', '--fx-version', $policy.targetRuntime, '--roll-forward', 'Disable', 'examples/logging/ConsoleHuman/bin/Release/net10.0/CisLib.Examples.ConsoleHuman.dll')
    $unitCases = Assert-PassedTests (Join-Path $sourceRoot '.artifacts/csharp/tests/CIS.Core.UnitTests.trx')
    $architectureCases = Assert-PassedTests (Join-Path $sourceRoot '.artifacts/csharp/tests/CIS.Core.ArchitectureTests.trx')
    $coverageRoot = Join-Path $runRoot 'coverage'
    Invoke-Logged 'parser-coverage' @('test', 'tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj', '-c', 'Release', '--no-build', '--no-restore', '--filter', 'FullyQualifiedName~SeverityTokenParserTests', '--settings', 'verification/coverage/core.runsettings', '--collect', 'Code Coverage;Format=cobertura', '--results-directory', $coverageRoot, '--logger', 'trx;LogFileName=parser.trx', '--diag', (Join-Path $coverageRoot 'vstest.log'))
    $parserCases = Assert-PassedTests (Join-Path $coverageRoot 'parser.trx')
    [xml] $coverageTrx = Get-Content -Raw -LiteralPath (Join-Path $coverageRoot 'parser.trx')
    $collector = $coverageTrx.SelectSingleNode("//*[local-name()='Collector' and @collectorDisplayName='Code Coverage']")
    if ($null -eq $collector -or $collector.uri -ine $policy.coverage.collector) { throw 'Expected the Microsoft Code Coverage collector.' }
    $attachment = $collector.SelectSingleNode(".//*[local-name()='A']").href
    $coverageFiles = @(Get-ChildItem -LiteralPath $coverageRoot -Recurse -File -Filter '*.cobertura.xml' | Where-Object { $_.FullName.EndsWith($attachment, [StringComparison]::OrdinalIgnoreCase) })
    if ($coverageFiles.Count -ne 1) { throw 'Expected one coverage report identified by the native TRX attachment.' }
    [xml] $coverage = Get-Content -Raw -LiteralPath $coverageFiles[0].FullName
    if ([int] $coverage.coverage.'lines-valid' -le 0 -or [int] $coverage.coverage.'branches-valid' -le 0 -or [double] $coverage.coverage.'line-rate' -lt $policy.coverage.minimumLineRate -or [double] $coverage.coverage.'branch-rate' -lt $policy.coverage.minimumBranchRate) { throw 'Coverage denominator or threshold failed.' }
    $assembly = Join-Path $isolatedLanguage 'src/CIS.Core/bin/Release/net10.0/CIS.Core.dll'
    Invoke-Logged 'apicompat-core' ($apiArguments + @('--left', $assembly, '--right', $assembly, '--strict-mode'))
    # Nontrivial tool controls are disposable fixtures, not public Core contracts or historical releases.
    $fixtureRoot = Join-Path $runRoot 'api-controls'
    foreach ($variant in @('baseline', 'compatible', 'breaking')) {
        $directory = Join-Path $fixtureRoot $variant
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><AssemblyName>ApiControl</AssemblyName><Nullable>enable</Nullable><Deterministic>true</Deterministic></PropertyGroup></Project>' | Set-Content -LiteralPath (Join-Path $directory 'ApiControl.csproj') -Encoding utf8
        $method = if ($variant -eq 'breaking') { '' } else { 'public static int Parse(string token) => token.Length;' }
        "namespace ToolchainControl; public static class Contract { $method }" | Set-Content -LiteralPath (Join-Path $directory 'Contract.cs') -Encoding utf8
        Invoke-Logged "api-build-$variant" @('build', (Join-Path $directory 'ApiControl.csproj'), '-c', 'Release', ('-p:RestoreConfigFile=' + (Join-Path $isolatedLanguage 'NuGet.config')))
    }
    $baseline = Join-Path $fixtureRoot 'baseline/bin/Release/net10.0/ApiControl.dll'
    $compatible = Join-Path $fixtureRoot 'compatible/bin/Release/net10.0/ApiControl.dll'
    $breaking = Join-Path $fixtureRoot 'breaking/bin/Release/net10.0/ApiControl.dll'
    Invoke-Logged 'apicompat-positive' ($apiArguments + @('--left', $baseline, '--right', $compatible, '--strict-mode'))
    Invoke-Logged 'apicompat-negative' ($apiArguments + @('--left', $baseline, '--right', $breaking, '--strict-mode')) -ExpectedExit 1
    if ((Get-Content -Raw -LiteralPath (Join-Path $runRoot 'logs/apicompat-negative.log')) -notmatch 'CP0002.*Parse') { throw 'API control failed for an unexpected reason.' }
    $assemblyHash = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash
    Set-Location (Join-Path $isolatedLanguage 'tests/CIS.Core.UnitTests')
    Invoke-Logged 'mutation' ($strykerArguments + @('--config-file', '../../verification/mutation/stryker-config.json', '--output', (Join-Path $runRoot 'mutation'), '--skip-version-check', '--log-to-file'))
    $mutationFiles = @(Get-ChildItem -LiteralPath (Join-Path $runRoot 'mutation') -Recurse -File -Filter 'mutation-report.json')
    if ($mutationFiles.Count -ne 1) { throw 'Expected exactly one native mutation report.' }
    $mutation = Get-Content -Raw -LiteralPath $mutationFiles[0].FullName | ConvertFrom-Json -AsHashtable
    $mutants = @($mutation.files.Values | ForEach-Object { $_.mutants } | ForEach-Object { $_ })
    $killed = @($mutants | Where-Object { $_.status -eq 'Killed' }).Count
    $denominator = @($mutants | Where-Object { $_.status -in @('Killed', 'Timeout', 'Survived', 'NoCoverage') }).Count
    if ($denominator -le 0 -or $killed / $denominator -lt 0.9) { throw 'Conservative mutation denominator or threshold failed.' }
    $reviewPath = Join-Path $isolatedLanguage 'verification/mutation/smoke-dispositions.json'
    $review = Get-Content -Raw -LiteralPath $reviewPath | ConvertFrom-Json -AsHashtable
    $parserPath = Join-Path $isolatedLanguage $review.source
    if ((Get-FileHash -LiteralPath $parserPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $review.sourceSha256 -or $policy.tools[0].version -ne $review.strykerVersion) { throw 'Mutation disposition review is stale for this source/tool version.' }
    $excluded = @($mutants | Where-Object { $_.status -ne 'Killed' })
    if ($excluded.Count -ne $review.mutants.Count) { throw 'Mutation exclusions differ from the source-bound review.' }
    foreach ($mutant in $excluded) {
        $expected = @($review.mutants | Where-Object { $_.id -eq $mutant.id })
        if ($expected.Count -ne 1) { throw "Unreviewed non-killed mutant: $($mutant.id)" }
        foreach ($field in @('status', 'statusReason', 'mutatorName', 'replacement')) {
            if ($expected[0][$field] -cne $mutant[$field]) { throw "Mutation disposition mismatch: $($mutant.id) $field" }
        }
    }
    [ordered]@{ nativeReportSha256 = (Get-FileHash -LiteralPath $mutationFiles[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant(); reviewedInputSha256 = (Get-FileHash -LiteralPath $reviewPath -Algorithm SHA256).Hash.ToLowerInvariant(); sourceSha256 = $review.sourceSha256; exclusions = $review.mutants; numerator = $killed; denominator = $denominator; reclassifiedMutants = @() } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $runRoot 'mutation-dispositions.json') -Encoding utf8
    # Prove mutations did not alter repository inputs; rebuild the isolated candidate after mutation.
    foreach ($item in $sourceInventory) {
        foreach ($root in @($repositoryRoot, $sourceRoot)) {
            if ((Get-FileHash -LiteralPath (Join-Path $root $item.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $item.sha256) { throw "Mutation changed source: $($item.path)" }
        }
    }
    Set-Location $isolatedLanguage
    Invoke-Logged 'fresh-unmutated-build' @('build', 'src/CIS.Core/CIS.Core.csproj', '-c', 'Release', '--no-restore', '--no-incremental')
    if ((Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash -ne $assemblyHash) { throw 'Fresh unmutated assembly differs from the qualified assembly.' }
    Invoke-Logged 'fresh-unmutated-pack' @('pack', 'src/CIS.Core/CIS.Core.csproj', '-c', 'Release', '--no-build', '--no-restore', '-o', (Join-Path $runRoot 'unmutated-package'))
    $package = @(Get-ChildItem -LiteralPath (Join-Path $runRoot 'unmutated-package') -Filter '*.nupkg')
    if ($package.Count -ne 1) { throw 'Expected one fresh unmutated package.' }
    Invoke-Logged 'unmutated-package-boundaries' @('-NoProfile', '-File', 'eng/check-dependencies.ps1', '-ProjectPath', 'src/CIS.Core/CIS.Core.csproj', '-AssemblyPath', $assembly, '-PackagePath', $package[0].FullName) -Executable 'pwsh'
    $summary = [ordered]@{ schemaVersion = 1; task = 'WP01-S03'; outcome = 'Passed'; recordedUtc = [DateTime]::UtcNow.ToString('o'); runRoot = $runRoot; sdk = $sdk; targetRuntime = $policy.targetRuntime; toolHostRuntime = $policy.toolHostRuntime; runner = $policy.runner; unitCases = $unitCases; architectureCases = $architectureCases; parserCases = $parserCases; coverage = [ordered]@{ linesCovered = [int] $coverage.coverage.'lines-covered'; linesValid = [int] $coverage.coverage.'lines-valid'; branchesCovered = [int] $coverage.coverage.'branches-covered'; branchesValid = [int] $coverage.coverage.'branches-valid' }; mutation = [ordered]@{ total = $mutants.Count; conservativeDenominator = $denominator; killed = $killed; conservativeScore = $killed / $denominator; statuses = @($mutants | Group-Object -Property status | Select-Object Name,Count) }; apiCompatibility = 'Core self-comparison plus compatible and deliberately breaking public fixtures; no historical release claim'; sourceFileCount = $sourceInventory.Count; coreAssemblySha256 = $assemblyHash.ToLowerInvariant(); packageSha256 = (Get-FileHash -LiteralPath $package[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    $summary | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $runRoot 'summary.json') -Encoding utf8
    $evidenceFiles = @(Get-ChildItem -LiteralPath (Join-Path $runRoot 'logs') -Recurse -File) + @(Get-ChildItem -LiteralPath $coverageRoot -File -Filter '*.log') + @(Get-ChildItem -LiteralPath (Join-Path $runRoot 'mutation/logs') -Recurse -File) + @($coverageFiles) + @($mutationFiles) + @(Get-ChildItem -LiteralPath (Join-Path $runRoot 'mutation') -Recurse -File -Filter '*.html') + @($package) + @(Get-Item -LiteralPath (Join-Path $runRoot 'source-inventory.json'), (Join-Path $runRoot 'commands.json'), (Join-Path $runRoot 'prerequisites.json'), (Join-Path $runRoot 'mutation-dispositions.json'), (Join-Path $runRoot 'summary.json'), (Join-Path $coverageRoot 'parser.trx'), (Join-Path $sourceRoot '.artifacts/csharp/tests/CIS.Core.UnitTests.trx'), (Join-Path $sourceRoot '.artifacts/csharp/tests/CIS.Core.ArchitectureTests.trx'))
    $evidenceFiles | ForEach-Object { [ordered]@{ path = [IO.Path]::GetRelativePath($runRoot, $_.FullName).Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(); bytes = $_.Length } } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runRoot 'evidence-inventory.json') -Encoding utf8
    Write-Output "WP01-S03 toolchain qualification passed. Retain the complete run directory: $runRoot"
}
finally {
    Pop-Location
    foreach ($name in $previousEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $previousEnvironment[$name]) }
}
