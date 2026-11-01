#Requires -Version 7.5
param(
    [ValidateSet('Developer', 'Release')][string] $Profile = 'Developer',
    [string] $Lane = $(if ($IsWindows) { 'windows-x64' } else { 'linux-ubuntu2404-x64' }),
    [ValidateRange(0, 64)][int] $MaxBuildNodes = 0,
    [string] $OutputRoot = '',
    [switch] $PrepareOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($PrepareOnly -and ($Profile -ne 'Developer' -or -not $OutputRoot)) {
    throw 'PrepareOnly requires Developer and a new explicit OutputRoot; it does not qualify a Developer checkpoint.'
}
if (-not [string]::IsNullOrWhiteSpace($env:CIS_ARCHITECTURE_INSPECTION)) {
    throw 'CIS_ARCHITECTURE_INSPECTION is only for focused tests. Clear it before running a Developer or Release checkpoint.'
}
Import-Module (Join-Path $PSScriptRoot 'Evidence.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'Verification.psm1') -Force
$languageRoot = Split-Path $PSScriptRoot -Parent
$repositoryRoot = Split-Path (Split-Path $languageRoot -Parent) -Parent
$reportRoot = if ($OutputRoot) { [IO.Path]::GetFullPath($OutputRoot) } else { Join-Path $repositoryRoot ".artifacts/csharp/verification/$Profile" }
if ($OutputRoot -and (Test-Path -LiteralPath $reportRoot)) { throw 'Use a new output directory for retained qualification evidence.' }
$relativeOutput = [IO.Path]::GetRelativePath($repositoryRoot, $reportRoot).Replace('\', '/')
$pathComparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
foreach ($maintainedRoot in @('implementations/csharp', 'eng', 'contracts', 'conformance', 'compatibility/csharp', 'docs')) {
    if ($relativeOutput.Equals($maintainedRoot, $pathComparison) -or $relativeOutput.StartsWith($maintainedRoot + '/', $pathComparison)) {
        throw 'Verification output must be outside maintained source inputs; use .artifacts or a separate directory.'
    }
}
$packageRoot = Join-Path $reportRoot 'packages'
$candidatePackage = ''
$legacyTests = Join-Path $repositoryRoot '.artifacts/csharp/tests'
$manifest = Get-Content -Raw -LiteralPath (Join-Path $languageRoot 'verification/families.json') | ConvertFrom-Json -AsHashtable -DateKind String
$inventory = Get-Content -Raw -LiteralPath (Join-Path $repositoryRoot 'conformance/logging/case-inventory.json') | ConvertFrom-Json -AsHashtable -DateKind String
$context = @{ stages = [System.Collections.Generic.List[object]]::new() }
$summary = [ordered]@{ schema = 'cis.verification/v1'; profile = $Profile; lane = $Lane; runId = [guid]::NewGuid().ToString('N'); startedUtc = [DateTime]::UtcNow.ToString('o'); finishedUtc = $null; exitCode = $null; source = $null; families = @(); stages = @(); issues = @() }
if ($PrepareOnly) { $summary.profile = 'Preparation'; $summary.scope = 'prepared-inputs' }
$reconciliationPath = Join-Path $reportRoot 'discovery-reconciliation.json'
$summary.reconciliation = [ordered]@{ status = 'NotProduced'; report = $null; reason = 'No complete family inventory for this run.' }
if (Test-Path -LiteralPath $reconciliationPath) { Remove-Item -LiteralPath $reconciliationPath }
$exitCode = 1
$previousTelemetry = @{}
$previousChecker = $env:CIS_VERIFICATION_CHECKER
New-Item -ItemType Directory -Path (Join-Path $reportRoot 'logs'), $legacyTests -Force | Out-Null

function Publish-Summary {
    $summary.stages = @($context.stages.ToArray())
    Write-VerificationJson (Join-Path $reportRoot 'summary.json') $summary
}

function Invoke-Family {
    param([hashtable] $Family, [object] $Entry)
    $directory = Join-Path $reportRoot "families/$($Family.id)"
    $settings = if ($Family.id -eq 'architecture') { 'verification/architecture.runsettings' } else { 'verification/developer.runsettings' }
    # One native host selection may contain several assemblies, preserving the
    # six families without recompiling checker implementation in product tests.
    $projects = @($Family.project) + @($Family['additionalProjects'])
    $assemblies = @($projects | Where-Object { $_ } | ForEach-Object {
        Join-Path $languageRoot (Join-Path (Split-Path $_ -Parent) "bin/Release/net10.0/$([IO.Path]::GetFileNameWithoutExtension($_)).dll")
    })
    foreach ($assembly in $assemblies) {
        if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) { throw "Missing prepared family assembly: $assembly" }
    }
    $arguments = @('vstest') + $assemblies + @("/Settings:$(Join-Path $languageRoot $settings)",
        "/Environment:CIS_VERIFICATION_ROOT=$reportRoot", "/Environment:CIS_VERIFICATION_REPOSITORY=$repositoryRoot",
        "/Environment:CIS_VERIFICATION_PACKAGE=$candidatePackage")
    $discoveryNativePath = Join-Path $directory 'discovery-native.json'
    if (Test-Path -LiteralPath $discoveryNativePath) { Remove-Item -LiteralPath $discoveryNativePath }
    $loggerDirectory = Join-Path $PSScriptRoot 'CIS.Discovery.TestLogger/bin/Release/net10.0'
    $null = Invoke-VerificationCommand (Join-Path $directory 'discovery.log') ($arguments + @('/ListTests', "/TestAdapterPath:$loggerDirectory", "/Logger:cis-discovery;OutputPath=$discoveryNativePath"))
    $discoveryPath = Join-Path $directory 'discovery.json'
    $null = Invoke-EvidenceTool -Arguments @('discovery', $reportRoot, (Join-Path $repositoryRoot 'conformance/logging/case-inventory.json'), $Family.id, $Lane, $discoveryNativePath, $discoveryPath)
    $Entry.discovery = New-EvidenceReference $reportRoot $discoveryPath 'cis.discovery/v1'

    $nativePath = Join-Path $directory 'report.trx'
    # VSTest must emit a new report for this invocation; never accept an old report after a failed run.
    if (Test-Path -LiteralPath $nativePath) { Remove-Item -LiteralPath $nativePath }
    $runFailure = $null
    try {
        $null = Invoke-VerificationCommand (Join-Path $directory 'execution.log') ($arguments + @("/ResultsDirectory:$directory", '/Logger:trx;LogFileName=report.trx', '--', 'RunConfiguration.TreatNoTestsAsError=true'))
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
    $actualLane = if ($IsWindows) { 'windows-x64' } elseif ($IsLinux) { 'linux-ubuntu2404-x64' } else { 'unsupported' }
    if ($Lane -ne $actualLane -or [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -ne 'X64') { throw 'Requested lane does not match the actual execution host.' }
    foreach ($name in @('DOTNET_CLI_TELEMETRY_OPTOUT', 'TESTINGPLATFORM_TELEMETRY_OPTOUT', 'VSTEST_TELEMETRY_OPTEDOUT')) {
        $previousTelemetry[$name] = [Environment]::GetEnvironmentVariable($name)
        [Environment]::SetEnvironmentVariable($name, '1')
    }
    $sourceInputs = @(Get-SourceInventory $repositoryRoot -VerificationInputs)
    if ([string]::IsNullOrWhiteSpace($env:CIS_VERIFICATION_CHECKER)) {
        $env:CIS_VERIFICATION_CHECKER = Join-Path $PSScriptRoot 'CIS.DependencyChecker/bin/Release/net10.0/CIS.DependencyChecker.dll'
    }
    Write-VerificationJson (Join-Path $reportRoot 'source-inputs.json') $sourceInputs
    $commit = & git -C $repositoryRoot rev-parse --verify HEAD 2>$null
    $summary.source = [ordered]@{ commit = $(if ($LASTEXITCODE -eq 0) { $commit } else { $null }); inputScope = 'maintained-csharp-verification-inputs/v1'; inputInventory = New-EvidenceReference $reportRoot (Join-Path $reportRoot 'source-inputs.json') 'cis.source-inputs/v1' }
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
    if ($Profile -in @('Developer', 'Release')) {
        Invoke-VerificationStage $context 'restore' {
            $null = Invoke-VerificationCommand (Join-Path $reportRoot 'logs/restore.log') @('restore', 'verification/CisLib.Verification.slnx', '--locked-mode', '--configfile', 'NuGet.config', '-m:1')
            # Include evaluated development-project assets in the complete
            # architecture inventory without running a duplicate fast suite.
            $null = Invoke-VerificationCommand (Join-Path $reportRoot 'logs/restore-development.log') @('restore', 'CisLib.slnx', '--locked-mode', '--configfile', 'NuGet.config', '-m:1')
        }
        Invoke-VerificationStage $context 'project-boundaries' { $null = Invoke-VerificationCommand (Join-Path $reportRoot 'logs/project-boundaries.log') @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'check-dependencies.ps1')) 'pwsh' }
        Invoke-VerificationStage $context 'format' { $null = Invoke-VerificationCommand (Join-Path $reportRoot 'logs/format.log') @('format', 'verification/CisLib.Verification.slnx', '--verify-no-changes', '--no-restore') }
        Invoke-VerificationStage $context 'build' {
            $buildArguments = @('build', 'verification/CisLib.Verification.slnx', '-c', 'Release', '--no-restore',
                '-p:CisMutationTesting=false', '-p:CisHealthcheckMutationCohort=false', '-p:CisHealthcheckPilotCohort=false', '-p:CisLinuxIdentityQualification=false')
            if ($MaxBuildNodes -gt 0) { $buildArguments += "-maxcpucount:$MaxBuildNodes" }
            $null = Invoke-VerificationCommand (Join-Path $reportRoot 'logs/build.log') $buildArguments
            # The full native route partitions original cases across the existing
            # Fast owners and residual owners; the verification solution stays unchanged.
            $fastBuildArguments = @('build', 'CisLib.slnx', '-c', 'Release', '--no-restore',
                '-p:CisMutationTesting=false', '-p:CisHealthcheckMutationCohort=false', '-p:CisHealthcheckPilotCohort=false', '-p:CisLinuxIdentityQualification=false')
            if ($MaxBuildNodes -gt 0) { $fastBuildArguments += "-maxcpucount:$MaxBuildNodes" }
            $null = Invoke-VerificationCommand (Join-Path $reportRoot 'logs/build-fast.log') $fastBuildArguments
        }
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
        $version = (& dotnet msbuild src/CIS.Core/CIS.Core.csproj -p:Configuration=Release -getProperty:Version).Trim()
        if ($LASTEXITCODE -ne 0) { throw 'Unable to evaluate package version.' }
        $candidatePackage = Join-Path $packageRoot "CIS.Core.$version.nupkg"
        $summary.candidatePackage = New-EvidenceReference $reportRoot $candidatePackage 'nuget.package'
        Invoke-VerificationStage $context 'package-boundaries' { $null = Invoke-VerificationCommand (Join-Path $reportRoot 'logs/package-boundaries.log') @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'check-dependencies.ps1'), '-ProjectPath', 'src/CIS.Core/CIS.Core.csproj', '-AssemblyPath', $core, '-PackagePath', (Join-Path $packageRoot "CIS.Core.$version.nupkg")) 'pwsh' }
        $architectureRoot = Join-Path $reportRoot "architecture/$($summary.runId)"
        Invoke-VerificationStage $context 'architecture-inspection' { $null = Invoke-VerificationCommand (Join-Path $reportRoot 'logs/architecture-inspection.log') @((Join-Path $PSScriptRoot 'CIS.DependencyChecker/bin/Release/net10.0/CIS.DependencyChecker.dll'), 'architecture', $languageRoot, (Join-Path $packageRoot "CIS.Core.$version.nupkg"), $architectureRoot) }
        $summary.architectureInspection = New-EvidenceReference $reportRoot (Join-Path $architectureRoot 'inspection.json') 'cis.architecture-inspection/v1'
        $nativeInputs = Join-Path $reportRoot 'native-inputs.json'
        $null = Invoke-VerificationCommand (Join-Path $reportRoot 'logs/native-inputs.log') @($env:CIS_VERIFICATION_CHECKER, 'capture-prepared-native-inputs', '--repo-root', $repositoryRoot,
            '--source-inputs', (Join-Path $reportRoot 'source-inputs.json'), '--output-path', $nativeInputs, '--include-fast', 'true')
        $summary.nativeInputs = New-EvidenceReference $reportRoot $nativeInputs 'cis.prepared-regression-native-inputs/v1'
        Publish-Summary
        if (-not $PrepareOnly) {
            foreach ($family in $manifest.families | Where-Object { $_.developerEnabled -or ($Profile -eq 'Release' -and $_.project) }) {
                $entry = $summary.families | Where-Object { $_.id -eq $family.id }
                Invoke-VerificationStage $context "tests-$($family.id)" { Invoke-Family $family $entry }
                Publish-Summary
            }
            Invoke-VerificationStage $context 'host' { $null = Invoke-VerificationCommand (Join-Path $reportRoot 'logs/host.log') @('run', '--project', 'examples/logging/ConsoleHuman', '-c', 'Release', '--no-build', '--no-restore') }
        }
    }
    Publish-Summary
    $exitCode = 0
}
catch {
    $failureDetail = $_.Exception.Message
    Publish-Summary
    $summary.issues += [ordered]@{ code = 'StageFailure'; detail = $failureDetail }
    $exitCode = 1
}
finally {
    if (-not $PrepareOnly -and $summary.families.Count -eq $manifest.families.Count) {
        try {
            $null = Invoke-EvidenceTool -Arguments @('reconcile', $reportRoot, (Join-Path $languageRoot 'verification/families.json'), (Join-Path $repositoryRoot 'conformance/logging/case-inventory.json'), $Lane)
            $summary.reconciliation = [ordered]@{ status = 'Produced'; report = New-EvidenceReference $reportRoot $reconciliationPath 'cis.discovery-reconciliation/v1'; reason = $null }
        }
        catch {
            $summary.reconciliation.reason = $_.Exception.Message
            $summary.issues += [ordered]@{ code = 'ReconciliationFailure'; detail = $_.Exception.Message }
            $exitCode = 1
        }
    }
    Publish-Summary
    if (-not $PrepareOnly) {
        try {
            $validation = Invoke-EvidenceTool -Arguments @('validate', $reportRoot, (Join-Path $languageRoot 'verification/families.json'), (Join-Path $repositoryRoot 'conformance/logging/case-inventory.json'), $Profile, $Lane, '-') -AllowRefusal
            $summary.issues = @($summary.issues) + @($validation.issues)
        }
        catch { $summary.issues += [ordered]@{ code = 'UnrecognizedReportSchema'; detail = $_.Exception.Message } }
    }
    if ($exitCode -eq 0 -and $summary.issues.Count -gt 0) { $exitCode = 2 }
    $summary.exitCode = $exitCode
    $summary.finishedUtc = [DateTime]::UtcNow.ToString('o')
    Publish-Summary
    Write-VerificationJson (Join-Path $reportRoot 'validation.json') ([ordered]@{ schema = 'cis.validation/v1'; profile = $summary.profile; scope = $summary['scope']; lane = $Lane; exitCode = $exitCode; issues = $summary.issues })
    Pop-Location
    foreach ($name in $previousTelemetry.Keys) { [Environment]::SetEnvironmentVariable($name, $previousTelemetry[$name]) }
    [Environment]::SetEnvironmentVariable('CIS_VERIFICATION_CHECKER', $previousChecker)
}
Write-Output "Verification $($summary.profile) exited $exitCode. Structured report: $(Join-Path $reportRoot 'summary.json')"
if ($exitCode -ne 0) { $summary.issues | ConvertTo-Json -Depth 6 | Write-Output }
exit $exitCode
