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
Import-Module (Join-Path $PSScriptRoot 'Evidence.psm1') -Force
$inputs = @(Copy-EvidenceTool $OutputRoot)
foreach ($file in @('global.json', 'Directory.Build.props', 'Directory.Packages.props', 'NuGet.config', 'eng/Evidence.psm1', 'eng/toolchain.json',
    'tests/CIS.Core.UnitTests/TypeManagerManifestTests.cs',
    'tests/CIS.Core.UnitTests/TypeManagerInputTests.cs',
    'tests/CIS.Core.UnitTests/TypeManagerCatalogTests.cs',
    'tests/CIS.Core.UnitTests/TypeManagerOrderingTests.cs',
    'tests/CIS.Core.UnitTests/TypeManagerFailureContractTests.cs',
    'tests/CIS.Core.UnitTests/TypeManagerFreshAssemblyTests.cs',
    'tests/CIS.Core.ArchitectureTests/TypeManagerRuntimeArchitectureTests.cs')) {
    $source = Join-Path $languageRoot $file
    $sourceHash = (Get-FileHash $source).Hash.ToLowerInvariant()
    $destination = Join-Path $OutputRoot (Split-Path $file -Leaf)
    Copy-Item -LiteralPath $source -Destination $destination
    $copiedHash = (Get-FileHash $destination).Hash.ToLowerInvariant()
    if ($copiedHash -cne $sourceHash) { throw "Input changed while copying: $file" }
    $inputs += @{ path = $file; sha256 = $copiedHash }
}
foreach ($name in @('CIS.Core.dll', 'Microsoft.Extensions.Logging.Abstractions.dll', 'Microsoft.Extensions.DependencyInjection.Abstractions.dll')) {
    $source = if ($name -eq 'CIS.Core.dll') { $CoreAssembly } else { Join-Path (Split-Path $CoreAssembly -Parent) $name }
    $sourceHash = (Get-FileHash $source).Hash.ToLowerInvariant()
    $destination = Join-Path $OutputRoot "candidate/$name"
    Copy-Item -LiteralPath $source -Destination $destination
    $copiedHash = (Get-FileHash $destination).Hash.ToLowerInvariant()
    if ($copiedHash -cne $sourceHash) { throw "Candidate changed while copying: $name" }
    $inputs += @{ path = "candidate/$name"; sha256 = $copiedHash }
}
$inputs | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 (Join-Path $OutputRoot 'inputs.json')
$runtimePolicy = Get-Content -Raw (Join-Path $OutputRoot 'toolchain.json') | ConvertFrom-Json
'<Project />' | Set-Content -Encoding utf8 (Join-Path $OutputRoot 'Directory.Build.targets')
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>CIS.Core.UnitTests</AssemblyName>
    <IsTestProject>true</IsTestProject>
    <EnableMSTestRunner>false</EnableMSTestRunner>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <RuntimeFrameworkVersion>$($runtimePolicy.targetRuntime)</RuntimeFrameworkVersion>
    <RollForward>Disable</RollForward>
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
"@ | Set-Content -Encoding utf8 (Join-Path $OutputRoot 'RuntimeTests.csproj')
[IO.File]::WriteAllText((Join-Path $OutputRoot 'AssemblyInfo.cs'), "[assembly: Microsoft.VisualStudio.TestTools.UnitTesting.DoNotParallelize]`n", [Text.UTF8Encoding]::new($false))
Push-Location $OutputRoot
try {
    $sdk = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $sdk -cne $runtimePolicy.sdk) { throw 'Runtime verifier SDK differs from its pin.' }
    & dotnet test RuntimeTests.csproj -c Release -m:1 --results-directory results --logger 'trx;LogFileName=runtime-tests.trx' *> execution.log
    if ($LASTEXITCODE -ne 0) { throw "Type Manager tests failed; inspect $OutputRoot/execution.log" }
    Import-Module (Join-Path $OutputRoot 'Evidence.psm1') -Force
    $native = Read-NativeTrx (Join-Path $OutputRoot 'results/runtime-tests.trx')
    if ($native.total -ne 90 -or $native.passed -ne 90 -or $native.failed -ne 0 -or $native.skipped -ne 0) { throw 'Unexpected Type Manager test count or outcome.' }
    @{ coreSha256 = (Get-FileHash candidate/CIS.Core.dll).Hash.ToLowerInvariant(); sdk = $sdk; runtimePin = $runtimePolicy.targetRuntime; trxSha256 = (Get-FileHash results/runtime-tests.trx).Hash.ToLowerInvariant(); total = $native.total; passed = $native.passed; failed = $native.failed; skipped = $native.skipped; scope = '81 Core unit cases plus 9 runtime architecture cases against frozen candidate' } |
        ConvertTo-Json | Set-Content -Encoding utf8 results.json
}
finally { Pop-Location }
Write-Output "Type Manager frozen-candidate tests passed: 90. Report: $OutputRoot/results.json"
