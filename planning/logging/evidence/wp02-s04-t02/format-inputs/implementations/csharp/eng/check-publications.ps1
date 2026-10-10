param([string] $LedgerPath, [string] $ArtifactRoot, [string] $PreviousLedgerPath, [string] $PreviousLedgerSha256, [string] $OutputPath)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Publications.psm1') -Force
$repositoryRoot = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$schemaRoot = Join-Path $repositoryRoot 'compatibility/csharp/publications'
if (-not $LedgerPath) { $LedgerPath = Join-Path $schemaRoot 'ledger.json' }
if (-not $ArtifactRoot) { $ArtifactRoot = $repositoryRoot }
$ownedOutput = Join-Path $repositoryRoot '.artifacts/csharp/publications/validation.json'
if (-not $OutputPath) { $OutputPath = $ownedOutput }
try { $result = Get-PublicationValidation $LedgerPath $ArtifactRoot $schemaRoot $PreviousLedgerPath $PreviousLedgerSha256 }
catch {
    $message = $_.Exception.Message
    $result = [ordered]@{ schema = 'cis.publication-validation/v1'; exitCode = 2; compatibilityQualified = $false; issues = @([ordered]@{ code = $message.Split(':', 2)[0]; detail = $message }) }
}
$fullOutput = [IO.Path]::GetFullPath($OutputPath)
if ([string]::Equals($fullOutput, [IO.Path]::GetFullPath($LedgerPath), [StringComparison]::OrdinalIgnoreCase) -or ($PreviousLedgerPath -and [string]::Equals($fullOutput, [IO.Path]::GetFullPath($PreviousLedgerPath), [StringComparison]::OrdinalIgnoreCase))) { throw 'Refuse to overwrite a publication ledger with validation output.' }
if ($fullOutput.StartsWith([IO.Path]::GetFullPath($schemaRoot) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refuse to write validation reports into retained publication storage.' }
if ((Test-Path -LiteralPath $fullOutput) -and -not [string]::Equals($fullOutput, [IO.Path]::GetFullPath($ownedOutput), [StringComparison]::OrdinalIgnoreCase)) { throw 'Refuse to overwrite an existing custom output file.' }
New-Item -ItemType Directory -Path (Split-Path $fullOutput -Parent) -Force | Out-Null
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $fullOutput -Encoding utf8
$result | ConvertTo-Json -Depth 8
exit $result.exitCode
