---
title: CIS-0010 verification
type: verification-record
status: Draft
change_id: CIS-0010
---

# Verification

| Task ID | Check | Command or artifact | Result | Notes |
| --- | --- | --- | --- | --- |
| Preparation | Baseline | main `7b5923c031027b932f730e1cf45b2d51270b17cc` | Observed | Matches both supplied source references; no RestClient runtime exists. |
| Preparation | Source preservation | `planning/rest-client/sources` and `.gitattributes` | Copied and hashed | Spec SHA256 `93ba18c7f29113dd178a2fe70fd19dc24fbe8df03ab7a463b158dd8c85470a7e`; guide `4babc76204c66cf897fd56afbe1a63b4c690e9c5f0b680e45286972f0dc28b1d`. |
| Preparation | Installed CIS | `--version` | Observed | `0.3.0+2b916962dbedef18bdde8959e3144184d37d64f9`; one owned authority, root docs. |
| Preparation | Feature intake | `brd feature intake --dry-run`; `.cis/local/rest-client-intake/preview.json` | Target rejected; applied=false | Existing-mode intake requires a product-owned participant; no repository reclassification or Active feature was fabricated. |
| Preparation | Foundation refresh | `.cis/local/rest-client-intake/technical-refresh.json` | Partial: upstream unchanged, backlog review pending | BRD and technical intent remain Active/current. Generated `docs/plans/high-level-backlog.md` is Review Required; no approval was recorded. Existing classification/lifecycle warnings remain advisory. |
| Preparation | Draft dossier and plan | `change create CIS-0010`; `plan build CIS-0010` | Proposed change; Draft seed | Generic WORK-001 still has planning placeholders and no authorization. The seven detailed RC labels are proposals, not allocated WORK IDs. |
| Preparation | Source-plan import | `.cis/local/rest-client-intake/plan-import.json` | Blocked; applied=false | At invocation no impact findings/accepted impacts existed. Subsequent analysis retained four proposed findings; none was self-approved. No eligible `plan derive` feature is claimed. |
| Preparation | Existing contracts | Actual Core project, Metrics catalog/context and Startup/HB hosting; three independent source reviews | Reviewed | Identified retry-delay API gap, cancellation/defect and shutdown ordering, correlation/absence details, opaque borrowed-chain limits and conditional Metrics compatibility. |
| Preparation | Optional package metadata | Public pinned Polly.Core 8.9.1 v3 nuspec and tagged public retry API | Candidate verified, no native pass | net10.0 dependency group is empty. No package was restored/installed and no retry conformance was inferred from metadata. |
| Preparation | Storage | `.cis/local/rest-client-intake/storage-baseline.json` | Sampled | 5,661,509,307 counted bytes under 6 GiB; 713,832,773B available after the existing 64-MiB restoration reserve. No cleanup or extra pool. |
| Preparation | User files | Both untracked example launchSettings files | Preserved | Startup CountryList SHA256 `1d65b76547423e534c95085865c646667153c7313fbe3f2dc910ca73a8d89c71`; Metrics MetricApi `b91ec52066232e9e2c7416925607c4b7aa7abbc16360d8b3fc091d6b5ef8bc64`. |

## Preparation boundaries before implementation approval

The user asked for import, reconciliation and a bounded implementation plan.
All new REST APIs, dependency choice, reconciled defaults, seven task contracts
and the recommended 12-hour/16-attempt pool remain proposed. No runtime code,
native REST restore/build/test/pack, package publication, main merge, new skill or
implementation allocation was produced. Closed earlier grants remain closed.
Planning/source inspection is not passing native acceptance evidence.

The complete raw inputs and seven coherent proposals are linked in
[the implementation plan](implementation-plan.md). The literal acceptance map
retains A01–A22 without claiming that test source exists or ran. Current CIS
readiness warnings and the generated unapproved backlog are preserved, not cleared
by invented approvals or a tooling campaign. Future task IDs and authority must
be recorded through supported CIS operations after the actual implementation
decision. Metadata/prose validation and independent plan reviews follow below.

## Independent plan review

Three source-aware read-only reviews covered the complete supplied documents,
actual contracts and prepared plan. They found the retry public-API delay gap,
terminal fault/stopping details, opaque borrowed transport limits, conditional
catalog compatibility and early hosting cutoff; these are explicit in P01–P12.
The final reviews found no remaining material source/plan contradiction. A
structured storage-root mismatch was corrected: the authorization proposal now
counts all `.artifacts` with no historical disposal permission, not just new REST
scratch. No source, runtime or approval record was changed by these reviews.

A separate context-limited reader correctly distinguished Proposed/Draft from
implementation and approval. Its clarity findings were incorporated: define
logical work attempts separately from HTTP AttemptCount; show the conditional
retry dependency; explain four recording handles versus 27 catalog streams; and
state how future outcome authority differs from the unapproved product backlog.
This is advisory model-assisted review, not human acceptance or usability testing.

Mechanical preparation checks in `.cis/local/rest-client-intake/plan-checks.json`
confirmed seven proposed leaves, an acyclic dependency order, all 10 REST IDs and
all 22 acceptance IDs, and sums of 43,200 seconds/16 logical work attempts with 1,200
included restoration seconds. These checks establish plan structure and totals,
not semantic completeness or passing product behavior. The actual native tests
and optional dependency compatibility remain unexecuted.

## Metadata checks and retained limits

Strict documentation validation passed: 157 catalog entries/documents, zero errors
and zero warnings. The final planning graph built with 23,337 nodes/88,148 edges,
zero warnings/errors, ID
`sha256:b5e1472d16ef7d3af5c78aaf544b50cab0eed11683156bbb8a8105e0dbe77a20`.
Two new catalog rows initially used an unsupported authority label; changing
those rows to `derived`, while retaining Draft status, corrected the graph check.

The original four findings remain proposals against graph
`sha256:887b3ae20a87ae34b9c0a99cd53df2d5d6855f2a5426fa1c27b4e61377e2c2a2`.
A fresh reanalysis after adding the planning documents was refused because this
exact dossier graph baseline differs from the new graph. The diagnostic is
retained in `.cis/local/rest-client-intake/impact-final.json`. Runtime source and
the Git implementation baseline are unchanged. No protected baseline hash was
edited, findings approved or new baseline invented to suppress that guard.

`plan validate` reports a valid Draft with its two expected warnings: generated
WORK-001 still has generic planning placeholders, and outcome authorization has
not been recorded. That is validation of the current seed, not validation or
approval of seven executable task records. The seven reviewable contracts live
in implementation-plan.md; their dependency/coverage/resource checks and advisory
reviews are reported separately above. Feature routing, unaccepted impacts,
unreviewed backlog and missing implementation authority remain explicit.

All planned validation is therefore metadata/source preparation. No native REST
test, restore, build, package consumer, retry experiment, benchmark or release
qualification was run. Both supplied documents and both pre-existing user files
still match their recorded hashes.

## User-approved implementation start — 17 November 2026

Andrew Spiteri approved the RestClient plan and proposed 12 active hours/16 logical
work attempts, including 1,200 restoration seconds, under the existing 6-GiB
ceiling, and explicitly directed implementation. This supersedes the earlier
planning-only restriction. The accepted P01–P12 profile, dependency candidate,
bounded early retry proof, seven leaves and delegation remain the reviewed scope.

Supported `change authorize` recorded authority
`cislib:CIS-0010:outcome:23214d429eb5401485eba6e61987f44d`, original digest
`sha256:fd9de03495c27b0d7f8577f847f028de5586365a1a620c0dacf0cbe1f30a8a49`,
decision reference `CISLIB-RESTCLIENT-2026-11-17:USER-APPROVED-PLAN-ALLOCATION`.
The request is retained in authorization-request.json; the original unsubmitted
proposal is preserved separately. CIS requires external counted roots to use its
declared native-scratch classification. The initial ordinary-root representation
was rejected before authorization; marking the same `.artifacts` root accordingly
made it valid. Counted paths, ceiling/reserve and empty cleanup delegation did not
change, and historical/user artifact deletion remains unauthorised.

The generated seed was replaced through task update and six coherent leaves were
added through task add under the accepted outcome. `plan validate` reports seven
valid concrete tasks and no seed/authority warnings. `agent work allocate` bound
the exact shared total with task distributions from the reviewed plan, retaining
the existing storage-ceiling reference and all previous claims. WORK-001 is
InProgress; no old task or allocation was reopened. Source authoring uses the
current approved checkout; native restore/build/test/pack remains serialized by
the coordinating owner through CIS. No external provider-run or extra worktree is
being created, and no source push, main merge, product/backlog approval or package
publication follows from the implementation grant.

The 21:15:17 UTC pre-native physical sample counted 5,656,088,813 bytes across
the three existing roots, leaving 719,253,267 bytes after the 64-MiB restoration
reserve. Initial operations keep the proposed 256-MiB scratch envelope. This is
a storage observation, not a new allowance or cleanup delegation.

## RC1 contract verification in progress

The first governed Unit invocation, execution
`native/incremental/c76adad87b8d41639fd156a0767dfc4f`, settled failed before tests:
the compiler reported CS0136 for two `out Uri` locals in the registration snapshot.
Renaming the inner origin local corrects that source defect. No passing case or
TRX was produced. CIS charged 412,000 ms inclusively and retained logical attempt
WORK-001:1 with no reservation or recovery hold. Authoring and review effort is
additional to that measured native cost and remains inside the same shared total.

The initial incremental profile selected each contract file as a separate batch.
The corrected profile retains every source file as an explicit fingerprint input
and uses the Core project as its one all-tests batch owner. This avoids repeating
the same filtered suite once per contract file; it does not remove test inputs.
The unit-only runner's representative-pilot advisory remains visible and no
whole-runner or release qualification is claimed. Retry/transport/host behavior
still requires the subsequent implementation owners.

The next invocation settled before native execution because source changes need
a fresh graph; it charged a further 56,534 ms and ran zero cases. Independent
review also identified a contradictory internal construction route: success could
carry a non-2xx status, or HTTP failure could lack one. Terminal factories now
reject those states, while preserving valid HTTP-200 business/decoding/limit/timeout
failures and a general detached context. Seven focused cases cover the guards and
valid controls, bringing the pending Unit suite to 36 cases. Refreshing the graph
and rerunning remains the next bounded action in WORK-001:1.

The refreshed invocation compiled and ran all 36 Unit cases: 35 passed, one
failed, none skipped. The failed clock test set UTC to DateTimeOffset.MaxValue
and then advanced it, overflowing the existing test helper. It now uses a safe
2050 date, which still checks that UTC changes do not reset the monotonic budget.
The runtime and shared time helper are unchanged. This invocation settled with
768,427 ms cumulative measured cost, no reservation/hold and the same logical
attempt. These partial results are retained separately from the required full pass.

### RC1 passing checkpoint

The corrected Unit suite passed **36/36**, with zero failed/skipped, and the
Architecture suite passed **9/9**, with zero failed/skipped, on the actual Windows
net10.0 lane. Executions `native/incremental/2fac9f2fcae24d399d4292728119b148` and
`native/incremental/f0ebbda2b53b47ad81064b64bee9fa8a` both settled, reported zero
pending batches and current unit inputs at that checkpoint. Retained copies are
`.cis/local/rest-client-intake/rc1-unit-pass.trx` and `rc1-architecture-pass.trx`.
The portable result construction vectors were actually consumed; the response
decode/retry literals still await their real producers.

Independent reviews checked ordinary API boundaries, finalized-method retry
eligibility, supported delay limits, bounded metadata before copying, and passive
composition without factory/runtime activation. Required findings were corrected.
Contracts, snapshots, detached results and seams are established; transport,
orchestration, optional retry, host/export and consumers are not yet established.
Cumulative inclusive native cost is 1,363,823 ms, with one logical attempt and no
reservation/recovery hold. Runner-pilot/release/coverage/mutation qualification
remains unclaimed.

The controller conservatively budgets 2.5 shared active hours for RC1 authoring,
reviews, corrections and native work; interactive agent effort is not fully
measured by the native ledger. The delegated redistribution will leave 9.5 hours
for subsequent work inside the same 12-hour total, preserving all 16 logical
attempts, spent native work and restoration/storage reserves. This is no reset
or additional grant.

## RC4 bounded public-API proof — passed native cases; reuse held

The first RC4 logical attempt started at 22:17:43 UTC and settled at 22:39:43 UTC.
Actual pinned-package restore and MSTest execution passed **19/19**, zero
failed/skipped. Retained evidence is `.cis/local/rest-client-intake/rc4-early-proof-pass.trx`,
`rc4-early-proof.stdout.log` and `rc4-early-proof-run.json`; execution
`native/incremental/b633c35e534840a2924f80a81cf7eb09`. Polly.Core resolved to 8.9.1
with the reviewed empty net10 dependency group. No additional NuGet ID was needed;
Core's closure is unchanged. The Integration lock adds only the optional project
and approved Polly dependency. The Metrics lock was rewritten without semantic
changes during restore.

Real upstream timers establish long/short/date/past/invalid/missing Retry-After,
one chosen/admitted delay, no early dispatch, insufficient-budget preservation,
cancellation in supplemental/native waits, held attempt cleanup, zero-retry policy,
finite actual-method/replay eligibility, exhaustion and narrowed decisions.
Pinned Task.Delay truncates native delays to milliseconds; the adapter admits
ceil_ms(max(selected local delay, server minimum)) once and waits its supplemental
remainder plus upstream's unchanged local delay. Actual scheduled time equals that
admitted value; no jitter is resampled and the server minimum is never shortened.

CIS correctly rejected result reuse because restore changed lockfile bytes and
the initial profile mistakenly included generated AssemblyInfo.cs from obj.
Its overall status is therefore failed/input-changed, not a reusable passing run.
The actual 19-case TRX remains valid native proof of the public delay bridge.
Remove generated inputs, include the new lock and package binary, refresh the graph
and require a later locked-mode passing/current checkpoint before RC4 completion.
No force/reuse override or fabricated qualification is used.

This first part used one logical attempt and 290,201 inclusive measured native ms.
About 25 shared active minutes conservatively cover its authoring, coordinating,
graph preparation and review as well, inside its 1,800-second limit. The bounded
public-API question is answered; further policy/real-operation integration remains
inside RC4's original task capacity. No hold or active reservation remains.

The controller's conservative planning debits now total four shared hours:
RC1 150 minutes, RC2 25, RC3 30, RC4 25 and RC6 10. These include measured native
work and estimate unmetered interactive effort; they are not claims of exact
measurement or another allowance. Eight shared hours remain for implementation,
tests, documentation, review and restoration inside the original 12-hour total.

## RC2/RC3 components and RC6 binding — correction checkpoint

Execution `native/incremental/0a55383291cb41ec8d9e035a811c236c` compiled the full
Core and selected **132 cases: 131 passed, one failed, none skipped**. The failed
Kestrel test committed headers without flushing a body chunk, then waited for a
release; the client correctly remained in Send until the test budget expired.
The fixture now flushes its first byte before signalling header readiness and
holds the remainder/EOF. Its actual read and server work remain joined in finally.
Original stdout/TRX are retained as `rc23-components-first-failure.*` in the local
intake evidence directory. Inclusive native cost was 465,672 ms; no reservation
or recovery hold remains and WORK-003:1 stays open for the bounded correction.

Review also found that a retained authentication view could start late signing
work, or start materialization and return without awaiting it. The authentication
scope now owns one cached materialization task, closes further access, joins that
actual task and clears native-header/signing delegates when sealed. Two paired
real-lease regressions cover successful bytes and the original unexpected fault.
The next checkpoint therefore has 134 cases, not a claim that 132 passed.

The controller moved 30 unreserved minutes from RC4 to RC3 for these fixed
callback/ownership corrections. `work-allocation-extensions.json` records the
same 43,200-second/16-attempt total and all original reserves/consumption. This
uses the user's delegated distribution; no new scope or allowance is introduced.

### Passing component and locked optional checkpoints

The corrected Core run passed **134/134**, zero failed/skipped, current unit inputs
and zero pending batches at that checkpoint. Execution
`native/incremental/ba6fdecdc301435ab18876d85338c20f` includes 36 RC1 regression
cases, **44 native content/transport cases, 40 callback/mapping cases and 14 REST
metric-binding cases**. Evidence is `.cis/local/rest-client-intake/rc23-components-pass.trx`.
Real Kestrel, actual headers/bytes/cancellation/disposal and actual signing-task
barriers are exercised; runtime entry/deadline/host composition is still pending.

Locked optional integration then passed **36/36**, zero failed/skipped, current
unit inputs and zero pending batches. Execution
`native/incremental/46d18a0a9e05422487ef910281f60937` covers **25 retry-policy cases
and 11 collector cases**; retained TRX is `rc46-optional-pass.trx`. The strict
pre-REST profile, conditional catalog, final passive composition, both registration
orders, mismatched/opaque metadata guards and two actual root-factory native HTTP
exports pass. These use the fixed module seam; actual public RestClient operation,
payload/retry and shutdown proofs must still join WORK-005 before RC6 completion.

The corrected optional profile fingerprints handwritten source and generated
locks/package bytes, excludes bin/obj, and uses actual locked restore. This resolves
the earlier changed-input hold through new native verification without force/reuse
overrides. The actual Windows lane and unit-only pilot advisory remain explicit;
no full-platform, coverage, mutation or release qualification is established.
Cumulative measured inclusive native work is 2,854,795 ms, four logical attempts,
with no active reservation/recovery hold. Interactive effort remains conservatively
budgeted separately inside the same shared total.
| WORK-001 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1206; failed=179; possibleTokenSavings=310; ledgerDigest=sha256:277e7ca2f9b400c097ed8156d669aca4a7030605689252a4c608c631a4809bca |
| WORK-002 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1236; failed=181; possibleTokenSavings=310; ledgerDigest=sha256:a64deaf512418b1fb8c456d1f0149cde518f170c6f672a36c31760f06b53fc48 |
| WORK-003 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1237; failed=181; possibleTokenSavings=310; ledgerDigest=sha256:758459b758eaa5f393142da0f309b1043a3819e5dbb16baa7193b40c28efc699 |

## Public runtime and host checkpoint

Execution `native/incremental/447cf65639a74260aae535eda1a96253` passed **93/93**
actual integration cases, zero failed/skipped, current unit inputs and zero pending
batches. Retained evidence is `.cis/local/rest-client-intake/rc5-public-pass.trx`.
This joins 25 policy cases, 11 collector cases and 57 public runtime/registration,
result, replay, payload and host cases. Named/None Core composition, entry-based
caller/total/stopping/attempt precedence, same-instance programming faults,
prepared signing replay, later no-response status, actual partial/retried bytes,
late Metrics attachment and all four HB-only/Startup shutdown rows pass.
The Startup coordinator retains its sequential responsibilities and no REST dependency.

The first profile launch rejected duplicate source/config paths before compilation
(`d1e81003a1c34c0b8dbbae10463febc4`). A subsequent compile found CS0108 in the new
prepared-body member; renaming it WireBytes corrected the collision
(`c3c13e6e4daa41f98f665057ee2b8ab9`). The first actual 93-case execution passed 89
and failed four (`3320baf4af1c4a0789173bb0a8f053ee`): two HB-only fixtures used an
unsupported Web host tail; two actual custom encoder faults were obscured by
native cancellation. Its stdout/TRX remain `rc5-public-first-failure.*`; a command
capture reported still-open streams, and subsequent custody/status inspection
confirmed no running child, active reservation or recovery hold before correction.

HB-only rows now use the real Generic Host, preserving the existing ordering
contract. The content lease closes serialization admission and joins actual
encoder work before disposal, retaining the original fault independently of a
native cancellation wrapper. Runtime classification then preserves that fault;
independent cleanup failures remain paired. Additional two-case direct ownership
regressions are authored and await native unit execution before WORK-005 closes.

Source review also corrected received-status preservation during cancellation,
HEAD/no-value presence, original pre-cancel OCE provenance, known caller-source
cancellation, canceled one-shot acquisition replayability and typed primary-stop
admission. A prepared wire descriptor bypasses a custom encoder only for explicitly
bounded, already materialized signing bytes; ordinary caller Bytes still use their
codec. No broad exception-type inference or second retry owner was introduced.

The started-task path-update request was rejected without mutation; existing task
contracts/history remain intact. The already-covered HTTP façade and shared P04
seams are integrated under the original authorized outcome. `work-allocation-delivery.json`
redistributes task capacity within the same 43,200 seconds, 16 attempts, 1,200-second
restoration allocation and physical 6 GiB ceiling. Conservative controller budgeting
now reserves 8.5 shared active hours for completed/current work, including all agents,
measured native work and interactive review; 3.5 hours remain inside the original
12 hours. This estimate is deliberately separate from the ledger's measured native
4,006,594 ms and is not another allowance. Five logical attempts are used; no active
reservation or recovery hold remains at this checkpoint.

The unit-layer pilot advisory remains qualified=false. These passing Windows
checks establish the stated behavior, not full platform, coverage, mutation or
release qualification. Example/package delivery and proportionate final gates
remain WORK-007; no publication, push or main merge is authorized.
| WORK-004 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1258; failed=187; possibleTokenSavings=310; ledgerDigest=sha256:2ac10c1ef0bca0926bc51f43d92779a19d3895fc9b024643bed018ac3dd6a8fe |
| WORK-006 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1261; failed=188; possibleTokenSavings=310; ledgerDigest=sha256:3eb73e9836003ac4326c84f9f6a0396482e657f0899d740f50c8c72e62e18cc5 |

WORK-005's additional component run passed **136/136**, zero failed/skipped,
current unit inputs and zero pending: execution
`native/incremental/5524f9dac49946659cc74182abb059a7`, retained
`rc5-components-final-pass.trx`. It includes both new HTTP/IO encoder ownership
regressions and the prior 134 contracts/content/callback/metric-binding cases.
An earlier attempt to use the completed WORK-003 profile was held before native
execution to preserve its required evidence; its 61,592 ms preparation cost is
retained. The fresh WORK-005 profile uses a distinct result directory and preserves
the original owner and receipt. All final delivery profiles likewise need their
own current owner and output identity. Total inclusive measured native work is
4,479,487 ms, five logical attempts, with no active reservation or recovery hold.
| WORK-005 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1268; failed=189; possibleTokenSavings=310; ledgerDigest=sha256:938fc89a7ed35e3f9397f4553d62a59af9ff35ac0598eeee9fe1131de9218a86 |

## Final Windows delivery gates — 18 November 2026

The actual RestApi source example and both package consumers are implemented.
The following fresh, distinct WORK-007 profiles all passed with zero failed or
skipped cases, current declared inputs and zero pending unit batches at their
respective checkpoints. All use the pinned Windows SDK 10.0.401/runtime 10.0.12;
the unit-layer correctness-pilot advisory remains qualified=false.

| Gate | Passed cases | Native execution suffix | Retained TRX under `.cis/local/rest-client-intake/` |
| --- | ---: | --- | --- |
| Affected REST/Metrics/Startup/Heartbeat/Configuration unit tests | 1172 | `8483773105df4b37ae67a35e5f7195b9` | `rc7-final-unit-pass.trx` |
| Affected host integration, including the final encoder/signing cancellation cases | 201 | `5f40c6a5bd2a478fbec7ff53a55d6162` | `rc7-final-host-pass.trx` |
| Complete existing Architecture project | 361 | `4019e07d7818487483c0fb6d4e47893b` | `rc7-final-architecture-pass.trx` |
| Complete existing Compatibility project | 179 | `7685e02da348474db66a8a729b84e171` | `rc7-final-compatibility-pass.trx` |
| Actual Core-only and optional package consumer story | 1 | `9852124c6de44d3cab80365c5098d4c6` | `rc7-final-package-pass.trx` |
| Locked actual RestApi source example story | 1 | `a488c003f0d2446cb90e42ca855d7442` | `rc7-final-business-pass.trx` |

These executions have the prefix `native/incremental/`; together they comprise
**1915 passing checks** across six gates. The package story packs the actual three
candidate assemblies once and exercises build and folder-publish deployments of
two ordinary public-API consumers with separate initially empty caches. The
Core-only consumer uses None without optional retry or Metrics types/dependencies.
The optional consumer compiles the actual RestApi example sources with versioned
Core SDK and optional PackageReferences. Actual loaded paths, package/deployed
assembly hashes, Polly 8.9.1 and DiagnosticSource 11.0.0.0 are verified. It uses the
existing package/process fixtures; there is no replacement package harness.

The source and optional deployed example exercise JSON POST, awaited
authentication, explicit retry GET with three actual attempts, an HTTP 200 business
rejection, 204 absence, caller cancellation before dispatch, five logical metric
completions, native outgoing observations with central identity, and drained
shutdown of both real hosts. The example's reported runtime and stopped state
are asserted by the business/package tests. Earlier source/package passes are
retained separately; the table records the final production inputs.

The final P04 correction identifies actual operation/caller cancellation at the
encoder catch boundary, retains an unexpected original encoder/signing fault,
closes callback admission and joins active encoding before releasing resources.
Runtime classification consults the retained fault after actual disposal and
preserves independent cleanup failures. The final host run includes three public
boundary cases distinguishing actual caller cancellation from an unrelated
pre-cancelled signing exception followed by later caller cancellation. Public
fault identity and faulted task state remain intact. None and the standalone
client remain in Core; optional Polly owns retry, ordinary keyed DI owns hosting,
and the sequential Startup coordinator has no REST or Heartbeat dependency.

Independent source reviews covered the whole implementation, host ordering,
public/package example composition, native callback ownership and final P04 fix.
No new DI, Configuration or lifecycle framework, collector SDK in Core, test
harness or tooling campaign was introduced. The frozen compatibility consumers,
controlled shipping/dependency inventories and historical approvals remain intact.
The only existing lockfile with a semantic dependency change is the Integration
project's approved optional-project/Polly addition; other observed lockfile changes
are line endings from native restore. Core's approved dependency closure is unchanged.

## Focused mutation followup

The selected scope is the **entire RestResponseMapping.cs**, using the existing
pinned Stryker 4.16.0 on the pinned runtime, with the break threshold unchanged at 90.
The first two profile submissions were rejected before native execution: an
unsupported toolFiles field, then 574 configuration inputs exceeding 128. The
corrected profile has 64 configuration inputs and one whole source file. These
validation failures are retained and are not represented as executed mutants.

Execution `native/mutation/71cbf40ed6aa49708d30cc14abd2bfe6` produced22 mutants:
15 killed, 5 survived, 2 ignored, denominator 20, **75%**, no timeout/no-coverage or
incomplete outcomes. It is below the required 90 and is not a passing score.
The CIS canary label only establishes valid native kills; it does not certify
the score or qualification. Its original summary is retained at
`.cis/local/testing/mutation/rest-client-mapping-mutation/runs/canary-20261118-031338-b02b06055c0642e9a5a5d377aa37cee9/summary.json`.
Native Stryker exited 2 and the separate normal rebuild/postflight exited 0.
CIS cleaned the successful-canary raw report after assessment (reported SHA256
`789f2cdac39f6e09a9f34875eade1cba4c9c34480da982aa15296081d0032368`).
The existing small postflight invocation glue now copies future immutable native
report bytes to the counted intake before controller cleanup, then rebuilds with
CisMutationTesting=false and runs the normal REST unit selection. No CIS tooling,
pilot requirement, mutation source selection or threshold was changed.

Review found missing useful checks for mapper-provided explanations, detached
multi-value case-insensitive response headers and null extension decisions. These
were added for the same-scope followup. A contradictory fabricated decoded value
on an HTTP error and exact default punctuation were explicitly rejected as
score-driven assertions. Followup score and restoration are recorded below when
the native execution settles.

At this checkpoint inclusive measured native work is 8,442,087 ms, six logical
attempts used, with no reservation/recovery hold. The separate conservative
controller/all-agent/native estimate is 10.6 shared active hours of the approved 12;
the native ledger's remaining time is not an additional all-agent allowance.
All original 43,200 seconds, 16 attempts, 1,200 included restoration seconds and
physical 6-GiB/64-MiB restoration reserve remain unchanged. Windows scoped proof
does not establish full-platform, coverage, complete mutation or release
qualification; the full 95/90/90 obligations remain. Publication, push and main
merge remain outside this instruction.

### Retained same-scope 85% observation

Execution `native/mutation/feb1a8e49b78479bb97206e6e73fc340` settled with
17 killed, 3 survived, 2 ignored, denominator 20, **85%**, no timeout/no-coverage,
compile/runtime/pending/unknown outcomes in the declared mapping scope. It remains
below 90. The immutable native report is retained as
`.cis/local/rest-client-intake/canary-20261118-033108-d25d07d7e5f84a29b70b4d14ac05258d-native-result.json`;
its actual SHA256 matches the CIS assessment:
`87e19ab9c76992b894a7b987092a24abb0146b486ee865609c41456e52b08e0b`.
The separate normal rebuild passed without warnings/errors and 133 REST unit cases
passed, zero failed/skipped, retained beside that report with suffix-postflight.trx.
The restored Core DLL matches its pre-mutation SHA256
`6dec35c7aca41dc297642d0f7dfefecb28f0ce7de8209b66786e69575904de0f`.

The actual survivors are: forcing the successful-value conditional true where the
normal error reader already supplies absence; changing ConfigureAwait(false) to
true; and removing the default explanation's final period. The first and third
remain honest survivors. Independent review confirmed an existing Healthcheck
test precedent for library-owned completion without pumping the caller context.
One deterministic mapping case now uses that same small private held-context
pattern: its mapper returns an actually pending task, completion is released,
and the operation must finish without a caller-queue post. Cleanup always releases
and joins mapping, including when the assertion fails. This does not promise that
a user mapper's own awaits avoid its chosen context, or endorse sync-over-async.
Production code, the whole-file scope, denominator and 90 threshold are unchanged.

Inclusive measured native work is 9,116,957 ms; the separate conservative shared
effort estimate is 11.25 active hours at this checkpoint. Six logical attempts are
used and WORK-007:1 remains open. The final bounded same-scope run and closeout
must fit the original shared totals, without spending the ledger's unmeasured
controller/agent remainder twice.

### Final focused mapping result and normal restoration

Execution `native/mutation/c1edaadb27564f8d911fcb24df324e25`, run
`canary-20261118-034603-4f4e9084829b4203802f41067f8ccc00`, passed the unchanged
**90%** threshold: 18 killed, 2 survived, 2 ignored, denominator 20. There are zero
timeout, no-coverage, compile/runtime, pending or unknown outcomes in this scope.
CIS reports validEvidence=true, thresholdMet=true, inputs unchanged, and no
diagnostics. It retains partial=true/qualified=false; this is component evidence,
not runner-pilot or whole-release qualification. The two survivors are the
supported-reader-input-equivalent conditional and default final punctuation;
both remain in the scored denominator, without fabricated assertions or exclusions.

The immutable raw report is
`.cis/local/rest-client-intake/canary-20261118-034603-4f4e9084829b4203802f41067f8ccc00-native-result.json`,
SHA256 `66cdb57961c6942c3849ae782d30f542bea6e5241e0fd1e9e33881e06661107a`,
matching the native assessment. The normal postflight rebuild then passed with
zero warnings/errors and **134/134 REST unit cases**, zero failed/skipped. Its TRX
is retained beside the report with suffix-postflight.trx. Core's actual DLL again
matches the original pre-mutation hash above. No production source changed after
the six final delivery gates; the subsequent mapper test additions are covered
by this final normal postflight. The earlier broad receipts describe their own
checked inputs, rather than forced current reuse after later edits or commits.

The original specification and companion guide, and both pre-existing user
launchSettings files, still match their recorded SHA256 values. Strict docs
validation passed 169/169 with zero warnings/errors; the existing one skill
validated and deterministic strict/no-LLM audit was clean. Independent final
scope review found no outstanding implementation blocker. The focused native
mutation configuration is now linked from the existing verification README.

## Bounded delivery completion

All required implementation outcomes and scoped verification are complete.
Inclusive measured native work is **9,623,579 ms** across six logical attempts;
WORK-007 used 5,144,092 ms and one logical attempt. The separate conservative
shared active estimate is **11.75 hours of 12**, including all agents, controller
work, native execution, review, documentation and restoration. This estimate is
not an additional allocation and does not treat the ledger's remaining native
capacity as unused all-agent time. The original 12-hour/16-attempt totals and
1,200 included restoration seconds remain unchanged.

The final recorded counted-storage peak is 5,839,159,770 B under the original
6,442,450,944 B ceiling, with the 67,108,864 B restoration reserve preserved.
Both native mutation and normal postflight have safelyStopped=true and no
settlement errors. Work status confirms zero active reservation, no recovery
hold and known recovery. No additional test launch is required for this delivery.
The original 75%/85% observations, profile validation failures and all earlier
failed attempts/costs remain recorded; no score, source hash or approval was
rewritten. Final strict documentation and existing-skill checks pass, with
deterministic audit explicitly disabling model review.

The bounded task completion establishes the implemented module and the evidence
above. Ubuntu, complete platform/release coverage, full 95/90 coverage and
whole-library 90% mutation obligations remain unproven. No publication, push,
main merge, branch closure, product-foundation approval or new tooling campaign
was performed. Future work requires its own applicable authority.

Supported CIS progress closed WORK-007:1 and `plan task transition` marked
WORK-007 Complete. All seven canonical tasks are now Complete; the outcome's
original approval, allocation and historical records remain preserved. There is
no open logical attempt or active native reservation. The implementation remains
on its local branch for review; this task does not close or merge that branch.

## Configuration source-consumer correction — 18 November 2026

Andrew reported both Console/Web rows of ConfigurationSourceConsumerTests failing
their exact-Core-byte guard after rebuilding the candidate. Review confirmed the
guard was valid: both source-host output directories contained an older 650,240-byte
Core assembly, while the current Release candidate was 904,704 bytes. The retained
before-state hashes are in `.cis/local/rest-client-intake/configuration-source-before.json`.
The Integration project had no build dependency on either source host or ExampleLate;
the earlier Configuration preparation script had built those separately, masking
this missing permanent dependency. The previous 1915-case delivery selection did
not include these two root-namespace source-consumer cases.

The correction adds three ordinary build-only ProjectReferences to the Integration
project, matching its existing fixture pattern. ReferenceOutputAssembly=false,
Private=false and PrivateAssets=all keep these as build prerequisites. Existing
Type Manager targets select the native RID and regenerate the host inventories.
The test, exact-byte assertion and runtime source remain unchanged; no testhost
build, hand-copied Core replacement, new harness or lifecycle change is introduced.
Independent review confirmed this is the smallest durable correction.

WORK-007 remains Complete. The attempted Complete-to-InProgress transition was
rejected without changing it; Ready was also rejected as an unsupported status.
Supported task organization added WORK-008 under the existing delegated outcome.
It receives 900 existing seconds and the unused WORK-007 attempt, reducing that
owner's distribution to 8100 seconds/one already-used attempt. The total 43,200
seconds, 16 attempts, 1200 restoration seconds, 6-GiB ceiling and 64-MiB reserve remain
unchanged; all original spent work and completed owners are preserved.

The focused native invocation refreshes the new restore graph, verifies locked
restore, then builds the ordinary Integration project and runs exactly the two
reported cases. Its changed Integration lock is a generated restore output for
this initial correction; the existing Core/optional locks and source inputs stay
fingerprinted. Actual result and final settlement follow below.

### Actual source correction observations

The initial filter-template submission was rejected before execution (887 ms),
followed by a stale-graph rejection (91,896ms); no product test ran in either.
The refreshed invocation `native/incremental/e7fc3745ed9745d1b0f66c2ee2c73772`
then passed both actual Console/Web cases with matching current Core SHA256
`9fa0b615db448bf24b7f98ff7729ad3ebe2d82eb14ecda13816a5cffbbfe1bc3`.
Its retained TRX is `.cis/local/rest-client-intake/configuration-source-correction-pass.trx`.
The native pass was held for reuse because force-evaluation regenerated the five
fixture lock files during execution. Git shows no semantic lock/dependency change;
their exact input bytes changed. This remains a held receipt, without forced reuse.
WORK-008 consumed 642,260 ms in its single open logical attempt. The final logging
run below repeats both cases against the now-current locks and new Core bytes.

## Automatic REST completion logging

Andrew explicitly requested the REST log format and approved one additional shared
active hour for this correction and logging: **46,800 seconds/13 hours total**,
with 16 logical attempts, 1200 restoration seconds, 6 GiB counted storage and 64 MiB
restoration reserve unchanged. The same authorization retains its original
decision/history. A first representation changed global outcome constraints and
held allocation against old scope bindings. The corrected additive
rest-client-logging scope preserves original task bindings; its numeric time
amendment and later explicit user decision supersede the original 12-hour time
statement. This is the same human approval, with a corrected structured record,
not another approval or a reset. WORK-009 receives the 3600 added seconds and one
unused WORK-005 attempt; all containing attempt/storage/restoration totals stay fixed.

Runtime reuses its existing borrowed ILogger and emits one settled logical-call
summary outside the terminal gate, after cleanup and outcome commit, before
public-task publication. Success is Information; finite failures, cancellation
and faults are Error. The final dispatched method and bounded scheme/authority/
escaped path are recorded without query, credentials, fragment, payload, provider
explanation or exception details in either message or structured state. Existing
elapsed duration and actual dispatch count supply the duration and optional retry
suffix. Ordinary logger/factory failures cannot replace the REST result or cause.
The original standalone constructor remains; a distinct four-argument overload
borrows an application's ILoggerFactory. No provider, global Logging runtime,
new dependency, extra clock/metric or lifecycle framework was introduced.

Example message bodies (using actual runtime values):

```text
[REST] POST https://apiexample.com/ 200 Ok 10ms
[REST] POST https://apiexample.com/ 500 Technical Error 10ms (Attempt 2/3)
```

The first new logging build compiled production and found CS1503 in the bridge
test: JsonLine is UTF-8 memory. Converting its span to string fixed that test;
execution `native/incremental/2420b1fd056d4e25827616cb8fff9a3b` retains 450,273 ms
measured cost and its original build log; no testcases ran and no missing TRX is
represented as a test pass. The same WORK-009:1 attempt continued.

The subsequent native run passed **98/98**, zero failed/skipped, on the pinned
Windows SDK 10.0.401/runtime 10.0.12. This includes all 11 new logging cases, both
Configuration source consumers and 85 existing REST runtime/retry/hosting cases;
the existing package-consumer story was excluded. The retained report is
`.cis/local/rest-client-intake/rest-client-logging-final-pass.trx`, with the native
build/test output beside it. Success/error severity, actual final POST/URI,
fr-FR invariant 10.25 ms rendering, one 2-of-3 retry summary, business HTTP 200 failure,
bounded safe structured state, held-cleanup success/cancel/original fault,
disabled/throwing loggers, borrowed factory and the actual queued CIS provider
bridge all passed. No new test harness or mirrored implementation test was added.

Both source consumers again passed their unchanged exact-byte assertion, now
against Core SHA256 `71b63838445c1a0d22756739f2d3836f7715bd6498ea6afcc02be9d1a4108392`.
This second normal project build updates their prerequisites without hand-copying
candidate DLLs. The earlier held locks and receipt remain historical evidence;
final currentness/accounting and owner settlement are recorded below.
| WORK-007 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1311; failed=191; possibleTokenSavings=591; ledgerDigest=sha256:79b9ef1dce5e892b2f98d8207f1a89b95f2e1f0110b5b473c1eacb1dc9fa69f1 |

### Current proof and settlement

Execution `native/incremental/1d0881d9e9904c6197b68356c32ccf10` completed the 98-case
selection with scopeComplete=true, unitCurrent=true, zero pending batches and no
failed/skipped cases. The unit-layer pilot advisory remains qualified=false; this
is actual bounded Windows behavior evidence, not whole-release qualification.
The final proof covers both WORK-008's source-build correction and WORK-009's
logging through the same real Integration build and unchanged child-host guards.

Inclusive measured native work is 11,075,768 ms shared: 642,260 ms for WORK-008 and
809,929 ms for WORK-009, each using one logical attempt, eight used of 16 overall.
The broader original results, original authority/history, failed preparations,
compile error and held receipt remain preserved. All agents, native work,
controller activity, documentation/review and restoration share the approved 13
active hours; the conservative completion estimate is 12.9 shared active hours,
not a measured ledger remainder or another allowance. Storage and restoration
limits are unchanged. All three current user launchSettings files are excluded
from staging; the RestApi file's recorded SHA256 is
`496e4bfdfc50d218c6811f947c26dfe72984782fe51a21916cb113f7d5f08f85`.
Native result currentness describes these checked inputs; later commits do not
turn historical receipts into unconditional reuse. Full platform/coverage/mutation,
publication, push and main merge remain outside this correction.
| WORK-008 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1346; failed=199; possibleTokenSavings=591; ledgerDigest=sha256:4ce7129adfb72db652e36d81411b1d945d971d32764e71dfe3e564816e06a3da |
| WORK-009 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1348; failed=199; possibleTokenSavings=591; ledgerDigest=sha256:f2b11946bd45980b137a2d02b468fdefb64f9a4a43b4fb75ff26ecde1d57a9c8 |

WORK-008 and WORK-009 are now Complete through supported CIS transitions; all nine
canonical tasks are Complete. Their real logical attempts are closed, with zero
active native reservations and no recovery uncertainty. Strict documentation
validation passes171/171 with zero warnings/errors. No further native execution
is queued; all three protected user files and both original supplied sources
retain their recorded hashes.
