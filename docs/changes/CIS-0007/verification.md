---
title: CIS-0007 verification
type: verification-record
status: Draft
change_id: CIS-0007
---

# Verification

| Task ID | Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- | --- |
| Planning | CIS supplied-document import | `definition documents --role technical --source ...`, graph, `definition prepare --page technical`, `definition status` | Loaded/prepared; no import errors | Draft module source selected. Incidental templates/questionnaire refresh removed; existing accepted product intent preserved. |
| Planning | Original source identity | `planning/startup/sources/CISLIB_Startup_and_DI_Specification_v1_0.md` | SHA-256 `a0812a281c6696f16ccbe1d0d006e62988eae9982e581add51bb66f74ab16451`; 28,663 bytes | CIS wrapper adds metadata; original is separately byte-preserved. |
| Planning | Reconciled graph before dossier creation | `graph build --workspace . --format agent` | Built; 0 warnings, 0 errors | Build `sha256:c1a103eafad11d80982e81d5c93ca8f2060095e676832dcec19808d98be85c28`. |
| Planning | Draft dossier and seed | `change create ... CIS-0007`; `plan build CIS-0007` | Proposed/Draft; one unresolved seed | Expected warnings: concrete seed update and actual outcome authority still required. Detailed two-task input proposal prepared; no ready/complete task claimed. |
| Planning | Counted storage observation | PowerShell file-length measurement, 2026-11-16 16:04:54 UTC | `.cis/local` 740,682,744 B; `implementations/csharp` 4,461,128,695 B; all `.artifacts` 179,345,482 B; total 5,381,156,921 B | Conservative directory observation, including older artifacts; not an allocation/reservation or zero-ownership claim. Re-measure canonical shared group before execution. |
| Planning | Bounded existing-contract impact discovery | `impact analyse CIS-0007 --depth 2 --limit 100 --format agent` | 2 proposed findings, no truncation | `IMPACT-74A6664235` HealthcheckService; `IMPACT-248CA586EE` Heartbeat hosting. Both match the proposed outcome scope; human disposition is pending with the outcome decision. |
| Planning | Draft validation and final planning graph | `plan validate CIS-0007`; `graph build --workspace . --format agent` | Draft validation valid with 2 expected warnings; graph 0 warnings/errors | Warnings retain the unresolved generated seed and missing outcome authority; no executable-task claim. |
| Planning | Focused document/JSON checks | JSON parse, local-link resolution in 5 planning records, source hash, `git diff --check` | Passed | No runtime source change or native behavioral evidence implied. |
| Planning | Independent read-only design/authority review | Existing scheduler, Healthcheck and acceptance review agents | No responsibility/framework/tooling expansion found | Incorporated explicit ordered host options/unknown-factory rejection and failed/stopped activation acknowledgement evidence. Reviews are planning observations, not human approval or native test results. |
| Planning | Actual execution authority | `agent work status CIS-0007 --task WORK-001 --format agent` | `missing-allocation`; 0 allocated time/attempts | Accounting revision at observation `8351035b241f4939ace19b3cc8d71d81:508`. Proposed limits remain unapplied; re-read before actual allocation. |

At the initial planning checkpoint above, no Startup implementation, native
build/test or passing acceptance result had been recorded and the proposed
time/attempt/storage limits were unapplied. The older
completed Heartbeat evidence remains historical and does not establish Startup
behavior or grant new execution capacity.

## Authorized implementation continuation

Andrew Spiteri replied **Approve proposed allocation** to the prepared exact
CIS-0007 allocation question. `change authorize` recorded decision
`CISLIB-STARTUP-2026-11-16:USER-APPROVED-ALLOCATION`, authorization
`cislib:CIS-0007:outcome:c997f879fbcb418ca460744ba3ea25f0`.
The two concrete task contracts validate under that outcome. Work allocation
revision `8351035b241f4939ace19b3cc8d71d81:509` records 10,800 active seconds and
4 logical attempts, including 300 restoration seconds, in the existing shared
storage group. WORK-001 has 4,200 seconds/2 attempts/100 reserve seconds;
WORK-002 has 6,600 seconds/2 attempts/200 reserve seconds. All original costs,
owners, reserves and history remain intact.

The first task transition identified that the supplied-document import selected
the module attachment as the global product technical intent. That role requires
the already approved whole-product document. Restored only the selection pointer
to `docs/specs/technical-intent-spec.md`; retained the imported module source,
byte-preserved original, catalog identity and CIS-0007 requirements. No product
intent approval was fabricated and no tooling change was introduced.

### Core first native build

`test incremental run --profile .cis/local/testing/profiles/startup-core-unit.json
--layer unit --max-batches 1 --max-seconds 900 --change CIS-0007 --task WORK-001
--authority C:/miscwork/portfolio/cislib --actor Codex` admitted execution
`native/incremental/fd06a972422b4e12a669da5edcbe61c1`, logical attempt `WORK-001:1`.
Core compiled, but the test project failed on three `MSTEST0017` assertion argument
ordering diagnostics. No TRX or passed test count is claimed. The actual native
child exited 1 and the invocation exited 4, settled with zero reservation/recovery
hold and **338,015 ms** inclusive charged work. Retained bounded stdout:
`.cis/local/testing/startup/core-first-build-stdout.log` (1,792 bytes).

Corrected only those three assertion argument orders, then launched the same
focused unit-only profile under the existing owner/remaining allocation. The
failed build, original cost and logical attempt remain recorded. This is a changed
test input, not a rerun to repair bookkeeping. Unit-only execution does not broaden
into the reported mutation-pilot path; no mutation work is requested or claimed.

The immediate corrected-input retry `native/incremental/89ad35c25a724fdf9ff9199ea06f17bc`
held before any native batch because the changed test inputs required a fresh
graph. It settled with no recovery/reservation and **46,928 ms** additional
inclusive charge (**384,943 ms** core total), still in `WORK-001:1`. No test result
is inferred. Refreshing the existing graph and rerunning the same focused profile
is the bounded correction; no CIS implementation/tooling change is undertaken.

### Standard DI validation boundary

The subsequent real native execution
`native/incremental/646d882b4e4e41eaa27d1871ab5b64f3` ran **57 cases: 56 passed,
1 failed, 0 skipped**. The failing fixture deliberately disabled build validation
and mixed a first supplied `IStartable` instance with a final singleton constructor
capturing a scoped dependency. Standard Microsoft DI did not reject that graph.
The native child exited 1; the invocation exited 4 and settled without recovery
or reservation. Inclusive core consumption reached **620,526 ms** in the same
logical attempt. The bounded stdout and original failing TRX are retained under
`.cis/local/testing/startup/core-scope-validation-*`.

Two independent source reviews found the same pinned .NET 10.0.12 boundary:
supplied-instance `ConstantCallSite` uses `ResultCache.None(serviceType)` with
slot 0, colliding with the final constructor descriptor's validation key. The
validator caches an earlier no-scoped-dependency result for that key. Merely
enabling build validation cannot be claimed to fix this particular mixed shape.
See the primary [result cache](https://github.com/dotnet/runtime/blob/v10.0.12/src/libraries/Microsoft.Extensions.DependencyInjection/src/ServiceLookup/ResultCache.cs),
[constant call site](https://github.com/dotnet/runtime/blob/v10.0.12/src/libraries/Microsoft.Extensions.DependencyInjection/src/ServiceLookup/ConstantCallSite.cs)
and [scope validator](https://github.com/dotnet/runtime/blob/v10.0.12/src/libraries/Microsoft.Extensions.DependencyInjection/src/ServiceLookup/CallSiteValidator.cs).
This source explanation is an assessment consistent with the actual native
observation, not a claim of universal framework validation.

The source specification explicitly delegates scoped-capture checking to standard
DI and excludes a custom graph validator. The meaningful standard-validation test
now uses a normal factory descriptor for the first participant and asserts the
actual root Build with both validation options enabled rejects the scoped capture
before any startup call. Supplied-instance identity/disposal tests remain intact.
The original mixed-shape limitation remains documented in the usage guide; it is
not reclassified as passing or repaired by a dependency upgrade, registration
rewrite, extra provider, participant re-resolution or custom DI machinery.

### Passing core checkpoint

Execution `native/incremental/fc42f87a75eb432ea2e81b4c9eb24765` completed with
exit 0, scope complete and **57 passed / 0 failed / 0 skipped**. The actual TRX
is `.cis/local/testing/incremental/startup-core-unit/reports/file-e7e4df149e0949e4a8a2-unit.xml`.
The focused native command built Core and UnitTests with warnings as errors.
No mutation or release qualification is inferred (`qualified=false`).

Core DLL SHA-256 at this checkpoint:
`2d3b85edbc17be8ffe5936f568aaa57726e2f0bdad7db47704e8df524182500a`.
Inclusive measured native work across all four core invocations is **986,537 ms**
(16 min 26.537 s), **1 logical attempt**, zero reservations/recovery holds;
the successful invocation reports **43,993,016 counted retained bytes**.
This byte observation is not a new-growth or shared-group peak claim. The
original failures and costs remain intact. The controller recorded the passing
checkpoint and closed the first logical attempt; native tests are not rerun for
these accounting/documentation edits.

Independent read-only reviews found no blocking module defects or responsibility
growth in the retained-operation, full construction, sequential invocation,
identity, original-fault/neutral-cancellation, final HB acknowledgement or actual
settlement behavior. The revised standard-DI test and its explicit framework
limitation received an additional review. Host/probe/example acceptance remains
owned by the separate dependent integration task.

### Passing host integration checkpoint

`startup-host-integration` completed under WORK-002 with exit 0, unit scope complete
and **25 passed / 0 failed / 0 skipped**: 18 new Startup integration cases and
7 affected existing Heartbeat hosting cases. Execution
`native/incremental/d3e4d610a8ca430ebfaa622f99105959`, logical attempt `WORK-002:1`.
Real Kestrel serves readiness 500 and gated business 503 while initialization is
held; actual pipe/probe exit 1 transitions to 0 after success. Tests also cover
readiness 200 without awaiting the first callback, critical/noncritical recovery,
failure/expected cancellation, stopped activation, supported composition and
actual startup/callback/error-handler drains before dependency teardown.

WORK-002 inclusive measured native charge is **338,194 ms** at this checkpoint;
combined measured change charge is **1,324,731 ms**, 2 used logical attempts,
zero reservations/recovery holds. The invocation reports **44,652,685 counted
retained bytes**, not a new-growth/peak claim. Broader release/mutation qualification
is not inferred. Example/business and affected regression checks remain.

Independent review found one static example-copy layout mismatch before example
execution: Type Manager enables current RID output, while the existing-style
business copy target expected a flat framework directory. The new example now
explicitly uses `AppendRuntimeIdentifierToOutputPath=false`, retaining its actual
RID and generated inventory while keeping all native output together for the
source/business invocation. This changes no shared build machinery or dependency.

### Passing runnable consumer and Type Manager stories

`startup-business-example` completed with exit 0, unit scope complete and
**11 passed / 0 failed / 0 skipped**: the actual new country-list source consumer
plus ten existing Type Manager preparation/failure stories. Execution
`native/incremental/36fa72dd435a4581b61292b9cad91c20`, same `WORK-002:1`.
The normal native `dotnet test` command restored/built the example and business
project, generated its actual win-x64 type inventory, copied the real files, and
executed the example's bounded self-test through the existing ProcessRunner.
The story asserts the emitted 500/503/zero-side-effect observation, subsequent
200/data/reader/prepared-instance observation while first refresh remains held,
actual refresh completion and completed stop/disposal. Existing negative Type
Manager stories prove failed preparation never reaches the application factory.

WORK-002 inclusive native charge reached **726,227 ms**; whole change
**1,712,764 ms**, two logical attempts used, no reservations/recovery holds. The
business invocation reports **45,294,263 counted retained bytes**. Candidate Core
DLL SHA-256 after host integration is
`3ec9a27ba89bc22b8353fe048eedb91419da4e7dc6c52764bc29b89685a9cf90`.
The new example lockfile contains only its Core project reference and the actual
native RID selection; no approved runtime dependency was added or upgraded.

### Final affected native selections

All use the existing native projects and pinned toolchain, `--layer unit`, one
bounded source-project batch and ordinary performance/stress exclusions. The
actual TRX in each profile is `reports/file-e7e4df149e0949e4a8a2-unit.xml` beneath
`.cis/local/testing/incremental/<profile>/`.

| Profile | Actual passed / failed / skipped | Execution (`native/incremental/` prefix) | Inclusive native charge |
| --- | --- | --- | ---: |
| `startup-unit-regression` | 176 / 0 / 0 | `3b9013990aa84d098178980997382f13` | 238,931 ms |
| `startup-architecture` | 334 / 0 / 0 | `802b5af514e74326a8fc290bb9ad10e3` | 349,118 ms |
| `startup-compatibility` | 179 / 0 / 0 | `b434ce480ca141bbaa340b83de023a41` | 399,635 ms |

Each invocation completed exit 0, scope complete, zero pending unit batches,
zero mutation batches, no active reservation/recovery requirement and
`qualified=false`. The final unit selection covers all 57 cases in the initial
core/metadata selection plus 119 Heartbeat cases. Architecture covers the current
Core surface, assembly/configuration/dependency boundaries; complete compatibility
includes historical source/frozen consumers, public API and the retained negative
dependency controls. No frozen input or dependency policy was changed.

WORK-002's inclusive measured native total is **1,713,911 ms**. Including the
complete core history, the change total is **2,700,448 ms** (45 min 0.448 s),
**2 of 4 shared logical attempts used**, zero reserved work/recovery holds.
The 300 restoration seconds and 64 MiB storage reserve remain protected within
the unchanged three-hour/six-GiB outer limits. These are native-accounting facts;
they do not claim measurement of independent authoring/review wall time.

The final Core SHA-256 remains
`3ec9a27ba89bc22b8353fe048eedb91419da4e7dc6c52764bc29b89685a9cf90`.
The final source example and its copied business-case binaries/inventory were
independently checked for identity. Read-only reviewers also checked retained
receipt/TRX counts, current recorded file hashes, package/toolchain/frozen-input
diffs, all fifteen acceptance mappings, local links and the original attachment
hash. No unresolved module defect or responsibility/verification-framework
expansion was found. The standard-DI mixed-instance limitation above remains
explicit and is not presented as fixed.

Documentation/accounting closeout changes do not launch another native test run.
Actual Windows checks establish this development scope; other platforms and
coverage/mutation/release qualification are not inferred.

### Final local checks and counted storage

Focused JSON parsing, local links across eleven change/module/planning documents,
`git diff --check`, raw attachment identity and final Core DLL identity passed.
The source-local `.gitattributes` preserves the attachment bytes across Git
operations. No generated native binaries/reports belong in the tracked change.

A final conservative directory observation counted `.cis/local` **753,776,438 B**,
`implementations/csharp` **4,458,455,345 B**, and all `.artifacts`
**179,352,707 B**: **5,391,584,490 B** combined. That is **10,427,569 B** net growth
from the planning observation, with **983,757,590 B** below the six-GiB ceiling
after the protected 64 MiB reserve. This is a sampled file-length observation,
not a deduplicated peak claim. Historical owners/reports remain preserved and
no general cleanup authority was inferred.

### Canonical task closeout

The controller closed `WORK-002:1` after all required checks. Both canonical
implementation tasks are **Complete** under the existing approved outcome;
`plan validate CIS-0007` reports valid with no warnings or unresolved decisions.
Final graph refresh reports zero warnings/errors, unchanged build
`sha256:cb6cc46fb192deb2fc22011950f1633c892c178cc46a960ec66a27bc6525384a`.
There is no active logical attempt, native reservation or recovery hold.
Historical owners and the remaining original limits are preserved. This task
closeout records no package publication, remote source transmission or merge.

## User-authorized branch and change closure

The subsequent user instruction explicitly requests merging the Startup branch
and closing it before the next module. This later decision authorizes publishing
the reviewed branch to the existing GitHub repository, merging it into `main`,
and deleting the finished feature branch. The implementation-only exclusions
above remain historical; no package release or new module work is authorized.

Merge preparation verified clean implementation commit
`af7de31936796776402e4f73eeedd119438849d9`, with remote `main` unchanged at
`ae61b1f63bd961e2e349d7c2fd67398b7451aef0` and an ancestor of the branch.
Main has no required status checks, reviews or branch rules. Independent read-only
closeout review confirmed both tasks Complete, all six retained passing receipts
and their counts, unchanged tested Core/attachment hashes, and canonical accounting
with no open attempts, reservations or recovery holds. Historical owners and
ignored native evidence remain intact; no metadata-only native rerun is needed.

`cis change close CIS-0007` recorded the dossier as **Closed** under this explicit
user instruction. The actual merge and branch-deletion result is reported with
the GitHub pull request. The documented standard-DI validation limitation and
unexecuted platform/release obligations remain unchanged.
| WORK-001 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=792; failed=125; possibleTokenSavings=123; ledgerDigest=sha256:ccd35fc4d51e92cb6a3f544e0bc99aba772b9f3fc1323b2a22c643e4f09e33b9 |
| WORK-002 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=809; failed=125; possibleTokenSavings=123; ledgerDigest=sha256:080fa7aba8fbfebdc6d4b12fad90be245cd1f204e15f40fbed9cf6b12d532480 |

## Heartbeat hosting boundary follow-up — WORK-003

After the original Startup merge, Andrew Spiteri explicitly requested moving
Heartbeat lifecycle wiring into hosting, keeping the coordinator independent of
Heartbeat and preserving behavior with tests. This same-module follow-up uses
the retained Startup outcome and unchanged shared 10,800-second / four-attempt /
six-GiB limits. The supported `plan task add` route added WORK-003 under the
approved outcome; completed WORK-001/002 and the original Closed dossier remain
historical. `work-allocation-refactor.json` redistributes unused capacity, without
changing past consumption, used attempts or the protected 300-second / 64-MiB
reserve. The follow-up grants no new numeric capacity or publication authority.

### Result and responsibility boundary

`StartupHostedService` owns one coordinator and optional concrete Heartbeat
connection. Hosting now owns activation acknowledgement, operation-token cutoff,
application-stopping cutoff and the actual Heartbeat drain. It publishes generic
coordinator stop intent before closing dispatch and entering the completion gate.
Both actual drains precede coordinator resource disposal and dependent teardown.

`StartupCoordinator` has no Heartbeat import, field, constructor dependency,
resolution or lifecycle call. It retains sequential initialization and one
terminal operation. A single internal Boolean completion callback remains inside
that operation's final gate: check stopping, confirm, recheck stopping, reject
unconfirmed completion, then publish Healthcheck startup completion. Known
cancellation during activation therefore stays neutral; activation failure uses
the original retained fault/critical-monitor path. No public contract, package
dependency, DI framework, Configuration implementation or scheduler was added.

### Actual native observations

All four commands ran serially through the existing native unit layer under
`CIS-0007/WORK-003`, logical attempt `WORK-003:1`, with the original ordinary
`PerformanceObservation` / `ExtendedStress` exclusions:

```text
dotnet .cis/local/tools/cis/cis.dll test incremental run --profile .cis/local/testing/profiles/<profile>.json --layer unit --max-batches 1 --max-seconds 900 --change CIS-0007 --task WORK-003 --authority C:/miscwork/portfolio/cislib --actor Codex --format agent
```

| Profile | Native project / selection | Passed | Failed / skipped | Reported elapsed ms |
| --- | --- | ---: | ---: | ---: |
| startup-host-boundary-unit | Unit: Startup and affected Heartbeat regressions | 180 | 0 / 0 | 730581 |
| startup-host-boundary-integration | Integration: real Startup host and existing Heartbeat hosting | 25 | 0 / 0 | 795466 |
| startup-host-boundary-architecture | Existing Architecture suite plus coordinator dependency guard | 341 | 0 / 0 | 715488 |
| startup-host-boundary-example | BusinessCases: real Startup country-list process and Type Manager startup stories | 11 | 0 / 0 | 444667 |

Each profile has a settled `latest-run.json` and native TRX report at
`.cis/local/testing/incremental/<profile>/reports/file-e7e4df149e0949e4a8a2-unit.xml`.
All report exit 0, one executed unit batch and zero pending unit batches:
**557 passed, zero failed or skipped**. These are the actual Windows observations;
unit-only `qualified=false` and the existing mutation-pilot advisory do not imply
broader platform, mutation or release qualification.

- AC-07/08/09: four new generic completion cases retain failure/cancellation,
  prevent readiness on false/faulted confirmation and prioritize known stopping.
- AC-10/13: three Heartbeat-specific completion cases now run through the actual
  Generic Host and both adapters, covering timer failure, stopped no-op and
  cancellation during real timer creation, with zero dispatch and truthful health.
- AC-10/11/12/14: the existing real-listener, business gate, pipe/probe, critical
  recovery and actual startup/callback/error-handler drain cases remain passing.
- Dependency boundary: the compiled coordinator and generated async/lambda work
  are inspected for signature, field, local and IL references to Heartbeat; six
  negative controls prove body-only, async and type-token dependencies are caught.
- AC-01/15: the unchanged runnable source consumer proves actual Type Manager
  preparation, ordered cache/reader priming, 500/503 then 200 responses, readiness
  before the first held refresh, actual recurring refresh and safe shutdown.

Independent read-only reviews found no required production, test, authority or
history defect. The existing standard-DI limitation above is unchanged. Guides
under `docs/modules/startup`, the cooperating Heartbeat paragraph and Startup
planning/acceptance records describe the refined ownership boundary. The user-owned
untracked example `Properties/launchSettings.json` remains untouched.

### Identity, accounting and storage

The tested Core DLL SHA-256 is
`c464dc14097e8e428a5247e693993599f330185889f4b088a5b853546806eb61`.
The original attachment remains byte-identical at SHA-256
`a0812a281c6696f16ccbe1d0d006e62988eae9982e581add51bb66f74ab16451`.
The source-frozen graph used by all four native runs is
`sha256:7e47d1be230f7e50da8554c03142401609d4e60050210be538da81ad24f9e159`.

Settled native accounting charges WORK-003 **2,636,486 ms**, using one logical
attempt, with zero reservations or recovery holds. Shared recorded consumption
is **5,336,934 ms**, three of four attempts used; the original 300,000-ms reserve
remains protected. Reported command elapsed times above are separate observations
from canonical charges. Historical unmeasured effort remains unknown; these
records do not invent complete metering for all authoring and review activity.

At **2026-11-16 21:00:16 UTC**, a conservative file-length observation counted
`.cis/local` **758,165,887 B**, `implementations/csharp` **4,468,077,555 B**, and all
`.artifacts` **179,355,528 B**: **5,405,598,970 B** combined. That is **14,014,480 B**
growth from original closeout and **969,743,110 B** below six GiB after the protected
64-MiB reserve. This is a sampled directory observation, not a peak or deduplicated
claim. Historical results remain preserved; no cleanup authority was inferred.

## Early-cancellation admission correction — WORK-004

Final review after the WORK-003 checkpoint found a missed observable behavior:
the original coordinator installed its linked-token Heartbeat cutoff before
participant construction, while the first refactor installed it only during
final confirmation. Canceling the first startup token while an initializer
ignored cancellation still prevented dispatch/readiness, but Heartbeat owner
creation and new/duplicate registration remained accepting until explicit host
stop. The 557 passing cases above did not cover that early admission boundary;
their actual results remain preserved, rather than being treated as proof of it.

WORK-004 owns this narrow correction and its tests/documentation under the same
outcome. The completed owners remain historical. The supported allocation update
in `work-allocation-early-cancellation.json` redistributes only unused capacity:
unchanged 10,800 seconds, four attempts, 300-second / 64-MiB protected reserve and
the same six-GiB group. Existing costs and all three used attempts are retained.

The hosted adapter now connects the first host-start token in its existing
once-only `StartAsync` subscription block, before participant construction. It
retains the final operation-token connection and disposes both only after actual
drains. The coordinator and single internal completion callback are unchanged.
One real-host regression cancels only the startup token while an ignoring
initializer is held, proves `ApplicationStopping` remains unset, and asserts
immediate rejection of owner creation plus new and duplicate registration before
stop or release. It then proves host stop still waits for actual initialization
settlement, with neutral cancellation and no readiness/dispatch.

### Corrected candidate: actual final observations

The four final profiles ran serially under WORK-004 using the native command
above with `--task WORK-004`, the same ordinary category exclusions and one shared
logical attempt `WORK-004:1`. No earlier receipt was overwritten.

| Profile | Passed | Failed / skipped | Reported elapsed ms |
| --- | ---: | ---: | ---: |
| startup-host-boundary-final-unit | 181 | 0 / 0 | 594410 |
| startup-host-boundary-final-integration | 25 | 0 / 0 | 547743 |
| startup-host-boundary-final-architecture | 341 | 0 / 0 | 372437 |
| startup-host-boundary-final-example | 11 | 0 / 0 | 627949 |

**558 passed, zero failed or skipped**, on the corrected candidate. Each settled
receipt reports exit 0, one executed unit batch and zero pending unit batches;
TRX reports retain the same per-profile location pattern described above. The
new held-initializer regression is included in the 181-case Unit result. Real
host readiness/gates/drains, compiled dependency guard and actual source example
all pass on this candidate. Independent review found no remaining material
production, test, authority or history defect.

Final tested Core DLL SHA-256:
`e8d5231789054ead81fc2dd7b572ef3c9d9739133c1a2923755ae4c54b4ff82a`.
The original specification remains at SHA-256
`a0812a281c6696f16ccbe1d0d006e62988eae9982e581add51bb66f74ab16451`.
All final native runs use frozen graph
`sha256:510d1ca22cb6500fdd7ff39ef84ab3c788ce941b382ac0c1e6ed473072692e52`.
The user-owned untracked launch settings remain at SHA-256
`1d65b76547423e534c95085865c646667153c7313fbe3f2dc910ca73a8d89c71`.

WORK-004's settled canonical native charge is **1,982,090 ms**, one logical
attempt, zero reservations or recovery holds. Shared recorded consumption is
**7,319,024 ms**, all four attempts used; the original **300,000-ms** reserve
remains protected. Historical metering limits and unit-only qualification limits
remain as stated above; no broader execution or release claim is made.

At **2026-11-16 21:57:07 UTC**, conservative file-length measurement counted
`.cis/local` **762,030,198 B**, `implementations/csharp` **4,469,141,522 B**, and all
`.artifacts` **179,358,361 B**, totaling **5,410,530,081 B**. This is **18,945,591 B**
above original closeout and leaves **964,811,999 B** below six GiB after the
protected 64-MiB reserve. It is a sampled directory observation, with all earlier
history retained and no general cleanup inferred. Module guides and acceptance
now explicitly include immediate admission cutoff during held initialization.

The supported final transition recorded WORK-004 **Complete**. All four canonical
tasks are Complete; `plan validate CIS-0007` is valid with no unresolved decisions.
Final graph refresh has zero warnings/errors and retains the frozen build above.
Both follow-up logical attempts are closed, with no native reservation or recovery
hold. The original Closed dossier and earlier completion history remain intact.

## User-authorized refactor merge

The subsequent user instruction explicitly requests merging this refactor and
closing its branch before the next module. This authorizes publishing the reviewed
refactor branch to the existing repository, merging its pull request into `main`,
and deleting the finished feature branch. It does not authorize another module or
native execution beyond the completed allocation.

Merge preparation found local and remote `main` at
`e8dccb39bea25da56c073a859fcfda7a6a6f3efb`. Independent read-only closeout review
confirmed all four tasks Complete, the final 558 passing results, matching frozen
source facts and Core/specification hashes, and settled accounting. The user-owned
untracked launch settings are excluded from the commit. The existing Closed
dossier and earlier task/result history remain preserved. The actual merge and
branch-deletion result is reported with the pull request.
| WORK-003 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=829; failed=125; possibleTokenSavings=123; ledgerDigest=sha256:84132d8db3e3eb614f7c1d6711d67a3eca8d2a17e1f21ad44f05b28f347b733e |
| WORK-004 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=845; failed=125; possibleTokenSavings=123; ledgerDigest=sha256:b74a61b3ab549a34df44fae9a316c8c2235b9510e92eae4a8721f096864bbaeb |
