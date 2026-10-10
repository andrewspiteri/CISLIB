param(
    [Parameter(Mandatory)][string] $RunRoot,
    [Parameter(Mandatory)][string] $LanguageRoot,
    [Parameter(Mandatory)][string] $Cache
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$policy = Get-Content -Raw -LiteralPath (Join-Path $LanguageRoot 'eng/toolchain.json') | ConvertFrom-Json
$runtime = Get-Content -Raw -LiteralPath (Join-Path $LanguageRoot 'eng/runtime-dependencies.json') | ConvertFrom-Json
$development = Get-Content -Raw -LiteralPath (Join-Path $LanguageRoot 'eng/development-dependencies.json') | ConvertFrom-Json
$approved = @($runtime.packages) + @($development.packages) + @($policy.tools)
$restored = @(Get-ChildItem -LiteralPath $Cache -Directory | ForEach-Object { $id = $_.Name; Get-ChildItem -LiteralPath $_.FullName -Directory | ForEach-Object { "$id/$($_.Name)" } })
$expected = @($approved | ForEach-Object { "$($_.id.ToLowerInvariant())/$($_.version)" })
if (@(Compare-Object $expected $restored).Count -ne 0) { throw 'Fresh cache differs from the approved package/tool inventory.' }

$inventory = foreach ($package in $approved) {
    $directory = Join-Path $Cache ($package.id.ToLowerInvariant() + '/' + $package.version)
    $archive = Join-Path $directory ($package.id.ToLowerInvariant() + '.' + $package.version + '.nupkg')
    [xml] $nuspec = Get-Content -Raw -LiteralPath (Join-Path $directory ($package.id.ToLowerInvariant() + '.nuspec'))
    $metadata = $nuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
    $license = $metadata.SelectSingleNode("*[local-name()='license']")
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    $retainedDirectory = Join-Path $RunRoot "retained/sha256/$hash"
    New-Item -ItemType Directory -Path $retainedDirectory -Force | Out-Null
    Copy-Item -LiteralPath $archive -Destination $retainedDirectory
    $entry = [ordered]@{ id = $package.id; version = $package.version; authors = $metadata.SelectSingleNode("*[local-name()='authors']").InnerText; sha256 = $hash; retainedArchive = "retained/sha256/$hash/$(Split-Path $archive -Leaf)"; source = $package.provenance; licenseType = $license.type; license = $license.InnerText }
    if ($license.type -eq 'file') {
        $licensePath = Join-Path $directory $license.InnerText
        Copy-Item -LiteralPath $licensePath -Destination $retainedDirectory
        $entry.licenseSha256 = (Get-FileHash -LiteralPath $licensePath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $tool = @($policy.tools | Where-Object { $_.id -eq $package.id })
    if ($tool.Count -eq 1) {
        $entry.scope = 'development-only CLI; embedded dependencies are never Core runtime dependencies'
        $depsPath = Join-Path $directory ('tools/net8.0/any/' + [IO.Path]::GetFileNameWithoutExtension($tool[0].entryPoint) + '.deps.json')
        $deps = Get-Content -Raw -LiteralPath $depsPath | ConvertFrom-Json -AsHashtable
        $entry.embeddedDependencyManifestSha256 = (Get-FileHash -LiteralPath $depsPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $entry.embeddedDependencies = @($deps.libraries.Keys | Sort-Object | ForEach-Object { [ordered]@{ identity = $_; type = $deps.libraries[$_].type } })
        $entry.licenseReview = 'Top-level NuGet licence recorded; complete embedded third-party notices review remains release retention work in WP07-S06.'
        Copy-Item -LiteralPath $depsPath -Destination $retainedDirectory
    }
    $entry
}

$prerequisites = [ordered]@{
    sdk = $policy.sdk
    targetRuntime = $policy.targetRuntime
    toolHostRuntime = $policy.toolHostRuntime
    powershellVersion = $PSVersionTable.PSVersion.ToString()
    gitVersion = (& git --version)
    architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    operatingSystem = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
    network = 'HTTPS access to api.nuget.org for fresh replay; exact retained NuGet archives are included locally.'
    sdkRuntimeRetention = 'Installed SDK/runtime versions are qualified; installer/distribution bytes are not retained by this smoke command. Immutable offline materialization remains WP07-S06.'
    retention = 'Retain the entire run directory, including source snapshot, native reports/logs, commands.json, fresh caches and content-addressed archives. This is local evidence, not a published verification kit.'
    packages = @($inventory)
}
$prerequisites | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $RunRoot 'prerequisites.json') -Encoding utf8
