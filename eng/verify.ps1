param(
    [ValidateSet('Developer', 'Release')]
    [string] $Profile = 'Developer'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
& (Join-Path $repositoryRoot 'implementations/csharp/eng/verify.ps1') -Profile $Profile
exit $LASTEXITCODE
