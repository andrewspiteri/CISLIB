param([string] $LedgerPath, [string] $ArtifactRoot, [string] $PreviousLedgerPath, [string] $PreviousLedgerSha256, [string] $OutputPath)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Evidence.psm1') -Force
$repositoryRoot = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
if (-not $LedgerPath) { $LedgerPath = Join-Path $repositoryRoot 'compatibility/csharp/publications/ledger.json' }
if (-not $ArtifactRoot) { $ArtifactRoot = $repositoryRoot }
if (-not $OutputPath) { $OutputPath = Join-Path $repositoryRoot '.artifacts/csharp/publications/validation.json' }
$previous = if ($PreviousLedgerPath) { [IO.Path]::GetFullPath($PreviousLedgerPath) } else { '-' }
$hash = if ($PreviousLedgerSha256) { $PreviousLedgerSha256 } else { '-' }
$tool = Get-EvidenceTool
& dotnet $tool publications $repositoryRoot ([IO.Path]::GetFullPath($LedgerPath)) ([IO.Path]::GetFullPath($ArtifactRoot)) $previous $hash ([IO.Path]::GetFullPath($OutputPath))
exit $LASTEXITCODE
