param(
    [Parameter(Mandatory)][string] $RunRoot,
    [Parameter(Mandatory)][string] $LanguageRoot,
    [Parameter(Mandatory)][string] $Cache
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Evidence.psm1') -Force
$tool = Get-EvidenceTool
& dotnet $tool record-toolchain-inputs --run-root ([IO.Path]::GetFullPath($RunRoot, (Get-Location).Path)) --language-root ([IO.Path]::GetFullPath($LanguageRoot, (Get-Location).Path)) --cache ([IO.Path]::GetFullPath($Cache, (Get-Location).Path)) --powershell-version $PSVersionTable.PSVersion.ToString()
exit $LASTEXITCODE