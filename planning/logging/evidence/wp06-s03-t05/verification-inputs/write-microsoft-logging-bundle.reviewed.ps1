param(
    [Parameter(Mandatory)][string] $PackagePath,
    [Parameter(Mandatory)][string] $OutputRoot,
    [string] $RegistrationPath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'verification/bundle/manifest.json')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$languageRoot = Split-Path $PSScriptRoot -Parent
$repositoryRoot = Split-Path (Split-Path $languageRoot -Parent) -Parent
$PackagePath = [IO.Path]::GetFullPath($PackagePath)
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $OutputRoot) { throw 'Use a new manifest output directory.' }
$registration = Get-Content -Raw -LiteralPath $RegistrationPath | ConvertFrom-Json
if ($registration.schema -cne 'cis.csharp.verification-bundle-registration/v1' -or $registration.module.id -cne 'logging-microsoft') { throw 'Unsupported bundle registration.' }
$requiredAreas = @('Product','Source','Tests','Contracts','History','Examples','Tooling','Evidence','Integrity')
if (@(Compare-Object $requiredAreas @($registration.requiredReleaseAreas)).Count -ne 0) { throw 'Bundle must preserve all nine release content areas.' }
$files = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
function Add-File([string] $Path, [string] $Role) {
    $absolute = [IO.Path]::GetFullPath($Path)
    if (-not $absolute.StartsWith($repositoryRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Inventory input is outside the repository.' }
    if (-not (Test-Path -LiteralPath $absolute -PathType Leaf)) { throw "Missing required inventory input: $absolute" }
    $relative = [IO.Path]::GetRelativePath($repositoryRoot, $absolute).Replace('\','/')
    if (-not $files.ContainsKey($relative)) {
        $files.Add($relative, @{ path = $relative; bytes = (Get-Item -LiteralPath $absolute).Length; sha256 = (Get-FileHash -LiteralPath $absolute -Algorithm SHA256).Hash.ToLowerInvariant(); role = $Role })
    }
}
Add-File $RegistrationPath 'BundleRegistration'
Add-File $PSCommandPath 'ManifestProducer'
foreach ($relative in @($registration.module.documentation) + @($registration.module.requiredFiles)) { Add-File (Join-Path $repositoryRoot $relative) 'ModuleDocumentationSourceOrInventory' }
foreach ($sourceRoot in $registration.module.sourceRoots) {
    $relative = $sourceRoot.path
    $root = Join-Path $repositoryRoot $relative
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "Missing module source root: $relative" }
    $members = @(& git -C $repositoryRoot ls-files --cached --others --exclude-standard -- $relative)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate maintained validation sources.' }
    $members = @($members | Where-Object { Test-Path -LiteralPath (Join-Path $repositoryRoot $_) -PathType Leaf })
    if ($members.Count -eq 0) { throw "Empty module source root: $relative" }
    foreach ($member in $members) { Add-File (Join-Path $repositoryRoot $member) $sourceRoot.role }
}
$reportGroups = foreach ($relative in $registration.module.priorEvidenceRoots) {
    $root = Join-Path $repositoryRoot $relative
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "Missing required provider evidence root: $relative" }
    $members = @(Get-ChildItem -LiteralPath $root -Recurse -File)
    if ($members.Count -eq 0) { throw "Empty provider evidence root: $relative" }
    foreach ($file in $members) { Add-File $file.FullName 'PriorDevelopmentEvidenceOnly' }
    @{ root = $relative; files = $members.Count; candidateQualification = 'Not credited to selected candidate; preserve original reports and native gate status.' }
}
$mutationAuditPath = Join-Path $repositoryRoot $registration.module.nativeMutation.audit
Add-File $mutationAuditPath 'PriorDevelopmentEvidenceOnly'
$mutationAudit = Get-Content -Raw -LiteralPath $mutationAuditPath | ConvertFrom-Json
$expectedMutationHash = $mutationAudit.($registration.module.nativeMutation.run).nativeReportSha256
$nativeJson = Join-Path $repositoryRoot $registration.module.nativeMutation.json
if ((Get-FileHash -LiteralPath $nativeJson -Algorithm SHA256).Hash.ToLowerInvariant() -cne $expectedMutationHash) { throw 'Native mutation JSON differs from tracked run hash.' }
foreach ($kind in @('json','html')) { Add-File (Join-Path $repositoryRoot $registration.module.nativeMutation.$kind) 'LocalNativeMutationReportNotReleaseRetained' }
$developerRoot = Join-Path $repositoryRoot '.artifacts/csharp/verification/Developer'
$summary = Get-Content -Raw -LiteralPath (Join-Path $developerRoot 'summary.json') | ConvertFrom-Json
if ($summary.exitCode -ne 0) { throw 'Require a terminal passing Developer observation for this inventory checkpoint.' }
foreach ($name in @('summary.json','source-inputs.json','validation.json')) { Add-File (Join-Path $developerRoot $name) 'CurrentDeveloperProfileOnly' }
foreach ($family in $summary.families) {
    foreach ($slot in @('discovery','execution','report')) {
        if ($family.$slot) { Add-File (Join-Path $developerRoot $family.$slot.path) 'CurrentDeveloperProfileOnly' }
    }
}
$archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
try {
    $duplicates = @($archive.Entries.FullName | Group-Object | Where-Object Count -gt 1)
    if ($duplicates.Count -ne 0) { throw 'Ambiguous duplicate package entries.' }
    function Read-Entry([string] $Name) {
        $entry = $archive.GetEntry($Name)
        if ($null -eq $entry) { throw "Required package entry missing: $Name" }
        $stream = $entry.Open(); $memory = [IO.MemoryStream]::new()
        try { $stream.CopyTo($memory); return ,$memory.ToArray() } finally { $stream.Dispose(); $memory.Dispose() }
    }
    function Read-PackageXml([byte[]] $Bytes) {
        $document = [Xml.XmlDocument]::new(); $stream = [IO.MemoryStream]::new($Bytes, $false)
        try { $document.Load($stream); return $document } finally { $stream.Dispose() }
    }
    $nuspec = Read-PackageXml (Read-Entry 'CIS.Core.nuspec')
    $id = $nuspec.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='id']").InnerText
    if ($id -cne 'CIS.Core') { throw 'Selected package identity must be CIS.Core.' }
    $repository = $nuspec.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='repository']")
    $packageSourceCommit = if ($repository) { $repository.GetAttribute('commit') } else { $null }
    $version = $nuspec.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='version']").InnerText
    $core = Read-Entry 'lib/net10.0/CIS.Core.dll'
    $xmlBytes = Read-Entry 'lib/net10.0/CIS.Core.xml'
    $api = Read-PackageXml $xmlBytes
    foreach ($member in $registration.module.apiMembers) { if ($member -cnotin @($api.doc.members.member.name)) { throw "Provider API documentation missing: $member" } }
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($core)).ToLowerInvariant()
    $built = Join-Path $languageRoot 'src/CIS.Core/bin/Release/net10.0/CIS.Core.dll'
    $buildHash = (Get-FileHash -LiteralPath $built -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -cne $buildHash) { throw 'Selected package Core differs from current Release build output.' }
    $comparedBuildOutput = @{ path = [IO.Path]::GetRelativePath($repositoryRoot, $built).Replace('\','/'); sha256 = $buildHash; qualification = 'Current file comparison only; not cryptographically tied to Developer run.' }
    New-Item -ItemType Directory -Path (Join-Path $OutputRoot 'products') -Force | Out-Null
    $retainedPackage = Join-Path $OutputRoot ('products/' + [IO.Path]::GetFileName($PackagePath))
    Copy-Item -LiteralPath $PackagePath -Destination $retainedPackage
    [IO.File]::WriteAllBytes((Join-Path $OutputRoot 'products/CIS.Core.dll'), $core)
    [IO.File]::WriteAllBytes((Join-Path $OutputRoot 'products/CIS.Core.xml'), $xmlBytes)
    $product = @{ comparedBuildOutput = $comparedBuildOutput; packageSourceCommit = $packageSourceCommit; packageId = $id; packageVersion = $version; sourcePackage = $PackagePath; retainedPackage = 'products/' + [IO.Path]::GetFileName($PackagePath); packageSha256 = (Get-FileHash -LiteralPath $retainedPackage -Algorithm SHA256).Hash.ToLowerInvariant(); assembly = 'products/CIS.Core.dll'; assemblySha256 = $hash; apiDocumentation = 'products/CIS.Core.xml'; apiDocumentationSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($xmlBytes)).ToLowerInvariant(); providerApiMembers = @($registration.module.apiMembers) }
}
finally { $archive.Dispose() }
Push-Location $repositoryRoot
try {
    $head = (& git rev-parse HEAD).Trim(); if ($LASTEXITCODE -ne 0) { throw 'Cannot record source commit.' }
    $tree = (& git rev-parse 'HEAD^{tree}').Trim(); if ($LASTEXITCODE -ne 0) { throw 'Cannot record source tree.' }
}
finally { Pop-Location }
$manifest = @{ schema = 'cis.csharp.verification-bundle-draft/v1'; state = 'IncompleteReleaseDraft'; releaseQualified = $false; module = $registration.module.id; product = $product; source = @{ headCommit = $head; headTree = $tree; qualification = 'Head identity plus individually hashed working inputs; not an exact complete release source snapshot.' }; developer = @{ runId = $summary.runId; sourceCommit = $summary.source.commit; profile = $summary.profile; lane = $summary.lane; scope = 'Developer profile only; full candidate-package suites remain required.' }; reportGroups = @($reportGroups); files = @($files.Values | Sort-Object path); pendingReleaseRequirements = @($registration.pendingReleaseRequirements); releaseAreas = @($requiredAreas | ForEach-Object { @{ area = $_; fullReleaseState = 'Pending' } }); retention = 'File entries reference existing inputs; this command does not materialize a complete bundle. Local-only native reports, all final candidate evidence and durable prerequisites must be retained before release.' }
$path = Join-Path $OutputRoot 'manifest.json'
$manifest | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $path -Encoding utf8
(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() | Set-Content -LiteralPath (Join-Path $OutputRoot 'manifest.sha256') -Encoding utf8
Write-Output "Provider bundle draft indexed $($files.Count) files. Release qualification remains pending. Manifest: $path"
