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
| WORK-001 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=792; failed=125; possibleTokenSavings=123; ledgerDigest=sha256:ccd35fc4d51e92cb6a3f544e0bc99aba772b9f3fc1323b2a22c643e4f09e33b9 |
| WORK-002 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=809; failed=125; possibleTokenSavings=123; ledgerDigest=sha256:080fa7aba8fbfebdc6d4b12fad90be245cd1f204e15f40fbed9cf6b12d532480 |
