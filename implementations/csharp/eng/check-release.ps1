#Requires -Version 7.5
param(
    [string] $EvidenceRoot,
    [ValidateSet('Developer', 'Release')][string] $Profile = 'Release',
    [string] $Lane = 'windows-x64',
    [string] $ManifestPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'verification/families.json'),
    [string] $InventoryPath = (Join-Path (Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent) 'conformance/logging/case-inventory.json'),
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Evidence.psm1') -Force
if (-not $EvidenceRoot) { $EvidenceRoot = Join-Path (Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent) ".artifacts/csharp/verification/$Profile" }
if (-not $OutputPath) { $OutputPath = Join-Path $EvidenceRoot 'validation.json' }
try {
    $result = Invoke-EvidenceTool -Arguments @('validate', [IO.Path]::GetFullPath($EvidenceRoot), [IO.Path]::GetFullPath($ManifestPath), [IO.Path]::GetFullPath($InventoryPath), $Profile, $Lane, [IO.Path]::GetFullPath($OutputPath)) -AllowRefusal
}
catch {
    $result = [ordered]@{ schema = 'cis.validation/v1'; profile = $Profile; lane = $Lane; exitCode = 2; issues = @([ordered]@{ code = 'UnrecognizedReportSchema'; detail = $_.Exception.Message }) }
    Write-VerificationJson $OutputPath $result
}
$result | ConvertTo-Json -Depth 8
exit $result.exitCode
