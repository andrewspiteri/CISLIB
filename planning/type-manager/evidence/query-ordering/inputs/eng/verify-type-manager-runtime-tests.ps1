#Requires -Version 7
param(
    [Parameter(Mandatory)][string] $CoreAssembly,
    [Parameter(Mandatory)][string] $OutputRoot
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$languageRoot = Split-Path $PSScriptRoot -Parent
$CoreAssembly = [IO.Path]::GetFullPath($CoreAssembly)
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
if ((Test-Path -LiteralPath $OutputRoot) -and @(Get-ChildItem -LiteralPath $OutputRoot -Force).Count -ne 0) { throw 'Use an empty output directory.' }
New-Item -ItemType Directory -Force -Path $OutputRoot, (Join-Path $OutputRoot 'candidate') | Out-Null
$inputs = @()
foreach ($file in @('global.json', 'Directory.Build.props', 'Directory.Packages.props', 'NuGet.config', 'eng/Evidence.psm1',
    'tests/CIS.Core.UnitTests/TypeManagerManifestTests.cs',
    'tests/CIS.Core.UnitTests/TypeManagerInputTests.cs',
    'tests/CIS.Core.UnitTests/TypeManagerCatalogTests.cs',
    'tests/CIS.Core.UnitTests/TypeManagerOrderingTests.cs',
    'tests/CIS.Core.ArchitectureTests/TypeManagerRuntimeArchitectureTests.cs')) {
    $source = Join-Path $languageRoot $file
    Copy-Item -LiteralPath $source -Destination (Join-Path $OutputRoot (Split-Path $file -Leaf))
    $inputs += @{ path = $file; sha256 = (Get-FileHash $source).Hash.ToLowerInvariant() }
}
foreach ($name in @('CIS.Core.dll', 'Microsoft.Extensions.Logging.Abstractions.dll', 'Microsoft.Extensions.DependencyInjection.Abstractions.dll')) {
    $source = Join-Path (Split-Path $CoreAssembly -Parent) $name
    Copy-Item -LiteralPath $source -Destination (Join-Path $OutputRoot "candidate/$name")
    $inputs += @{ path = "candidate/$name"; sha256 = (Get-FileHash $source).Hash.ToLowerInvariant() }
}
$inputs | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 (Join-Path $OutputRoot 'inputs.json')
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>CIS.Core.UnitTests</AssemblyName>
    <IsTestProject>true</IsTestProject>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="CIS.Core" HintPath="candidate/CIS.Core.dll" />
    <Reference Include="Microsoft.Extensions.Logging.Abstractions" HintPath="candidate/Microsoft.Extensions.Logging.Abstractions.dll" />
    <Reference Include="Microsoft.Extensions.DependencyInjection.Abstractions" HintPath="candidate/Microsoft.Extensions.DependencyInjection.Abstractions.dll" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="MSTest.TestFramework" />
    <PackageReference Include="MSTest.TestAdapter" />
  </ItemGroup>
</Project>
'@ | Set-Content -Encoding utf8 (Join-Path $OutputRoot 'RuntimeTests.csproj')
'[assembly: Microsoft.VisualStudio.TestTools.UnitTesting.DoNotParallelize]' | Set-Content -Encoding utf8 (Join-Path $OutputRoot 'AssemblyInfo.cs')
Push-Location $OutputRoot
try {
    & dotnet test RuntimeTests.csproj -c Release -m:1 --results-directory results --logger 'trx;LogFileName=runtime-tests.trx' *> execution.log
    if ($LASTEXITCODE -ne 0) { throw "Type Manager tests failed; inspect $OutputRoot/execution.log" }
    Import-Module (Join-Path $OutputRoot 'Evidence.psm1') -Force
    $native = Read-NativeTrx (Join-Path $OutputRoot 'results/runtime-tests.trx')
    if ($native.total -ne 80 -or $native.passed -ne 80 -or $native.failed -ne 0 -or $native.skipped -ne 0) { throw 'Unexpected Type Manager test count or outcome.' }
    @{ coreSha256 = (Get-FileHash candidate/CIS.Core.dll).Hash.ToLowerInvariant(); total = 80; passed = 80; failed = 0; skipped = 0; scope = '71 Core unit cases plus 9 runtime architecture cases against frozen candidate' } |
        ConvertTo-Json | Set-Content -Encoding utf8 results.json
}
finally { Pop-Location }
Write-Output "Type Manager frozen-candidate tests passed: 80. Report: $OutputRoot/results.json"
