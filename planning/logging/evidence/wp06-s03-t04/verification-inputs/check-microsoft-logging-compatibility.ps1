param([Parameter(Mandatory)][string] $CoreAssembly, [string] $OutputRoot)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$languageRoot = Split-Path $PSScriptRoot -Parent
$repositoryRoot = Split-Path (Split-Path $languageRoot -Parent) -Parent
$fixtures = Join-Path $languageRoot 'verification/compatibility/microsoft-logging'
$policy = Get-Content (Join-Path $PSScriptRoot 'toolchain.json') -Raw | ConvertFrom-Json
$CoreAssembly = [IO.Path]::GetFullPath($CoreAssembly)
if (-not $OutputRoot) { $OutputRoot = Join-Path $repositoryRoot ('.artifacts/csharp/provider-compatibility/' + [guid]::NewGuid().ToString('N')) }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $OutputRoot) { throw 'Use a new output directory to retain immutable observations.' }
New-Item -ItemType Directory -Path $OutputRoot | Out-Null
function Hash([string] $Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
$inputs = @(Get-ChildItem -LiteralPath $fixtures -Recurse -File | ForEach-Object { @{ path = [IO.Path]::GetRelativePath($fixtures, $_.FullName).Replace('\','/'); sha256 = Hash $_.FullName } })
$ledger = Get-Content (Join-Path $repositoryRoot 'compatibility/csharp/publications/ledger.json') -Raw | ConvertFrom-Json
$publicationRows = @($ledger.publications).Count
$report = @{ publicationRows = $publicationRows; historyStatus = $(if ($publicationRows -eq 0) { 'NoHistoricalPublications' } else { 'HistoricalReplayNotPerformedByFixtureRunner' }); scope = 'Unpublished provider fixture qualification; not historical compatibility or full release'; completed = $false; historicalComparisons = 0; coreSha256 = Hash $CoreAssembly; sourceInputs = $inputs; runnerSha256 = Hash $PSCommandPath; toolchainSha256 = Hash (Join-Path $PSScriptRoot 'toolchain.json'); results = @() }
function Invoke-Consumer([string] $Id, [string] $Directory) {
    $consumer = Join-Path $Directory 'CIS.Logging.ProviderContractConsumer.dll'
    $start = [Diagnostics.ProcessStartInfo]::new('dotnet')
    foreach ($arg in @('exec', '--fx-version', $policy.targetRuntime, '--roll-forward', 'Disable', $consumer, (Join-Path $OutputRoot "observations/$Id"))) { $start.ArgumentList.Add($arg) }
    $start.WorkingDirectory = $Directory; $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    foreach ($key in @($start.Environment.Keys)) { if ($key.StartsWith('CORE_', [StringComparison]::OrdinalIgnoreCase) -or $key -in @('DD_SERVICE','DD_VERSION','DD_ENV')) { $null = $start.Environment.Remove($key) } }
    $process = [Diagnostics.Process]::Start($start)
    try {
        $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(60000)) { $process.Kill($true); $process.WaitForExit(); throw "Consumer timeout: $Id" }
        $out = $stdout.GetAwaiter().GetResult(); $err = $stderr.GetAwaiter().GetResult()
        [IO.File]::WriteAllText((Join-Path $OutputRoot "$Id.stdout.json"), $out, [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $OutputRoot "$Id.stderr.txt"), $err, [Text.UTF8Encoding]::new($false))
        if ($process.ExitCode -ne 0 -or $err.Length -ne 0) { throw "Consumer failed: $Id; inspect retained native stdout/stderr." }
        $observed = $out | ConvertFrom-Json
        if ($observed.contract -ne 'passed' -or $observed.records -ne 3 -or $observed.runtime -ne $policy.targetRuntime) { throw "Unexpected consumer result: $Id" }
        foreach ($name in @('core','logging','dependencyInjection','consumer')) {
            $file = switch ($name) { 'core' {'CIS.Core.dll'} 'logging' {'Microsoft.Extensions.Logging.Abstractions.dll'} 'dependencyInjection' {'Microsoft.Extensions.DependencyInjection.Abstractions.dll'} 'consumer' {'CIS.Logging.ProviderContractConsumer.dll'} }
            $expected = Join-Path $Directory $file
            if ([IO.Path]::GetFullPath($observed.$name.path) -ne $expected -or $observed.$name.sha256 -ne (Hash $expected)) { throw "Unexpected actual loaded assembly: $Id/$name" }
        }
        if ($observed.core.sha256 -ne $report.coreSha256) { throw 'Consumer did not load selected Core bytes.' }
        $api = Get-Content (Join-Path $fixtures 'api-contract.json') -Raw | ConvertFrom-Json
        if ($observed.api.type -ne $api.type -or $observed.api.isSealed -ne $api.isSealed) { throw 'Provider API identity changed.' }
        foreach ($interface in $api.interfaces) { if ($interface -notin $observed.api.interfaces) { throw 'Supported provider interface missing.' } }
        foreach ($member in @(@{ expected = $api.constructor; actual = $observed.api.constructor }) + @($api.methods | ForEach-Object {
            $actual = @($observed.api.methods | Where-Object name -eq $_.name)
            if ($actual.Count -ne 1 -or $actual[0].returns -ne $_.returns) { throw "Supported method missing or return changed: $($_.name)" }
            @{ expected = $_.signature; actual = $actual[0].signature }
        })) {
            $expected = @($member.expected.parameters); $actual = @($member.actual.parameters)
            if ($actual.Count -ne $expected.Count) { throw 'Provider parameter count changed.' }
            for ($i=0; $i -lt $expected.Count; $i++) {
                foreach ($field in @('name','type','optional')) { if ($actual[$i].$field -cne $expected[$i].$field) { throw "Provider parameter $field changed." } }
            }
        }
        $report.results += @{ id = $Id; passed = $true; actual = $observed; consumerSha256 = Hash $consumer }
    }
    finally { $process.Dispose() }
}
Push-Location $languageRoot
try {
    if ((& dotnet --version).Trim() -ne $policy.sdk) { throw 'SDK differs from policy.' }
    $assets = Get-Content 'src/CIS.Core/obj/project.assets.json' -Raw | ConvertFrom-Json -AsHashtable
    $approved = Get-Content 'eng/runtime-dependencies.json' -Raw | ConvertFrom-Json
    $libraries = @(@{ path = $CoreAssembly; name = 'CIS.Core.dll' })
    foreach ($package in $approved.packages) {
        $key = "$($package.id)/$($package.version)"
        $asset = @($assets.targets['net10.0'][$key].runtime.Keys | Where-Object { $_ -like '*.dll' })
        if ($asset.Count -ne 1) { throw 'Expected one approved runtime assembly per package.' }
        $relative = Join-Path $assets.libraries[$key].path $asset[0]
        $found = @($assets.packageFolders.Keys | ForEach-Object { Join-Path $_ $relative } | Where-Object { Test-Path -LiteralPath $_ })
        if ($found.Count -ne 1) { throw 'Approved dependency missing or ambiguous.' }
        $libraries += @{ path = $found[0]; name = [IO.Path]::GetFileName($found[0]) }
    }
    $source = Join-Path $OutputRoot 'source'
    New-Item -ItemType Directory -Path $source | Out-Null
    Copy-Item (Join-Path $fixtures 'source/Program.cs') (Join-Path $source 'Program.cs')
    Copy-Item (Join-Path $fixtures 'Consumer.csproj.template') (Join-Path $source 'Consumer.csproj')
    foreach ($name in @('Directory.Build.props','Directory.Build.targets','Directory.Packages.props')) { '<Project />' | Set-Content -LiteralPath (Join-Path $source $name) -Encoding utf8 }
    '<configuration><packageSources><clear /></packageSources></configuration>' | Set-Content -LiteralPath (Join-Path $source 'NuGet.config') -Encoding utf8
    $logging = ($libraries | Where-Object name -eq 'Microsoft.Extensions.Logging.Abstractions.dll').path
    $di = ($libraries | Where-Object name -eq 'Microsoft.Extensions.DependencyInjection.Abstractions.dll').path
    & dotnet build (Join-Path $source 'Consumer.csproj') -c Release "-p:CoreAssemblyPath=$CoreAssembly" "-p:LoggingAssemblyPath=$logging" "-p:DependencyInjectionAssemblyPath=$di" *> (Join-Path $OutputRoot 'source-build.log')
    if ($LASTEXITCODE -ne 0) { throw 'Source consumer failed to build; inspect source-build.log.' }
    $compiled = Join-Path $source 'bin/Release/net10.0'
    Invoke-Consumer 'Source' $compiled
    $frozen = Join-Path $OutputRoot 'frozen'
    New-Item -ItemType Directory -Path $frozen | Out-Null
    foreach ($suffix in @('.dll','.pdb','.deps.json','.runtimeconfig.json')) { Copy-Item (Join-Path $compiled ('CIS.Logging.ProviderContractConsumer'+$suffix)) $frozen }
    $frozenHashes = @(Get-ChildItem -LiteralPath $frozen -File | ForEach-Object { @{ name = $_.Name; sha256 = Hash $_.FullName } })
    foreach ($scenario in @('CoreReplacement','CompleteUpgrade')) {
        $directory = Join-Path $OutputRoot $scenario
        New-Item -ItemType Directory -Path $directory | Out-Null
        Copy-Item -LiteralPath (Join-Path $frozen 'CIS.Logging.ProviderContractConsumer.dll'), (Join-Path $frozen 'CIS.Logging.ProviderContractConsumer.pdb'), (Join-Path $frozen 'CIS.Logging.ProviderContractConsumer.deps.json'), (Join-Path $frozen 'CIS.Logging.ProviderContractConsumer.runtimeconfig.json') -Destination $directory
        foreach ($library in $libraries) { Copy-Item -LiteralPath $library.path -Destination (Join-Path $directory $library.name) }
        foreach ($entry in $frozenHashes) { if ((Hash (Join-Path $directory $entry.name)) -ne $entry.sha256) { throw 'Consumer changed before binary replay.' } }
        Invoke-Consumer $scenario $directory
        foreach ($entry in $frozenHashes) { if ((Hash (Join-Path $directory $entry.name)) -ne $entry.sha256) { throw 'Replayed consumer changed.' } }
    }
    foreach ($entry in $frozenHashes) { if ((Hash (Join-Path $frozen $entry.name)) -ne $entry.sha256) { throw 'Frozen consumer changed.' } }
    foreach ($entry in $inputs) { if ((Hash (Join-Path $fixtures $entry.path)) -ne $entry.sha256) { throw 'Source fixture changed during qualification.' } }
    $report.frozenInputs = $frozenHashes
    $report.runtimeInputs = @($libraries | ForEach-Object { @{ name = $_.name; sha256 = Hash $_.path } })
    $report.sameRuntimePayloadForBothScenarios = $true
    $report.completed = $true
}
finally {
    $report | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath (Join-Path $OutputRoot 'results.json') -Encoding utf8
    Pop-Location
}
Write-Output "Provider compatibility fixtures passed. Reports: $OutputRoot"

