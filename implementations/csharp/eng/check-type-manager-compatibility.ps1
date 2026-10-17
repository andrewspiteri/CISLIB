param([Parameter(Mandatory)][string] $CoreAssembly, [string] $OutputRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Evidence.psm1') -Force
$repo = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
if (-not $OutputRoot) { $OutputRoot = Join-Path $repo '.artifacts/type-manager/compatibility' }
$tool = Get-EvidenceTool
& dotnet $tool type-manager-baseline --repo-root $repo --candidate-core ([IO.Path]::GetFullPath($CoreAssembly, (Get-Location).Path)) --output-root ([IO.Path]::GetFullPath($OutputRoot, (Get-Location).Path))
exit $LASTEXITCODE