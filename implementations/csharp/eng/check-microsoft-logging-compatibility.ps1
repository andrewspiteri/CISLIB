param([Parameter(Mandatory)][string] $CoreAssembly, [string] $OutputRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Evidence.psm1') -Force
$repo = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
if (-not $OutputRoot) { $OutputRoot = Join-Path $repo ('.artifacts/csharp/provider-compatibility/' + [guid]::NewGuid().ToString('N')) }
$tool = Get-EvidenceTool
& dotnet $tool microsoft-logging-compatibility --repo-root $repo --candidate-core ([IO.Path]::GetFullPath($CoreAssembly, (Get-Location).Path)) --output-root ([IO.Path]::GetFullPath($OutputRoot, (Get-Location).Path))
exit $LASTEXITCODE
