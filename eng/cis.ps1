#requires -Version 7.0
# Run the selected local CIS tool without adding a shipping dependency.
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$cisExecutable = $env:CIS_EXECUTABLE
if (-not $cisExecutable) {
    $localName = if ($IsWindows) { 'cis.exe' } else { 'cis' }
    $localCandidate = Join-Path $repositoryRoot ".artifacts/cis/cli/$localName"
    if (Test-Path -LiteralPath $localCandidate -PathType Leaf) {
        $cisExecutable = $localCandidate
    }
    else {
        $installedCis = Get-Command cis -CommandType Application -ErrorAction SilentlyContinue
        if ($installedCis) { $cisExecutable = $installedCis.Source }
    }
}
if (-not $cisExecutable -or -not (Test-Path -LiteralPath $cisExecutable -PathType Leaf)) {
    throw 'CIS CLI was not found. Set CIS_EXECUTABLE to the installed tool or put cis on PATH.'
}
& $cisExecutable @args
exit $LASTEXITCODE
