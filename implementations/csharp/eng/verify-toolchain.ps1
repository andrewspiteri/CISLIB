param([string] $OutputRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Evidence.psm1') -Force
$repo = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
if (-not $OutputRoot) { $OutputRoot = Join-Path $repo ('.artifacts/csharp/toolchain/' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [guid]::NewGuid().ToString('N')) }
$tool = Get-EvidenceTool
& dotnet $tool toolchain-qualification --repo-root $repo --output-root ([IO.Path]::GetFullPath($OutputRoot, (Get-Location).Path)) --powershell-version $PSVersionTable.PSVersion.ToString()
exit $LASTEXITCODE