param([Parameter(Mandatory)][string] $CoreAssembly, [Parameter(Mandatory)][string] $OutputRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Evidence.psm1') -Force
$repo = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$tool = Get-EvidenceTool
& dotnet $tool type-manager-frozen-tests runtime --repo-root $repo --candidate-core ([IO.Path]::GetFullPath($CoreAssembly, (Get-Location).Path)) --output-root ([IO.Path]::GetFullPath($OutputRoot, (Get-Location).Path))
exit $LASTEXITCODE
