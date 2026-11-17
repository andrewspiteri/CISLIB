---
title: CIS-0009 verification
type: verification-record
status: Draft
change_id: CIS-0009
---

# Verification

| Task ID | Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- | --- |
| Intake | Actual checkout | `main` at `1f72e9ea4d8630edacb59c01795ebc8467807cd8`; local `codex/metrics-bounded-collection` | Inspected | Configuration merged/Closed; user launch settings unchanged. No remote publication or implementation. |
| Intake | Supplied sources | `planning/metrics/sources/` | Byte-for-byte preserved | Specification SHA-256 `b40ab50eaf9bfd679b1ba85196c4470e39930bf46534ad2532cced41ef687b13`; guide `e3cf1558b78df91a3c0a28bda86afcc02c2b00b7be97064507cbad480a2b8bf5`. Embedded prompts are reference, not authority. |
| Intake | Installed tools/workspace | CIS `0.3.0+2b916962dbedef18bdde8959e3144184d37d64f9`; `.cis/workspace.yml` | Inspected | One registered product-owned authority. No workspace recreation, tool upgrade or engineering-default adoption. |
| Intake | Derived baseline graph | `graph build --workspace .` | Built; zero warnings/errors | `sha256:e1de17cd576293665a871ce5385a0b27c2e035b4431a60ddb64d8a3164368437`, 22,249 nodes / 83,849 edges, before Metrics planning additions. |
| Intake | Existing-repository feature route | `brd feature intake --input .cis/local/metrics-intake/request.json --dry-run` | Rejected; no apply | Installed route requires a product-owned participant and rejects authority reclassification. No plan hash/feature approval was invented; supported in-place Draft change used. |
| Intake | Proposed dossier/seed | `change create`; `plan build CIS-0009` | Proposed/Draft | Generated WORK-001 still has placeholder criteria and no outcome authority. Plan validation warns accordingly; not executable. |
| Intake | Contract/module review | Actual Core, Startup/HB/HC/Configuration/Type Manager/Logging and independent read-only review | Feasible boundaries; early proofs identified | Keep existing constructor/pre-DI contracts, finite aliases and actual callback drain. No native test/restore/build was launched and no source was sent to an external review provider. |
| Intake | Public SDK/reference review | Pinned exporter options, SDK view selection and .NET host meter-factory sources | Required APIs present in source; restore/runtime unverified | No local OTel packages. Exact host Meter references plus Drop can isolate controlled views; uncontrolled views/readers unsupported. HTTP timeout/provider disposal alone do not settle callbacks. |
| Intake | Current storage sample | File lengths at 2026-11-17T10:24:53Z | 5,484,645,805 B | `.cis/local` 802,354,618; `implementations/csharp` 4,469,975,898; all `.artifacts` 212,315,289. Leaves 890,696,275 B after the included 64-MiB reserve beneath the same six GiB. Sample, not peak or new pool. |
| Intake | Proposed work/profile | `planning/metrics/{README,implementation-plan,acceptance}.md`; `authorization-proposal.json` | Prepared, unapplied | Three outcomes/five medium leaves; 36,000 seconds/12 attempts, included 900-second/64-MiB restoration; all workers/docs/tests/review included. |
| Intake | Bounded concrete impacts | `impact analyse CIS-0009` with existing identity and Startup.Hosting roots, depth 2/limit 60 | Two proposed findings; not truncated | `IMPACT-06BE594EFF` EnvironmentOptions identity reuse and `IMPACT-5E7BF2C291` hosting. No human dispositions/accepted impacts are invented. |
| Intake | Draft documentation | `docs validate --strict` | Valid | 136 catalog entries/documents, zero warnings/errors. This validates documentation, not Metrics functionality. |
| Intake | Actual execution eligibility | `agent work status CIS-0009 --task WORK-001` | `missing-allocation`, exit 4 | Zero allocated/consumed/reserved work for this change; no Metrics execution authority. Earlier allocations remain scoped to completed changes. |
| Intake | Independent proposal review | Read-only review of sources, actual seams, plan, acceptance and limits | No material scope/ownership/approval ambiguity found | Five leaves preserve three outcomes; seconds/reserve/attempt totals reconcile. Host isolation/drain remain early native proof gates; no implementation or approval inferred. |

## Subsequent explicit implementation authority

Andrew Spiteri answered **Approve proposed profile and allocation** to the exact
Metrics five-task/three-outcome profile and shared 10-hour/12-attempt proposal,
including 900-second/64-MiB restoration under the existing physical six GiB.
`change authorize` recorded
`cislib:CIS-0009:outcome:56b4c76df486408d8d80b6e0a124c37e`, decision
`CISLIB-METRICS-2026-11-17:USER-APPROVED-PROFILE-ALLOCATION`, digest
`sha256:8fab56caa545f4ad770fb657e08522179f22517221746120e081e3d130948341`.
Task update/add established concrete WORK-001–WORK-005 through supported commands;
all five inherit the same outcome. The allocation applied at ledger revision 1240:
5,400/7,200/7,200/7,200/9,000 seconds, 2/3/2/2/3 attempts, reserves
120/240/180/120/240 seconds. No historical owner/cost was reset. WORK-001 is
InProgress; the others depend on its frozen contract, and WORK-005 additionally
waits for completed collection/runtime/bootstrap leaves. This section supersedes
the earlier intake-only eligibility observations without rewriting them.

## Retained intake and qualification limits

The 24 repo-doctor warnings concern existing derived index/Ollama availability,
conditional native classification, unadopted engineering defaults, deferred
security/standards setup and Draft documents. No wholesale initialization,
classification, skill quarantine or tooling campaign was applied. These warnings
are not passing native evidence or permission to repair unrelated capabilities.

At intake, Metrics implementation, compatible optional-package restore, all-format exported
identity, callback-owner shutdown proof, two-host isolation, bounded memory/response
observation, installed skill validation and native acceptance remain unexecuted.
Prior module allowances stay Closed/Complete. Proposed defaults and public names
are not represented as an already published Metrics contract. Subsequent actual
implementation observations are recorded below without converting that intake
state into an earlier pass.

## WORK-001 Core primitive checkpoint

The frozen Core source adds five Metrics files and one optional-assembly friend
attribute; no NuGet or existing module source changed. Public consumer types are
CIS-owned. The runtime borrows the host Meter, stores finite metadata rather than
aggregate values, isolates ordinary recording/diagnostic failures, and closes/
joins actual observable callback materialization before releasing owner delegates.
Timers retain only operation-local state and cannot retry a completed invalid
attempt. Independent source review found no required defect or expansion into
an aggregation engine, lifecycle framework or test harness.

`metrics-core-unit` completed with **80 passed, zero failed/skipped**, real Core
and Unit compilation and zero pending unit batches. Native TRX is
`.cis/local/testing/incremental/metrics-core-unit/reports/file-e7e4df149e0949e4a8a2-unit.xml`;
the exact input/tool/work references remain in the adjacent state/receipt. Twelve
portable measurement/timing/observable vectors are consumed by native tests. The
literal exported-family expectations are still pending actual SDK verification.

The first logical attempt consumed **239,714 measured inclusive native ms** with
no reservation/recovery hold after settlement. A qualifying controller checkpoint
retained that observation before the Architecture invocation. Current graph at
this boundary is
`sha256:da8014d5e684c0d291b18f6a166fb97b70588b0cd20d5fdab84ab91f5f293f6f`,
22,596 nodes / 85,117 edges, zero warnings/errors. Full Architecture checks are
the next bounded action; SDK/provider/HTTP/package/footprint proofs remain later
scope and are not inferred from this Core pass.

The subsequent `metrics-core-architecture` invocation settled with **352 passed,
zero failed/skipped**, including five new portable-surface/dependency cases.
WORK-001 therefore completed with **432 distinct native cases passed**, one used
logical attempt, **466,152 inclusive measured native ms**, zero reservations and
no recovery hold. Core bytes at this foundation boundary hash to
`e8e66dbf85aea21765b04ccf7ddb74878545d44e1359adc56ead442341f1ade6`.
The supported close-attempt and Complete transition preserved that evidence;
WORK-002/003/004 then entered InProgress independently. These foundation results
precede their subsequent source changes and do not claim later cache currency.

## Package source-mapping scope correction

The approved optional package requires NuGet source mapping for its five selected
OpenTelemetry package IDs. Root `implementations/csharp/NuGet.config` is an
explicit existing allowlist; its path was omitted from the original generated
Metrics file scope. A supported task-path update was attempted before editing it
and rejected with `Scope expansion needs a targeted human decision for path:
implementations/csharp/NuGet.config`. The exact five-entry proposed mapping is
reviewable at `.artifacts/metrics/CIS-0009/NuGet.config.proposed`; the real mapping
has not been changed while that decision is pending. No extra source, wildcard,
Core dependency or capacity increase is proposed. Unaffected authorized production
and tests continue; no duplicate-config or disabled-mapping workaround is used.
| WORK-001 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1029; failed=149; possibleTokenSavings=123; ledgerDigest=sha256:f3f175200eb1ae111edadc55d39f3c7d2522587aba12d90b388bc43dcd7faa2a |

The user approved the prepared five-ID one-file correction. It is now applied;
XML comparison confirms all original sources and mapping entries are retained.
The supported corrected scope representation and supporting WORK-006 preserve
started contracts, all consumption and the unchanged shared totals.

Actual WORK-002 restore then failed NU1100: the downloaded OpenTelemetry.Api1.21.0
package has a net10.0 asset and requires System.Diagnostics.DiagnosticSource11.0.0,
which also needs an exact source-mapping entry. No collection test ran. The
preserved stdout/stderr and generated partial locks are failure evidence, not a
successful locked closure. That additional mapping is prepared separately for a
targeted decision because the approved support contract explicitly says five IDs.
| WORK-006 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1057; failed=157; possibleTokenSavings=123; ledgerDigest=sha256:ddb267e07e608c223e3b2554f99d231da59a47e8a62ba5f47dbc8e71f9639d29 |

## Bootstrap Metrics checkpoint

metrics-bootstrap-unit passed13/13 with zero failed/skipped on runtime10.0.12. Initial compilation exposed four shared C test params/new assertion syntax errors; those were corrected before this real pass. WORK-004 retains both native executions,461710 inclusive measured ms, one logical attempt, no reservation/hold. Sequential Startup, retained Configuration facts/actual refresh notification settlement, and immutable Type Manager report observations passed without replay. Optional host attachment is still pending WORK-005.
| WORK-004 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1074; failed=158; possibleTokenSavings=123; ledgerDigest=sha256:8ddcc90b9742308d859672979a9de9c4849ab51e62564648e9e43cbdc059d376 |

The sixth exact DiagnosticSource mapping was explicitly approved and applied; WORK-007 preserves the completed five-ID task contract. Actual second restore failed NU1102 with nearest==requested11.0.0. Its retained log is .artifacts/metrics/CIS-0009/collection-restore-nu1102.log. A fresh local153-version index includes stable11.0.0, while no candidate package exists locally; this does not prove incompatibility or availability. One same-source cache-bypassed governed retry is the bounded next diagnostic, with exact versions and toolchain unchanged. No SDK native test has passed yet.


## Runtime Metrics checkpoint

metrics-runtime-unit passed22/22, zero failed/skipped on runtime10.0.12,215710 inclusive measured native ms and one logical attempt. Tests exercised actual Logging frontend/backend/discard/writer boundaries, Heartbeat actual execution/outcome/active settlement, and Healthcheck evaluation/state/readiness without scrape-triggered evaluation or retirement. The collected weak-handle regression passed. Core source compiled and operation-entry attachment preserves no-replay behavior. Actual optional provider/hosting source installation remains WORK-005 scope.


## Locked collection checkpoint

Final metrics-collection-integration passed31/31, zero failed/skipped and zero pending unit batches with unitCurrent=true. The approved1.21.0/beta.1/DS11.0.0 net10 closure restored in locked mode, with runtime10.0.12 unchanged. Real supported format/escaping identity, native/histogram/overflow/target-info samples, exact host-owned selection and two-host isolation, finite options/catalog controls and actual Core callback materialization/drain passed. The first two restores failed, and the first31-case pass regenerated the lock so CIS correctly rejected reuse; the stable locked rerun is native fd6199e633ea4200ae005be912f159e2. No downgrade, alternate feed, cache deletion or forced currentness was used. WORK-002 retains900973 inclusive measured native ms in one logical attempt; product HTTP/source/lifetime integration and deployment/footprint remain WORK-005. Unit-only completed is not release qualification.
| WORK-003 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1083; failed=159; possibleTokenSavings=123; ledgerDigest=sha256:c99302015d589fad92d2be1411888edf48e0420bbde1dd051f64c9545dbde885 |
| WORK-002 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1084; failed=159; possibleTokenSavings=123; ledgerDigest=sha256:bbeb225fbc6cd78ab778f48f8cac7bbaf9472e3e0f8a60bd9f4a64b7162e07d0 |

## WORK-005 real host checkpoint

The final frozen `metrics-host-integration` invocation passed **65/65**, zero
failed/skipped, zero pending batches and current unit inputs. It is native
`731b91f5e3154471b7fb40290d581571`. This includes the31 collection controls,
12 actual host/lifetime cases,20 native HTTP/branch cases, one automatic six-module
snapshot/live-refresh case and one exporter test consuming all8 literal portable
families. Required sequential startup remains held while metrics responds; actual
callback owners survive expired requests and canceled host waits; late scrapes are
rejected even before ApplicationStopping publication. Configuration uses the same
objects/context/provider and no bootstrap event replay.

The first61/62 result exposed mapped error-route substitution. A duplicate-tag
attempt caused SDK point rejection (61/65); those failed observations remain in
`.artifacts/metrics/CIS-0009/host-first-61-pass-one-route-failure.trx` and
`host-second-duplicate-route-attribute-failure.trx`. The final policy restores the
public original endpoint after handler execution and preserves one native
observation, the original declared route, native root normalization and original
management exclusion. Unknown originals emit no route label. No second HTTP
counter/timer, wire rewriter or SDK view chain was introduced.

Ordinary Code Coverage collection retained actual Cobertura under
`.artifacts/metrics/CIS-0009/coverage-host/`. The final focused selection observed
Core38.46% line/25.71% branch and optional Metrics93.05% line/75.56% branch. This is
partial focused-suite evidence, below full95/90 release gates; it does not claim
whole-shipping coverage or release readiness. The initial collector's inherited
command streams did not close after exit; small ordinary invocation glue now
redirects those streams and preserves native reports, with no new runner or
process controller. Both later invocations settled normally. WORK-005 keeps all
three inclusive native costs (264971+269416+271201=805588ms) and the same open logical
attempt; package/example, one footprint observation and remaining proportional
checks are still pending.

The subsequent whole Core Unit regression (excluding the existing PerformanceObservation/ExtendedStress categories) passed5787/5787, zero failed/skipped and zero pending unit batches. Native c0cdc042b6bd4671b7a4e60e63c330ef consumed266543 inclusive ms within the same WORK-005 logical attempt; source unit inputs current at that checkpoint. This includes the diagnostic-owner release regression. Core DLL SHA256 caa01613fa6c2539c85340eced2b881de91260b28484565ad9d1a94ae03afa08; optional DLL a5f4821731a63a8b17e76587a596b22c102952059716ee6807633e5c747f28ed. These are local frozen implementation bytes, not a release publication.


The single default-catalog PerformanceObservation passed:23 selected streams,
19 Core+2 app definitions, configured capacity sum3520 plus SDK reserved points,
14 finite duration bounds, two real formats/handlers, default300-ms cache/5-s wait
and4-MiB cap. Baseline2310816B; warm5080784B; first retained7518424B;
second8812776B; incremental retained6501960B (6.20MiB), below64MiB.
The second round grew1294352B, including retained response strings; this is not
an exact SDK allocator/RSS plateau claim. Each round introduced4096 new category
strings in each of the app counter/timer. Prometheus samples stayed1337 and
229843B, OpenMetrics stayed1538 and265794B across both saturated rounds.
Authoritative identity and standard overflow remained present. Actual catalog,
workload and all measurements are retained in
`.artifacts/metrics/CIS-0009/footprint-observation.json` and the native TRX.
No second footprint campaign or claim about every possible native/module
combination is inferred.

## Final host, package and compatibility checkpoint

The first final Architecture run passed 351/352 and identified a real ownership
violation: Healthcheck transport directly used a Microsoft DI extension to find
the optional Metrics alias. Native `13930d0d1cd94d65a42f894c7981c93b` and the
retained `metrics-architecture-final/latest-failure-stdout.log` preserve that
failure. The unchanged boundary rule was retained. A small internal
`CIS.Metrics.Hosting.MetricsHostAttachment` now owns exact-alias lookup and source
dispatch. Healthcheck invokes it once while building its pipeline, after provider
activation and before listener startup; it retains external source ownership.
No fallback, registry, second container/provider or policy change was added.

The corrected frozen implementation then passed these actual Windows checks:

| Check | Observed result | Native execution |
| --- | --- | --- |
| Architecture, whole existing suite | 352 passed, zero failed/skipped | `190c871e701d48d588ae36d1d6203664` |
| Host/HTTP plus existing Healthcheck HTTP transport | 123 passed: 65 Metrics plus 58 Healthcheck cases; zero failed/skipped | `bd0ad64411be44d3bef19e2d503fc8d2` |
| Isolated versioned Core SDK / optional PackageReference consumer | 1 passed; actual build, publish and both application executions | `1d6d728bb4de451c8c5b5973449ce834` |
| Compatibility, whole existing suite | 179 passed, zero failed/skipped, including historical source/frozen consumers and public API | `83b319f00f0246d9902499c67dbe70ad` |

Each incremental invocation completed its selected scope, with zero pending unit
batches and current unit inputs at its own checkpoint. The corresponding local
state/report directories are `.cis/local/testing/incremental/metrics-architecture-final`,
`metrics-host-integration`, `metrics-package-windows` and `metrics-compatibility-final`.
Unit-only completion retains CIS's pilot-required/qualified=false advisory and
does not establish full release qualification. No new runner/pilot campaign was
created to change that advisory.

The real package consumer deploys DiagnosticSource 11.0.0 privately, runs on the
pinned .NET/ASP.NET 10.0.12 frameworks and verifies a valid Unicode authoritative
identity of 108 scalars / 308 UTF-8 bytes. Core's approved NuGet closure remains
unchanged. The prior source-example business story passed 1/1 under native
`e6f5b68aecc54458ad537aae2e825cd6`; the first package attempt ran before its
source-example assets existed and failed its fixture prerequisite. Restoring and
building the real source example before the package consumer corrected that
sequence, without a product workaround. The earlier package pass
`3ae7b31c38d546eb956bb029f67a6032` and both failed admissions/attempts retain their
actual costs. The final package pass above verifies the subsequent boundary fix.

The expanded 123-case Cobertura observation is Core 41.2727% line / 27.3496%
branch and optional Metrics 93.0481% / 75.5556%. Reports remain under
`.artifacts/metrics/CIS-0009/coverage-host/`; these focused percentages remain
below the full 95/90 release gates. The 5,787-case Unit regression and single
6.20-MiB footprint predate only the narrow attachment correction; no second
footprint run is inferred. Native implementation DLL hashes at this checkpoint
are Core `332b32c7502723d27733d1d5b1509c6de58be6a3c9f708065255ff82aa29dec7`
and optional `5997b9750057dceae2e7cd8f528ce4a2ef355da7d5ee52d92ef0f87a1d5ecd9d`.

Documentation closeout replaces stale pending statements and adds the explicit
`CIS.Metrics.ITimer` alias in the usage snippet. The portable JSON's descriptive
verification string changed; measurement/timing/observable inputs and expected
values did not. Such metadata edits can stale an earlier receipt and do not
justify claiming forced currentness or rerunning unchanged package/architecture
behavior merely to clear it. Timer-component mutation and a normal postflight
Metrics Unit rebuild remain the final native check. Other platforms, whole-suite
coverage/mutation qualification, publication and default-branch merge are outside
this completed-checkpoint claim.

## Final mutation, normal build and delivery evidence

The diagnostic mutation route was held before native launch because it requires
a correctness pilot. The installed, supported canary route accepts the explicit
narrow command without creating a pilot or qualification campaign. Both commands
select Complete mutation of the whole `Metrics/TimingScope.cs` file, with the
existing 90% break threshold, one test session and the existing Metrics Unit
suite. This is component evidence, partial=true and qualified=false.

The initial local mutation-profile schema rejection (`toolFiles` is not a field
of MutationRunProfile) launched no native child. Correcting the profile and the
normal-build target spelling did not change production scope. The held diagnostic
admission consumed 424 measured ms under the existing owner. First canary native
`2c75144dfe1b464595fd2348fd907a9a`, run
`canary-20261117-155123-18e1cb2dae424ccbb76dabf433086601`, observed seven killed,
one survived, zero timeouts/no-coverage: **87.5%**, below 90. Its canary-passed
status alone was not accepted as meeting the component threshold. The survivor
changed `initial.IsInvalid || completionDimensions.IsInvalid` to `&&`.

Two cases now extend the existing public timer-completion test: a null params
array and nine supplied dimension pairs. Both require one dropped completion,
fixed elapsed time, zero emissions and no later Stop/Dispose retry. These detect
loss of an invalid marker after the constructor discards over-capacity pairs.
Production code and the mutation threshold remained unchanged.

Final native `e91916158b174fd1a6255410875ddb39`, run
`canary-20261117-160146-3b73646a26144354bd2e379ce986ad84`, observed **8/8 killed,
100% conservative/native score**, zero survived/timed-out/no-coverage/runtime
errors/pending/unknown. The selected file also has two compiler rollbacks and two
automatic Stryker block-filter cases; they are not counted as kills. Source
identity was unchanged during execution; thresholdMet=true, qualified=false.
The retained full report is `.artifacts/metrics/CIS-0009/timing-mutation-final.json`
(SHA256 `ad1ea1960510c8a454d40bfd90c3559768c20befaa81568c43bcb60bd0f8ffa1`).
Each run's compact authoritative summary remains under its CIS mutation run
directory; native reportAvailability is cleaned-after-assessment, so the copied
final report is additional retained observation, not a fabricated CIS receipt.

The first profile omitted Core's project XML from configFiles. Supplementary
before/after hashes matched `067c7908703b2365f549d2d137d9e045ee158528fee63558a8be2f241f8a54b9`;
the final profile explicitly includes that project. Both normal-build health
commands exited zero, but they did not retain a test TRX and are not asserted as
118 executed postflight tests. A separate ordinary normal-build invocation then
passed **118/118**, zero failed/skipped, zero pending batches, with current unit
inputs: native `b568ddeef518426fa10dd045828e8de3`, profile
`metrics-normal-unit-final`. Its admitted TRX is retained under
`.cis/local/testing/incremental/metrics-normal-unit-final/reports/`.
Core and optional DLL hashes again match the final host/package checkpoint above.
No further production change or repeat broad/footprint run was needed.

All six relevant native categories now have proportionate actual evidence:
Unit (whole 5,787 regression plus final affected 118), Integration (123 final
host/Healthcheck and actual isolated package), Architecture (352), Compatibility
(179), Business (real source example) and Mutation (the whole timer component).
Strict skill validation passed one skill with zero errors/warnings; deterministic
audit with --no-llm was clean with zero duplicates/overlaps/conflicts/quarantines.
Independent source, hosting and documentation reviews found no remaining blocking
defect or new framework/harness/tooling responsibility.

The final XML comparison at
`.artifacts/metrics/CIS-0009/nuget-mapping-comparison.json` confirms all original
sources/mappings preserved, exactly six approved additions, and exactly one
DiagnosticSource addition after the completed five-ID proposal. The actual
package consumer proves private deployment on runtime10.0.12. Original attached
source hashes and the user's pre-existing startup launchSettings hash are
unchanged; that user file remains excluded from the module commit.

Settled native ledger consumption is **7,035,222 inclusive measured ms** across
CIS-0009, **4,990,677 ms** for WORK-005; five logical attempts across the change,
one for WORK-005, with zero active reservations/recovery holds. This reports the
ledger's measured native coverage, not an exhaustive measurement of authoring or
review effort. No additional pool or increase to the shared10-hour/12-attempt,
6-GiB ceiling and restoration reservations was made. The pre-final count was
5,583,973,011 bytes, with ample room for the retained4.7-MB report; final storage
is sampled at closure. Historical failures, admissions and costs remain intact.

The bounded implementation is ready for local review. Full95/90 shipping coverage,
whole-release90% mutation and another platform remain unqualified. The source and
documentation commits can also stale cached receipts without changing these
actual observations; no forced reuse/currentness is claimed. Product-record
approval, publication and default-branch merge are separate from this delivery.

## Implementation closure

All five implementation tasks and both approved mapping-support tasks are
Complete through supported CIS transitions. The WORK-005 logical attempt is
closed; native reservations and recovery holds are zero. The final closure
storage sample was **5,655,873,087 bytes** (5.27 GiB), including retained native
evidence, within the physical6-GiB ceiling and with the64-MiB restoration reserve
intact; available space below that ceiling after the reserve was719,468,993 bytes.
See `.artifacts/metrics/CIS-0009/storage-final.json` for the sampled roots/time.
This completes the bounded local implementation and documentation, with the
qualification and product-approval limits above preserved.
| WORK-007 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1142; failed=167; possibleTokenSavings=310; ledgerDigest=sha256:eca6f14cca9bb00c5b7c8a13bd9cee352b7b08afeb583b992cdfa04a0f6fd499 |
| WORK-005 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1143; failed=167; possibleTokenSavings=310; ledgerDigest=sha256:09b0f04c98b7a8c256e1efc1c4f31bed6660d9f3f16f7c2af044668b130d004c |

## Authorized branch merge — 17 November 2026

Andrew Spiteri explicitly requested merging the Metrics branch and closing it
before the next module. CIS-0009 was closed with the supported `change close`
command after confirming all seven tasks Complete and the implementation attempt
settled. Remote main still equals the reviewed implementation baseline
`1f72e9ea4d8630edacb59c01795ebc8467807cd8`; implementation commit `17c83924`
is its direct successor, so no source conflict or integration change is required.

This closeout adds only dossier metadata. The recorded actual native passes,
costs and footprint remain their original scoped observations; no completed test
owner is reopened and no forced cache-currentness claim is made. The explicit
merge decision authorizes this branch integration, not release qualification or
publication. Existing coverage/platform limitations remain open. The user's
pre-existing startup launchSettings file remains unchanged and excluded.
