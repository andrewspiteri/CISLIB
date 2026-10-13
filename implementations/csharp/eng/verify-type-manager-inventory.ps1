param([string] $OutputRoot)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$languageRoot = Split-Path $PSScriptRoot -Parent
if (-not $OutputRoot) { $OutputRoot = Join-Path $languageRoot '../../.artifacts/type-manager/inventory' }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$hostProject = 'tests/fixtures/TypeManager/Host/Host.csproj'
$manifestName = 'CIS.TypeManager.Fixture.Host.types.json'
$results = [Collections.Generic.List[object]]::new()
# A failed rerun must not retain a previous successful report.
if (Test-Path -LiteralPath (Join-Path $OutputRoot 'results.json')) { Remove-Item -LiteralPath (Join-Path $OutputRoot 'results.json') }

function Clear-Inventory([string] $Directory, [string] $FileName = $manifestName) {
    $path = Join-Path $Directory $FileName
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
}

function Invoke-DotNet([string] $Name, [string[]] $Arguments, [string] $ExpectedError = '') {
    $logPath = Join-Path $OutputRoot ($Name + '.log')
    & dotnet @Arguments *> $logPath
    $exitCode = $LASTEXITCODE
    if ($ExpectedError) {
        if ($exitCode -eq 0 -or -not (Select-String -LiteralPath $logPath -SimpleMatch $ExpectedError -Quiet)) {
            throw "Expected $ExpectedError from $Name; inspect $logPath"
        }
    }
    elseif ($exitCode -ne 0) { throw "Failed $Name ($exitCode); inspect $logPath" }
    return $logPath
}

function Check-Inventory([string] $Name, [string] $Directory, [int] $ExpectedCount) {
    $path = Join-Path $Directory $manifestName
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "The $Name command did not generate its inventory: $path" }
    $inventory = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
    $names = @($inventory.assemblies | ForEach-Object { ([Reflection.AssemblyName]::new($_.assemblyName)).Name })
    $expectedNames = @('CIS.Core', 'CIS.TypeManager.Fixture.Host', 'Microsoft.Extensions.DependencyInjection.Abstractions', 'Microsoft.Extensions.Logging.Abstractions')
    if ($ExpectedCount -eq 7) { $expectedNames += 'CIS.TypeManager.Fixture.Contracts', 'CIS.TypeManager.Fixture.Transitive', 'CIS.TypeManager.Fixture.Unused' }
    if ($inventory.schemaVersion -ne 1 -or $names.Count -ne $ExpectedCount -or (Compare-Object ($names | Sort-Object) ($expectedNames | Sort-Object))) {
        throw "Wrong selected set in $Name"
    }
    if ($inventory.hostAssembly -ne [Reflection.AssemblyName]::GetAssemblyName((Join-Path $Directory 'CIS.TypeManager.Fixture.Host.dll')).FullName) {
        throw "Wrong host identity in $Name"
    }
    $actualOrder = @($inventory.assemblies | ForEach-Object assemblyName)
    $ordered = [string[]] $actualOrder.Clone()
    [Array]::Sort($ordered, [StringComparer]::Ordinal)
    if (($actualOrder -join '|') -cne ($ordered -join '|')) { throw "Nondeterministic order in $Name" }
    foreach ($entry in $inventory.assemblies) {
        if ($entry.assemblyName -ne [Reflection.AssemblyName]::GetAssemblyName((Join-Path $Directory $entry.relativePath)).FullName) {
            throw "Wrong deployed identity in $Name"
        }
    }
    Copy-Item -LiteralPath $path -Destination (Join-Path $OutputRoot ($Name + '.types.json'))
    $results.Add(@{ name = $Name; passed = $true; selectedAssemblyCount = $names.Count })
}

Push-Location $languageRoot
try {
    $directoryLog = Invoke-DotNet 'target-directory' @('msbuild', $hostProject, '-p:Configuration=Release', '-getProperty:TargetDir', '-nologo')
    $hostDirectory = (Get-Content -Raw -LiteralPath $directoryLog).Trim()
    if (-not [IO.Path]::IsPathFullyQualified($hostDirectory)) { throw 'MSBuild returned an invalid TargetDir.' }
    Clear-Inventory $hostDirectory
    $null = Invoke-DotNet 'build' @('build', $hostProject, '-c', 'Release', '-m:1', '--nologo', '-v:minimal')
    Check-Inventory 'build' $hostDirectory 7
    if (-not (Test-Path (Join-Path $hostDirectory 'fr/CIS.TypeManager.Fixture.Unused.resources.dll'))) { throw 'Satellite fixture was not deployed.' }
    if (-not (Test-Path (Join-Path $hostDirectory 'content-only.dll'))) { throw 'Content exclusion fixture was not deployed.' }
    $runLog = Invoke-DotNet 'fresh-process' @('run', '--project', $hostProject, '-c', 'Release', '--no-build', '--no-restore')
    $run = Get-Content -Raw $runLog | ConvertFrom-Json
    if ($run.unusedInitiallyLoaded -or $run.identities.Count -ne 7) { throw 'Initially-unloaded fixture failed.' }
    $results.Add(@{ name = 'initially-unloaded'; passed = $true })
    if (-not $run.containerConstructed -or $run.typeCount -le 0) { throw 'Preparation did not finish before the container boundary.' }
    $cancelLog = Invoke-DotNet 'cancel-during-load' @((Join-Path $hostDirectory 'CIS.TypeManager.Fixture.Host.dll'), 'cancel-during-load')
    $canceled = Get-Content -Raw $cancelLog | ConvertFrom-Json
    if (-not $canceled.canceledDuringLoad -or $canceled.containerConstructed -or $canceled.unusedInitiallyLoaded) { throw 'Cancellation did not prevent publication during real loading.' }
    $results.Add(@{ name = 'cancel-during-load'; passed = $true })

    $manifest = Get-Item (Join-Path $hostDirectory $manifestName)
    $beforeTicks = $manifest.LastWriteTimeUtc.Ticks
    $beforeHash = (Get-FileHash $manifest.FullName).Hash
    $incrementalLog = Invoke-DotNet 'incremental' @('build', $hostProject, '-c', 'Release', '--no-restore', '-m:1', '--nologo', '-v:normal')
    if (-not (Select-String -LiteralPath $incrementalLog -SimpleMatch 'Type Manager inventory:' -Quiet)) { throw 'Incremental build did not execute the inventory generator.' }
    if ((Get-Item $manifest.FullName).LastWriteTimeUtc.Ticks -ne $beforeTicks -or (Get-FileHash $manifest.FullName).Hash -ne $beforeHash) {
        throw 'Unchanged manifest was rewritten.'
    }
    $results.Add(@{ name = 'unchanged-output'; passed = $true })

    $publishDirectory = Join-Path $OutputRoot 'publish'
    Clear-Inventory $publishDirectory
    $null = Invoke-DotNet 'publish-no-build' @('publish', $hostProject, '-c', 'Release', '--no-build', '--no-restore', '-m:1', '-o', $publishDirectory, '--nologo')
    Check-Inventory 'publish-no-build' $publishDirectory 7
    $null = Invoke-DotNet 'published-process' @((Join-Path $publishDirectory 'CIS.TypeManager.Fixture.Host.dll'))

    $publishPathLog = Invoke-DotNet 'default-publish-directory' @('msbuild', $hostProject, '-p:Configuration=Release', '-getProperty:PublishDir', '-nologo')
    $defaultPublish = [IO.Path]::GetFullPath((Get-Content -LiteralPath $publishPathLog -Raw).Trim(), (Split-Path (Join-Path $languageRoot $hostProject) -Parent))
    Clear-Inventory $defaultPublish
    $null = Invoke-DotNet 'default-publish-no-build' @('publish', $hostProject, '-c', 'Release', '--no-build', '--no-restore', '-m:1', '--nologo')
    Check-Inventory 'default-publish-no-build' $defaultPublish 7
    $null = Invoke-DotNet 'default-published-process' @((Join-Path $defaultPublish 'CIS.TypeManager.Fixture.Host.dll'))

    $null = Invoke-DotNet 'package-copy-disabled' @('build', $hostProject, '-c', 'Release', '--no-restore', '-m:1', '-p:CopyLocalLockFileAssemblies=false', '--nologo') 'CISTM003: Type Manager requires'
    $results.Add(@{ name = 'package-copy-disabled'; passed = $true })

    foreach ($property in @('PublishSingleFile', 'PublishTrimmed', 'PublishAot', 'SelfContained', 'PublishSelfContained')) {
        $null = Invoke-DotNet ('unsupported-' + $property) @('msbuild', $hostProject, '-t:CisValidateTypeManagerProfile', "-p:$property=true", '-nologo') 'CISTM003'
        $results.Add(@{ name = 'unsupported-' + $property; passed = $true })
    }
    foreach ($rid in @('win-arm64', 'linux-arm64', 'osx-x64')) {
        $null = Invoke-DotNet ('unsupported-' + $rid) @('msbuild', $hostProject, '-t:CisValidateTypeManagerProfile', "-p:RuntimeIdentifier=$rid", '-nologo') 'CISTM004'
        $results.Add(@{ name = 'unsupported-' + $rid; passed = $true })
    }
    $null = Invoke-DotNet 'disabled-current-rid' @('msbuild', $hostProject, '-t:CisValidateTypeManagerProfile', '-p:UseCurrentRuntimeIdentifier=false', '-nologo') 'CISTM004'
    $results.Add(@{ name = 'disabled-current-rid'; passed = $true })
    $null = Invoke-DotNet 'library-host' @('msbuild', $hostProject, '-t:CisValidateTypeManagerProfile', '-p:OutputType=Library', '-nologo') 'CISTM002'
    $results.Add(@{ name = 'library-host'; passed = $true })
    Clear-Inventory (Join-Path $OutputRoot 'unsupported-publish')
    $null = Invoke-DotNet 'unsupported-no-build-publish' @('publish', $hostProject, '-c', 'Release', '--no-build', '--no-restore', '-p:PublishSingleFile=true', '-o', (Join-Path $OutputRoot 'unsupported-publish'), '--nologo') 'CISTM003'
    if (Test-Path (Join-Path $OutputRoot "unsupported-publish/$manifestName")) { throw 'Unsupported publish generated an inventory.' }
    $results.Add(@{ name = 'unsupported-no-build-publish'; passed = $true })

    Remove-Item -LiteralPath (Join-Path $hostDirectory $manifestName)
    $null = Invoke-DotNet 'design-time' @('msbuild', $hostProject, '-t:CisWriteBuildTypeInventory', '-p:Configuration=Release', '-p:DesignTimeBuild=true', '-nologo')
    if (Test-Path (Join-Path $hostDirectory $manifestName)) { throw 'Design-time build generated an inventory.' }
    $results.Add(@{ name = 'design-time'; passed = $true })
    $null = Invoke-DotNet 'after-design-time' @('build', $hostProject, '-c', 'Release', '--no-restore', '-m:1', '--nologo')
    Check-Inventory 'after-design-time' $hostDirectory 7

    $customOutput = Join-Path $OutputRoot 'custom-host-output'
    Clear-Inventory $customOutput
    $null = Invoke-DotNet 'custom-host-output' @('build', $hostProject, '-c', 'Release', '--no-restore', '-m:1', '-o', $customOutput, '--nologo')
    Check-Inventory 'custom-host-output' $customOutput 7
    if (Test-Path (Join-Path $customOutput 'CIS.TypeManager.Build.dll')) { throw 'Build helper leaked into host output.' }
    $dependencies = Get-Content -Raw -LiteralPath (Join-Path $customOutput 'CIS.TypeManager.Fixture.Host.deps.json')
    if ($dependencies -match 'CIS.TypeManager.Build|Microsoft.Build') { throw 'Build tooling leaked into runtime dependencies.' }
    $results.Add(@{ name = 'build-tool-runtime-isolation'; passed = $true })

    $renamedHost = 'CIS.TypeManager.RenamedHost'
    Clear-Inventory $hostDirectory "$renamedHost.types.json"
    $null = Invoke-DotNet 'renamed-host' @('build', $hostProject, '-c', 'Release', '--no-restore', '-m:1', "-p:TypeManagerFixtureHostName=$renamedHost", '--nologo')
    $renamedInventory = Get-Content -Raw -LiteralPath (Join-Path $hostDirectory "$renamedHost.types.json") | ConvertFrom-Json
    if ($renamedInventory.hostAssembly -ne [Reflection.AssemblyName]::GetAssemblyName((Join-Path $hostDirectory "$renamedHost.dll")).FullName) { throw 'Renamed host inventory identity is stale.' }
    $results.Add(@{ name = 'renamed-host'; passed = $true })

    # Re-resolve a removed project reference; stale deployed DLLs must not keep catalog membership.
    try {
        Clear-Inventory $hostDirectory
        $null = Invoke-DotNet 'remove-reference' @('build', $hostProject, '-c', 'Release', '-m:1', '-p:ExcludeUnusedFixture=true', '--nologo')
        Check-Inventory 'remove-reference' $hostDirectory 4
    }
    finally {
        Clear-Inventory $hostDirectory
        $null = Invoke-DotNet 'restore-reference' @('build', $hostProject, '-c', 'Release', '-m:1', '--nologo')
    }
    Check-Inventory 'restore-reference' $hostDirectory 7
    $existingAsset = Join-Path $languageRoot 'tests/fixtures/TypeManager/Contracts/bin/Release/net10.0/CIS.TypeManager.Fixture.Contracts.dll'
    Clear-Inventory $hostDirectory
    $null = Invoke-DotNet 'explicit-duplicate' @('build', $hostProject, '-c', 'Release', '--no-restore', '-m:1', "-p:ExtraDiscoveryAsset=$existingAsset", '--nologo')
    Check-Inventory 'explicit-duplicate' $hostDirectory 7

    $conflictingAsset = Join-Path $OutputRoot 'ConflictingIdentity.dll'
    Copy-Item -LiteralPath $existingAsset -Destination $conflictingAsset
    $null = Invoke-DotNet 'identity-conflict' @('build', $hostProject, '-c', 'Release', '--no-restore', '-m:1', "-p:ExtraDiscoveryAsset=$conflictingAsset", '--nologo') 'CISTM001: Conflicting assembly selection'
    $results.Add(@{ name = 'identity-conflict'; passed = $true })

    $invalidAsset = Join-Path $OutputRoot 'InvalidManagedAsset.dll'
    Set-Content -LiteralPath $invalidAsset -Value 'This explicitly selected assembly is invalid.'
    $null = Invoke-DotNet 'invalid-managed-asset' @('build', $hostProject, '-c', 'Release', '--no-restore', '-m:1', "-p:ExtraDiscoveryAsset=$invalidAsset", '--nologo') 'CISTM001: Selected managed asset has invalid executable metadata: InvalidManagedAsset.dll'
    $results.Add(@{ name = 'invalid-managed-asset'; passed = $true })

    $referenceAsset = Join-Path $languageRoot 'tests/fixtures/TypeManager/Contracts/obj/Release/net10.0/ref/CIS.TypeManager.Fixture.Contracts.dll'
    $referenceCopy = Join-Path $OutputRoot 'ReferenceOnly.dll'
    Copy-Item -LiteralPath $referenceAsset -Destination $referenceCopy
    $null = Invoke-DotNet 'reference-assembly' @('build', $hostProject, '-c', 'Release', '--no-restore', '-m:1', "-p:ExtraDiscoveryAsset=$referenceCopy", '--nologo') 'CISTM001: Selected runtime asset is a reference assembly:'
    $results.Add(@{ name = 'reference-assembly'; passed = $true })
    Clear-Inventory $hostDirectory
    $null = Invoke-DotNet 'final-build' @('build', $hostProject, '-c', 'Release', '--no-restore', '-m:1', '--nologo')
    Check-Inventory 'final-build' $hostDirectory 7
    $null = Invoke-DotNet 'clean' @('clean', $hostProject, '-c', 'Release', '-m:1', '--nologo')
    if (Test-Path (Join-Path $hostDirectory $manifestName)) { throw 'Clean left the generated build manifest behind.' }
    $results.Add(@{ name = 'clean'; passed = $true })
    $null = Invoke-DotNet 'rebuild' @('build', $hostProject, '-c', 'Release', '--no-restore', '-m:1', '--nologo')
    Check-Inventory 'rebuild' $hostDirectory 7
    $results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $OutputRoot 'results.json')
    Write-Output "Type Manager inventory: $($results.Count) checks passed. Evidence: $OutputRoot"
}
finally { Pop-Location }
