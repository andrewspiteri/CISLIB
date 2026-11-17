---
title: CIS-0008 verification
type: verification-record
status: Draft
change_id: CIS-0008
---

# Verification

## User-authorized merge and dossier closure — 17 November 2026

Andrew Spiteri subsequently explicitly requested merging the Configuration branch
and closing it before the next module. This later instruction authorizes publishing
the reviewed branch to the existing CISLIB GitHub repository, merging into `main`
and deleting the finished feature branch. The earlier implementation-only merge
exclusion remains historical; no package release or next-module implementation is
implied.

Merge preparation verified implementation commit
`e44688456b37590b506f57a65f9cf02ca00a3df2`, all four tasks Complete, no open logical
attempt, active reservation or recovery hold, and fetched remote `main` unchanged
at `4206a870130f577621277847788022e76f1dbe8f`. The user-owned launch settings remain
untracked and unchanged. The documented 1,587-case Windows matrix and normal-build
recovery are retained; the failed 27/35 mutation canary and unexecuted platform,
adapter and full-release obligations remain explicit. Independent read-only
closeout review confirmed the six TRX counters, tested Core/package and supplied
source hashes, settled ownership and absence of generated binaries in the commit.
Metadata-only closure does
not warrant another native run or reset of prior costs/attempts. The GitHub pull
request records the actual merge and branch-deletion outcome.

| Task ID | Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- | --- |
| Intake | Actual checkout | `main` at `4206a870130f577621277847788022e76f1dbe8f`, then local `codex/configuration-retained-pocos` | Inspected | Existing user launch settings remain untracked and untouched. No earlier feature branch was reopened. |
| Intake | Supplied specification identity | `planning/configuration/sources/CIS_Configuration_Module_Specification_v1.0.md` | Preserved byte-for-byte | 51,187 B; SHA-256 `e6a71229914424b5584348c63c21de3616c58858a778ba2a80daf597016cc3c8`. |
| Intake | Pasted user handoff identity | `planning/configuration/sources/CIS_Configuration_Implementation_Handoff_v1.0.md` | Preserved byte-for-byte | SHA-256 `ee0db9df1b2981084c2f84c0002deaa7acb8b4dd1cd67b353753d9d2f2618989`; this supplies the user's request, including its explicit absence of a new budget. |
| Intake | Supported supplied-input workflow | `definition documents --role technical --source ...`, graph, `definition prepare --page technical`, `definition status` | Loaded and prepared; no import errors | Source selected at `docs/imports/technical/cb1e5fd4a71b4bfe92a5994af4050414/`; working Draft source is distinct from accepted product baselines. |
| Intake | Source catalog and reconciled graph | `docs validate --strict`; `graph build --workspace .` | Valid, zero warnings/errors | Catalog/document counts 118 at that check; graph `sha256:fd9b45ba6b94f0a95a15a6ee7a39b6f556af21f591bccdf6e084d61c51000d80`, 20,903 nodes / 79,586 edges. |
| Intake | Existing dependency/contract review | Current Type Manager, Startup, Heartbeat, Healthcheck, Configuration diagnostics and environment sources | No conflicting adopted P/key contract found | Actual Startup/Heartbeat are implemented. Preserve `CoreConfigurationException`, `ConfigurationIssue`, `ConfigurationIssueCodes`; explicit host identity must not be redirected by an environment display-name override. |
| Intake | Necessary lifetime integration | Actual Heartbeat and Startup hosting source, independent review | Narrow concrete closure proposed | Heartbeat shutdown does not automatically cancel execution tokens; Startup.Hosting must cancel owned Configuration provider work before HB drain, then join actual Configuration/startup/HB work. No second coordinator, scheduler or generic closing registry. |
| Intake | Bounded impact analysis | `impact analyse CIS-0008 --root <source-document> --root <StartupHostedService> --depth 2 --limit 100` | Two proposed findings; not truncated | `IMPACT-BA302F90DA` supplied documentation and `IMPACT-7E21858304` concrete hosting integration; pending the one actual outcome decision. |
| Intake | Draft CIS plan | `plan build CIS-0008`; `plan validate CIS-0008` | Draft validation valid with two expected warnings | Generated seed still contains placeholders and has no outcome authorization. It is not executable. Four concrete proposed contracts are retained separately for supported update/add after the single authorization. |
| Intake | Actual execution eligibility | `agent work status CIS-0008 --task WORK-001` | `missing-allocation`, exit 4 | No Configuration owner/allocation exists. Earlier Startup authority excludes Configuration and has used all four attempts; other grants remain scoped to completed work. |
| Intake | Pending contract/plan bundle | `planning/configuration/{README,acceptance,implementation-plan}.md`; proposal JSON inputs | Prepared, not approved | U01–U12 and CFG-A01–CFG-A28 mapped. P01–P08 remain one explicit recommended selection. Four proposed medium owners include their tests/docs/review; W2/W3 can run in parallel after W1. |
| Intake | Proposed limits | `authorization-proposal.json` | Unapplied | One six-hour / eight-attempt pool, existing physical six-GiB ceiling, included 600-second / 64-MiB reserve; all workers/tests/docs share it. No production store/default polling interval is selected. |
| Intake | Counted storage sample | PowerShell file-length measurement, 2026-11-16 22:49:22 UTC | 5,401,958,937 B | `.cis/local` 762,613,650 B; `implementations/csharp` 4,459,986,926 B; all `.artifacts` 179,358,361 B. Leaves 973,383,143 B below six GiB after the existing 64-MiB reserve. Sampled directory observation, not a peak/deduplication claim or new pool. |

## Reconciliation and scope

The supplied specification restores the conversation-derived Configuration
requirements. Its references to unavailable v1.1/alignment files are provenance,
not a reason to keep calling the complete source missing. Earlier sources and
completed work remain intact. The accepted Type Manager owns exact runtime
identity and catalog membership; Configuration owns eligibility and durable keys.
No historical persisted-key scheme or adopted P01–P08 decision was found.

Current Startup and Heartbeat code supersede older availability qualifications in
the attachment. Initial values must be prepared before consumer construction to
meet the proposed P01 guarantee. The proposed W4 hosting connection addresses the
actual stopping order without changing Heartbeat execution-token semantics or
moving canceled-wait work outside its counted callback/drain.

The supplied-input coordinator also reported Draft-source/full-product-wizard
review/adoption warnings and existing SDK/native-binding classification limits.
Those are not fresh product failures or authority. No replacement product intent,
new full questionnaire, runner or toolchain qualification campaign was adopted.
Thirty-one unrelated generated starter documents and three tracked template
refreshes from that preparation were removed/restored to their clean prior state;
accepted product documents and historical records were preserved. Only the
material source selection, source copies, catalog and Configuration planning
records remain changed.

## Current boundary

This checkpoint contains **no Configuration implementation, product build/test,
mutation, package or platform pass**. Current planning/review evidence is not
release qualification. The production backend remains host-supplied and unselected;
future source/package/provider claims must name the actual adapter, scheduler,
platform and candidate bytes. No earlier native receipt is claimed as Configuration
behavioral evidence.

The remaining human decision is one concrete bundle: select the recommended
P01–P08 profile and authorize the outcome, delegated task distribution and shared
finite limits. Applying that decision will use the supported CIS authorization,
task and allocation operations. It will not reset prior consumption/attempts,
create separate worker budgets or add a preliminary approval chain.

## Authorized implementation continuation — 17 November 2026

Andrew Spiteri answered **Approve proposed contracts and allocation** to the exact
P01–P08/four-task/six-hour/eight-attempt/existing-six-GiB question, including the
600-second/64-MiB reserve and delegated distribution. `change authorize` recorded
`CISLIB-CONFIGURATION-2026-11-17:USER-APPROVED-PROFILE-ALLOCATION`, authority
`cislib:CIS-0008:outcome:cb8dfe5d5f71451f94bc5d7743cfcb5e`, digest
`sha256:943331c3362cfe00756996061d876e26d1b4e365a790e381a9b27e66e5814078`.
The controller updated the generated seed and added three concrete dependent
owners through supported operations; the plan is Approved and validates.

`agent work allocate` applied the single shared 21,600-second/eight-attempt pool
and the four declared distributions, preserving all earlier owners and the same
physical storage group. Initial consumption/reservations/used attempts were zero.
W1 is InProgress; W2/W3 remain dependent on its shared contract and W4 on both.

The first task-admission check exposed that the input loader had selected the
module Draft as the entire product technical intent. Supported `definition
documents --role technical --path docs/specs/technical-intent-spec.md` restored the
accepted canonical product selection while retaining the imported module source.
Task admission then succeeded. No approval hash, accepted product prose or
underlying Startup/Heartbeat contract was rewritten to clear that check.

An attempted addition of an existing fixture path to the started W1 contract was
rejected by CIS, preserving its original executable contract. The unapplied input
was restored. W1 uses its declared source/Unit scope; broader real consumer/fixture
integration remains W4's work. No extra approval or test-only task was created.

## Foundation build correction — 17 November 2026

W1 now contains the marker/store/options/read/event contracts and the retained
catalog, passive shape/default capture and stable storage identity. An independent
source review found no required foundation defect or extra storage, scheduling,
provider construction or DI container responsibility. Shared seams are internal;
the existing `CIS.Configuration` diagnostic contracts remain intact.

The first measured `configuration-objects-unit` invocation compiled Core and test
support, then stopped at Unit compilation before producing a TRX. The new internal
`PreparedConfiguration` collided with the existing historical fixture type of the
same name through friend-assembly access. Renaming only the new internal type to
`ConfigurationPreparedValues` preserves the historical fixtures and consumers.
A second independent scan found no other exact new/old declaration collision.

That invocation settled 264,632 inclusive milliseconds against W1's first logical
attempt, with no remaining reservation. Its source-review/build evidence does not
establish a test pass. The correction and the focused Unit/Architecture continuation
remain in the same open logical attempt and shared approved pool.

## Foundation accepted checkpoint — 17 November 2026

After the internal-name correction, Core, test support and Unit compiled. The
`configuration-objects-unit` native profile passed **619 cases, zero failures or
skips**, including all **42** new catalog/event/reader cases. The retained
`configuration-discovery-architecture` profile passed **five cases, zero failures
or skips**: the compiled Configuration boundary and four violating negative
controls. Both receipts are settled, scope-complete Unit-layer results with zero
pending batches. Neither is coverage, mutation, platform or release qualification.

The frozen W1 seams include `ConfigurationCatalog`, `ConfigurationEntry`,
`ConfigurationShape`, `IConfigurationValuePipeline`, `ConfigurationPreparedValues`
and `ConfigurationAssignment`. The public surface supplies the marker/event,
helper/optional base, synchronized reader, explicit options and host-owned store
contracts. No production adapter, mapper, scheduler or additional provider is
hidden in those contracts. Initial construction/default capture, exact retained
ownership, versionless keys, registration conflicts and read/event behavior are
covered. The actual Type Manager query test selects a valid candidate from an
assembly containing negative test fixtures; full unfiltered module/bootstrap/host
evidence remains W4.

W1's three native invocations settled **795,421 inclusive milliseconds** in its
first logical attempt, without an active reservation or recovery hold. The failed
compile remains part of that cost/history. A physical sample of `.cis/local`,
`implementations/csharp` and `.artifacts` totals **5,417,864,203 bytes**; the same
6-GiB ceiling leaves **957,477,877 bytes** after the 64-MiB restoration reserve.
This is a sample, not a peak claim. The unrelated user-owned CountryList
`launchSettings.json` remains 284 bytes with its original SHA-256
`1d65b76547423e534c95085865c646667153c7313fbe3f2dc910ca73a8d89c71`.

W1 source review and these actual results permit its completion and independent
W2/W3 implementation under the existing authorization. They do not complete the
Configuration module or authorize publication/merge.

### Integration follow-up found during mapping review

Reviewing W2 found a small default-shape edge in the frozen Objects code:
serialization-override validation examines `Nullable<T>` before unwrapping its
enum type. An enum-level converter could therefore reach default capture through
a nullable property. W4's declared Configuration-wide scope will reject that
underlying override and add a focused regression before final verification.
The completed W1 owner/history and started W2 executable path contract are not
rewritten to conceal the follow-up. This remains an open implementation defect,
not an accepted converter extension or a reason to add a separate tiny task.

## Mapping and refresh native checkpoint — 17 November 2026

The frozen W2 value pipeline passed **68 native Unit cases**, with zero failures
or skips, through `configuration-mapping-unit`. Core, test support and Unit
compiled. Literal expected values cover the four record-presence combinations,
explicit false/zero/empty/null, nested overlays, collection replacement/removal,
captured defaults, current masked common composition and a separately computed
typed UTF-8 SHA-256 vector. Date/enum/number/culture rules, unknown-subtree
duplicates, candidate/root/shared-child ownership, pure validation, the real
reader/write gate, cancellation admission and terminal live-setter poison are
covered. Full source and separate test/API reviews found no remaining required
W2 defect after the duplicate-subtree correction. W2 does not publish events or
claim source-cache/production-provider behavior.

The frozen W3 coordinator passed **27 native Unit cases**, with zero failures
or skips, through `configuration-refresh-unit`. Its controlled store and value
seam independently exercise source tokens versus effective state, failed reads
versus deletion, latest masked common retention, invalid-payload-plus-Unchanged,
serialized trigger handoffs, watch activation/reconnect gaps and initial/unchanged
event suppression. Tests hold real provider, synchronous-handler, subscription
disposal and cancellation-callback work to prove actual drains. A committed
notification completes after stop; handler failures get a safe separate code
without changing accepted-data validity. Stop detaches an acquired or late watch
before joining held activation/read work, and cleanup faults cannot skip drains.

Independent lifecycle review corrected final initial-load publication to share
the stopping gate. Assertions about intentionally isolated event callbacks are
observed and checked outside those callbacks. No production timer, store, extra
DI provider or framework is introduced by W2/W3. Their exact receipts/TRX files
are retained under their respective `.cis/local/testing/incremental` profiles.
Unit-layer scope completion is not coverage/mutation, platform, package or release
qualification. W4 must compose the real modules and close the recorded Objects
converter edge before the module is complete.

Before execution, CIS rejected duplicate source/test/config declarations in the
new profiles. Removing those duplicate declarations corrected only invocation
metadata; no product source or expected result was changed to obtain the passes.
That failed admission remains in W2's first logical attempt and measured cost.

Both W2/W3 receipts are now settled and scope-complete with no pending batches,
reservation or recovery hold. W2 consumed **337,062 inclusive native milliseconds**
in its first logical attempt (including rejected profile admission); W3 consumed
**344,383 milliseconds** in its first logical attempt. The shared change has
**1,476,866 measured native milliseconds** consumed, three attempts used and five
remaining, with **19,523,134 milliseconds** available after the unchanged
600,000-millisecond restoration reserve. Authoring time outside measured native
operations is not retrospectively invented as metered evidence.

The post-W2/W3 physical sample at `2026-11-17T01:13:37.9063028Z` is
5,424,813,713 bytes (`.cis/local` 780,756,225; C# tree 4,464,687,930;
`.artifacts` 179,369,558). It leaves 950,528,367 bytes after the unchanged
64-MiB reserve under the original shared 6-GiB ceiling. No historical evidence,
completed prior owner or user file was removed to create capacity.

## Composed hosting and source checkpoint — 17 November 2026

W4 adds `ConfigurationRuntime.PrepareAsync`, `AddCisConfiguration`, compact
public status and bounded bootstrap diagnostics. Preparation completes before the
single root Build; the first Configuration `IStartable` directly establishes
observation/reconciles before dependent start methods. Explicit polling uses the
actual retained Heartbeat owner/subscriber. Health criticality is mandatory when
Healthcheck is connected. Instance POCO descriptors and the runtime's shared
async disposal preserve borrowed data/store ownership and Build-failure cleanup.

`Startup.Hosting` connects optional Configuration cutoff before Heartbeat and
joins Configuration/core/HB actual work with `Task.WhenAll`. The original
host-start token also closes module admission while a later initializer ignores
cancellation. The coordinator remains independent; its existing compiled
architecture boundary now checks Configuration as well as Heartbeat. Independent
lifetime/source review found one stop-versus-module-admission exception race,
corrected by guarded neutral cancellation classification.

The new real host tests query the complete actual Type Manager result over two
valid marker types in the Integration assembly, without filtering or rescanning.
The first executable run passed all **40 host/lifecycle cases**, including the
15 new Configuration cases and affected existing Startup/Heartbeat cases. They
prove constructor-ready data, retained scoped roots, HTTP readiness/business
gating, real watch/health recovery, actual Heartbeat polling, first-token cutoff,
late watch acquisition and held provider/notification/HB/error-handler drains.
Two separate source-process cases failed before application execution on their
runtime framework selection; they are not counted as passes at that checkpoint.

All maintained source consumers built without warnings/errors. The focused
Configuration selection passed **729 cases**, including 15 added hosting/default
converter boundary cases. The usable attributed-enum negative control proves the
recorded nullable-enum Objects defect is closed before converter invocation.
One initial setup receipt correctly refused reuse because normal restore added
the planned Unused-to-Core edge to the three example lock files. Those 22 inserted
lines contain only the existing approved Logging/DI 10.0.12 closure.

The two source examples now explicitly use the adopted runtime 10.0.12 and
disabled roll-forward. SDK incremental runtimeconfig generation did not track a
RuntimeFrameworkVersion-only edit; a real project Rebuild regenerated and verified
both .NET/ASP.NET framework entries at 10.0.12. No generated runtime JSON was patched
and no assertion was weakened. Two nullable diagnostic-code checks in the new
health tests were corrected before execution. One retry also reported native
capture streams remaining open after exit; its failed observation/cost is retained.

The supported delegated redistribution is recorded in `work-redistribution.json`:
W1/W2/W3/W4 now have 1,800/3,600/3,600/12,600 seconds respectively. Their actual
consumption/history and attempts were preserved. The shared total remains 21,600
seconds/eight attempts with the same 600-second and 64-MiB restoration reserves
and physical six-GiB ceiling. W4 remains in its first logical attempt; four of the
eight shared attempts have been used. Native final verification is still in
progress at this checkpoint.

## Final Windows native matrix

The final composed Windows x64/.NET 10.0.12 profiles settled with **1,587 cases
passed, zero failed and zero skipped**. These are selected cases in distinct
final suites, not a sum of repeated development runs:

| Profile | Actual native project/scope | Passed |
| --- | --- | ---: |
| `configuration-unit-final` | Affected Configuration/Startup/Heartbeat/environment Unit cases | 1,003 |
| `configuration-host-integration` | Actual Type Manager/DI/Kestrel/Startup/HB/HC; held lifetime races; real Console/Web source processes | 42 |
| `configuration-architecture` | Existing Architecture project including discovery/coordinator boundaries | 347 |
| `configuration-compatibility` | Existing retained source/binary/API compatibility project | 179 |
| `configuration-business-example` | Maintained startup CountryList, Type Manager and Heartbeat business stories | 12 |
| `configuration-package-windows` | Existing isolated SDK/package Console/Web and negative/ordinary controls | 4 |

Each profile retains its native TRX at
`.cis/local/testing/incremental/<profile>/reports/file-e7e4df149e0949e4a8a2-unit.xml`
and its settled receipt. The later diagnostic-assertion change requires fresh
Unit/host input checks; retained passes are not automatically current reuse.
The architecture retry preserved the original
ambient-state ban and forbidden-call detector. Only the three explicitly owned
runtime roots and their generated work are separated from pure validation/getters;
legacy environment code plus new contracts/Objects/Mapping remain checked.
Explicit positive inclusions and a never-invoked UnsafeQueueUserWorkItem negative
control protect that boundary. The initial 346-pass/one-failure observation and
its cost remain history; production namespaces/dispatch behavior were not moved
to evade the guard.

The maintained source programs execute their real inventories and verify literal
constructor values, initially-unused direct/inherited classes, same roots across
scopes and watch reloads. CountryList additionally proves a masked common update
does not notify, override removal reveals the latest common value exactly once,
the same subscriptions/root survive and normal host shutdown detaches the watch.

The actual package fixture ran 23 native commands against one candidate using an
isolated first SDK restore/feed/cache, Console/Web build and normal/no-build folder
publication, missing-selected-assembly failure and ordinary PackageReference
control. Child Unused uses the same exact normal Core package; the genuine build
SDK/inventory is retained. No production backend is implied by the literal demo
or controlled providers.

Retained candidate: `.artifacts/configuration/CIS-0008/candidate/CIS.Core.0.1.0-dev.1.nupkg`,
312,623 bytes, SHA-256
`c764b5e5b76ec7f3efc6eeb97c4979faf04f43d5e431675931266a3fa102de41`.
Its actual Core SHA-256 is
`4afa351c81e5de890160fce48ef32b174a8b2953485be5d8e83c45779ac8c8a2`;
the build helper is
`0851964a1e15779022ab7982f21aca37d28e8f5272dee49756e15b482af69c64`.
The fixture verifies the child Core bytes equal the selected package and checks
both .NET/ASP.NET framework entries. Current developer source and exact native
input/command/tool identities remain attached to the receipts.

These Windows observations do not claim a Configuration Ubuntu execution,
production adapter qualification, performance/stress, complete coverage, full
mutation or published-release readiness. The unchanged 95% line/90% branch and
90% mutation release obligations remain separate. The bounded prepared-commit
mutation canary and normal postflight are recorded separately below.

## Bounded mutation observation and ordinary-build recovery

The existing Stryker 4.16.0/MSTest canary selected the prepared-commit `Apply`
method in `ConfigurationValuePipeline.cs` and 68 mapping/commit cases. It retained
**27 killed, eight survived, zero timed out and zero runtime errors** among 35
tested mutants: **77.14%, failing the unchanged 90% threshold**. The raw report is
`.cis/local/testing/mutation/configuration-commit-canary/runs/canary-20261117-034637-faee1b04a7c34d019568e1321120827a/native-result.json`
(SHA-256 `709ce980bae9111256f726fbda7eef6a2f5ae06d3755d1ab68e6825040a9f3bc`).
The adjacent summary, assessment and native streams preserve compiler rollbacks,
ignored/filtered mutants, exact scope and the failed configured threshold. None
receives kill credit or establishes full-source mutation qualification.

Independent survivor review retained all eight in the denominator. Three remove
guards for malformed internal calls; one changes an internal synthetic cancellation
token, whereas supported refresh neutrality uses the actual stopping state. Four
empty real failure explanations. Existing issue-helper assertions now require
nonblank source context and guidance in the real failure tests, as required by
CFG-A26/P04; no exact wording, synthetic-token or unsupported-input tests were
added to raise a score. Production behavior did not change. **No second mutation
run or improved score is claimed for the assertion change.**

The canary's configured normal postflight failed before compilation with MSB1008:
`dotnet test` interpreted `-t:Rebuild` as its test-listing option and treated
`Rebuild` as another project. Its failed native invocation and cost remain in the
same summary. Ordinary output must be rebuilt through the existing source-example
bootstrap path, where `dotnet build -t:Rebuild` is actual build syntax, followed by
the selected normal Unit and host checks. This is recovery and current-input
verification within WORK-004's existing attempt, not a release qualification or a
new mutation campaign.

The corrected `configuration-bootstrap-source` invocation subsequently settled
successfully: ordinary example restore/build and **729 Configuration cases passed,
zero failed/skipped**, including the stronger diagnostic assertions. Normal
`CIS.Core.dll` again hashes to
`4afa351c81e5de890160fce48ef32b174a8b2953485be5d8e83c45779ac8c8a2`,
the exact retained package Core bytes. Its settled receipt reports zero pending
unit batches and no reservations. This is actual normal-build recovery; it does
not erase the failed canary or its original postflight. Repeated recovery cases
are not added to the distinct 1,587-case matrix.

The subsequent final affected Unit invocation also settled successfully with
**1,003 passed, zero failed/skipped**, zero pending unit batches and current unit
inputs at admission. The latest source graph was
`sha256:e1de17cd576293665a871ce5385a0b27c2e035b4431a60ddb64d8a3164368437`
(22,249 nodes / 83,849 edges, zero warnings/errors). A fresh host integration
invocation also settled with **42 passed, zero failed/skipped**, zero pending unit
batches and current unit inputs at admission. The four other suites retain their genuine native
passes against the unchanged production/package bytes; their caches are not
asserted current after the helper-only test edit. Read-only verification attempts
while recovery owned the native state returned IOException and supplied no reuse
claim. No completed suite is rerun merely to clear administrative currentness.

At 2026-11-17T04:01:56Z, the same three physical counted roots totalled
5,483,195,887 bytes, leaving 892,146,193 bytes after the unchanged 64-MiB restoration
reserve beneath six GiB. No older owner's evidence or user files were removed.

## Implementation closeout

P01–P08 and U01–U12/CFG-A01–CFG-A28 are implemented at the boundaries mapped in
`planning/configuration/acceptance.md`. The new public Configuration contracts,
runtime and hosting extension are additions in the existing Core assembly. The
existing diagnostics, EnvironmentOptions, discovery/build machinery and approved
dependency closure are preserved. Runtime startup remains sequential; independent
mapping/refresh authoring and source review did not parallelize startup.

The genuine Windows matrix, corrected ordinary recovery and independent reviews
complete the bounded implementation evidence. The canary's failed 90% threshold,
unexecuted Ubuntu/full coverage/full-source mutation/performance/stress and
unselected production backend remain explicit qualification gaps. No release,
default-branch merge or external publication follows from this closeout.

Final measured native accounting is **7,532,898 inclusive milliseconds** for the
change, including **6,056,032** for WORK-004, with four of eight shared attempts
used. There are zero active reservations and no recovery hold. The unchanged
21,600,000-ms grant retains its 600,000-ms restoration reserve and 13,467,102 ms
available after that reserve. These are CIS's measured preparation/native/result
admission costs; unmetered authoring/review is not fabricated as a measured total.

At 2026-11-17T04:25:05Z the same physical roots measured `.cis/local` 802,336,758 B,
`implementations/csharp` 4,469,976,015 B and all `.artifacts` 212,314,244 B:
**5,484,627,017 B**, leaving **890,715,063 B** beneath six GiB after the included
64-MiB restoration reserve. This is a sampled file-length observation, not a peak
claim. No historical/source/user cleanup was performed. The raw source/handoff
hashes remain as recorded above; the user launch-settings SHA-256 remains
`1d65b76547423e534c95085865c646667153c7313fbe3f2dc910ca73a8d89c71`.

Strict documentation validation passed with 131 catalog entries/documents and
zero warnings/errors. The native results qualify only their declared scope, not
release readiness or saved-cache currency after administrative closeout edits.
| WORK-001 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=902; failed=131; possibleTokenSavings=123; ledgerDigest=sha256:048cb6234fb06b652746ba2c63346201ce4bc2ca450cc14d0a13c266da39c8a1 |
| WORK-002 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=921; failed=132; possibleTokenSavings=123; ledgerDigest=sha256:2572d60527687ce87ebf6c27b06dbb46ae0e2288613a8b058d22e22fbb6e43b8 |
| WORK-003 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=922; failed=132; possibleTokenSavings=123; ledgerDigest=sha256:1c6857db342fe932182d0897894b43631b032b2f70ee5710226f02f7aafbc14a |
| WORK-004 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=983; failed=146; possibleTokenSavings=123; ledgerDigest=sha256:3627c96a14f94a79f4490ed4c9c35cb019c00c7dcab3639b0c6f0964e132acc1 |
