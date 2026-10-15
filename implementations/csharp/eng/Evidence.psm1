#Requires -Version 7.5
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
        $definitions[$definition.id] = @{ method = "$($method.className).$($method.name)"; source = $method.codeBase }
    }
    $results = @($xml.SelectNodes("//*[local-name()='Results']/*[local-name()='UnitTestResult']") | ForEach-Object {
        if (-not $definitions.ContainsKey($_.testId)) { throw 'UnrecognizedReportSchema: result has no test definition.' }
        if ($_.outcome -notin @('Passed', 'Failed', 'NotExecuted')) { throw "UnrecognizedReportSchema: unsupported outcome $($_.outcome)." }
        [ordered]@{ testId = $_.testId; method = $definitions[$_.testId].method; source = $definitions[$_.testId].source; name = $_.testName; outcome = $_.outcome }
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

function ConvertTo-LegacyTrxDisplayName {
    param([string] $Name)
    # Older pinned TRX writers escape UTF-16 surrogate code units, including valid pairs.
    # Encode discovery text one way; never unescape arbitrary literal backslash sequences.
    return [regex]::Replace($Name, '[\x00-\x08\x0B\x0C\x0E-\x1F\uD800-\uDFFF\uFFFE\uFFFF]', [Text.RegularExpressions.MatchEvaluator]{
        param($match)
        return '\u' + ([int][char]$match.Value[0]).ToString('x4', [Globalization.CultureInfo]::InvariantCulture)
    })
}

function Read-NativeDiscovery {
    param([string] $Path)
    $report = Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json -AsHashtable -DateKind String
    if (@('schema', 'aborted', 'errors', 'total', 'tests') | Where-Object { -not $report.ContainsKey($_) }) { throw 'InvalidDiscovery: required native discovery fields are missing.' }
    if ($report.schema -ne 'cis.vstest-discovery/v1' -or $report.aborted -isnot [bool] -or $report.aborted -or $report.errors -ne 0 -or
        $report.total -le 0 -or $report.total -ne @($report.tests).Count) { throw 'InvalidDiscovery: incomplete native discovery.' }
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($test in $report.tests) {
        $parsed = [guid]::Empty
        if (-not [guid]::TryParse($test.Id, [ref]$parsed) -or $parsed -eq [guid]::Empty -or -not $ids.Add($test.Id) -or
            [string]::IsNullOrWhiteSpace($test.Method) -or [string]::IsNullOrWhiteSpace($test.Name) -or
            [string]::IsNullOrWhiteSpace($test.Source) -or -not [IO.Path]::IsPathFullyQualified($test.Source)) { throw 'InvalidDiscovery: duplicate or invalid native test identity.' }
    }
    return $report
}

function Get-MethodCounts {
    param([object[]] $Tests, [string] $Property = 'method')
    $counts = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
    foreach ($test in $Tests) {
        $method = $test[$Property]
        if (-not $counts.ContainsKey($method)) { $counts.Add($method, 0) }
        $counts[$method]++
    }
    return ,$counts
}

function Get-CaseBindings {
    param([hashtable] $Case)
    if ($Case.ContainsKey('bindings')) {
        if ($Case.method -or @($Case.bindings).Count -eq 0) { throw "InvalidCaseInventory: $($Case.id) has ambiguous or empty bindings." }
        $methods = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($binding in $Case.bindings) {
            if ([string]::IsNullOrWhiteSpace($binding.method) -or $binding.minimumExecutions -le 0 -or -not $methods.Add($binding.method)) {
                throw "InvalidCaseInventory: $($Case.id) has invalid or duplicate group methods."
            }
            $binding
        }
    }
    else { $Case }
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
        $summary = Get-Content -Raw -LiteralPath (Join-Path $EvidenceRoot 'summary.json') | ConvertFrom-Json -AsHashtable -DateKind String
        if ($summary.schema -ne 'cis.verification/v1' -or $Manifest.schemaVersion -ne 1 -or $Inventory.schemaVersion -ne 1) { throw 'UnrecognizedReportSchema: summary or inventory version.' }
        $known = @('unit', 'integration', 'mutation', 'architecture', 'compatibility', 'business')
        if (@(Compare-Object $known @($Manifest.families.id)).Count -gt 0 -or $Manifest.families.Count -ne 6) { throw 'InvalidFamilyManifest: exactly six unique families are required.' }
        if ($Lane -notin @($Manifest.lanes.id)) { throw "UnsupportedLane: $Lane" }
        $caseIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($case in $Inventory.cases) {
            if (-not $caseIds.Add($case.id) -or $case.family -notin $known -or $case.implementation -notin @('Implemented', 'Planned') -or
                @($case.lanes).Count -eq 0 -or @($case.lanes | Where-Object { $_ -notin $Manifest.lanes.id }).Count -gt 0) { throw 'InvalidCaseInventory: duplicate ID, family, state or lane.' }
            $null = @(Get-CaseBindings $case)
        }

        if ($summary.lane -ne $Lane) { Add-Issue 'LaneMismatch' "Evidence lane $($summary.lane); requested $Lane." }
        if ($summary.profile -ne $Profile) { Add-Issue 'ProfileMismatch' "Evidence profile $($summary.profile); requested $Profile." }
        # Older foundation reports have no supplementary reconciliation. Current runners always declare it.
        if (($summary.ContainsKey('runId') -or $summary.ContainsKey('reconciliation')) -and @($summary.families | Where-Object status -eq 'Executed').Count -gt 0) {
            try {
                if (-not $summary.ContainsKey('reconciliation') -or $summary.reconciliation.status -ne 'Produced') { throw 'InvalidReconciliation: executed families require the declared reconciliation.' }
                $reconciliation = Get-Content -Raw -LiteralPath (Resolve-EvidenceFile $EvidenceRoot $summary.reconciliation.report) | ConvertFrom-Json -AsHashtable -DateKind String
                if ($reconciliation.schema -ne 'cis.discovery-reconciliation/v1' -or $reconciliation.runId -cne $summary.runId -or
                    $reconciliation.lane -cne $summary.lane -or $reconciliation.profile -cne $summary.profile -or
                    $reconciliation.source.commit -cne $summary.source.commit -or
                    $reconciliation.source.inputInventory.path -cne $summary.source.inputInventory.path -or
                    $reconciliation.source.inputInventory.sha256 -cne $summary.source.inputInventory.sha256) { throw 'InvalidReconciliation: report belongs to different run/source/profile/lane.' }
            }
            catch {
                $code = if ($_.Exception.Message -match '^(InvalidReconciliation|ReportHashMismatch|MissingReport|EvidencePathOutsideRoot):') { $Matches[1] } else { 'InvalidReconciliation' }
                Add-Issue $code $_.Exception.Message
            }
        }
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
                $discovery = Get-Content -Raw -LiteralPath $discoveryPath | ConvertFrom-Json -AsHashtable -DateKind String
                $execution = Get-Content -Raw -LiteralPath $executionPath | ConvertFrom-Json -AsHashtable -DateKind String
                if ($discovery.schema -ne 'cis.discovery/v1' -or $execution.schema -ne 'cis.execution/v1' -or $discovery.family -ne $family.id -or $execution.family -ne $family.id) { throw 'UnrecognizedReportSchema: family discovery/execution.' }
                if ($entry.report.schema -ne $family.reportSchema) { throw 'UnrecognizedReportSchema: native report declaration.' }
                if ($family.reportSchema -eq 'vstest.trx/2010') {
                    $native = Read-NativeTrx $nativePath
                    if (-not $discovery.ContainsKey('nativeReport') -or $discovery.nativeReport.schema -ne 'cis.vstest-discovery/v1') { throw 'InvalidDiscovery: native discovery reference is required.' }
                    $discoveredNative = Read-NativeDiscovery (Resolve-EvidenceFile $EvidenceRoot $discovery.nativeReport)
                    $nativeCounts = Get-MethodCounts $native.results
                    $discoveredCounts = Get-MethodCounts $discoveredNative.tests 'Method'
                    $byId = @{}
                    foreach ($test in $discoveredNative.tests) { $byId[$test.Id] = $test }
                    $identitiesMatch = $discoveredNative.total -eq $native.total -and $discovery.discovered -eq $discoveredNative.total
                    foreach ($result in $native.results) {
                        $test = $byId[$result.testId]
                        $sameName = $null -ne $test -and ([string]::Equals($test.Name, $result.name, [StringComparison]::Ordinal) -or [string]::Equals((ConvertTo-LegacyTrxDisplayName $test.Name), $result.name, [StringComparison]::Ordinal))
                        if ($null -eq $test -or -not [string]::Equals($test.Method, $result.method, [StringComparison]::Ordinal) -or -not $sameName -or
                            -not [string]::Equals([IO.Path]::GetFullPath($test.Source), [IO.Path]::GetFullPath($result.source), $(if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }))) {
                            $identitiesMatch = $false
                        }
                    }
                    if (-not $identitiesMatch) { Add-Issue 'DiscoveryExecutionMismatch' "$($family.id): discovered IDs, methods, names, source paths or totals differ from execution." }

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
                        $bindings = @(Get-CaseBindings $case)
                        $discovered = @($discovery.cases | Where-Object { $_.id -ceq $case.id })
                        $missing = $bindings.Count -eq 0 -or $discovered.Count -ne 1
                        $discoveryCount = 0
                        foreach ($binding in $bindings) {
                            $minimum = $binding.minimumExecutions
                            $executedCount = if ($binding.method -and $nativeCounts.ContainsKey($binding.method)) { $nativeCounts[$binding.method] } else { 0 }
                            $found = if ($binding.method -and $discoveredCounts.ContainsKey($binding.method)) { $discoveredCounts[$binding.method] } else { 0 }
                            $discoveryCount += $found
                            if (-not $binding.method -or $minimum -le 0 -or $executedCount -lt $minimum -or $found -lt $minimum) { $missing = $true }
                        }
                        if ($missing) { Add-Issue 'MissingRequiredCase' "$($family.id): $($case.id)" }
                        elseif ($discovered[0].count -ne $discoveryCount) { Add-Issue 'DiscoveryCaseMismatch' "$($family.id): $($case.id) count differs from native methods." }
                    }
                }
                elseif ($family.reportSchema -eq 'stryker.mutation/v3') {
                    $mutation = Get-Content -Raw -LiteralPath $nativePath | ConvertFrom-Json -AsHashtable -DateKind String
                    if ($mutation.schemaVersion -ne '3') { throw 'UnrecognizedReportSchema: mutation report.' }
                    $mutants = @($mutation.files.Values | ForEach-Object { $_.mutants } | ForEach-Object { $_ })
                    if ($mutants.Count -eq 0) { Add-Issue 'ZeroDiscoveredTests' 'mutation' }
                    if ($entry.scope -ne 'full-shipping-implementation') { Add-Issue 'IncompleteMutationScope' $entry.scope }
                    # Full matrix/scoring/source-bound disposition checks remain WP07-S03/WP07-S05.
                    Add-Issue 'MissingReleaseGate' 'Full mutation matrix validator is not yet implemented.'
                }
                else { throw 'UnrecognizedReportSchema: unsupported native format.' }
            }
            catch {
                $code = if ($_.Exception.Message -match '^(EvidencePathOutsideRoot|MissingReport|ReportHashMismatch|UnrecognizedReportSchema|InvalidDiscovery|InvalidCaseInventory):') { $Matches[1] } else { 'UnrecognizedReportSchema' }
                Add-Issue $code $_.Exception.Message
            }
        }
        if ($Profile -eq 'Release') {
            foreach ($case in $Inventory.cases | Where-Object { $Lane -in $_.lanes -and $_.implementation -ne 'Implemented' }) { Add-Issue 'MissingRequiredCase' $case.id }
            foreach ($gate in $Manifest.releaseGates) { Add-Issue 'MissingReleaseGate' "$($gate.id): $($gate.reason)" }
        }
    }
    catch {
        $code = if ($_.Exception.Message -match '^(InvalidCaseInventory|UnsupportedLane|InvalidFamilyManifest|UnrecognizedReportSchema|InvalidReconciliation|ReportHashMismatch|MissingReport|EvidencePathOutsideRoot):') { $Matches[1] } else { 'UnrecognizedReportSchema' }
        Add-Issue $code $_.Exception.Message
    }
    return @($issues.ToArray())
}

function Write-DiscoveryReconciliation {
    param([string] $Root, [hashtable] $Summary, [hashtable] $Manifest, [hashtable] $Inventory, [string] $Lane)
    $families = foreach ($family in $Manifest.families) {
        $entry = @($Summary.families | Where-Object id -eq $family.id)[0]
        $results = @()
        $discovered = @()
        if ($entry.status -eq 'Executed' -and $family.reportSchema -eq 'vstest.trx/2010') {
            $results = (Read-NativeTrx (Resolve-EvidenceFile $Root $entry.report)).results
            $wrapper = Get-Content -Raw -LiteralPath (Resolve-EvidenceFile $Root $entry.discovery) | ConvertFrom-Json -AsHashtable -DateKind String
            $discovered = (Read-NativeDiscovery (Resolve-EvidenceFile $Root $wrapper.nativeReport)).tests
        }
        $found = Get-MethodCounts $discovered 'Method'
        $executed = Get-MethodCounts @($results | Where-Object outcome -ne 'NotExecuted')
        $passed = Get-MethodCounts @($results | Where-Object outcome -eq 'Passed')
        $failed = Get-MethodCounts @($results | Where-Object outcome -eq 'Failed')
        $skipped = Get-MethodCounts @($results | Where-Object outcome -eq 'NotExecuted')
        $requiredMethods = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $inapplicableMethods = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $cases = foreach ($case in $Inventory.cases | Where-Object family -eq $family.id) {
            $applicable = $Lane -in $case.lanes
            $bindings = foreach ($binding in @(Get-CaseBindings $case)) {
                if (-not $binding.method) { continue }
                if ($case.implementation -eq 'Implemented') {
                    if ($applicable) { $null = $requiredMethods.Add($binding.method) }
                    else { $null = $inapplicableMethods.Add($binding.method) }
                }
                [ordered]@{ method = $binding.method; minimumExecutions = $binding.minimumExecutions;
                    discovered = $(if ($found.ContainsKey($binding.method)) { $found[$binding.method] } else { 0 });
                    executed = $(if ($executed.ContainsKey($binding.method)) { $executed[$binding.method] } else { 0 });
                    passed = $(if ($passed.ContainsKey($binding.method)) { $passed[$binding.method] } else { 0 });
                    failed = $(if ($failed.ContainsKey($binding.method)) { $failed[$binding.method] } else { 0 });
                    skipped = $(if ($skipped.ContainsKey($binding.method)) { $skipped[$binding.method] } else { 0 }) }
            }
            [ordered]@{ id = $case.id; title = $(if ($case.ContainsKey('title')) { $case.title } else { $case.id }); implementation = $case.implementation; applicable = $applicable;
                bindings = @($bindings); pending = $(if ($case.implementation -eq 'Planned') { 'Required later roadmap work; no missing execution is waived.' } else { $null }) }
        }
        $allMethods = [Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
        $allMethods.UnionWith([string[]]@($found.Keys))
        $allMethods.UnionWith([string[]]@($executed.Keys))
        $supporting = foreach ($method in $allMethods) {
            if (-not $requiredMethods.Contains($method)) {
                [ordered]@{ method = $method; classification = $(if ($inapplicableMethods.Contains($method)) { 'BoundOnlyToInapplicableCases' } else { 'UnboundTechnical' }); discovered = $(if ($found.ContainsKey($method)) { $found[$method] } else { 0 });
                    executed = $(if ($executed.ContainsKey($method)) { $executed[$method] } else { 0 }); passed = $(if ($passed.ContainsKey($method)) { $passed[$method] } else { 0 });
                    failed = $(if ($failed.ContainsKey($method)) { $failed[$method] } else { 0 });
                    skipped = $(if ($skipped.ContainsKey($method)) { $skipped[$method] } else { 0 }) }
            }
        }
        [ordered]@{ id = $family.id; status = $entry.status; nativeFormat = $family.reportSchema; report = $entry.report;
            discoveredRows = @($discovered).Count; reportedRows = @($results).Count; executedRows = @($results | Where-Object outcome -ne 'NotExecuted').Count; cases = @($cases); supportingMethods = @($supporting) }
    }
    Write-VerificationJson (Join-Path $Root 'discovery-reconciliation.json') ([ordered]@{
        schema = 'cis.discovery-reconciliation/v1'; runId = $Summary.runId; source = $Summary.source; lane = $Lane; profile = $Summary.profile;
        scope = 'All preserved requirement/case IDs plus actual unbound technical methods. Group/leaf bindings may share rows; native row totals count each ID once. This is not full release qualification.';
        families = @($families); plannedCaseIds = @($Inventory.cases | Where-Object implementation -eq 'Planned' | ForEach-Object id);
        remainingReleaseGates = $Manifest.releaseGates
    })
}

Export-ModuleMember -Function Write-VerificationJson, Read-NativeTrx, New-EvidenceReference, Get-VerificationIssues, Read-NativeDiscovery, Get-CaseBindings, Get-MethodCounts, Write-DiscoveryReconciliation
