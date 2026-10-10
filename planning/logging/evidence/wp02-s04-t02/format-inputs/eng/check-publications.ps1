param([string] $LedgerPath, [string] $ArtifactRoot, [string] $PreviousLedgerPath, [string] $PreviousLedgerSha256, [string] $OutputPath)
$repositoryRoot = Split-Path $PSScriptRoot -Parent
& (Join-Path $repositoryRoot 'implementations/csharp/eng/check-publications.ps1') -LedgerPath $LedgerPath -ArtifactRoot $ArtifactRoot -PreviousLedgerPath $PreviousLedgerPath -PreviousLedgerSha256 $PreviousLedgerSha256 -OutputPath $OutputPath
exit $LASTEXITCODE
