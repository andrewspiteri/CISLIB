param(
    [ValidateSet('Developer', 'Release')][string] $Profile = 'Developer',
    [string] $Lane = $(if ($IsWindows) { 'windows-x64' } else { 'linux-ubuntu2404-x64' })
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Evidence.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Verification.psm1') -Force
$languageRoot = Split-Path $PSScriptRoot -Parent
$repositoryRoot = Split-Path (Split-Path $languageRoot -Parent) -Parent
$reportRoot = Join-Path $repositoryRoot ".artifacts/csharp/verification/$Profile"
$packageRoot = Join-Path $repositoryRoot '.artifacts/csharp/packages'
$legacyTests = Join-Path $repositoryRoot '.artifacts/csharp/tests'
$manifest = Get-Content -Raw -LiteralPath (Join-Path $languageRoot 'verification/families.json') | ConvertFrom-Json -AsHashtable
$inventory = Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot 'conformance/logging/case-inventory.json') | ConvertFrom-Json -AsHashtable
$context = @{ stages = [System.Collections.Generic.List[object]]::new() }
$summary = [ordered]@{ schema = 'cis.verification/v1'; profile = $Profile; lane = $Lane; runId = [guid]::NewGuid().ToString('N'); startedUtc = [DateTime]::UtcNow.ToString('o'); finishedUtc = $null; exitCode = $null; source = $null; families = @(); stages = @(); issues = @() }
$exitCode = 1
$previousTelemetry = @{}
New-Item -ItemType Directory -Path (Join-Path $reportRoot 'logs'), $legacyTests -Force | Out-Null

function Publish-Summary {
    $summary.stages = @($context.stages.ToArray())
    Write-VerificationJson (Join-Path $reportRoot 'summary.json') $summary
}

function Invoke-Family {
    param([hashtable] $Family, [object] $Entry)
    $directory = Join-Path $reportRoot "families/$($Family.id)"
    $arguments = @('test', $Family.project, '-c', 'Release', '--no-build', '--no-restore')
    $lines = @(Invoke-VerificationCommand (Join-Path $directory 'discovery.log') ($arguments + @('--list-tests')))
    $names = @($lines | Where-Object { $_ -match '^    \S' } | ForEach-Object { $_.Trim() })
    $cases = @($inventory.cases | Where-Object { $_.family -eq $Family.id -and $_.implementation -eq 'Implemented' } | ForEach-Object {
        $methodName = $_.method.Split('.')[-1]
        [ordered]@{ id = $_.id; count = @($names | Where-Object { $_ -match ('^' + [regex]::Escape($methodName) + '(?:\s|\(|$)') }).Count }
    })
    $discoveryPath = Join-Path $directory 'discovery.json'
    Write-VerificationJson $discoveryPath ([ordered]@{ schema = 'cis.discovery/v1'; family = $Family.id; status = 'Discovered'; discovered = $names.Count; cases = $cases; names = $names })
    $Entry.discovery = New-EvidenceReference $reportRoot $discoveryPath 'cis.discovery/v1'
    if ($names.Count -eq 0) { throw "Zero discovered tests: $($Family.id)" }
    $nativePath = Join-Path $directory 'report.trx'
    # VSTest must emit a new report for this invocation; never accept an old report after a failed run.
    if (Test-Path -LiteralPath $nativePath) { Remove-Item -LiteralPath $nativePath }
    $runFailure = $null
    try {
        $null = Invoke-VerificationCommand (Join-Path $directory 'execution.log') ($arguments + @('--results-directory', $directory, '--logger', 'trx;LogFileName=report.trx', '--', 'RunConfiguration.TreatNoTestsAsError=true', 'RunConfiguration.TestSessionTimeout=120000'))
    }
    catch { $runFailure = $_ }
    if (-not (Test-Path -LiteralPath $nativePath)) {
        if ($null -ne $runFailure) { throw $runFailure }
        throw "MissingReport: $nativePath"
    }
    $native = Read-NativeTrx $nativePath
    $executionPath = Join-Path $directory 'execution.json'
    Write-VerificationJson $executionPath ([ordered]@{ schema = 'cis.execution/v1'; family = $Family.id; status = 'Executed'; executed = $native.total - $native.skipped; passed = $native.passed; failed = $native.failed; skipped = $native.skipped })
    $Entry.execution = New-EvidenceReference $reportRoot $executionPath 'cis.execution/v1'
    $Entry.report = New-EvidenceReference $reportRoot $nativePath $Family.reportSchema
    $Entry.status = 'Executed'
    $Entry.reason = $null
    Copy-Item -LiteralPath $nativePath -Destination (Join-Path $legacyTests "$([IO.Path]::GetFileNameWithoutExtension($Family.project)).trx")
    if ($null -ne $runFailure) { throw $runFailure }
}

Push-Location $languageRoot
try {
    foreach ($name in @('DOTNET_CLI_TELEMETRY_OPTOUT', 'TESTINGPLATFORM_TELEMETRY_OPTOUT', 'VSTEST_TELEMETRY_OPTEDOUT')) {
        $previousTelemetry[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, '1')
    }
    $sourceInputs = @(Get-SourceInventory $repositoryRoot)
    Write-VerificationJson (Join-Path $reportRoot 'source-inputs.json') $sourceInputs
    $commit = & git -C $repositoryRoot rev-parse --verify HEAD 2>$null
    $summary.source = [ordered]@{ commit = $(if ($LASTEXITCODE -eq 0) { $commit } else { $null }); inputInventory = New-EvidenceReference $reportRoot (Join-Path $reportRoot 'source-inputs.json') 'cis.source-inputs/v1' }
    foreach ($family in $manifest.families) {
        $directory = Join-Path $reportRoot "families/$($family.id)"
        $reason = if ($Profile -eq 'Release') { $family.pendingReason } else { $family.developerReason }
        $discovery = Join-Path $directory 'discovery.json'
        $execution = Join-Path $directory 'execution.json'
        Write-VerificationJson $discovery ([ordered]@{ schema = 'cis.discovery/v1'; family = $family.id; status = 'NotRun'; discovered = $null; cases = @(); reason = $reason })
        Write-VerificationJson $execution ([ordered]@{ schema = 'cis.execution/v1'; family = $family.id; status = 'NotRun'; executed = 0; passed = 0; failed = 0; skipped = 0; reason = $reason })
        $summary.families += [ordered]@{ id = $family.id; status = 'NotRun'; reason = $reason; scope = 'foundation-only'; discovery = New-EvidenceReference $reportRoot $discovery 'cis.discovery/v1'; execution = New-EvidenceReference $reportRoot $execution 'cis.execution/v1'; report = $null }
    }
    Publish-Summary
    if ($Profile -eq 'Developer') {
        Invoke-VerificationStage $context 'restore' { $null = Invoke-VerificationCommand (Join-Path $reportRoot 'logs/restore.log') @('restore', 'CisLib.slnx', '--locked-mode', '--configfile', 'NuGet.config') }
        Invoke-VerificationStage $context 'project-boundaries' { $null = Invoke-VerificationCommand (Join-Path $reportRoot 'logs/project-boundaries.log') @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'check-dependencies.ps1')) 'pwsh' }
        Invoke-VerificationStage $context 'format' { $null = Invoke-VerificationCommand (Join-Path $reportRoot 'logs/format.log') @('format', 'CisLib.slnx', '--verify-no-changes', '--no-restore') }
        Invoke-VerificationStage $context 'build' { $null = Invoke-VerificationCommand (Join-Path $reportRoot 'logs/build.log') @('build', 'CisLib.slnx', '-c', 'Release', '--no-restore') }
        $core = Join-Path $languageRoot 'src/CIS.Core/bin/Release/net10.0/CIS.Core.dll'
        Invoke-VerificationStage $context 'startup-contract' { $null = Invoke-VerificationCommand (Join-Path $reportRoot 'logs/startup-contract.log') @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'check-startup-contract.ps1'), '-OutputRoot', (Join-Path $reportRoot 'contracts/startup')) 'pwsh' }
        Invoke-VerificationStage $context 'compatibility-samples' { $null = Invoke-VerificationCommand (Join-Path $reportRoot 'logs/compatibility-samples.log') @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'check-compatibility-samples.ps1'), '-StartupRoot', (Join-Path $reportRoot 'contracts/startup'), '-OutputRoot', (Join-Path $reportRoot 'contracts/compatibility-samples')) 'pwsh' }
        Invoke-VerificationStage $context 'log-value-consumers' { $null = Invoke-VerificationCommand (Join-Path $reportRoot 'logs/log-value-consumers.log') @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'check-log-value-consumers.ps1'), '-OutputRoot', (Join-Path $reportRoot 'contracts/log-value-consumers')) 'pwsh' }
        Invoke-VerificationStage $context 'publication-ledger' {
            $publicationReport = Join-Path $reportRoot 'contracts/publications/validation.json'
            if (Test-Path -LiteralPath $publicationReport) { Remove-Item -LiteralPath $publicationReport }
            $null = Invoke-VerificationCommand (Join-Path $reportRoot 'logs/publication-ledger.log') @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'check-publications.ps1'), '-OutputPath', $publicationReport) 'pwsh'
        }
        Invoke-VerificationStage $context 'pack' { $null = Invoke-VerificationCommand (Join-Path $reportRoot 'logs/pack.log') @('pack', 'src/CIS.Core/CIS.Core.csproj', '-c', 'Release', '--no-build', '--no-restore', '-o', $packageRoot) }
        $version = (& dotnet msbuild src/CIS.Core/CIS.Core.csproj -getProperty:Version).Trim()
        if ($LASTEXITCODE -ne 0) { throw 'Unable to evaluate package version.' }
        Invoke-VerificationStage $context 'package-boundaries' { $null = Invoke-VerificationCommand (Join-Path $reportRoot 'logs/package-boundaries.log') @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'check-dependencies.ps1'), '-ProjectPath', 'src/CIS.Core/CIS.Core.csproj', '-AssemblyPath', $core, '-PackagePath', (Join-Path $packageRoot "CIS.Core.$version.nupkg")) 'pwsh' }
        foreach ($family in $manifest.families | Where-Object { $_.developerEnabled }) {
            $entry = $summary.families | Where-Object { $_.id -eq $family.id }
            Invoke-VerificationStage $context "tests-$($family.id)" { Invoke-Family $family $entry }
            Publish-Summary
        }
        Invoke-VerificationStage $context 'host' { $null = Invoke-VerificationCommand (Join-Path $reportRoot 'logs/host.log') @('run', '--project', 'examples/logging/ConsoleHuman', '-c', 'Release', '--no-build', '--no-restore') }
    }
    Publish-Summary
    $summary.issues = @(Get-VerificationIssues $reportRoot $manifest $inventory $Profile $Lane)
    $exitCode = if ($summary.issues.Count -eq 0) { 0 } else { 2 }
}
catch {
    $failureDetail = $_.Exception.Message
    Publish-Summary
    $summary.issues = @(Get-VerificationIssues $reportRoot $manifest $inventory $Profile $Lane)
    $summary.issues += [ordered]@{ code = 'StageFailure'; detail = $failureDetail }
    $exitCode = 1
}
finally {
    $summary.exitCode = $exitCode
    $summary.finishedUtc = [DateTime]::UtcNow.ToString('o')
    Publish-Summary
    Write-VerificationJson (Join-Path $reportRoot 'validation.json') ([ordered]@{ schema = 'cis.validation/v1'; profile = $Profile; lane = $Lane; exitCode = $exitCode; issues = $summary.issues })
    Pop-Location
    foreach ($name in $previousTelemetry.Keys) { [Environment]::SetEnvironmentVariable($name, $previousTelemetry[$name]) }
}
Write-Output "Verification $Profile exited $exitCode. Structured report: $(Join-Path $reportRoot 'summary.json')"
if ($exitCode -ne 0) { $summary.issues | ConvertTo-Json -Depth 6 | Write-Output }
exit $exitCode
