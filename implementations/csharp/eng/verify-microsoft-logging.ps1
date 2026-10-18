param(
    [ValidateSet('Coverage', 'Mutation')][string] $Mode = 'Coverage',
    [string] $OutputRoot
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Evidence.psm1') -Force
$repo = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
if (-not $OutputRoot) { $OutputRoot = Join-Path $repo ('.artifacts/csharp/microsoft-logging/' + [guid]::NewGuid().ToString('N')) }
$tool = Get-EvidenceTool
& dotnet $tool microsoft-logging-qualification --repo-root $repo --mode $Mode --output-root ([IO.Path]::GetFullPath($OutputRoot, (Get-Location).Path))
exit $LASTEXITCODE
