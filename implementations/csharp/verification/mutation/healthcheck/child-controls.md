# Bounded actual HTTP and probe activation

Run this control only after the fixed native pilot and its kill, survivor,
async-path and infrastructure controls succeed. The original
[version-3 route](../../../../../verification/healthcheck-child-control/README.md)
requires a healthy current whole Pipes group. The explicit composite route uses
the [version-4 operator](../../../../../verification/healthcheck-child-control-v4/README.md)
after its shared qualified consumer validates whole Windows Pipes plus the bounded
six-tuple Linux permission proof. A failed whole-Linux-Pipes run never supplies
that prerequisite. Both routes prove propagation into the same two real child
bodies; this bounded report contributes no module scope or score.

The selected maintained operator owns the process/storage guards and preserves
its admitted four-property build context through baseline, native execution and
restoration. Version 4 additionally pins its own actual producer receipt and the
consumer's source/build/output/PDB/runtime closure before and after execution.
These are explicit supplemental identities outside the original native archive.
The lower-level recipe below records their unchanged wrapper selection and
binding contract; it is not a standalone guarded launcher.

The former whole-body control would schedule roughly 153 viable definitions
from the retained historical HTTP99/probe95 populations. Fresh test hosts also
copy private child stages per mutation. Two narrow statement spans avoid that
multiplication while retaining actual native definitions in both shipping bodies.
Historical counts and outcomes supply no current credit.

Use the immutable candidate's workspace, the checker and native tool paths
already pinned by its `toolchain.json`, and a fresh profile and twelve-character
lowercase hexadecimal attempt. Run only one active native workspace. The same
private dotnet installation must provide SDK 10.0.401, subject 10.0.12 and the
actual tool host 8.0.31. Confirm that resolution before launching the wrapper.

The following variables describe the lower-level wrapper recipe. Use the selected maintained operator linked above as the guarded execution entry point:

```powershell
$repo = Join-Path $candidate 'workspace'
$language = Join-Path $repo 'implementations/csharp'
$profile = 'hc-child-control-01'
$attempt = [Guid]::NewGuid().ToString('N').Substring(0, 12)
$profileRoot = Join-Path $repo ".cis/local/testing/incremental/$profile"
$output = Join-Path $profileRoot "work/$attempt"
$config = Join-Path $output 'stryker-config.json'
if (Test-Path -LiteralPath $output) { throw 'Use a fresh owned control attempt.' }
[IO.Directory]::CreateDirectory($output) | Out-Null
$statements = [ordered]@{
    'Healthcheck/AspNetCore/HealthcheckApplicationBuilderExtensions.cs' = 'context.Response.Headers.CacheControl = "no-store, no-cache";'
    'Healthcheck/Pipes/HealthProbeClient.cs' = 'return parsed.SelectedHealthy ? 0 : 1;'
}
$selected = foreach ($source in $statements.Keys) {
    $text = [IO.File]::ReadAllText((Join-Path $language "src/CIS.Core/$source"))
    $statement = $statements[$source]
    $start = $text.IndexOf($statement, [StringComparison]::Ordinal)
    if ($start -lt 0 -or $text.LastIndexOf($statement, [StringComparison]::Ordinal) -ne $start) {
        throw "The reviewed statement changed or is ambiguous: $source"
    }
    '{0}{{{1}..{2}}}' -f $source, $start, ($start + $statement.Length)
}
$filter = 'FullyQualifiedName=CIS.Tests.IntegrationTests.Healthcheck.Reporting.HealthcheckHttpTransportTests.RealKestrelServesKnownViewsWithCachePreventionAndNoBusinessEffects|FullyQualifiedName=CIS.Tests.IntegrationTests.Healthcheck.Reporting.HealthcheckProbeProcessTests.OmittedViewAndValidUpperCliLimitsStillRequestAndEmitHealth|FullyQualifiedName=CIS.Tests.TestAssemblySetup.SchedulingMatchesRequestedMode'
$settings = [ordered]@{
    project = 'CIS.Core.csproj'
    'test-projects' = @('../../tests/CIS.Core.IntegrationTests/CIS.Core.IntegrationTests.csproj')
    'test-case-filter' = $filter
    mutate = @($selected)
    'mutation-level' = 'Complete'
    'test-runner' = 'vstest'
    'target-framework' = 'net10.0'
    configuration = 'Release'
    'coverage-analysis' = 'off'
    'disable-bail' = $true
    concurrency = 2
    'additional-timeout' = 10000
    reporters = @('json', 'progress', 'cleartext')
    thresholds = [ordered]@{ high = 100; low = 90; break = 0 }
}
[ordered]@{ 'stryker-config' = $settings } | ConvertTo-Json -Depth 6 |
    Set-Content -LiteralPath $config -Encoding utf8NoBOM
```

The offsets are UTF-16 character indices in the actual decoded source, rather
than line numbers or UTF-8 byte offsets. Recompute them in the selected frozen
workspace. The wrapper accepts exactly two spanned sources, one span per unique
base file, with `0 <= start < end <= source.Length`. Mixed whole/span lists,
globs, traversal, duplicate sources and command-line selection overrides fail.
Whole-file v1/v2 receipt semantics remain available unchanged.

Preserve all four properties from the admitted Pipes build context (the whole group for version 3 or the bounded Linux permission build for version 4):
`CisMutationTesting=true`, `CisHealthcheckMutationCohort=true`, `CisHealthcheckPilotCohort=false` and
`CisLinuxIdentityQualification=true` on Linux or `false` on Windows, for the
baseline, native run, restored build and postflight. The version-3 operator also
sets `MSBUILD_EXE_PATH` to the frozen SDK MSBuild pin and prefixes `PATH` with
the frozen subject host directory in every phase. Keep shared compilation/MSBuild
servers disabled and locked restore.
Use a fresh short owned TEMP/TMP/TMPDIR included in the existing aggregate guard.
The selected baseline and postflight must each pass three native cases. Reuse
already validated shipping build outputs when their exact inputs and compilation
context match; restore shipping Core and fixture copies after native execution.

The version-3 operator invokes the following wrapper through the frozen owning process/storage guard. Use its documented entry point; running this block directly does not provide the required guards, context or restoration:

```powershell
& $dotnet $checker healthcheck-mutation-run `
    --language-root $language --profile-id $profile --dotnet $dotnet `
    --stryker-cli $stryker --stryker-core $strykerCore `
    --child-root (Join-Path $profileRoot 'child-evidence') `
    --maximum-child-bytes 134217728 `
    -- --config-file $config --output $output --skip-version-check --log-to-file
```

The wrapper owns invocation binding; its caller owns the deadline and process-tree
cleanup. Set a 600-second total ceiling, reserve the final 90 seconds for healthy
restoration, and pass only the remaining native time to the existing process
runner. Sample all owned scratch, child stages and short temp against 2 GiB, keep
2 GiB free on their actual volumes, and retain at most 256 MiB ordinary reports and
diagnostics. The child-evidence subroot has a separate 128 MiB cap. Stop expansion
on a prerequisite, capacity, timeout, native or cleanup failure; do not enlarge
or repeat the same unsuccessful control unchanged.

Require actual native exit zero and an unchanged JSON report. Select one actual
completed definition inside each reviewed statement span, with native status
Killed or Survived and an actual corresponding active child launch. The HTTP
test checks Cache-Control through Kestrel. The probe test checks its real pipe
exchange, exact healthy stdout and exit zero. Native mutant IDs come from this
run; historical IDs cannot substitute.

Supply two receiving rows, one `http-host` and one `probe`, sharing the original
session/report/binding paths. Each row supplies `activeMutant` and
`definitionIdentity`, plus `nativeReportSha256` and `nativeBindingSha256`.
Receiving validates the unchanged inner v3 selection (under either outer operator version), whole-file pins, prepared/started/completed
chain and current frozen tools/inputs, then re-runs the actual origin, selector
and lifecycle audit. Preserve the raw binding phases, report and bounded child
evidence until that audit and healthy postflight are complete. Remove only owned
successful disposable scratch after required evidence is safely retained.
