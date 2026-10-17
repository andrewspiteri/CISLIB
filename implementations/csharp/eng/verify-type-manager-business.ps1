param([Parameter(Mandatory)][string] $CoreAssembly, [Parameter(Mandatory)][string] $CandidateReport, [Parameter(Mandatory)][string] $ExpectedCoreSha256, [Parameter(Mandatory)][string] $OutputRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Evidence.psm1') -Force
$repo = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$tool = Get-EvidenceTool
& dotnet $tool type-manager-frozen-tests business --repo-root $repo --candidate-core ([IO.Path]::GetFullPath($CoreAssembly, (Get-Location).Path)) --output-root ([IO.Path]::GetFullPath($OutputRoot, (Get-Location).Path)) --candidate-report ([IO.Path]::GetFullPath($CandidateReport, (Get-Location).Path)) --expect-core-sha256 $ExpectedCoreSha256
exit $LASTEXITCODE
