param([string] $OutputRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Evidence.psm1') -Force
$languageRoot = Split-Path $PSScriptRoot -Parent
$repositoryRoot = Split-Path (Split-Path $languageRoot -Parent) -Parent
if (-not $OutputRoot) { $OutputRoot = Join-Path $repositoryRoot '.artifacts/csharp/verification/Developer/contracts/log-value-consumers' }
$tool = Get-EvidenceTool
& dotnet $tool compiler-checks log-values $languageRoot ([IO.Path]::GetFullPath($OutputRoot))
exit $LASTEXITCODE
