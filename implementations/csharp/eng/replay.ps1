param(
    [ValidateSet('Developer', 'Release')][string] $Profile = 'Release',
    [string] $InputRoot,
    [string] $InventoryPath,
    [string] $Lane = $(if ($IsWindows) { 'windows-x64' } else { 'linux-ubuntu2404-x64' }),
    [string] $OutputRoot
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Evidence.psm1') -Force
$repo = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
if (-not $OutputRoot) { $OutputRoot = Join-Path $repo ('.artifacts/csharp/replay/' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [guid]::NewGuid().ToString('N')) }
$arguments = @('source-replay', '--repo-root', $repo, '--output-root', [IO.Path]::GetFullPath($OutputRoot, (Get-Location).Path), '--profile', $Profile, '--lane', $Lane)
if ($InputRoot) { $arguments += @('--input-root', [IO.Path]::GetFullPath($InputRoot, (Get-Location).Path)) }
if ($InventoryPath) { $arguments += @('--inventory-path', [IO.Path]::GetFullPath($InventoryPath, (Get-Location).Path)) }
$tool = Get-EvidenceTool
& dotnet $tool @arguments
exit $LASTEXITCODE
