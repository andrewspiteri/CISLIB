param([string] $StartupRoot, [string] $OutputRoot, [string] $ReuseRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Evidence.psm1') -Force
$languageRoot = Split-Path $PSScriptRoot -Parent
$repositoryRoot = Split-Path (Split-Path $languageRoot -Parent) -Parent
if (-not $OutputRoot) { $OutputRoot = Join-Path $repositoryRoot '.artifacts/csharp/verification/Developer/contracts/compatibility-samples' }
if (-not $StartupRoot) { $StartupRoot = Join-Path $repositoryRoot '.artifacts/csharp/verification/Developer/contracts/startup' }
$tool = Get-EvidenceTool
$arguments = @($tool, 'compiler-checks', 'samples', $languageRoot, ([IO.Path]::GetFullPath($OutputRoot)), ([IO.Path]::GetFullPath($StartupRoot)))
if ($ReuseRoot) { $arguments += [IO.Path]::GetFullPath($ReuseRoot) }
& dotnet @arguments
exit $LASTEXITCODE
