#Requires -Version 7
param(
    [Parameter(Mandatory)][string] $CoreAssembly,
    [Parameter(Mandatory)][string] $CandidateReport,
    [Parameter(Mandatory)][string] $ExpectedCoreSha256,
    [Parameter(Mandatory)][string] $OutputRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$language = Split-Path $PSScriptRoot -Parent
$CoreAssembly = [IO.Path]::GetFullPath($CoreAssembly)
$CandidateReport = [IO.Path]::GetFullPath($CandidateReport)
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
if ((Test-Path -LiteralPath $OutputRoot) -and @(Get-ChildItem -LiteralPath $OutputRoot -Force).Count) { throw 'Use an empty output directory.' }
New-Item -ItemType Directory -Force -Path $OutputRoot, (Join-Path $OutputRoot 'candidate') | Out-Null
function Hash([string] $Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
$report = [ordered]@{
    complete = $false
    scope = 'Development business stories against a frozen Core; not fresh-process discovery or final package qualification'
    verifierSha256 = Hash $PSCommandPath
    coreSha256 = Hash $CoreAssembly
    candidateReportSha256 = Hash $CandidateReport
    inputs = @()
}
try {
    if ($report.coreSha256 -cne $ExpectedCoreSha256) { throw 'Candidate Core differs from the independent pin.' }
    $producer = Get-Content -Raw -LiteralPath $CandidateReport | ConvertFrom-Json -AsHashtable
    if (-not $producer.complete -or $producer.coreSha256 -cne $ExpectedCoreSha256) { throw 'Candidate producer does not match Core.' }
    $module = Join-Path $language 'src/CIS.Core/TypeManagement'
    $moduleInputs = [ordered]@{}
    foreach ($source in Get-ChildItem -LiteralPath $module -Filter '*.cs' -File -Recurse) {
        $key = 'TypeManagement/' + [IO.Path]::GetRelativePath($module, $source.FullName).Replace('\', '/')
        $moduleInputs[$key] = Hash $source.FullName
        if ($moduleInputs[$key] -cne $producer.coreSourceInputs[$key]) { throw "Candidate module provenance differs: $key" }
    }
    if ($moduleInputs.Count -ne @($producer.coreSourceInputs.Keys | Where-Object { $_.StartsWith('TypeManagement/') }).Count) { throw 'Candidate module source set differs.' }
    $report.moduleSourceInputs = $moduleInputs
    Import-Module (Join-Path $PSScriptRoot 'Evidence.psm1') -Force
    $report.inputs += @(Copy-EvidenceTool $OutputRoot)
    foreach ($file in @('global.json', 'Directory.Build.props', 'Directory.Packages.props', 'eng/toolchain.json', 'eng/Evidence.psm1',
        'tests/TestAssembly.cs', 'tests/CIS.Core.BusinessCases/TypeManagerStartupStories.cs')) {
        $source = Join-Path $language $file
        $target = Join-Path $OutputRoot (Split-Path $file -Leaf)
        Copy-Item -LiteralPath $source -Destination $target
        $report.inputs += @{ path = $file; sha256 = Hash $target }
    }
    Copy-Item -LiteralPath $CandidateReport -Destination (Join-Path $OutputRoot 'candidate-report.json')
    foreach ($name in @('CIS.Core.dll', 'Microsoft.Extensions.Logging.Abstractions.dll', 'Microsoft.Extensions.DependencyInjection.Abstractions.dll')) {
        $source = Join-Path (Split-Path $CoreAssembly -Parent) $name
        $hash = Hash $source
        if ($hash -cne $producer.fixtureFiles["Host/bin/Release/net10.0/$name"]) { throw "Candidate dependency differs from producer: $name" }
        Copy-Item -LiteralPath $source -Destination (Join-Path $OutputRoot "candidate/$name")
        $report.inputs += @{ path = "candidate/$name"; sha256 = $hash }
    }
    $policy = Get-Content -Raw -LiteralPath (Join-Path $OutputRoot 'toolchain.json') | ConvertFrom-Json
    @'
<configuration><packageSources><clear /></packageSources><fallbackPackageFolders><clear /></fallbackPackageFolders><auditSources><clear /></auditSources></configuration>
'@ | Set-Content -Encoding utf8 (Join-Path $OutputRoot 'NuGet.config')
    '<Project />' | Set-Content -Encoding utf8 (Join-Path $OutputRoot 'Directory.Build.targets')
    @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>CIS.Core.BusinessCases</AssemblyName>
    <IsTestProject>true</IsTestProject>
    <RuntimeFrameworkVersion>$($policy.targetRuntime)</RuntimeFrameworkVersion>
    <RollForward>Disable</RollForward>
    <UseVSTest>true</UseVSTest>
    <EnableMSTestRunner>false</EnableMSTestRunner>
    <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="CIS.Core" HintPath="candidate/CIS.Core.dll" />
    <Reference Include="Microsoft.Extensions.Logging.Abstractions" HintPath="candidate/Microsoft.Extensions.Logging.Abstractions.dll" />
    <Reference Include="Microsoft.Extensions.DependencyInjection.Abstractions" HintPath="candidate/Microsoft.Extensions.DependencyInjection.Abstractions.dll" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="MSTest.TestFramework" />
    <PackageReference Include="MSTest.TestAdapter" />
  </ItemGroup>
</Project>
"@ | Set-Content -Encoding utf8 (Join-Path $OutputRoot 'BusinessTests.csproj')
    Push-Location $OutputRoot
    try {
        $report.sdk = (& dotnet --version).Trim()
        if ($LASTEXITCODE -ne 0 -or $report.sdk -cne $policy.sdk) { throw 'SDK differs from the toolchain pin.' }
        & dotnet test BusinessTests.csproj -c Release -m:1 --results-directory results --logger 'trx;LogFileName=business.trx' *> execution.log
        if ($LASTEXITCODE -ne 0) { throw 'Business stories failed; inspect execution.log.' }
        Import-Module ./Evidence.psm1 -Force
        $native = Read-NativeTrx ./results/business.trx
        if ($native.total -ne 10 -or $native.passed -ne 10 -or $native.failed -ne 0 -or $native.skipped -ne 0) { throw 'Unexpected business case count or outcome.' }
        $expectedMethods = @{
            PreparationHandsUsableCatalogToApplicationBeforeItCreatesServices = 2
            FailedPreparationNeverInvokesApplicationFactory = 4
            PreCanceledPreparationNeverInvokesApplicationFactory = 1
            ConcurrentConsumersReusePreparedCatalogWhileApplicationOwnsObjectLifetime = 1
            RunningApplicationKeepsSnapshotWhileNextStartupValidatesChangedInventory = 1
            FailedStartupCanBeRepairedWithIndependentPreparation = 1
        }
        $groups = @($native.results | ForEach-Object { [pscustomobject]$_ } | Group-Object method)
        if ($groups.Count -ne $expectedMethods.Count) { throw 'Unexpected business method set.' }
        foreach ($group in $groups) {
            $method = $group.Name.Replace('CIS.Tests.BusinessCases.TypeManagerStartupStories.', '')
            if (-not $expectedMethods.ContainsKey($method) -or $expectedMethods[$method] -ne $group.Count) { throw "Unexpected business method/count: $($group.Name)" }
        }
        [xml] $trx = Get-Content -Raw ./results/business.trx
        $report.observedRuntime = @()
        foreach ($result in $trx.SelectNodes("//*[local-name()='Results']/*[local-name()='UnitTestResult']")) {
            $stdout = $result.SelectSingleNode("*[local-name()='Output']/*[local-name()='StdOut']").InnerText
            $identities = @($stdout.Split("`n") | Where-Object { $_.Trim().StartsWith('{"runtime":') })
            if ($identities.Count -ne 1) { throw 'Missing or ambiguous runtime identity in native case output.' }
            $identity = $identities[0] | ConvertFrom-Json
            $expectedOs = if ($IsWindows) { 'windows' } elseif ($IsLinux) { 'linux' } else { 'unsupported' }
            if ($expectedOs -eq 'unsupported' -or $identity.os -cne $expectedOs -or $identity.architecture -cne 'X64' -or
                $identity.runtime -cne ".NET $($policy.targetRuntime)" -or $identity.coreSha256 -cne $ExpectedCoreSha256) { throw 'Executed runtime/OS/architecture/Core differs from its qualification pin.' }
            $report.observedRuntime += @{ testId = $result.testId; identity = $identity }
        }
        $report.native = $native
        $report.pinnedRuntime = $policy.targetRuntime
        $report.binaryFiles = [ordered]@{}
        foreach ($file in Get-ChildItem bin/Release/net10.0 -Recurse -File) {
            $relative = [IO.Path]::GetRelativePath((Join-Path $OutputRoot 'bin/Release/net10.0'), $file.FullName).Replace('\', '/')
            $report.binaryFiles[$relative] = Hash $file.FullName
        }
        if ($report.binaryFiles['CIS.Core.dll'] -cne $ExpectedCoreSha256) { throw 'Executed Core differs from the independent pin.' }
        foreach ($name in @('Microsoft.Extensions.Logging.Abstractions.dll', 'Microsoft.Extensions.DependencyInjection.Abstractions.dll')) {
            if ($report.binaryFiles[$name] -cne $producer.fixtureFiles["Host/bin/Release/net10.0/$name"]) { throw "Executed dependency differs from producer: $name" }
        }
        $report.complete = $true
    }
    finally { Pop-Location }
}
catch { $report.error = $_.Exception.Message; throw }
finally { $report | ConvertTo-Json -Depth 12 | Set-Content -Encoding utf8 (Join-Path $OutputRoot 'results.json') }
Write-Output "Type Manager business stories passed: 10. Report: $OutputRoot/results.json"
