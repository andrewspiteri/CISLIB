param(
    [string] $LanguageRoot = (Split-Path $PSScriptRoot -Parent),
    [string] $ProjectPath,
    [string] $AssetsPath,
    [string] $AssemblyPath,
    [string] $PackagePath
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'Evidence.psm1') -Force
$project = if ($ProjectPath) { $ProjectPath } else { '-' }
$assets = if ($AssetsPath) { [IO.Path]::GetFullPath($AssetsPath) } else { '-' }
$assembly = if ($AssemblyPath) { [IO.Path]::GetFullPath($AssemblyPath) } else { '-' }
$package = if ($PackagePath) { [IO.Path]::GetFullPath($PackagePath) } else { '-' }
$tool = Get-EvidenceTool
& dotnet $tool dependencies ([IO.Path]::GetFullPath($LanguageRoot)) $project $assets $assembly $package
exit $LASTEXITCODE
