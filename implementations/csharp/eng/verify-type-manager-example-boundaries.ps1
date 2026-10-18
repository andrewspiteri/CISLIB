#Requires -Version 7.2
param([Parameter(Mandatory)][string] $LanguageRoot, [Parameter(Mandatory)][string] $OutputRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Evidence.psm1') -Force
$repo = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$tool = Get-EvidenceTool
& dotnet $tool type-manager-example-boundaries --repo-root $repo --language-root ([IO.Path]::GetFullPath($LanguageRoot, (Get-Location).Path)) --output-root ([IO.Path]::GetFullPath($OutputRoot, (Get-Location).Path))
exit $LASTEXITCODE
