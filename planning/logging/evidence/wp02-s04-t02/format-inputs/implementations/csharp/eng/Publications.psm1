Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-UniqueJsonProperties {
    param([System.Text.Json.JsonElement] $Element)
    if ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
        $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($property in $Element.EnumerateObject()) {
            if (-not $names.Add($property.Name)) { throw 'DuplicateJsonProperty: duplicate object member.' }
            Assert-UniqueJsonProperties $property.Value
        }
    }
    elseif ($Element.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
        foreach ($item in $Element.EnumerateArray()) { Assert-UniqueJsonProperties $item }
    }
}

function Read-PublicationJson {
    param([string] $Path, [string] $SchemaPath, [string] $IssueCode)
    try {
        $text = [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($Path))
        $document = [System.Text.Json.JsonDocument]::Parse($text)
    }
    catch { throw 'InvalidJson: strict UTF-8 JSON is required.' }
    try {
        Assert-UniqueJsonProperties $document.RootElement
        if (-not (Test-Json -Json $text -SchemaFile $SchemaPath -ErrorAction SilentlyContinue)) { throw "$IssueCode`: document does not match its schema." }
        return @{ data = ($text | ConvertFrom-Json -AsHashtable -DateKind String); element = $document.RootElement.Clone() }
    }
    finally { $document.Dispose() }
}

function Assert-PublicationArtifact {
    param([string] $Root, [hashtable] $Reference)
    $fullRoot = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetFullPath($Root))
    $path = [IO.Path]::GetFullPath((Join-Path $fullRoot $Reference.path))
    if (-not $path.StartsWith($fullRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'ArtifactPathEscape: artifact escapes the retained root.' }
    $item = Get-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
    if ($null -eq $item -or $item.PSIsContainer) { throw "MissingArtifact: $($Reference.path)" }
    $ancestor = $item
    while ($null -ne $ancestor) {
        if (($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'ArtifactPathEscape: linked artifacts are not immutable retained files.' }
        if ($ancestor.FullName -eq $fullRoot) { break }
        $ancestor = if ($ancestor -is [IO.FileInfo]) { $ancestor.Directory } else { $ancestor.Parent }
    }
    if ($item.Length -ne $Reference.bytes) { throw "ArtifactLengthMismatch: $($Reference.path)" }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $Reference.sha256) { throw "ArtifactHashMismatch: $($Reference.path)" }
    return $path
}

function Assert-PublicationArtifacts {
    param([string] $Root, [object] $Node)
    if ($Node -is [Collections.IDictionary]) {
        if ($Node.Contains('path') -and $Node.Contains('sha256') -and $Node.Contains('bytes') -and $Node.Contains('mediaType')) { $null = Assert-PublicationArtifact $Root $Node; return }
        foreach ($value in $Node.Values) { Assert-PublicationArtifacts $Root $value }
    }
    elseif ($Node -is [array]) {
        foreach ($value in $Node) { Assert-PublicationArtifacts $Root $value }
    }
}

function Assert-PublicationAppendOnly {
    param([hashtable] $Current, [hashtable] $Previous)
    foreach ($name in @('schemaVersion', 'registryId', 'implementation')) {
        if ($Current.data[$name] -cne $Previous.data[$name]) { throw 'HistoryRewrite: registry identity changed.' }
    }
    foreach ($name in @('identities', 'publications')) {
        $old = $Previous.element.GetProperty($name)
        $new = $Current.element.GetProperty($name)
        if ($new.GetArrayLength() -lt $old.GetArrayLength()) { throw 'HistoryRemoval: historical membership was removed.' }
        for ($index = 0; $index -lt $old.GetArrayLength(); $index++) {
            if (-not [System.Text.Json.JsonElement]::DeepEquals($old[$index], $new[$index])) { throw 'HistoryRewrite: historical entries changed or were reordered.' }
        }
    }
}

function Assert-PublicationVersion {
    param([string] $Version)
    # Schema fixes canonical three-part package versions; numeric prerelease components cannot have leading zeroes.
    if ($Version.Contains('-')) {
        $suffix = $Version.Split('-', 2)[1]
        foreach ($part in $suffix.Split('.')) {
            if ($part -match '^\d+$' -and $part.Length -gt 1 -and $part[0] -eq '0') { throw 'InvalidPackageVersion: numeric prerelease labels must be canonical.' }
        }
    }
}

function Assert-PublicationRecordLinks {
    param([hashtable] $Record, [hashtable] $Identity)
    $targets = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($target in $Record.targets) {
        $key = "$($target.targetFramework)|$($target.runtimeIdentifier)|$($target.assetPath)"
        if (-not $targets.Add($key)) { throw 'DuplicateTarget: baseline target asset is duplicated.' }
        if ($target.assemblyIdentity.name -cne $Identity.assemblyName) { throw 'RecordIdentityMismatch: assembly identity differs from package membership.' }
    }
    $sourceIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($fixture in $Record.sourceFixtures) {
        if (-not $sourceIds.Add($fixture.fixtureId)) { throw 'DuplicateFixture: source fixture ID is duplicated.' }
        if ($fixture.contractTargetFramework -cnotin @($Record.targets.targetFramework)) { throw 'UnknownFixtureTarget: source fixture target has no retained asset.' }
    }
    $binaryIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($fixture in $Record.binaryFixtures) {
        if (-not $binaryIds.Add($fixture.fixtureId)) { throw 'DuplicateFixture: binary fixture ID is duplicated.' }
        if (-not $sourceIds.Contains($fixture.sourceFixtureId)) { throw 'UnknownSourceFixture: binary fixture has no preserved source origin.' }
    }
    $executionIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($execution in $Record.executions) {
        if (-not $executionIds.Add($execution.executionId)) { throw 'DuplicateExecution: execution ID is duplicated.' }
        if ($execution.targetFramework -cnotin @($Record.targets.targetFramework)) { throw 'UnknownFixtureTarget: execution target has no retained asset.' }
        foreach ($id in $execution.consumerFixtureIds) {
            if (-not $binaryIds.Contains($id)) { throw 'UnknownBinaryFixture: execution references an absent binary consumer.' }
        }
    }
    foreach ($fixture in $Record.binaryFixtures) {
        $original = @($Record.executions | Where-Object { $_.executionId -ceq $fixture.originalExecutionId })
        if ($original.Count -ne 1 -or $fixture.fixtureId -cnotin @($original[0].consumerFixtureIds)) { throw 'UnknownOriginalExecution: binary origin execution does not include its consumer.' }
    }
    $contracts = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($contract in $Record.contracts) {
        if (-not $contracts.Add("$($contract.contractId)|$($contract.contractVersion)")) { throw 'DuplicateContract: contract membership is duplicated.' }
    }
}

function Get-PublicationValidation {
    param([string] $LedgerPath, [string] $ArtifactRoot, [string] $SchemaRoot, [string] $PreviousLedgerPath, [string] $PreviousLedgerSha256)
    $current = Read-PublicationJson $LedgerPath (Join-Path $SchemaRoot 'ledger.schema.json') 'LedgerSchemaInvalid'
    if ([bool]$PreviousLedgerPath -ne [bool]$PreviousLedgerSha256) { throw 'MissingHistoryAnchor: previous ledger path and externally trusted SHA-256 must be supplied together.' }
    if ($PreviousLedgerPath) {
        if ($PreviousLedgerSha256 -cnotmatch '^[a-f0-9]{64}$' -or (Get-FileHash -LiteralPath $PreviousLedgerPath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $PreviousLedgerSha256) { throw 'HistoryAnchorMismatch: previous ledger bytes differ from the trusted anchor.' }
        $previous = Read-PublicationJson $PreviousLedgerPath (Join-Path $SchemaRoot 'ledger.schema.json') 'LedgerSchemaInvalid'
        Assert-PublicationAppendOnly $current $previous
    }
    $identities = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    $packages = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($identity in $current.data.identities) {
        if ($identities.ContainsKey($identity.identityId) -or -not $packages.Add($identity.packageId)) { throw 'DuplicateIdentity: package/contract membership must be unique.' }
        $identities.Add($identity.identityId, $identity)
    }
    $keys = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $position = 0
    foreach ($publication in $current.data.publications) {
        $position++
        if ($publication.sequence -ne $position) { throw 'InvalidSequence: publication order must be contiguous from one.' }
        Assert-PublicationVersion $publication.packageVersion
        if (-not $identities.ContainsKey($publication.identityId)) { throw 'UnknownIdentity: publication is absent from retained package membership.' }
        if (-not $keys.Add("$($publication.identityId)|$($publication.packageVersion)")) { throw 'DuplicatePublication: normalized package version was already published.' }
        $recordPath = Assert-PublicationArtifact $ArtifactRoot $publication.record
        $record = (Read-PublicationJson $recordPath (Join-Path $SchemaRoot 'baseline.schema.json') 'BaselineSchemaInvalid').data
        foreach ($name in @('sequence', 'identityId', 'packageVersion')) {
            if ($record[$name] -cne $publication[$name]) { throw 'RecordIdentityMismatch: ledger and immutable publication record disagree.' }
        }
        $identity = $identities[$publication.identityId]
        if ($record.packageId -cne $identity.packageId) { throw 'RecordIdentityMismatch: record package differs from immutable membership.' }
        $instant = [DateTimeOffset]::MinValue
        if (-not [DateTimeOffset]::TryParseExact($record.exposedAtUtc, "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$instant)) { throw 'InvalidPublicationTime: impossible UTC exposure instant.' }
        Assert-PublicationRecordLinks $record $identity
        Assert-PublicationArtifacts $ArtifactRoot $record
    }
    return [ordered]@{ schema = 'cis.publication-validation/v1'; exitCode = 0; identityCount = $identities.Count; publicationCount = $position; appendOnlyStatus = $(if ($PreviousLedgerPath) { 'ComparedWithTrustedPrevious' } else { 'NotCompared' }); historyStatus = $(if ($position -eq 0) { 'NoPublicationsRecorded' } else { 'RecordedPublicationsValidated' }); firstReleaseHarnessRequired = $true; actualPublicationAttested = $false; compatibilityChecksExecuted = 0; compatibilityQualified = $false; issues = @() }
}

Export-ModuleMember -Function Get-PublicationValidation
