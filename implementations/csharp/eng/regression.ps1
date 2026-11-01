#Requires -Version 7.5
param(
    [string] $OutputRoot = '',
    [ValidateRange(1, 3600)][int] $NativeBudgetSeconds = 300,
    [ValidateRange(1, 4)][int] $NativeWorkers = 1,
    [ValidateRange(1, 3600)][int] $PreparationTimeoutSeconds = 1800,
    [string] $PreparationRoot = '',
    [string] $ApiCompatTool = '',
    [string] $ApiDotnet = ''
)
$root = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
& (Join-Path $root 'eng/regression.ps1') @PSBoundParameters
exit $LASTEXITCODE
