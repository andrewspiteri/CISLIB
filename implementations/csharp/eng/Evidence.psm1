Set-StrictMode -Version Latest

function Write-VerificationJson {
    param([string] $Path, [object] $Value)
    New-Item -ItemType Directory -Path (Split-Path $Path -Parent) -Force | Out-Null
    ConvertTo-Json -InputObject $Value -Depth 30 | Set-Content -LiteralPath $Path -Encoding utf8
}

function Read-NativeTrx {
    param([string] $Path)
    [xml] $xml = Get-Content -Raw -LiteralPath $Path
    if ($xml.DocumentElement.LocalName -ne 'TestRun' -or $xml.DocumentElement.NamespaceURI -ne 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010') {
        throw 'UnrecognizedReportSchema: expected VSTest TRX 2010.'
    }
    $definitions = @{}
    foreach ($definition in $xml.SelectNodes("//*[local-name()='TestDefinitions']/*[local-name()='UnitTest']")) {
        $method = $definition.SelectSingleNode("*[local-name()='TestMethod']")
        $definitions[$definition.id] = "$($method.className).$($method.name)"
    }
    $results = @($xml.SelectNodes("//*[local-name()='Results']/*[local-name()='UnitTestResult']") | ForEach-Object {
        if (-not $definitions.ContainsKey($_.testId)) { throw 'UnrecognizedReportSchema: result has no test definition.' }
        if ($_.outcome -notin @('Passed', 'Failed', 'NotExecuted')) { throw "UnrecognizedReportSchema: unsupported outcome $($_.outcome)." }
        [ordered]@{ testId = $_.testId; method = $definitions[$_.testId]; name = $_.testName; outcome = $_.outcome }
    })
    $counters = $xml.SelectSingleNode("//*[local-name()='ResultSummary']/*[local-name()='Counters']")
    if ($null -eq $counters -or [int] $counters.total -ne $results.Count -or @($results.testId | Sort-Object -Unique).Count -ne $results.Count) {
        throw 'UnrecognizedReportSchema: inconsistent or duplicated native results.'
    }
    $passed = @($results | Where-Object { $_.outcome -eq 'Passed' }).Count
    $failed = @($results | Where-Object { $_.outcome -eq 'Failed' }).Count
    $skipped = @($results | Where-Object { $_.outcome -eq 'NotExecuted' }).Count
    if ([int] $counters.passed -ne $passed -or [int] $counters.failed -ne $failed -or [int] $counters.notExecuted -ne $skipped) {
        throw 'UnrecognizedReportSchema: native counters disagree with outcomes.'
    }
    return [ordered]@{ total = $results.Count; passed = $passed; failed = $failed; skipped = $skipped; results = $results }
}

function Resolve-EvidenceFile {
    param([string] $Root, [hashtable] $Reference)
    $fullRoot = [IO.Path]::GetFullPath($Root)
    $path = [IO.Path]::GetFullPath((Join-Path $fullRoot $Reference.path))
    if (-not $path.StartsWith($fullRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'EvidencePathOutsideRoot: report is outside its evidence directory.' }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "MissingReport: $($Reference.path)" }
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $Reference.sha256) { throw "ReportHashMismatch: $($Reference.path)" }
    return $path
}

function New-EvidenceReference {
    param([string] $Root, [string] $Path, [string] $Schema)
    return [ordered]@{ path = [IO.Path]::GetRelativePath($Root, $Path).Replace('\', '/'); sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant(); schema = $Schema }
}

function Get-VerificationIssues {
    param([string] $EvidenceRoot, [hashtable] $Manifest, [hashtable] $Inventory, [string] $Profile, [string] $Lane)
    $issues = [System.Collections.Generic.List[object]]::new()
    function Add-Issue([string] $Code, [string] $Detail) { $issues.Add([ordered]@{ code = $Code; detail = $Detail }) }
    try {
        $summary = Get-Content -Raw -LiteralPath (Join-Path $EvidenceRoot 'summary.json') | ConvertFrom-Json -AsHashtable
        if ($summary.schema -ne 'cis.verification/v1' -or $Manifest.schemaVersion -ne 1 -or $Inventory.schemaVersion -ne 1) { throw 'UnrecognizedReportSchema: summary or inventory version.' }
        $known = @('unit', 'integration', 'mutation', 'architecture', 'compatibility', 'business')
        if (@(Compare-Object $known @($Manifest.families.id)).Count -gt 0 -or $Manifest.families.Count -ne 6) { throw 'InvalidFamilyManifest: exactly six unique families are required.' }
        if ($Lane -notin @($Manifest.lanes.id)) { throw "UnsupportedLane: $Lane" }
        if ($summary.lane -ne $Lane) { Add-Issue 'LaneMismatch' "Evidence lane $($summary.lane); requested $Lane." }
        if ($summary.profile -ne $Profile) { Add-Issue 'ProfileMismatch' "Evidence profile $($summary.profile); requested $Profile." }
        foreach ($stage in $summary.stages) {
            if ($null -ne $stage.exitCode -and $stage.exitCode -ne 0) { Add-Issue 'FailedStage' $stage.id }
        }
        foreach ($family in $Manifest.families) {
            $entries = @($summary.families | Where-Object { $_.id -eq $family.id })
            if ($entries.Count -ne 1) { Add-Issue 'MissingFamily' $family.id; continue }
            $entry = $entries[0]
            if ($Profile -eq 'Developer' -and -not $family.developerEnabled) { continue }
            if ($entry.status -ne 'Executed') { Add-Issue 'MissingFamilyExecution' "$($family.id): $($entry.reason)"; continue }
            try {
                $discoveryPath = Resolve-EvidenceFile $EvidenceRoot $entry.discovery
                $executionPath = Resolve-EvidenceFile $EvidenceRoot $entry.execution
                $nativePath = Resolve-EvidenceFile $EvidenceRoot $entry.report
                $discovery = Get-Content -Raw -LiteralPath $discoveryPath | ConvertFrom-Json -AsHashtable
                $execution = Get-Content -Raw -LiteralPath $executionPath | ConvertFrom-Json -AsHashtable
                if ($discovery.schema -ne 'cis.discovery/v1' -or $execution.schema -ne 'cis.execution/v1' -or $discovery.family -ne $family.id -or $execution.family -ne $family.id) { throw 'UnrecognizedReportSchema: family discovery/execution.' }
                if ($entry.report.schema -ne $family.reportSchema) { throw 'UnrecognizedReportSchema: native report declaration.' }
                if ($family.reportSchema -eq 'vstest.trx/2010') {
                    $native = Read-NativeTrx $nativePath
                    if ($native.total -le 0 -or $discovery.discovered -le 0) { Add-Issue 'ZeroDiscoveredTests' $family.id }
                    foreach ($result in $native.results) {
                        if ($result.outcome -eq 'Failed') { Add-Issue 'FailedCase' "$($family.id): $($result.name)" }
                        if ($result.outcome -eq 'NotExecuted') { Add-Issue 'UnexpectedSkip' "$($family.id): $($result.name)" }
                    }
                    foreach ($field in @('passed', 'failed', 'skipped')) {
                        if ($execution[$field] -ne $native[$field]) { Add-Issue 'CounterMismatch' "$($family.id): $field" }
                    }
                    if ($execution.executed -ne ($native.total - $native.skipped)) { Add-Issue 'CounterMismatch' "$($family.id): executed" }
                    $required = @($Inventory.cases | Where-Object { $_.family -eq $family.id -and $Lane -in $_.lanes -and ($Profile -eq 'Release' -or $_.implementation -eq 'Implemented') })
                    foreach ($case in $required) {
                        $matches = @($native.results | Where-Object { $_.method -ceq $case.method })
                        $discovered = @($discovery.cases | Where-Object { $_.id -ceq $case.id })
                        if ($null -eq $case.method -or $matches.Count -lt $case.minimumExecutions -or $discovered.Count -ne 1 -or $discovered[0].count -lt $case.minimumExecutions) { Add-Issue 'MissingRequiredCase' "$($family.id): $($case.id)" }
                    }
                }
                elseif ($family.reportSchema -eq 'stryker.mutation/v3') {
                    $mutation = Get-Content -Raw -LiteralPath $nativePath | ConvertFrom-Json -AsHashtable
                    if ($mutation.schemaVersion -ne '3') { throw 'UnrecognizedReportSchema: mutation report.' }
                    $mutants = @($mutation.files.Values | ForEach-Object { $_.mutants } | ForEach-Object { $_ })
                    if ($mutants.Count -eq 0) { Add-Issue 'ZeroDiscoveredTests' 'mutation' }
                    if ($entry.scope -ne 'full-shipping-implementation') { Add-Issue 'IncompleteMutationScope' $entry.scope }
                    # Full matrix/scoring/source-bound disposition checks remain WP07-S03/WP07-S05.
                    Add-Issue 'MissingReleaseGate' 'Full mutation matrix validator is not yet implemented.'
                }
                else { throw 'UnrecognizedReportSchema: unsupported native format.' }
            }
            catch { Add-Issue ($_.Exception.Message.Split(':', 2)[0]) $_.Exception.Message }
        }
        if ($Profile -eq 'Release') {
            foreach ($case in $Inventory.cases | Where-Object { $Lane -in $_.lanes -and $_.implementation -ne 'Implemented' }) { Add-Issue 'MissingRequiredCase' $case.id }
            foreach ($gate in $Manifest.releaseGates) { Add-Issue 'MissingReleaseGate' "$($gate.id): $($gate.reason)" }
        }
    }
    catch { Add-Issue 'UnrecognizedReportSchema' $_.Exception.Message }
    return @($issues.ToArray())
}

Export-ModuleMember -Function Write-VerificationJson, Read-NativeTrx, New-EvidenceReference, Get-VerificationIssues
