param([string] $EvidenceRoot, [ValidateSet('Developer', 'Release')][string] $Profile = 'Release', [string] $Lane = 'windows-x64')

$repositoryRoot = Split-Path $PSScriptRoot -Parent
& (Join-Path $repositoryRoot 'implementations/csharp/eng/check-release.ps1') -EvidenceRoot $EvidenceRoot -Profile $Profile -Lane $Lane
exit $LASTEXITCODE
