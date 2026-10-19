param(
    [Parameter(Mandatory)][string] $PackagePath,
    [Parameter(Mandatory)][string] $OutputRoot,
    [string] $RegistrationPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'verification/bundle/manifest.json')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Evidence.psm1') -Force
$repo = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$tool = Get-EvidenceTool
& dotnet $tool microsoft-logging-bundle --repo-root $repo --package-path ([IO.Path]::GetFullPath($PackagePath, (Get-Location).Path)) --output-root ([IO.Path]::GetFullPath($OutputRoot, (Get-Location).Path)) --registration-path ([IO.Path]::GetFullPath($RegistrationPath, (Get-Location).Path))
exit $LASTEXITCODE
