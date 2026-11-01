param([ValidateSet('Developer', 'Release')][string] $Profile = 'Developer', [string] $Lane = $(if ($IsWindows) { 'windows-x64' } else { 'linux-ubuntu2404-x64' }), [ValidateRange(0, 64)][int] $MaxBuildNodes = 0, [string] $OutputRoot = '', [switch] $PrepareOnly)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
& (Join-Path $repositoryRoot 'implementations/csharp/eng/verify.ps1') -Profile $Profile -Lane $Lane -MaxBuildNodes $MaxBuildNodes -OutputRoot $OutputRoot -PrepareOnly:$PrepareOnly
exit $LASTEXITCODE
