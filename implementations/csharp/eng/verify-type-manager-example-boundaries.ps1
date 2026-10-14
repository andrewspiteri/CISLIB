#Requires -Version 7.2
param(
    [Parameter(Mandatory)][string] $LanguageRoot,
    [Parameter(Mandatory)][string] $OutputRoot
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
Set-StrictMode -Version Latest
$LanguageRoot = [IO.Path]::TrimEndingDirectorySeparator((Resolve-Path -LiteralPath $LanguageRoot).Path)
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
$canonical = [IO.Path]::TrimEndingDirectorySeparator((Resolve-Path -LiteralPath (Split-Path $PSScriptRoot -Parent)).Path)
if ($LanguageRoot.Equals($canonical, [StringComparison]::OrdinalIgnoreCase) -or
    $LanguageRoot.StartsWith($canonical + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Use an isolated example source tree outside the canonical language directory; this check temporarily mutates projects.'
}
for ($directory = [IO.DirectoryInfo]::new($LanguageRoot); $null -ne $directory; $directory = $directory.Parent) {
    if ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Isolated input must not use reparse-point ancestors.' }
}
if ((Test-Path -LiteralPath $OutputRoot) -and @(Get-ChildItem -LiteralPath $OutputRoot -Force).Count -ne 0) { throw 'Use an empty output directory.' }
$inputFiles = @('examples/type-manager/Contracts/Contracts.csproj', 'examples/type-manager/Unused/Unused.csproj',
    'examples/type-manager/Console/Console.csproj', 'examples/type-manager/Web/Web.csproj',
    'tests/fixtures/TypeManager/ExampleLate/ExampleLate.csproj', 'src/CIS.Core/CIS.Core.csproj',
    'eng/CIS.TypeManager.Build/CIS.TypeManager.Build.csproj', 'eng/project-boundaries.json',
    'eng/runtime-dependencies.json', 'eng/development-dependencies.json', 'global.json', 'Directory.Build.props', 'Directory.Packages.props')
if ($null -ne (Get-Item -LiteralPath (Join-Path $LanguageRoot 'Directory.Build.targets') -Force -ErrorAction Ignore)) { $inputFiles += 'Directory.Build.targets' }
# Require disposable copies, never hardlinked inputs. Check each descendant path
# before copying policy or mutating projects; ancestor-only checks miss junctions.
foreach ($file in $inputFiles) {
    for ($path = Join-Path $LanguageRoot $file; $path -ne $LanguageRoot; $path = Split-Path $path -Parent) {
        $item = Get-Item -LiteralPath $path -Force -ErrorAction Ignore
        if ($item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Isolated input uses a reparse point: $path"
        }
    }
}
New-Item -ItemType Directory -Force -Path $OutputRoot, (Join-Path $LanguageRoot 'eng') | Out-Null
foreach ($name in @('project-boundaries.json', 'runtime-dependencies.json', 'development-dependencies.json')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $LanguageRoot "eng/$name")
}
$inputHashes = @{}
foreach ($file in $inputFiles) { $inputHashes[$file] = (Get-FileHash (Join-Path $LanguageRoot $file)).Hash.ToLowerInvariant() }
$report = @{ complete = $false; inputScope = 'Checked project metadata, copied policy and named language configuration; not full transitive build provenance'; inputs = $inputHashes; verifierSha256 = (Get-FileHash $PSCommandPath).Hash.ToLowerInvariant(); checkerSha256 = (Get-FileHash (Join-Path $PSScriptRoot 'check-dependencies.ps1')).Hash.ToLowerInvariant(); results = @() }
$driver = Join-Path $OutputRoot 'invoke-checker.ps1'
@'
param([string] $Checker, [string] $LanguageRoot, [string] $Project)
try { & $Checker -LanguageRoot $LanguageRoot -ProjectPath $Project; if (-not $?) { exit 1 } }
catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
'@ | Set-Content -Encoding utf8 $driver
function Save-Report { $report | ConvertTo-Json -Depth 12 | Set-Content -Encoding utf8 (Join-Path $OutputRoot 'results.json') }
function Check-Project([string] $Name, [string] $Project, [string] $ExpectedError = '') {
    $log = Join-Path $OutputRoot "$Name.log"
    & pwsh -NoProfile -File $driver -Checker (Join-Path $PSScriptRoot 'check-dependencies.ps1') -LanguageRoot $LanguageRoot -Project $Project *> $log
    $code = $LASTEXITCODE
    if ($ExpectedError) {
        if ($code -eq 0 -or -not ([IO.File]::ReadAllText($log).Contains($ExpectedError))) { throw "Control did not reject $Name for $ExpectedError" }
    }
    elseif ($code -ne 0) { throw "Boundary check failed: $Name" }
    $report.results += @{ case = $Name; passed = $true; expectedError = $ExpectedError }
    Save-Report
}
Save-Report
foreach ($project in @('examples/type-manager/Contracts/Contracts.csproj', 'examples/type-manager/Unused/Unused.csproj',
    'examples/type-manager/Console/Console.csproj', 'examples/type-manager/Web/Web.csproj', 'tests/fixtures/TypeManager/ExampleLate/ExampleLate.csproj')) {
    Check-Project ([IO.Path]::GetFileNameWithoutExtension($project)) $project
}
foreach ($project in @('src/CIS.Core/CIS.Core.csproj', 'examples/type-manager/Console/Console.csproj')) {
    $path = Join-Path $LanguageRoot $project
    $original = [IO.File]::ReadAllBytes($path)
    try {
        [xml]$xml = [IO.File]::ReadAllText($path)
        $group = $xml.CreateElement('ItemGroup')
        $reference = $xml.CreateElement('FrameworkReference')
        $reference.SetAttribute('Include', 'Microsoft.AspNetCore.App')
        $null = $group.AppendChild($reference)
        $null = $xml.Project.AppendChild($group)
        $xml.Save($path)
        Check-Project ('reject-framework-' + [IO.Path]::GetFileNameWithoutExtension($project)) $project "Forbidden framework reference from ${project}: Microsoft.AspNetCore.App"
    }
    finally { [IO.File]::WriteAllBytes($path, $original) }
}
$policyPath = Join-Path $LanguageRoot 'eng/project-boundaries.json'
$originalPolicy = [IO.File]::ReadAllBytes($policyPath)
try {
    $policy = [IO.File]::ReadAllText($policyPath) | ConvertFrom-Json -AsHashtable
    ($policy.projects | Where-Object path -eq 'examples/type-manager/Web/Web.csproj').Remove('additionalImplicitFrameworkReferences')
    $policy | ConvertTo-Json -Depth 20 | Set-Content -Encoding utf8 $policyPath
    Check-Project 'reject-unregistered-web-framework' 'examples/type-manager/Web/Web.csproj' 'Forbidden framework reference from examples/type-manager/Web/Web.csproj: Microsoft.AspNetCore.App'
}
finally { [IO.File]::WriteAllBytes($policyPath, $originalPolicy) }
foreach ($project in @('src/CIS.Core/CIS.Core.csproj', 'tests/fixtures/TypeManager/ExampleLate/ExampleLate.csproj')) {
    $path = Join-Path $LanguageRoot $project
    $originalProject = [IO.File]::ReadAllBytes($path)
    try {
        $policy = [IO.File]::ReadAllText($policyPath) | ConvertFrom-Json -AsHashtable
        ($policy.projects | Where-Object path -eq $project)['additionalImplicitFrameworkReferences'] = @('Microsoft.AspNetCore.App')
        $policy | ConvertTo-Json -Depth 20 | Set-Content -Encoding utf8 $policyPath
        [xml]$xml = [IO.File]::ReadAllText($path)
        $group = $xml.CreateElement('ItemGroup')
        $reference = $xml.CreateElement('FrameworkReference')
        $reference.SetAttribute('Include', 'Microsoft.AspNetCore.App')
        # Mark it implicit so the role guard, not the separate implicit-only guard,
        # must reject this deliberate attempt to widen a non-example's policy.
        $reference.SetAttribute('IsImplicitlyDefined', 'true')
        $null = $group.AppendChild($reference)
        $null = $xml.Project.AppendChild($group)
        $xml.Save($path)
        Check-Project ('reject-policy-' + [IO.Path]::GetFileNameWithoutExtension($project)) $project "Forbidden framework reference from ${project}: Microsoft.AspNetCore.App"
        if ($project -eq 'src/CIS.Core/CIS.Core.csproj') {
            $checkerText = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'check-dependencies.ps1'))
            $anchor = '$project.role -eq ''example'' -and '
            if ([regex]::Matches($checkerText, [regex]::Escape($anchor)).Count -ne 1) { throw 'Role-gate mutation anchor is not unique.' }
            $mutant = Join-Path $OutputRoot 'checker-without-role-gate.ps1'
            [IO.File]::WriteAllText($mutant, $checkerText.Replace($anchor, ''))
            & pwsh -NoProfile -File $driver -Checker $mutant -LanguageRoot $LanguageRoot -Project $project *> (Join-Path $OutputRoot 'role-gate-mutant.log')
            if ($LASTEXITCODE -ne 0) { throw 'Role-gate control is masked by another rejection; inspect the mutant log.' }
            $report.results += @{ case = 'role-gate-mutant-detected'; passed = $true; observed = 'Removing only the role guard accepts the forbidden shipping framework, which the control rejects with the real checker.' }
            Save-Report
        }
    }
    finally {
        [IO.File]::WriteAllBytes($path, $originalProject)
        [IO.File]::WriteAllBytes($policyPath, $originalPolicy)
    }
}
foreach ($file in $inputFiles) {
    if ((Get-FileHash (Join-Path $LanguageRoot $file)).Hash.ToLowerInvariant() -ne $inputHashes[$file]) { throw "Input was not restored: $file" }
}
if ($report.results.Count -ne 11) { throw 'Unexpected boundary check count.' }
$report.complete = $true
Save-Report
Write-Output 'Type Manager example boundary checks passed: 11.'
