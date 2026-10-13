#Requires -Version 7
param(
    [Parameter(Mandatory)][string] $CoreAssembly,
    [string] $OutputRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$languageRoot = Split-Path $PSScriptRoot -Parent
$baseline = Join-Path $languageRoot 'verification/contracts/type-manager/v1-development'
$CoreAssembly = [IO.Path]::GetFullPath($CoreAssembly)
if (-not $OutputRoot) { $OutputRoot = Join-Path $languageRoot '../../.artifacts/type-manager/compatibility' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
if ((Test-Path -LiteralPath $OutputRoot) -and @(Get-ChildItem -LiteralPath $OutputRoot -Force).Count -ne 0) { throw 'Use an empty compatibility output directory.' }
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$report = [ordered]@{ scope = 'Unpublished initial development baseline; no released Type Manager history'; complete = $false; coreSha256 = (Get-FileHash $CoreAssembly -Algorithm SHA256).Hash.ToLowerInvariant(); results = @() }
$reportPath = Join-Path $OutputRoot 'results.json'
$report | ConvertTo-Json -Depth 10 | Set-Content -Encoding utf8 $reportPath

function Invoke-Check([string] $Name, [string[]] $Arguments) {
    $log = Join-Path $OutputRoot ($Name + '.log')
    & dotnet @Arguments *> $log
    if ($LASTEXITCODE -ne 0) { throw "$Name failed; inspect $log" }
    return $log
}

function Replay-Consumer([string] $Name, [string] $Directory) {
    # The historical consumer binaries remain unchanged; only its runtime implementation is replaced.
    $runtimeInputs = @()
    foreach ($fileName in @('CIS.Core.dll', 'Microsoft.Extensions.Logging.Abstractions.dll', 'Microsoft.Extensions.DependencyInjection.Abstractions.dll')) {
        $inputPath = Join-Path (Split-Path $CoreAssembly -Parent) $fileName
        Copy-Item -LiteralPath $inputPath -Destination $Directory -Force
        $runtimeInputs += @{ name = $fileName; sha256 = (Get-FileHash $inputPath).Hash.ToLowerInvariant() }
    }
    $consumer = Join-Path $Directory 'CIS.TypeManager.ContractConsumer.dll'
    $manifest = Join-Path $Directory 'consumer.types.json'
    $hostIdentity = [Reflection.AssemblyName]::GetAssemblyName($consumer).FullName
    $coreIdentity = [Reflection.AssemblyName]::GetAssemblyName($CoreAssembly).FullName
    @{ schemaVersion = 1; hostAssembly = $hostIdentity; assemblies = @(
        @{ assemblyName = $hostIdentity; relativePath = 'CIS.TypeManager.ContractConsumer.dll' },
        @{ assemblyName = $coreIdentity; relativePath = 'CIS.Core.dll' }
    ) } | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 $manifest
    $log = Invoke-Check $Name @($consumer, $manifest)
    $observed = Get-Content -Raw $log | ConvertFrom-Json
    if ($observed.contractConsumer -ne 'passed' -or $observed.selectedAssemblyCount -ne 2) { throw "Invalid result from $Name" }
    $report.results += @{ name = $Name; passed = $true; consumerSha256 = (Get-FileHash $consumer -Algorithm SHA256).Hash.ToLowerInvariant(); runtimeInputs = $runtimeInputs; result = $observed }
}

Push-Location $languageRoot
try {
    $hashes = Get-Content -Raw (Join-Path $baseline 'baseline-hashes.json') | ConvertFrom-Json
    foreach ($file in $hashes.files) {
        if ((Get-FileHash (Join-Path $baseline $file.path) -Algorithm SHA256).Hash -ine $file.sha256) { throw "Frozen baseline changed: $($file.path)" }
    }
    $null = Invoke-Check 'api' @('tool', 'run', 'apicompat', '--', '-l', (Join-Path $baseline 'reference/CIS.Core.dll'), '-r', $CoreAssembly, '--enable-rule-cannot-change-parameter-name', '--enable-rule-attributes-must-match')
    $report.results += @{ name = 'api'; passed = $true }

    $frozen = Join-Path $OutputRoot 'frozen'
    New-Item -ItemType Directory -Force -Path $frozen | Out-Null
    Copy-Item (Join-Path $baseline 'compiled/*') -Destination $frozen -Force
    Replay-Consumer 'frozen-binary' $frozen
    $frozenHash = ($hashes.files | Where-Object path -EQ 'compiled/CIS.TypeManager.ContractConsumer.dll').sha256
    if ((Get-FileHash (Join-Path $frozen 'CIS.TypeManager.ContractConsumer.dll')).Hash -ine $frozenHash) { throw 'Frozen consumer was modified.' }

    $source = Join-Path $OutputRoot 'source'
    New-Item -ItemType Directory -Force -Path $source | Out-Null
    Copy-Item (Join-Path $baseline 'source/Program.cs') $source
    '<Project />' | Set-Content -Encoding utf8 (Join-Path $source 'Directory.Build.props')
    $project = Get-Content -Raw (Join-Path $baseline 'source/Consumer.csproj.template')
    $project.Replace('../reference/CIS.Core.dll', [Security.SecurityElement]::Escape($CoreAssembly)) | Set-Content -Encoding utf8 (Join-Path $source 'Consumer.csproj')
    $null = Invoke-Check 'source-build' @('build', (Join-Path $source 'Consumer.csproj'), '-c', 'Release', '-m:1', '--nologo')
    Replay-Consumer 'source-consumer' (Join-Path $source 'bin/Release/net10.0')
    $report.complete = $true
}
finally {
    $report | ConvertTo-Json -Depth 10 | Set-Content -Encoding utf8 $reportPath
    Pop-Location
}
Write-Output "Type Manager compatibility passed. Report: $reportPath"
