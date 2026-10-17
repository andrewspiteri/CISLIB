param(
    [string] $LanguageRoot = (Split-Path $PSScriptRoot -Parent),
    [string] $ProjectPath,
    [string] $AssetsPath,
    [string] $AssemblyPath,
    [string] $PackagePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$LanguageRoot = [IO.Path]::GetFullPath($LanguageRoot)
$projectPolicy = Get-Content -Raw -LiteralPath (Join-Path $LanguageRoot 'eng/project-boundaries.json') | ConvertFrom-Json -AsHashtable
$runtimePolicy = Get-Content -Raw -LiteralPath (Join-Path $LanguageRoot 'eng/runtime-dependencies.json') | ConvertFrom-Json -AsHashtable
$developmentPolicy = Get-Content -Raw -LiteralPath (Join-Path $LanguageRoot 'eng/development-dependencies.json') | ConvertFrom-Json -AsHashtable
$pinnedSdk = (Get-Content -Raw -LiteralPath (Join-Path $LanguageRoot 'global.json') | ConvertFrom-Json).sdk.version
if ($pinnedSdk -cne (Get-Content -Raw -LiteralPath (Join-Path $LanguageRoot 'eng/toolchain.json') | ConvertFrom-Json).sdk) { throw 'SDK pins disagree.' }
$runtimeAllowed = @{}
foreach ($package in $runtimePolicy.packages) { $runtimeAllowed[$package.id] = $package.version }
$developmentAllowed = @{}
$testDevelopmentAllowed = @{}
foreach ($package in $developmentPolicy.packages) {
    $developmentAllowed[$package.id] = $package.version
    if ($package.role -notlike '*-fixture') { $testDevelopmentAllowed[$package.id] = $package.version }
}

function Invoke-Evaluation {
    param([string] $Path, [switch] $ResolveReferences)
    $arguments = @('msbuild', $Path)
    if ($ResolveReferences) {
        $arguments += @('-t:ResolveReferences', '-p:BuildProjectReferences=false', '-getItem:ReferencePath')
    }
    else {
        $arguments += @('-getItem:ProjectReference,PackageReference,FrameworkReference,Reference', '-getProperty:TargetFramework,IsPackable,IsTestProject,AssemblyName,PackageId,RootNamespace,OutputType,MSBuildBinPath,MSBuildSDKsPath,NETCoreSdkBundledVersionsProps,NetCoreRoot,NETCoreSdkVersion')
    }
    $result = & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Project evaluation failed: $Path" }
    return ($result -join "`n") | ConvertFrom-Json -AsHashtable
}

function Check-RestoredGraph {
    param([hashtable] $Project, [string] $Path)
    if (-not (Test-Path -LiteralPath $Path)) { throw "Restore assets are missing: $Path" }
    $assets = Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json -AsHashtable
    $target = $assets.targets[$projectPolicy.targetFramework]
    if ($null -eq $target) { throw "Missing restored target for $($Project.path): $($projectPolicy.targetFramework)" }
    $resolved = @{}
    $resolvedProjects = @()
    $expectedRestoredProjects = if ($Project.ContainsKey('restoredProjectReferences')) { $Project.restoredProjectReferences } else { $Project.projectReferences }
    foreach ($identity in $target.Keys) {
        $entry = $target[$identity]
        $id, $version = $identity.Split('/', 2)
        if ($entry.type -eq 'project') {
            $projectAsset = [IO.Path]::GetFullPath((Join-Path (Split-Path $assets.project.restore.projectPath -Parent) $assets.libraries[$identity].msbuildProject))
            if (-not ($expectedRestoredProjects | Where-Object { [IO.Path]::GetFullPath((Join-Path $LanguageRoot $_)) -eq $projectAsset })) {
                throw "Unapproved restored project dependency for $($Project.path): $id"
            }
            $resolvedProjects += $projectAsset
            continue
        }
        if ($entry.type -ne 'package') { throw "Unexpected restored dependency type: $identity" }
        $allowed = $Project.role -in @('shipping', 'test', 'example', 'process-fixture') -and $runtimeAllowed.ContainsKey($id) -and $runtimeAllowed[$id] -eq $version
        if ($Project.role -eq 'test') { $allowed = $allowed -or ($testDevelopmentAllowed.ContainsKey($id) -and $testDevelopmentAllowed[$id] -eq $version) }
        if ($Project.role -eq 'process-fixture' -and $Project.ContainsKey('developmentPackageReferences') -and $id -in $Project.developmentPackageReferences) {
            $allowed = $allowed -or ($developmentAllowed.ContainsKey($id) -and $developmentAllowed[$id] -eq $version)
        }
        if (-not $allowed) { throw "Unapproved $($Project.role) package dependency: $identity" }
        $resolved[$id] = $version
    }
    foreach ($id in $Project.packageReferences) {
        if (-not $resolved.ContainsKey($id)) { throw "Required restored package is missing: $id" }
    }
    foreach ($reference in $expectedRestoredProjects) {
        if ([IO.Path]::GetFullPath((Join-Path $LanguageRoot $reference)) -notin $resolvedProjects) {
            throw "Required restored project is missing: $reference"
        }
    }
    if ($Project.role -eq 'shipping' -and $resolved.Count -ne $runtimeAllowed.Count) { throw 'Core runtime closure differs from the approved inventory.' }
}

$projects = @($projectPolicy.projects)
if ($ProjectPath) {
    # Focused fixture checks use one registered project. Normal verification always checks the whole manifest.
    $projects = @($projects | Where-Object path -eq $ProjectPath)
    if ($projects.Count -ne 1) { throw "Unknown project requested: $ProjectPath" }
}
else {
    $knownPaths = @($projects | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $LanguageRoot $_.path)) })
    $unknown = @(Get-ChildItem -LiteralPath $LanguageRoot -Recurse -Filter '*.csproj' -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj|\.vs)[\\/]' -and $_.FullName -notin $knownPaths })
    if ($unknown.Count -ne 0) { throw "Unregistered C# project: $($unknown[0].FullName)" }
}

Push-Location $LanguageRoot
try {
    foreach ($project in $projects) {
        $path = Join-Path $LanguageRoot $project.path
        $evaluation = Invoke-Evaluation -Path $path
        if ($evaluation.Properties.TargetFramework -ne $projectPolicy.targetFramework) { throw "Unexpected target framework: $($project.path)" }
        $expectedPackable = if ($project.role -eq 'shipping') { 'true' } else { 'false' }
        if ($evaluation.Properties.IsPackable -ne $expectedPackable) { throw "Unexpected packability: $($project.path)" }
        if ($project.role -eq 'shipping') {
            foreach ($identityProperty in @('AssemblyName', 'PackageId', 'RootNamespace')) {
                if ($evaluation.Properties[$identityProperty] -ne $projectPolicy.shippingIdentity[$identityProperty]) { throw "Unexpected shipping identity $identityProperty for $($project.path)" }
            }
        }
        if ($project.role -eq 'test' -and $evaluation.Properties.IsTestProject -ne 'true') { throw "Test project is not enabled: $($project.path)" }
        if ($project.role -eq 'process-fixture' -and ($evaluation.Properties.IsTestProject -ne 'false' -or $evaluation.Properties.OutputType -ne 'Exe')) { throw "Process fixture must be a non-test executable: $($project.path)" }
        $actualReferences = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($reference in $evaluation.Items.ProjectReference) {
            $referencePath = [IO.Path]::GetFullPath($reference.Identity, (Split-Path $path -Parent))
            $allowedReferences = @($project.projectReferences | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $LanguageRoot $_)) })
            if ($referencePath -notin $allowedReferences) { throw "Forbidden project reference from $($project.path): $($reference.Identity)" }
            $buildOnlyReferences = @()
            if ($project.ContainsKey('buildOnlyProjectReferences')) {
                $buildOnlyReferences = @($project.buildOnlyProjectReferences | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $LanguageRoot $_)) })
            }
            if ($referencePath -in $buildOnlyReferences -and ($reference.ReferenceOutputAssembly -ne 'false' -or $reference.Private -ne 'false' -or $reference.PrivateAssets -ne 'all')) {
                throw "Build-only project must not become a runtime reference: $($reference.Identity)"
            }
            if (-not $actualReferences.Add($referencePath)) { throw "Duplicate project reference: $referencePath" }
        }
        if (-not $actualReferences.SetEquals([string[]]@($project.projectReferences | ForEach-Object { [IO.Path]::GetFullPath((Join-Path $LanguageRoot $_)) }))) { throw "Project reference inventory mismatch: $($project.path)" }
        $approvedFrameworks = @($projectPolicy.implicitFramework)
        if ($project.role -eq 'example' -and $project.ContainsKey('additionalImplicitFrameworkReferences')) {
            $approvedFrameworks += @($project.additionalImplicitFrameworkReferences)
        }
        foreach ($reference in $evaluation.Items.FrameworkReference) {
            if ($reference.Identity -notin $approvedFrameworks -or $reference['IsImplicitlyDefined'] -ne 'true') {
                throw "Forbidden framework reference from $($project.path): $($reference.Identity)"
            }
        }
        foreach ($reference in $evaluation.Items.Reference) {
            if ($project.role -eq 'build-tool') {
                $bundled = $evaluation.Properties.NETCoreSdkBundledVersionsProps
                $sdkRoot = $evaluation.Properties.NetCoreRoot
                if (-not [IO.Path]::IsPathFullyQualified($bundled) -or -not [IO.Path]::IsPathFullyQualified($sdkRoot)) { throw 'Missing absolute selected SDK facts.' }
                $selectedSdk = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($bundled))
                $expectedSdk = [IO.Path]::GetFullPath((Join-Path $sdkRoot ('sdk/' + $pinnedSdk)))
                $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
                if ([IO.Path]::GetFileName($bundled) -cne 'Microsoft.NETCoreSdk.BundledVersions.props' -or -not $selectedSdk.Equals($expectedSdk, $comparison)) { throw 'SDK origin does not match its root and version.' }
                $expectedPath = Join-Path $expectedSdk ($reference.Identity + '.dll')
                if ($reference.Identity -notin $project.sdkAssemblyReferences -or $reference.Private -cne 'false' -or
                    $evaluation.Properties.NETCoreSdkVersion -ne $pinnedSdk -or
                    -not [IO.Path]::GetFullPath($reference.HintPath).Equals($expectedPath, $comparison) -or -not (Test-Path -LiteralPath $expectedPath)) {
                    throw "Unapproved pinned-SDK build-tool reference: $($reference.Identity)"
                }
                continue
            }
            $approvedReference = @($developmentPolicy.buildAssemblyReferences | Where-Object name -eq $reference.Identity)
            if ($project.role -ne 'test' -or $approvedReference.Count -ne 1) { throw "Manual assembly references are forbidden: $($project.path)" }
            $packageId = $approvedReference[0].packageId
            $packageVersion = $developmentAllowed[$packageId]
            $assets = Get-Content -Raw -LiteralPath (Join-Path (Split-Path $path -Parent) 'obj/project.assets.json') | ConvertFrom-Json -AsHashtable
            $approvedOrigin = $false
            foreach ($folder in $assets.packageFolders.Keys) {
                $packageRoot = [IO.Path]::GetFullPath((Join-Path $folder ($packageId.ToLowerInvariant() + '/' + $packageVersion))) + [IO.Path]::DirectorySeparatorChar
                if ([IO.Path]::GetFullPath($reference.HintPath).StartsWith($packageRoot, [StringComparison]::OrdinalIgnoreCase) -and
                    [IO.Path]::GetFullPath($reference.DefiningProjectFullPath).StartsWith($packageRoot, [StringComparison]::OrdinalIgnoreCase)) { $approvedOrigin = $true }
            }
            if (-not $approvedOrigin) { throw "Unapproved origin for test-tool assembly reference: $($reference.Identity)" }
        }
        if ($project.role -eq 'build-tool' -and $evaluation.Items.Reference.Count -ne $project.sdkAssemblyReferences.Count) {
            throw "Build-tool SDK reference inventory mismatch: $($project.path)"
        }
        $actualPackages = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($reference in $evaluation.Items.PackageReference) {
            if ($reference.Identity -notin $project.packageReferences) { throw "Unapproved explicit package from $($project.path): $($reference.Identity)" }
            if ($project.role -eq 'test' -and $reference.PrivateAssets -ne 'all') { throw "Test package must have PrivateAssets=all: $($reference.Identity)" }
            if ($project.role -eq 'process-fixture' -and $project.ContainsKey('developmentPackageReferences') -and $reference.Identity -in $project.developmentPackageReferences -and $reference['PrivateAssets'] -ne 'all') {
                throw "Fixture development package must have PrivateAssets=all: $($reference.Identity)"
            }
            if (-not $actualPackages.Add($reference.Identity)) { throw "Duplicate package reference: $($reference.Identity)" }
        }
        if (-not $actualPackages.SetEquals([string[]]$project.packageReferences)) { throw "Package reference inventory mismatch: $($project.path)" }
        $restoredPath = if ($AssetsPath -and $project.role -eq 'shipping') { $AssetsPath } else { Join-Path (Split-Path $path -Parent) 'obj/project.assets.json' }
        Check-RestoredGraph -Project $project -Path $restoredPath
    }

    if ($AssemblyPath -or $PackagePath) {
        $corePath = Join-Path $LanguageRoot $runtimePolicy.shipping_project
        $references = (Invoke-Evaluation -Path $corePath -ResolveReferences).Items.ReferencePath
        $allowedNames = @()
        foreach ($reference in $references) {
            $frameworkName = $reference['FrameworkReferenceName']
            if ($frameworkName -eq $projectPolicy.implicitFramework) {
                $allowedNames += [IO.Path]::GetFileNameWithoutExtension($reference.Identity)
            }
            elseif ($runtimeAllowed.ContainsKey($reference.NuGetPackageId) -and $runtimeAllowed[$reference.NuGetPackageId] -eq $reference.NuGetPackageVersion) {
                $allowedNames += [IO.Path]::GetFileNameWithoutExtension($reference.Identity)
            }
            else { throw "Unapproved resolved compiler reference: $($reference.Identity)" }
        }
        $reportDirectory = Join-Path (Split-Path (Split-Path $LanguageRoot -Parent) -Parent) '.artifacts/csharp/dependencies'
        New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
        $allowlistPath = Join-Path $reportDirectory ('reference-names-' + [Guid]::NewGuid().ToString('N') + '.json')
        @($allowedNames | Sort-Object -Unique) | ConvertTo-Json | Set-Content -LiteralPath $allowlistPath
        $checkerPath = Join-Path (Split-Path $PSScriptRoot -Parent) 'eng/CIS.DependencyChecker/bin/Release/net10.0/CIS.DependencyChecker.dll'
        if (-not (Test-Path -LiteralPath $checkerPath)) { throw 'Build the Release dependency checker before inspecting compiled references.' }
        if ($AssemblyPath) {
            & dotnet $checkerPath $AssemblyPath $allowlistPath
            if ($LASTEXITCODE -ne 0) { throw 'Compiled assembly boundary check failed.' }
        }
    }

    if ($PackagePath) {
        $archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
        try {
            $allowedEntries = @('lib/net10.0/CIS.Core.dll', 'lib/net10.0/CIS.Core.xml', 'README.md', 'CIS.Core.nuspec', '[Content_Types].xml', '_rels/.rels')
            $typeManagerAssets = @{
                'buildSupport/CIS.TypeManager.targets' = 'build/type-manager/CIS.TypeManager.targets'
                'Sdk/Sdk.props' = 'build/type-manager/sdk/Sdk.props'
                'Sdk/Sdk.targets' = 'build/type-manager/sdk/Sdk.targets'
                'Sdk/BeforeSdk.targets' = 'build/type-manager/sdk/BeforeSdk.targets'
                'buildTransitive/CIS.Core.targets' = 'build/type-manager/package/CIS.Core.targets'
            }
            $allowedEntries += @($typeManagerAssets.Keys) + @('Sdk/PackageVersion.props', 'tools/net10.0/CIS.TypeManager.Build.dll')
            $packedNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach ($entry in $archive.Entries) {
                if (-not $packedNames.Add($entry.FullName)) { throw "Duplicate packed asset: $($entry.FullName)" }
                if ($entry.FullName -cnotin $allowedEntries -and $entry.FullName -cnotmatch '^package/services/metadata/core-properties/[^/]+\.psmdcp$') {
                    throw "Unexpected packed asset: $($entry.FullName)"
                }
            }
            if (@($archive.Entries | Where-Object FullName -ceq 'lib/net10.0/CIS.Core.dll').Count -ne 1) { throw 'Core DLL target asset is missing.' }
            foreach ($required in @($typeManagerAssets.Keys) + @('Sdk/PackageVersion.props', 'tools/net10.0/CIS.TypeManager.Build.dll')) {
                if (@($archive.Entries | Where-Object FullName -ceq $required).Count -ne 1) { throw "Type Manager package asset is missing or duplicated: $required" }
            }
            foreach ($name in $typeManagerAssets.Keys) {
                $input = $archive.GetEntry($name).Open()
                try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($input)) } finally { $input.Dispose() }
                if ($hash -ne (Get-FileHash -LiteralPath (Join-Path $LanguageRoot $typeManagerAssets[$name])).Hash) {
                    throw "Packed Type Manager build asset differs from source: $name"
                }
            }
            $toolPath = (& dotnet msbuild eng/CIS.TypeManager.Build/CIS.TypeManager.Build.csproj -p:Configuration=Release -getProperty:TargetPath -nologo).Trim()
            if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $toolPath)) { throw 'Build the Release Type Manager helper before inspecting its package.' }
            $input = $archive.GetEntry('tools/net10.0/CIS.TypeManager.Build.dll').Open()
            try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($input)) } finally { $input.Dispose() }
            if ($hash -ne (Get-FileHash -LiteralPath $toolPath).Hash) { throw 'Packed Type Manager helper differs from the checked Release tool.' }
            $nuspecEntry = $archive.GetEntry('CIS.Core.nuspec')
            if ($null -eq $nuspecEntry) { throw 'Core package manifest is missing.' }
            $reader = [IO.StreamReader]::new($nuspecEntry.Open())
            try { [xml] $nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
            $reader = [IO.StreamReader]::new($archive.GetEntry('Sdk/PackageVersion.props').Open())
            try { [xml] $versionProps = $reader.ReadToEnd() } finally { $reader.Dispose() }
            if ($versionProps.Project.PropertyGroup._CisCoreSdkPackageVersion -ne $nuspec.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='version']").InnerText) {
                throw 'Type Manager SDK and runtime package versions differ.'
            }
            $groups = @($nuspec.SelectNodes("//*[local-name()='dependencies']/*[local-name()='group']"))
            if ($groups.Count -ne 1 -or $groups[0].targetFramework -ne 'net10.0') { throw 'Unexpected package dependency groups.' }
            $directPackages = @($runtimePolicy.packages | Where-Object role -eq 'direct')
            $dependencies = @($groups[0].SelectNodes("*[local-name()='dependency']"))
            $dependencyIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach ($dependency in $dependencies) {
                if (-not $dependencyIds.Add($dependency.id)) { throw "Duplicate packed dependency: $($dependency.id)" }
            }
            if ($dependencies.Count -ne $directPackages.Count) { throw 'Packed direct dependency inventory mismatch.' }
            foreach ($dependency in $dependencies) {
                $expected = @($directPackages | Where-Object id -eq $dependency.id)
                if ($expected.Count -ne 1 -or $dependency.version -ne $expected[0].version) { throw "Unapproved packed dependency: $($dependency.id)/$($dependency.version)" }
            }
            if (-not $dependencyIds.SetEquals([string[]]@($directPackages | ForEach-Object id))) { throw 'Packed direct dependency set mismatch.' }
            $packedPath = Join-Path $reportDirectory ('CIS.Core-' + [Guid]::NewGuid().ToString('N') + '.dll')
            $input = $archive.GetEntry('lib/net10.0/CIS.Core.dll').Open()
            $output = [IO.File]::Create($packedPath)
            try { $input.CopyTo($output) } finally { $input.Dispose(); $output.Dispose() }
            & dotnet $checkerPath $packedPath $allowlistPath
            if ($LASTEXITCODE -ne 0) { throw 'Packed compiled assembly boundary check failed.' }
            if ($AssemblyPath -and (Get-FileHash -LiteralPath $AssemblyPath).Hash -ne (Get-FileHash -LiteralPath $packedPath).Hash) { throw 'Packed Core DLL differs from the checked Release assembly.' }
        }
        finally { $archive.Dispose() }
    }
    Write-Output "Dependency boundaries passed for $($projects.Count) evaluated projects; Core runtime inventory: $($runtimeAllowed.Count) approved packages."
}
finally { Pop-Location }
