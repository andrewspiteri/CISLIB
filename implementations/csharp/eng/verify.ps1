param(
    [ValidateSet('Developer', 'Release')]
    [string] $Profile = 'Developer'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($Profile -eq 'Release') {
    Write-Error 'Release verification is incomplete: logging behavior, six test families, candidate-package examples and historical/replay evidence are pending.' -ErrorAction Continue
    exit 2
}

$languageRoot = Split-Path $PSScriptRoot -Parent
$repositoryRoot = Split-Path (Split-Path $languageRoot -Parent) -Parent
$packageDirectory = Join-Path $repositoryRoot '.artifacts/csharp/packages'
$testDirectory = Join-Path $repositoryRoot '.artifacts/csharp/tests'

function Invoke-DotNet {
    param([string[]] $Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit $LASTEXITCODE."
    }
}

function Invoke-TestProject {
    param([string] $Name)
    Invoke-DotNet -Arguments @('test', "tests/$Name/$Name.csproj", '-c', 'Release', '--no-build', '--no-restore', '--results-directory', $testDirectory, '--logger', "trx;LogFileName=$Name.trx", '--', 'RunConfiguration.TreatNoTestsAsError=true')
    [xml] $report = Get-Content -Raw -LiteralPath (Join-Path $testDirectory "$Name.trx")
    $counters = $report.SelectSingleNode("//*[local-name()='ResultSummary']/*[local-name()='Counters']")
    if ($null -eq $counters -or [int] $counters.total -le 0 -or $counters.total -ne $counters.passed) {
        throw "Test project has missing, skipped or failed results: $Name"
    }
}

$previousTelemetry = @{}
foreach ($name in @('DOTNET_CLI_TELEMETRY_OPTOUT', 'TESTINGPLATFORM_TELEMETRY_OPTOUT', 'VSTEST_TELEMETRY_OPTEDOUT')) {
    $previousTelemetry[$name] = [Environment]::GetEnvironmentVariable($name)
    [Environment]::SetEnvironmentVariable($name, '1')
}

Push-Location $languageRoot
try {
    Invoke-DotNet -Arguments @('restore', 'CisLib.slnx', '--locked-mode', '--configfile', 'NuGet.config')
    & (Join-Path $PSScriptRoot 'check-dependencies.ps1')
    Invoke-DotNet -Arguments @('format', 'CisLib.slnx', '--verify-no-changes', '--no-restore')
    Invoke-DotNet -Arguments @('build', 'CisLib.slnx', '-c', 'Release', '--no-restore')
    $coreAssembly = Join-Path $languageRoot 'src/CIS.Core/bin/Release/net10.0/CIS.Core.dll'
    & (Join-Path $PSScriptRoot 'check-dependencies.ps1') -ProjectPath 'src/CIS.Core/CIS.Core.csproj' -AssemblyPath $coreAssembly
    Invoke-DotNet -Arguments @('pack', 'src/CIS.Core/CIS.Core.csproj', '-c', 'Release', '--no-build', '--no-restore', '-o', $packageDirectory)
    $version = (& dotnet msbuild src/CIS.Core/CIS.Core.csproj -getProperty:Version).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Unable to evaluate the current Core package version.' }
    & (Join-Path $PSScriptRoot 'check-dependencies.ps1') -ProjectPath 'src/CIS.Core/CIS.Core.csproj' -AssemblyPath $coreAssembly -PackagePath (Join-Path $packageDirectory "CIS.Core.$version.nupkg")
    Invoke-TestProject -Name 'CIS.Core.UnitTests'
    Invoke-TestProject -Name 'CIS.Core.ArchitectureTests'
    Invoke-DotNet -Arguments @('run', '--project', 'examples/logging/ConsoleHuman', '-c', 'Release', '--no-build')
    Write-Output 'Developer verification passed: dependency boundaries and foundation tests. Logging release qualification remains pending.'
}
finally {
    Pop-Location
    foreach ($name in $previousTelemetry.Keys) { [Environment]::SetEnvironmentVariable($name, $previousTelemetry[$name]) }
}
