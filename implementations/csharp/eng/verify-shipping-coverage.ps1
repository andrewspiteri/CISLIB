param([Parameter(Mandatory)][string] $OutputRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not [string]::IsNullOrWhiteSpace($env:CIS_ARCHITECTURE_INSPECTION)) {
    throw 'CIS_ARCHITECTURE_INSPECTION is only for focused tests. Clear it before running shipping coverage.'
}
Import-Module (Join-Path $PSScriptRoot 'Verification.psm1') -Force
$languageRoot = Split-Path $PSScriptRoot -Parent
$policy = Get-Content -Raw (Join-Path $languageRoot 'verification/coverage/shipping.json') | ConvertFrom-Json
$toolchain = Get-Content -Raw (Join-Path $PSScriptRoot 'toolchain.json') | ConvertFrom-Json
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $OutputRoot) { throw 'Use a new output directory; native reports are immutable.' }
New-Item -ItemType Directory -Path $OutputRoot | Out-Null
$repositoryRoot = Split-Path (Split-Path $languageRoot -Parent) -Parent
$sourceBefore = @(Get-SourceInventory $repositoryRoot)
$sourceBefore | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $OutputRoot 'source-inputs.json') -Encoding utf8
$commands = [Collections.Generic.List[object]]::new()
$results = [Collections.Generic.List[string]]::new()
$inputPaths = @((Join-Path $languageRoot 'Directory.Build.props'), (Join-Path $languageRoot 'Directory.Packages.props'), (Join-Path $languageRoot 'global.json'), (Join-Path $languageRoot 'NuGet.config'), $PSCommandPath, (Join-Path $PSScriptRoot 'CIS.DependencyChecker/ShippingCoverage.cs'), (Join-Path $PSScriptRoot 'toolchain.json'),
    (Join-Path $languageRoot 'verification/coverage/shipping.json'), (Join-Path $languageRoot $policy.settings))
$inputPaths += Get-ChildItem (Join-Path $PSScriptRoot 'CIS.DependencyChecker') -File |
    Where-Object Extension -In @('.cs', '.csproj', '.json') | ForEach-Object FullName
foreach ($assembly in $policy.assemblies) {
    $inputPaths += Join-Path $languageRoot $assembly.project
    $inputPaths += Get-ChildItem (Join-Path $languageRoot $assembly.sourceRoot) -Recurse -File -Filter '*.cs' |
        Where-Object FullName -NotMatch '[\\/](bin|obj)[\\/]' | ForEach-Object FullName
    $binary = Join-Path (Split-Path (Join-Path $languageRoot $assembly.project) -Parent) ("bin/Release/net10.0/" + $assembly.name + '.dll')
    $inputPaths += $binary
    $inputPaths += [IO.Path]::ChangeExtension($binary, '.pdb')
}
$inputPaths += Get-ChildItem (Join-Path $languageRoot 'tests') -Recurse -File |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and $_.Extension -in @('.cs', '.csproj', '.props', '.targets', '.json', '.py', '.ps1') } | ForEach-Object FullName
foreach ($family in $policy.families.PSObject.Properties) {
    $inputPaths += Join-Path $languageRoot ("tests/CIS.Core.$($family.Value)/bin/Release/net10.0/CIS.Core.$($family.Value).dll")
}
# Include every shipping assembly copy that the no-build test hosts can load.
$loadedCopies = @()
foreach ($family in $policy.families.PSObject.Properties) {
    $directory = Join-Path $languageRoot "tests/CIS.Core.$($family.Value)/bin/Release/net10.0"
    foreach ($assembly in $policy.assemblies) {
        $copy = Join-Path $directory ($assembly.name + '.dll')
        if (Test-Path -LiteralPath $copy) {
            $inputPaths += $copy
            $loadedCopies += @{ path = $copy; assembly = $assembly.name }
        }
        elseif ($assembly.name -eq 'CIS.Core') { throw "Missing copy-local shipping assembly: $copy" }
    }
}
# Pin all repository inputs, including shared conformance/contracts and tool configuration.
$inputPaths += $sourceBefore | ForEach-Object { Join-Path $repositoryRoot $_.path }
$checker = Join-Path $PSScriptRoot 'CIS.DependencyChecker/bin/Release/net10.0/CIS.DependencyChecker.dll'
$inputPaths += $checker
# These are the Developer observations and reference bytes read by architecture tests.
$developerRoot = Join-Path $repositoryRoot '.artifacts/csharp/verification/Developer'
$developerSummaryPath = Join-Path $developerRoot 'summary.json'
$developerSummary = Get-Content -Raw -LiteralPath $developerSummaryPath | ConvertFrom-Json
if ($developerSummary.exitCode -ne 0 -or $developerSummary.profile -cne 'Developer') { throw 'Coverage requires a successful Developer checkpoint.' }
$inspectionPath = Join-Path $developerRoot $developerSummary.architectureInspection.path
if ((Get-FileHash -LiteralPath $inspectionPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $developerSummary.architectureInspection.sha256) { throw 'Developer inspection differs from its recorded pin.' }
$inspection = Get-Content -Raw -LiteralPath $inspectionPath | ConvertFrom-Json
$startupRoot = Join-Path $developerRoot 'contracts/startup'
$startupSummaryPath = Join-Path $startupRoot 'summary.json'
$startupSummary = Get-Content -Raw -LiteralPath $startupSummaryPath | ConvertFrom-Json
$referencePath = Join-Path $startupRoot $startupSummary.referenceAssembly.path
if ((Get-FileHash -LiteralPath $referencePath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $startupSummary.referenceAssembly.sha256) { throw 'Startup reference assembly differs from its recorded pin.' }
if ((Get-FileHash -LiteralPath $inspection.package.path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $inspection.package.sha256) { throw 'Developer package differs from its recorded pin.' }
$inputPaths += @($developerSummaryPath, $inspectionPath, $inspection.package.path, $startupSummaryPath, $referencePath)
$inputs = @($inputPaths | Sort-Object -Unique | ForEach-Object {
    @{ path = [IO.Path]::GetRelativePath($languageRoot, $_).Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$inputs | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $OutputRoot 'inputs.json') -Encoding utf8
$previousReuse = $env:MSBUILDDISABLENODEREUSE
Push-Location $languageRoot
try {
    $env:MSBUILDDISABLENODEREUSE = '1'
    if ((& dotnet --version).Trim() -ne $toolchain.sdk) { throw 'Selected SDK differs from the pin.' }
    [ordered]@{ os = [Runtime.InteropServices.RuntimeInformation]::OSDescription; processArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString();
        sdk = (& dotnet --version); sourceCommit = (& git -C $languageRoot rev-parse HEAD) } | ConvertTo-Json | Set-Content (Join-Path $OutputRoot 'environment.json') -Encoding utf8
    $repositoryRoot = Split-Path (Split-Path $languageRoot -Parent) -Parent
    $version = (& dotnet msbuild src/CIS.Core/CIS.Core.csproj -p:Configuration=Release -getProperty:Version).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Unable to evaluate candidate version.' }
    $packages = @(Get-Item -LiteralPath (Join-Path $repositoryRoot ".artifacts/csharp/packages/CIS.Core.$version.nupkg"))
    Add-Type -AssemblyName System.IO.Compression
    $archive = [IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
    try {
        $expected = @($policy.assemblies | ForEach-Object packageEntry | Sort-Object)
        $actual = @($archive.Entries | Where-Object { $_.FullName -match '[.]dll$' } | ForEach-Object FullName | Sort-Object)
        if (@(Compare-Object $expected $actual).Count -ne 0) { throw 'Shipped DLL inventory differs from the coverage policy.' }
        foreach ($assembly in $policy.assemblies) {
            $entry = $archive.GetEntry($assembly.packageEntry)
            $stream = $entry.Open()
            try { $packedHash = [Convert]::ToHexStringLower([Security.Cryptography.SHA256]::HashData($stream)) }
            finally { $stream.Dispose() }
            $binary = Join-Path (Split-Path (Join-Path $languageRoot $assembly.project) -Parent) ("bin/Release/net10.0/" + $assembly.name + '.dll')
            if ((Get-FileHash $binary -Algorithm SHA256).Hash.ToLowerInvariant() -ne $packedHash) { throw "Repack the current candidate before coverage: $($assembly.name)" }
            foreach ($copy in $loadedCopies | Where-Object assembly -EQ $assembly.name) {
                if ((Get-FileHash -LiteralPath $copy.path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $packedHash) { throw "Copy-local shipping assembly differs from the package: $($copy.path)" }
            }
        }
    }
    finally { $archive.Dispose() }
    $packageHash = (Get-FileHash $packages[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    @{ path = [IO.Path]::GetRelativePath($repositoryRoot, $packages[0].FullName).Replace('\', '/'); sha256 = $packageHash } |
        ConvertTo-Json | Set-Content (Join-Path $OutputRoot 'candidate.json') -Encoding utf8
    foreach ($family in $policy.families.PSObject.Properties) {
        $directory = Join-Path $OutputRoot $family.Name
        $project = "tests/CIS.Core.$($family.Value)/CIS.Core.$($family.Value).csproj"
        $arguments = @('test', $project, '-c', 'Release', '--no-build', '--no-restore', '--settings', $policy.settings,
            '--collect', 'Code Coverage;Format=cobertura', '--results-directory', $directory, '--logger', 'trx;LogFileName=report.trx')
        $started = [DateTime]::UtcNow.ToString('O')
        & dotnet @arguments *> (Join-Path $OutputRoot ($family.Name + '.log'))
        $code = $LASTEXITCODE
        $commands.Add(@{ family = $family.Name; executable = 'dotnet'; argv = $arguments; workingDirectory = $languageRoot; startedUtc = $started; finishedUtc = [DateTime]::UtcNow.ToString('O'); exitCode = $code })
        $commands | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $OutputRoot 'commands.json') -Encoding utf8
        if ($code -ne 0) { throw "Coverage test family $($family.Name) failed ($code); see its retained log." }
        $results.Add($directory)
    }
    if ((Get-FileHash $packages[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant() -ne $packageHash) { throw 'Candidate package changed during coverage.' }
    $sourceAfter = @(Get-SourceInventory $repositoryRoot)
    if (Compare-Object $sourceBefore $sourceAfter -Property path,sha256) { throw 'Repository inputs changed during coverage.' }
    foreach ($input in $inputs) {
        if ((Get-FileHash -LiteralPath (Join-Path $languageRoot $input.path) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $input.sha256) { throw "Coverage input changed: $($input.path)" }
    }
    & dotnet $checker shipping-coverage $languageRoot (Join-Path $OutputRoot 'coverage.json') @results
    exit $LASTEXITCODE
}
finally {
    $env:MSBUILDDISABLENODEREUSE = $previousReuse
    Pop-Location
}
