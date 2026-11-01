# Grouped Healthcheck mutation

The 29 October recovery instruction adopts this prospective route. It uses the
pinned Stryker.NET 4.16.0/VSTest/MSTest combination against the single `CIS.Core`
assembly. Native results remain unchanged. Historical receivers and reports keep
their original interpretation; new acceptance does not require per-mutant C#
proof catalogues or historical capture/runner chains.

Build the real checker once. From `implementations/csharp`, select a fresh
candidate directory and the restored pinned Stryker CLI. The selected dotnet
host needs SDK 10.0.401, subject runtime 10.0.12 and tool-host runtime 8.0.31.
Missing tools fail before the test/mutation matrix.

```powershell
dotnet build eng/CIS.DependencyChecker/CIS.DependencyChecker.csproj -c Release
$checker = (Resolve-Path eng/CIS.DependencyChecker/bin/Release/net10.0/CIS.DependencyChecker.dll).Path
$candidate = [IO.Path]::GetFullPath('../../.artifacts/csharp/healthcheck-grouped/candidate-01')
$ownedRoot = [IO.Path]::GetFullPath('../../.artifacts/csharp/healthcheck-grouped')
$stryker = Join-Path $env:USERPROFILE '.nuget/packages/dotnet-stryker/4.16.0/tools/net8.0/any/Stryker.CLI.dll'
dotnet $checker healthcheck-grouped-mutation prepare --repo-root ../.. --output-root $candidate
dotnet $checker healthcheck-grouped-mutation run --candidate-root $candidate --owned-root $ownedRoot --group pilot --stryker-cli $stryker --budget-seconds 600
dotnet $checker healthcheck-grouped-mutation receive --repo-root ../.. --candidate-root $candidate --groups pilot --output-root "$candidate/pilot-review"
```

Partial receiving deliberately exits one: it writes current definitions/misses
and actual counts; incomplete scope has no final percentage. Its unchanged
report can contribute to final acceptance without another native execution.

`candidate.json` hashes maintained C# source, tests, project/build/configuration,
contracts/conformance, compatibility inputs and entry-point glue.
`candidate.zip` retains those exact bytes, including dirty and new inputs.
One active workspace reuses that snapshot; an exclusive lease prevents concurrent
groups from modifying its outputs. Changed relevant inputs require a new
candidate. The source inventory is discovered, including declaration-only files;
historical file counts are not permanent requirements.

The default checker and Tool tests maintain the grouped route and actual child
audit. Historical Union/Shared proof workflows and the three old manual drivers
are excluded unless `CisHistoricalVerification=true` is explicitly requested.
From the language root, use
`dotnet test tests/CIS.Verification.Tests/CIS.Verification.Tests.csproj -c Release -p:CisHistoricalVerification=true --filter FullyQualifiedName~HealthcheckMutationUnionQualificationTests`
for an explicit historical test selection. Rebuild the checker with
`dotnet build eng/CIS.DependencyChecker/CIS.DependencyChecker.csproj -c Release -p:CisHistoricalVerification=true`
before invoking an old command. Default old-command dispatch exits two with that
instruction. Historical inputs, reports and interpretation remain unchanged;
those workflows supply no new current-candidate credit.

The fixed pilot contains `HealthDiagnostic.cs`, `HealthMeasurement.cs`,
`IHealthCollector.cs` and `HealthExecutionBinder.cs`. The remaining runtime group
excludes those pilot files.

The commands below retain the original same-host grouped execution and receiving route. The separately qualified [composite analysis consumer](../../../../../verification/healthcheck-grouped-acceptance/README.md) preserves its historical accepted09g purpose and fixed native identities. It is not the receiver for a new candidate.

### Prospective composite receiving

The maintained checker also accepts an explicit composite manifest with the
captured [current policy](composite-policy.json). This mode selects whole Linux
pilot/runtime/HTTP groups and whole Windows Pipes; it does not choose a better
platform per definition. The policy is an actual candidate input, rather than a
caller-supplied approval or a campaign-specific C# digest. Current compiled
checker/helper bytes must match the producing toolchains and portable evidence.
Exact ordered source/test/build/policy membership replaces historical file-count
assumptions. Original Windows input digests retain their producing line endings;
receiving compares the actual tuples without rewriting a raw receipt.

```powershell
$policy = [IO.Path]::GetFullPath('verification/mutation/healthcheck/composite-policy.json')
dotnet $checker healthcheck-grouped-mutation receive --repo-root ../.. --candidate-root $candidate --composite-manifest $manifest --policy $policy --output-root "$candidate/composite-review"
```

`$manifest` is an absolute JSON path with schema
`cis.healthcheck.composite-native-inputs/v3`. Its `groups` array contains unique
objects with `group` and `platform`: Linux entries name an absolute
`candidateRoot`; Windows Pipes names the absolute exported `proof.json` in
`proof`. It also supplies absolute `reviewAuthority`, `reviewFiles` (an array),
`linuxPermissionProof` and `childControls` paths. A pilot-only manifest may omit
the last two inputs. Its incomplete result exits one and has no final score, but
the unchanged native pilot remains eligible for the later matching whole union.
Each review retains its actual origin candidate, definition, raw outcome,
requirement, delegated reviewer and rationale.

Full qualification requires the complete dynamic disjoint source inventory,
including zero-definition files, all six source-bound Unix permission kills,
seven actual Windows controls and both real HTTP/probe child activations.
Stable obligation IDs bind the shipping method and named behavioral witness
roles. Same-count role, source-method or witness substitutions are rejected.
Actual mutation identities, source hashes, operators, replacements and spans
remain reviewed policy data, matched to the unchanged native definitions;
there is no per-campaign C# mutation catalogue. Complete source coverage is
reported separately from healthy runtime completion.
Both native passing contexts, exact commands, tool inputs, witness IDs, source
bytes, actual UID/SID context, restoration and cleanup are checked. The Linux
supplement and child controls add no module population or kills. Raw and
reviewed-equivalence-adjusted conservative scores are separate; timeouts retain
zero kill credit. Missing or inadmissible evidence cannot be supplied by a
passing partial selection or by the historical bridge.

The original permission and child-v4 producers still contain historical checker
bindings. A fresh candidate needs their narrowly updated current-mode adapters
before producing new proofs; this receiver does not relabel their old output.
The permission adapter must retain its existing v1 proof shape and bounded
600-second execution, six exact reviewed obligations, five passing baseline and
restored cases, actual UID journals, complete retained-file pins, current
candidate/toolchain and actually executed checker/helper pins. It must derive
these from the frozen current candidate, not accept unchecked hash arguments.

For composite mode, `childControls` names a
`cis.healthcheck.composite-child-controls/v1` object. It records the current
`candidateIdentity`, actual `producerSha256`/`helperSha256`, observed
`buildProperties`/`environment`, `controls` (the existing two `ChildControl`
rows), `baselineContext` and `restoredContext` objects each containing the original TRX
`path`/`sha256` and actual `command` record. The commands use the existing
three policy cases, fixed Release/no-build/no-restore context, current pinned
host and actual test assembly. The envelope also retains `budgetSeconds: 600`,
`restorationReserveSeconds: 90`, actual `seconds`, `restored: true`,
`processCleanupHealthy: true`, `temporaryCleanup: "owned short temp removed"`
and an empty `failures` array. These flags alone supply no admission: the
receiver rechecks both exact passing TRXs, configuration, current source/tools,
original prepared/started/completed native bindings and the maintained real
child activation/lifecycle audit. The current-mode producer adapter is still
required to create this record from actual execution. Historical operator
build constants and receipts do not stand in for it.
Compile and run the focused `HealthcheckGroupedMutationTests` and
`HealthcheckPlatformProofTests` before a bounded current native correctness pilot.
That pilot must establish a healthy baseline, genuine kill and survivor/control,
infrastructure-failure rejection, and healthy restoration. No execution or
fresh-candidate acceptance is implied by the source implementation alone.

The four disjoint groups are `pilot`, `runtime`, `http` and `pipes`. Every group
uses Complete mutation without method/operator exclusions. HTTP/Pipes select
direct shipping methods and native OS transport controls. OS pipes
remain component/integration work. Coverage/early-bail changes require actual
pilot qualification on the pinned async/adapter path.

The pilot uses the explicit `CisHealthcheckPilotCohort=true` test compilation
context. Its fourteen maintained files protect the four selected shipping
sources, including all public Binder behavior and actual asynchronous settlement.
The narrowed compilation passed 378 native cases, including the shared
scheduling control, in both baseline and restored execution. This is
a behavior-derived selection, not a case-count target. The ordinary Unit project
and general Healthcheck cohort retain their whole suites. Every other native
group explicitly sets the pilot property false. `run.json` retains the declared
build properties and actual supplied child environment, and receiving checks
both against the current group recipe.

| Maintained files under Unit `Healthcheck/` | Preserved behavior |
| --- | --- |
| Foundation: `HealthcheckValueContractTests`, `HealthcheckContractMutationQualificationTests`, `HealthDiagnosticRuntimeAcceptanceBoundaryTests` | Scalar values/defaults, UTC observation, Unicode limits, nonfinite rejection, diagnostic sanitization/truncation and detachment |
| Execution: `HealthExecutionBinderTests`, `HealthBindingRetirementBoundaryTests`, `HealthExecutionFactValidationTests`, `HealthExecutionPayloadMutationQualificationTests`, `HealthExecutionSparseFactQualificationTests`, `HealthExecutionWithdrawalObservationTests`, `HealthRegistrationDiagnosticTests` | Bind overloads, foreign/null inputs, normalization, duplicate/enrichment/order rules, diagnostics, concurrency, lifetime, external/owned retirement and actual collection settlement |
| Core: `HealthcheckLifecycleQualificationTests` | Terminal admission and withdrawal/rebinding before an old binding is called |
| Reporting: `HealthReportFormatterBoundaryQualificationTests`, `HealthReportFormatterMutationQualificationTests`, `HealthReportFormatterTests` | Actual downstream field/diagnostic observations and the shared conformance-fixture reader |

The files use their existing nested helpers, the real Core/TestSupport references
and later-linked `TestAssembly.cs`. Registration diagnostics call
`HealthReportFormatterTests.ReadFixture`; the existing conformance JSON remains
in the frozen input archive. No checker or DiscoveryHost implementation source
is needed by this context. The unsuccessful 1,506-case pilot and its narrowed
378-case repeat produced no native JSON report and receive no outcome credit.
The latter stopped safely at its deadline with healthy restored tests.

The next bounded hypothesis raises stable grouped concurrency from two to eight
on the current Linux x64 execution host at
`/data/cis-healthcheck-20261021-01a15a72`, which has 32 available processors and
about 132 GB available memory. Its sampled trace recorded 88 request starts and
86 completions over 181.539 seconds; the two workers were occupied for 180.925
and 180.277 seconds. Request median was 3.001 seconds, P90 12.13 seconds and
maximum 15.4 seconds, without one dominant final stuck test. Dividing the observed
361 worker-seconds over eight workers gives an ideal 45-second scheduling cost;
contention and the roughly 272-second fixed native preparation remain unmeasured
for that profile. Those numbers were a prospective estimate.

The eight-worker pilot subsequently completed in 487.59 seconds, including
381 seconds native execution: roughly 272 seconds preparation and 109 seconds
mutant testing. Baseline and restored execution each passed 378 cases. Its
unchanged four-file report contains 246 definitions: 134 Killed, 23 Survived,
2 Timeout, 5 NoCoverage, 40 CompileError and 42 Ignored. The actual awaited Binder
settlement removal was Killed, with the entered-collection-reservation barrier
test among its four native killer witnesses. The reviewed finite-number guard
kill and incidental-message survivor also remained native outcomes. The actual
invalid-configuration control exited one, drained output and was rejected by
receiving. This establishes the bounded native route; full Healthcheck scope and
matching final-candidate inputs remain required for acceptance.

All four stable groups now use perTest coverage, eight workers, disabled bail
and Complete source scope. The four pilot production files, fourteen test files,
pinned framework/adapter and 600-second pilot ceiling remain unchanged. Each
transport report must separately demonstrate its actual async path before
acceptance or another group starts.

The mutation HTTP filter uses all direct middleware cases, including route,
authorization, cancellation and response-stream behavior. Its two Kestrel cases
remain in complete regression and bounded child controls. The Pipes filter uses
direct `HealthProbeClientTransportTests`, protocol/formatter tests, native pipe
server/budget/permission cases and scheduling controls. Real probe entrypoint
cases and the isolated Windows descriptor/GC probe remain in explicit complete
regression; their process/GC boundary is not repeated for every mutant. The
bounded actual child controls still prove activation in both shipping bodies.

The native whole-group HTTP report must contain a Killed `Statement mutation`
replacing `await WriteAsync(context, report, status).ConfigureAwait(false);` with
`;`. Its native `killedBy` mapping must include
`HealthcheckHttpTransportTests.ActualBodyWriteCompletionFaultAndCancellationGovernTheHealthRequest`.
That test holds the actual response stream pending, then exercises completion,
fault and cancellation with cleanup.

The Pipes report must contain two corresponding native Killed statement removals:

- `HealthProbeClient.RunAsync`: remove the `TimeoutOrCanceled` `WriteErrorAsync`
  await. The required killer is
  `HealthPipeActiveCancellationBoundaryTests.CallerCancellationAfterARealRequestDoesNotWaitForThePeerToSendAResponse`.
  It proves a real request is pending against an open silent peer before
  cancellation, then requires exit two and the bounded stderr code.
- `HealthPipeServer.DisposeAsync`: remove
  `await stopCompletion.Task.ConfigureAwait(false);`. A required native killer is
  `HealthPipeActiveCancellationBoundaryTests.ClosingTheTransportPrecedesSettlementOfItsActuallyRunningSource`
  or `HealthcheckPipeTransportTests.DisposalWaitsForAnActuallyRunningSnapshotSource`.
  Both use the real server and OS pipe, hold an actually entered source behind a
  release gate, and require disposal to remain pending until that source settles.
  Admission records the exact source-bound test that actually killed the mutant;
  merely covering the statement or appearing in the baseline supplies no kill.

Receiving derives exact UTF-16 spans from each unique current-source statement,
checks source/operator/span/replacement and raw Killed status, and resolves native
killer IDs through the report's exact test-file text and fully qualified names.
Timeout supplies no kill credit. These three small admission rules use the same
acceptance-eligible whole-group reports; they introduce no per-mutant catalogue.
`run.json` records their witnesses or an explicit async-control failure. A failed
transport admission blocks every next group and final receiving. The separate
actual child span control retains coverage off.

Review one genuine product kill, the actual async settlement-path kill and one
known survivor in `pilot-review/definitions.json`. Save three distinct rows with
`purpose` (`knownProductKill`, `knownAsyncProductKill` or `knownSurvivor`),
`definitionIdentity`, `expectedStatus`, `requirement`,
`reviewer` and `rationale`. Reviewers must be delegated in the maintained,
candidate-bound `review-authority.json`. Inspect the actual source, operator,
span, replacement and requirement; counts or approval flags are insufficient.

The async control must be the actual native Killed removal of
`await actual.ConfigureAwait(false)` in `HealthExecutionBinder.CompleteSettlementAsync`.
`RetiredOwnedBindingDisposalSharesTheActualCollectionSettlementBarrier` holds a
real reservation open and asserts that disposal stays incomplete before actual
settlement, then releases and awaits cleanup. This exercises instrumented async
shipping code and MSTest's async adapter path. A synchronous kill elsewhere in
an async test does not satisfy this control; Timeout receives no credit. The
four-file pilot keeps its 600-second ceiling and uses eight native workers.

```powershell
dotnet $checker healthcheck-grouped-mutation pilot-controls --candidate-root $candidate --controls <reviewed-controls.json> --stryker-cli $stryker
```

This also executes deliberately invalid native initialization and proves the
receiver rejects its actual failure even if an adapter supplies all Killed.
It binds the passing baseline, native kill/survivor, unchanged report and healthy
restored execution. Missing/changed controls block expansion. A pilot without
an independently reviewed survivor does not satisfy preflight.

The pilot ceiling is ten minutes, with time reserved for restoration. The driver
samples aggregate owned candidate scratch/evidence every second and cancels
through the existing process-tree cleanup when it observes 2 GiB or breaches
the 2 GiB free-space reserve. Peak bytes are sampled. Installed SDK/NuGet caches
are measured separately. Ordinary retained evidence is bounded to 256 MiB;
stdout/stderr diagnostics retain at most 64 KiB each.

The optional `--owned-root <task-root>` expands the existing aggregate guard to
the explicitly owned parent directory containing this candidate. Its default is
the candidate directory. The server route selects
`/data/user/cislib-recovery-20261029-01a1878f`, so earlier task evidence and staging
are counted alongside the active workspace. A short temp inside that scope is
counted once; a separate owned temp is added once. `run.json` records the counted
root, temp relationship, limit and actual owned/candidate/temp volume observations.
Installed SDK/NuGet caches remain outside this task scope and are measured
separately. Sibling prefixes and linked candidate ancestors are rejected.

Pilot native commands enable `--log-to-file` while running. Successful cleanup
retains unchanged JSON and bounded stdout/stderr in `native.log`, then removes
the remaining native scratch, including per-host/per-kill file logs. Nonpilot
native commands omit the optional flag. All groups retain native TRX, exact
configuration/toolchain and full source identity. The actual command is retained
in `run.json`.

All four stable grouped profiles declare eight workers. A host with fewer than
eight processors available to the checker is rejected before any build. Worker
count is not chosen dynamically. `run.json` records the actual `processorCount`
and declared `nativeConcurrency`; receiving validates that prerequisite. The
separate child span control retains its explicitly bounded
two-worker recipe.

Declare the full-run budget from actual pilot build, native, mutant and postflight
costs before expanding. Use an explicit `--budget-seconds` for each remaining
group; the command never automatically launches a full campaign or exceeds one
hour per invocation. Do not enlarge or repeat an unsuccessful pilot unchanged.
Repair one concrete cause or state a bounded hypothesis first.

For planning, the current 37-file runtime group has 230,590 source bytes and
4,738 lines, versus the pilot's 22,722 bytes and 484 lines. Those size ratios
alone underestimate the retained native population. The historical report has
2,610 eligible and 2,589 scheduled runtime definitions; 36 of its 37 source
hashes match, while the Runner changed. Its scheduled population is 16.28 times
the pilot's 159 scheduled definitions. The population-based planning projection
is about 39.5 minutes including native preparation and wrapper work. Runtime's
1,506-case context, source changes, test fanout, batching and contention remain
unmeasured for the final candidate. Declare a hard 3,600-second runtime ceiling,
with the same 90-second restoration reserve and storage guards. This grants no
execution or outcome credit and never raises itself.

```powershell
dotnet $checker healthcheck-grouped-mutation run --candidate-root $candidate --owned-root $ownedRoot --group runtime --stryker-cli $stryker --budget-seconds 3600
dotnet $checker healthcheck-grouped-mutation run --candidate-root $candidate --owned-root $ownedRoot --group http --stryker-cli $stryker --budget-seconds <declared-budget>
dotnet $checker healthcheck-grouped-mutation run --candidate-root $candidate --owned-root $ownedRoot --group pipes --stryker-cli $stryker --budget-seconds <declared-budget>
```

Successful native scratch is removed after retaining its raw JSON report and
identity. The candidate workspace remains while groups are pending to reuse
builds. Automatic cleanup never prunes historical evidence or shared caches.

For final receiving, each actual miss review supplies `candidateIdentity`,
`definitionIdentity`, `rawStatus`, `requirement`, `decision` (`Equivalent`,
`ReviewedNoncritical`, `ReviewedTimeout` or `NonTermination` for a raw Timeout, or the separately proved
`PlatformCovered`), `reviewer`,
`rationale` and `reviewedAtUtc`. The maintained authority delegates technical
review, not product approval. Reviews never add kills. Genuine reviewed
Equivalent decisions remove their eligible definition from the denominator;
reviewed noncritical misses and every Timeout stay in it. Timeout has no kill
credit. Unreviewed eligible misses remain holds.

Use `ReviewedTimeout` when a source-backed review explains a bounded stall or
resource-retention outcome without establishing literal nontermination. Reserve
`NonTermination` for a demonstrated inability to terminate. Both retain the native
Timeout, zero kills and the complete eligible denominator; neither closes an
unresolved critical ownership, cancellation or permission requirement by itself.

```powershell
dotnet $checker healthcheck-grouped-mutation receive --repo-root ../.. --candidate-root $candidate --reviews <reviewed-misses.json> --review-authority verification/mutation/healthcheck/review-authority.json --child-controls <actual-child-controls.json> --output-root "$candidate/acceptance"
```

The authority path above is relative to the language-root working directory and
must match its frozen maintained bytes. Receiving validates exact disjoint scope,
raw source/report bytes and zero-definition membership, sums outcomes and applies
the adopted gate only after complete current scope:
`Killed / (Killed + Survived + NoCoverage + Timeout - reviewed Equivalent) >= 90%`.
It also records the raw eligible denominator and raw conservative percentage.
Individual groups do not need 90%.

For the declared composite route, final receiving uses the qualified supplemental consumer and its exact source/build receipt:

```text
<private-dotnet> exec --fx-version 10.0.12 --roll-forward Disable <consumer.dll> receive --checker <original-frozen-checker> --consumer-source <qualified-consumer-source> --consumer-build <actual-consumer-build.json> --manifest <composite-inputs.json> --policy <qualified-policy.json> --output <new-results-directory>
```

Its input manifest names the four whole-group origins, Linux permission proof, original review authority and current source-bound reviews, both original child rows, and the completed child-v4 operator manifest. Whole Windows Pipes is the chosen four-file population; `PlatformCovered` may cite the exact validated Linux permission proof for a chosen Windows Survived/NoCoverage Unix obligation. That direction is specific to this explicit consumer. It preserves the chosen raw statuses and denominator, adds zero kills, and never applies to Timeout. The original frozen receiver's Windows-proof direction below remains unchanged. Provider, physical-remote/elevated, source coverage and all six release-family obligations remain separate.

`PlatformCovered` is a prospective technical disposition for a chosen-lane raw
Survived or NoCoverage definition whose exact critical obligation is demonstrated
by a current native Killed definition on Windows. It adds no chosen-lane kill,
excludes no denominator and never changes the raw status. A platform name, inactive
branch, metadata-only observer, old report or reviewer Boolean supplies no proof.
Timeout cannot use this decision. It does not declare a critical guard globally
noncritical or qualify unexecuted release/platform boundaries.

Use the same frozen platform-neutral checker and TestSupport DLL bytes on both
hosts. Windows uses its actual pinned SDK 10.0.401, subject 10.0.12 and tool host
8.0.31, with a distinct platform/host candidate identity. All maintained production,
test, build and configuration input membership and bytes must match the new
chosen-lane candidate exactly. The sole declared compilation-context difference
is `CisLinuxIdentityQualification=false` for Windows versus true for Linux Pipes;
the other cohort properties and whole native recipe remain identical.

After an actually healthy Windows pilot and complete Pipes invocation, create a
compact claims array. Each row contains `definitionIdentity`, `witnessSource`
(language-relative `tests/...cs`), the exact native `witnessName` and `requirement`.
Every claimed definition must really be Killed, with that named current-source
test among its unambiguous native `killedBy` witnesses. On the Windows host, run
with absolute paths under an explicit outer process deadline:

```powershell
& $subjectDotnet exec --fx-version 10.0.12 --roll-forward Disable $checker healthcheck-grouped-mutation export-platform-proof --repo-root "$windowsCandidate/workspace" --candidate-root $windowsCandidate --claims $claims --output-root "$windowsCandidate/platform-proof"
```

The exporter validates the actual execution identity, baseline/restored TRXs,
source/config/report/toolchain, cleanup and native populations before exporting.
Its fixed portable bundle retains the original source archive, unchanged native
JSON and bounded native log, run/config/toolchain/TRXs, and a compressed actual
tool/SDK/runtime/adapter/input closure. It hashes the files before and after
copying. Export remains subject to the existing aggregate 2 GiB scratch/evidence,
256 MiB retained and 2 GiB free-reserve limits. A failed or partial export is not
an admissible receipt.

Copy the complete unchanged bundle into the chosen candidate's retained root.
The named miss review uses `decision: "PlatformCovered"` and an additional
`platformProof: { "path": "<absolute imported proof.json>", "sha256": "<actual hash>" }`.
Its requirement must match the claim. Receiving rehashes every artifact and both
compressed source/tool closures without extracting arbitrary paths. It validates
the common frozen inputs, fixed native command/profile, context, producer bytes,
real exact-definition Killed result and actual test-source witness. The acceptance
output records the foreign candidate/platform/host and proof hash separately.
The chosen-lane raw counts and full-scope formula above remain unchanged.

Adding these validators or meaningful test fixes creates a new main candidate.
Earlier candidate reports retain their original identities and receive no
automatic current-candidate credit. No current Windows execution is implied by
these receiving rules or their parser tests. The direct Windows endpoint pin
test must establish its actual zero-growth baseline and genuine pin-removal kill;
the original isolated child GC test remains in complete regression. Actual
physical remote-client, alternate-user and elevated-identity release obligations
remain separately visible even when one source-bound mutant has a valid witness.

Raw CompileError and Ignored definitions receive no kill credit and are outside
the eligible denominator. Receiving recognizes the pinned native compiler's
`Mutant caused compile errors` outcome. It accepts native block redundancy only
when the exact `Removed by block already covered filter` reason, block-removal
operator and a contained eligible definition all agree. Unknown exclusions or
nonviable reasons, runtime errors, pending work and infrastructure/cleanup
failures remain holds. Each source reports its actual generated/eligible/native
counts; a compiler-only or ignored population is never called zero-definition.
This receiving policy does not waive source coverage or other release families.
These native reasons follow the [pinned compiler producer](https://github.com/stryker-mutator/stryker-net/blob/f9109e24c615a7030a3b33e5532c665c974e4ec5/src/Stryker.Core/Stryker.Core/MutationTest/CsharpMutationProcess.cs)
and [block filter](https://github.com/stryker-mutator/stryker-net/blob/f9109e24c615a7030a3b33e5532c665c974e4ec5/src/Stryker.Core/Stryker.Core/MutantFilters/IgnoreBlockMutantFilter.cs).

The pinned native JSON includes exact ignored markers for project files outside
an individual group. Receiving permits only the native `none`/empty-mutants
marker bound to a frozen Core input. Every selected file, including a
zero-definition file, must contain its actual source bytes. Out-of-group markers
receive no scope or kill credit; their count remains in the compact report
manifest. See the [pinned reporter implementation](https://github.com/stryker-mutator/stryker-net/blob/f9109e24c615a7030a3b33e5532c665c974e4ec5/src/Stryker.Core/Stryker.Core/Reporters/Json/JsonReport.cs).

In-process mutation proves shipping methods in the test process. Bounded actual
HTTP and probe child activation controls remain required. The child-control
input has rows with `session`, `kind` (`http-host` or `probe`) and the actual
`activeMutant`, `nativeReport`, `nativeReportSha256`, `definitionIdentity`,
`nativeBinding` (the existing wrapper's `binding.json`) and its
`nativeBindingSha256`.
The exact completed definition must belong to the shipping HTTP/probe body.
Receiving checks that binding's prepared/started/completed invocation chain,
native exit zero, exact session/configuration/report paths and hashes, selected
source list, current candidate source/test/build/configuration inventory and
frozen CLI/Core/host/wrapper pins. A session and raw report from separate
same-source invocations cannot be paired. Receiving then re-runs the maintained
origin/selector/lifetime audit against those sessions and the same candidate
pins. Parser-only controls and
manual source replay cannot substitute. The child wrapper accepts bounded
unique source lists and emits v2 list receipts; single-source v1 semantics remain
unchanged. The separate bounded child control also supports exactly two unique
base files with explicit UTF-16 `{start..end}` selectors. Its v3 receipt retains
both whole-file pins and the exact selection array. Receiving checks the activated
completed definition lies wholly inside its selected span. This control report
contributes no Complete union scope or score credit. See the [three-case child
control recipe](child-controls.md). Parser tests alone prove no actual propagation.

`results.json` records mutation qualification. Product implementation, named
platform execution and six-family/platform/package/API/compatibility release
qualification remain separate. Real Heartbeat/startup integrations remain pending
until the actual providers are available.
