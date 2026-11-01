param(
    [Parameter(Mandatory)][string] $OutputRoot,
    [Parameter(Mandatory)][string] $VerificationRoot,
    [Parameter(Mandatory)][string] $PackagePath,
    [ValidateRange(1, 3600)][int] $BudgetSeconds = 1800
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# Refuse focused test fixtures before bootstrapping/building the shared verification tool.
if (-not [string]::IsNullOrWhiteSpace($env:CIS_ARCHITECTURE_INSPECTION)) {
    throw 'CIS_ARCHITECTURE_INSPECTION is only for focused tests. Clear it before running shipping coverage.'
}
Import-Module (Join-Path $PSScriptRoot 'Evidence.psm1') -Force
$repo = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$tool = Get-EvidenceTool
& dotnet $tool collect-shipping-coverage --repo-root $repo --output-root ([IO.Path]::GetFullPath($OutputRoot, (Get-Location).Path)) --verification-root ([IO.Path]::GetFullPath($VerificationRoot)) --package ([IO.Path]::GetFullPath($PackagePath)) --budget-seconds $BudgetSeconds
exit $LASTEXITCODE
