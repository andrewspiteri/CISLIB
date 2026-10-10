param([string] $OutputRoot)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Evidence.psm1') -Force
$languageRoot = Split-Path $PSScriptRoot -Parent
$repositoryRoot = Split-Path (Split-Path $languageRoot -Parent) -Parent
$sourceRoot = Join-Path $languageRoot 'verification/contracts/startup'
if (-not $OutputRoot) { $OutputRoot = Join-Path $repositoryRoot '.artifacts/csharp/verification/Developer/contracts/startup' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null
$summary = [ordered]@{ schema = 'cis.startup-contract/v1'; scope = 'Reference-only API specification and compile-time consumers; no runtime implementation or published compatibility claim.'; status = 'Running'; exitCode = 1; source = @(); controls = @(); detail = $null }

function Invoke-CompilerControl {
    param([string] $Id, [string] $Directory, [string] $Source, [string] $Diagnostic)
    New-Item -ItemType Directory -Path $Directory -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $sourceRoot 'Consumer.csproj.template') -Destination (Join-Path $Directory 'Consumer.csproj')
    Copy-Item -LiteralPath $Source -Destination (Join-Path $Directory 'Consumer.cs')
    $lines = @(& dotnet build (Join-Path $Directory 'Consumer.csproj') -c Release -v minimal -p:RestoreConfigFile="$languageRoot/NuGet.config" 2>&1 | ForEach-Object { $_.ToString() })
    $actual = $LASTEXITCODE
    $log = Join-Path $Directory 'compiler.log'
    $lines | Set-Content -LiteralPath $log -Encoding utf8
    $matched = if ($Diagnostic) { $actual -ne 0 -and ($lines -join "`n") -match [regex]::Escape($Diagnostic) } else { $actual -eq 0 }
    $summary.controls += [ordered]@{ id = $Id; actualExitCode = $actual; expectedDiagnostic = $Diagnostic; matched = $matched; log = New-EvidenceReference $OutputRoot $log 'dotnet.compiler-log/v1' }
    if (-not $matched) { throw "Compiler control $Id did not match its expected outcome. See $log" }
}

Push-Location $languageRoot
try {
    $summary.source = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -File | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path = [IO.Path]::GetRelativePath($repositoryRoot, $_.FullName).Replace('\','/'); sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
    $referenceRoot = Join-Path $OutputRoot 'reference'
    New-Item -ItemType Directory -Path $referenceRoot -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $sourceRoot 'PublicSurface.cs') -Destination (Join-Path $referenceRoot 'PublicSurface.cs')
    Copy-Item -LiteralPath (Join-Path $sourceRoot 'Reference.csproj.template') -Destination (Join-Path $referenceRoot 'Reference.csproj')
    $lines = @(& dotnet build (Join-Path $referenceRoot 'Reference.csproj') -c Release -v minimal -p:RestoreConfigFile="$languageRoot/NuGet.config" 2>&1 | ForEach-Object { $_.ToString() })
    $actual = $LASTEXITCODE
    $log = Join-Path $referenceRoot 'compiler.log'
    $lines | Set-Content -LiteralPath $log -Encoding utf8
    $summary.controls += [ordered]@{ id = 'reference-only-specification'; actualExitCode = $actual; expectedDiagnostic = $null; matched = ($actual -eq 0); log = New-EvidenceReference $OutputRoot $log 'dotnet.compiler-log/v1' }
    if ($actual -ne 0) { throw "Reference-only specification failed to compile. See $log" }
    $summary.referenceAssembly = New-EvidenceReference $OutputRoot (Join-Path $referenceRoot 'bin/Release/net10.0/CIS.Core.ContractSpecification.dll') 'dotnet.reference-assembly/v1'
    Invoke-CompilerControl 'positive-consumer' (Join-Path $OutputRoot 'positive') (Join-Path $sourceRoot 'Consumer.cs') ''
    foreach ($case in @(Get-Content -Raw -LiteralPath (Join-Path $sourceRoot 'negative-cases.json') | ConvertFrom-Json)) {
        Invoke-CompilerControl $case.id (Join-Path $OutputRoot $case.id) (Join-Path $sourceRoot $case.file) $case.expectedDiagnostic
    }
    $summary.status = 'Passed'
    $summary.exitCode = 0
}
catch { $summary.status = 'Failed'; $summary.detail = $_.Exception.Message }
finally {
    Write-VerificationJson (Join-Path $OutputRoot 'summary.json') $summary
    Pop-Location
}
$summary | ConvertTo-Json -Depth 8
exit $summary.exitCode
