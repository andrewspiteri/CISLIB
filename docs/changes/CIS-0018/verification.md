---
title: RM-03 intake and verification record
type: verification-record
status: Draft
cis:
  stable_id: cislib:change:cis-0018:verification
---

# Intake checkpoint — 21 November 2026

Actual main/remote baseline:
`8ee1858ce9d84dc4210bbfda8690f03ecb6e9fbc` (RM-02 merge PR11).
Planning branch: `codex/durable-operations`.
CIS created CIS-0018 as Proposed at that exact baseline. Creation inputs preserve
the one existing untracked CountryData launch settings file; it is not staged.

Imported source SHA-256:
`c39086d03379b2110b8408c1e02798a93c6c2690de6491ee9ffff5e9b1ce9430`.
Protected untracked launch settings SHA-256:
`ea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784`.
The source copy is byte-preserved with a local -text attribute.

Root read the supplied handoff, root guidance, current decisions/dependency policy,
Transactions/Security/Metrics skills and actual integration APIs. Independent
read-only intakes examined protocol/normalization (8 estimated source minutes),
production PostgreSQL/concurrency (10), and authorization/observations (10).
No required existing-contract blocker or new runtime dependency was found.
Recommended routine implementation choices and all14 acceptance groups are mapped
in the [plan](implementation-plan.md) and [reconciliation](reconciliation.md).

Current physical observation:
2,960,996,728 filesystem/native bytes, plus conservatively retained1GiB Docker
allowance and64MiB reserve, leaving2,340,603,528 bytes within6GiB.
Ignored `.cis/local/operations-intake/baseline.json` retains exact roots/counts.
No cleanup or capacity increase occurred.

Preparation only: no implementation code, native build/test/pack/restore,
Docker/SQL operation, installed dependency or public helper/skill scaffold.
No runtime/provider/package/exporter/coverage/mutation/performance/release proof.
RM-02 results remain historical evidence for that module, not RM-03 passes.

The six draft contracts and proposed allocation are review inputs. No human
approval, outcome authorization or eligible native work owner is manufactured.
Canonical task organization/validation/allocation follows the one actual fresh
decision. Existing completed/closed owners stay unchanged.


## Draft readiness

[Independent draft reviews](review-intake.md) used4+4 additional source minutes
and found no material required correction. Read-only structural checks confirm
six medium implementation contracts, six declared scopes, acyclic dependencies,
positive finite ordinary allocations and exact sums115200seconds/36attempts/
3600restorationseconds, with the original6GiB/64MiB storage values unchanged.
Local Markdown links resolve. No authorization.json, canonical plan.json or
executable agent task was created before an actual user decision.

Conservative external intake source estimate: root60minutes plus28focused-agent
minutes plus8draft-review minutes =96minutes (1.6hours), proposed against WORK-001
within the shared allocation. This is an estimate, not automatic CIS consumption;
no product-native duration or implementation attempt is claimed. Ignored
.cis/local/operations-intake/resources.json retains that breakdown. All future
implementation/tests/documentation/review/restoration share the same proposed
limits, including this already-performed intake.

## WORK-001 portable foundation — actual native checkpoint

The approved outcome and shared allocation are applied; the canonical six-task
plan is valid. WORK-001 delivered eleven Core files, three focused Unit files,
twelve exact portable byte/digest vectors and the portable Operations contract.
Definition registration is passive, capped at16 and does not register a throwing
runtime service. Actual Execute/GetResult orchestration remains WORK-002.

Pinned ordinary formatting and verify-no-changes passed. The latest affected
Release build passed with0warnings/errors. Exactly28new cases across
OperationCanonicalTests/OperationContractTests/OperationRegistrationTests passed
on actual Windows and Ubuntu24.04, zero failures/skips. Both execute the same
current Core bytes, SHA256
6bcf070211acae8ee20299a5de605d8b78e0f76df50fb00d50f110538b8a7d9d.
SDK10.0.401/runtime10.0.12/rollForwardDisable remain unchanged. Receipt:
.artifacts/operations/CIS-0018/WORK-001/foundation-receipt.json, with source,
TRX and vector hashes, exact class scope and runtime config.

Early source review found a real passive-DI descriptor bypass; full pre-mutation
validation now rejects opaque/keyed/duplicate/oversized or mismatched metadata,
with focused regression cases. The first build exposed a nullable test assertion;
Assert.IsNotNull now expresses its precondition. Initial Windows execution then
caught a genuine absent-payload getter defect: the conditional null arm selected
ReadOnlyMemory's implicit array conversion, producing present empty memory for
rejection. The explicit nullable arm fixes that boundary; the existing mutable-
bytes/rejection regression now passes. The other nullable-byte consumers were
inspected; no duplicated affected getter remained.

That initial case-insensitive namespace filter also selected115retained
OperationScope cases, all passing, plus27newpasses/1newfailure. Its143-case TRX,
the failed build and all actual costs are preserved. Latest filters include the
namespace's trailing dot and run exactly28new cases on each OS. Incidental older
passes are not advertised as latest-candidate qualification.

Independent source/correction/necessity review is in review-001.md. The small
closed frame directly supplies the required portable encoding; no rules engine,
DI/runtime framework or test harness was added. Vector preimage hex was manually
specified; root computed SHA256 constants from those bytes without invoking Core
encoding. No database, durable coordinator, telemetry or package-consumer pass
is inferred from this foundation.
| WORK-001 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1909; failed=270; possibleTokenSavings=591; ledgerDigest=sha256:0d3bd10c2e23f7ea158d297d97861e5b714d1617d726b5352e9b82d8d6342bdf |

## WORK-002 authorized coordinator — actual native checkpoint

The coordinator now authorizes the resolved resource before execution, replay or
lookup; uses one shared weak context-admission marker; and sequentially awaits
claim, outside-callback handler, encoding/decoding and terminal write. Prepared
remains uncommitted. Admitted faults and cancellation poison the original context
without replacing the fault or the first rollback reason. Fresh result lookup
owns and actually settles only its ReadCommitted read owner. Known committed
Found results survive read cleanup faults; transient mapping is restricted to
native Begin/Read phases.

Scoped pinned formatting and verify-no-changes passed. The final affected Release
build passed with zero warnings/errors. All84 focused Operations cases passed on
each actual Windows and Ubuntu24.04 lane, zero failures/skips:28 retained foundation
cases plus56 new runtime cases. The seven test classes cover authorization and
fault boundaries, cross-instance/type nested and concurrent admission, exact
context and callback sequencing, result fidelity and version/expiry precedence,
and fresh lookup cleanup. Controlled journal/ADO seams use actual RM-02 owners;
they do not prove SQL uniqueness or atomicity.

Current Core SHA256 is
f0ade871822e81f22554c256d0cd230eb9e507fab961351347c1b8f6ef82dbd3,
matching the executed Unit output. The exact receipt is
.artifacts/operations/CIS-0018/WORK-002/runtime-receipt.json, including all22
current Operations C# source hashes, test assembly/TRX hashes, class counts,
portable vectors and runtime configuration. SDK10.0.401 and .NET/ASP.NET10.0.12
with rollForward Disable remain unchanged. The initial build's three CS0411
throw-only test-handler inference errors and measured costs are preserved;
explicit generic arguments corrected those three calls before the clean build.

Independent review is recorded in review-002.md. The bounded passive registration
snapshot fixes the provider/collection mismatch without invoking opaque factories.
No DI framework, alternate runtime, SQL simulator or test harness was introduced.
The observation task's existing approved scope now also binds the already-approved
execution-recovery paths needed for its narrow public-boundary decorator, passive
hosting wiring and captured LogCalls option. Canonical task update was applied
under delegated organization; overall scope, tasks, packages and limits did not
increase. No PostgreSQL, telemetry exporter, package-consumer or release pass is
inferred from this coordinator checkpoint.
| WORK-002 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1918; failed=270; possibleTokenSavings=591; ledgerDigest=sha256:da4f0cf4b5e45cbdb70aff072456f9c2678f85a0302428543e62914c88839ba6 |

## WORK-003 production journal — actual PostgreSQL checkpoint

The provider-free BCL ADO.NET PostgreSQL adapter and packaged version1 SQL are
implemented. Installation is explicit and one-time; an existing or partial table
installation fails. Registration performs no migration. Full five-field ordinal
identity uses C collation and a real nondeferrable primary key. Claim arbitration
uses INSERT ON CONFLICT DO NOTHING RETURNING and a separate subsequent read.
Only the original native xid can finalize its incomplete claim exactly once;
reads exclude the current transaction's own uncommitted row. Claim-only SQLSTATE
mapping occurs after RM-02 poisoning, without statement or handler retry.

The latest affected Release build passed with zero warnings/errors; scoped pinned
formatting and verify-no-changes passed. Exactly19 provider cases passed on each
actual Windows and Ubuntu24.04 lane, zero failures/skips, against the new isolated
PostgreSQL18.7 fixture with normal durability. Cases cover atomic visibility,
commit/replay/conflict/rejection, independent-session races and blocked sessions,
rollback/disposal retry, cancellation/native timeout/55P03, unsupported isolation,
incomplete and wrong-version protection, strict installation and the full2488-byte
identity using uncompressed PLAIN storage. The existing atomicity case now
submits a forged payload account: the handler receives the authorized resolved
account and its ordinary retry replays the same original result.

Receipt: .artifacts/operations/CIS-0018/WORK-003/provider-receipt.json.
Current Core SHA256 f2576bc062d64f95180bd79e644e56865a8f69d8fe12ff97a13ac6a33f988329
matches source, Unit and Integration output copies. Independent review aligned
all32 source hashes, Integration assembly, both19-case TRX reports and the actual
workload facts; review-003.md has no remaining required finding.

The separate workload-evidence.json uses the task's review evidence route and
retains one actual end-to-end prepare/commit/independent lookup/replay observation
per OS. Each stores40 identity bytes,20 result bytes and a192-byte journal row,
with1 journal/business/outgoing row committed together. Observed elapsed times
were370.7554ms Windows and301.3922ms Ubuntu; these are bounded workload facts,
not a benchmark or installation-only substitute. Resource/candidate/row-shape
facts remain separate from the component case totals.

The new fixture has zero live client sessions and zero remaining case schemas;
its observed data footprint is67035136bytes. provider-idle-handoff.json records
intentional custody under the same approved shared outcome for dependent
WORK-004. No owner or native transaction remains active. Only that task, after
canonical admission, may use this fresh private service and perform exact owned
restoration. The full1GiB allowance remains inside6GiB, with64MiB protected.
No historical fixture or existing database is reused.

The initial read-only dependency-count assertion assumed54 and failed. Actual
baseline and current Integration locks both have55 packages, including the already
approved Npgsql10.0.4. All identities, versions and content hashes match, and SDK,
runtime, NuGet source/mapping and central pin files are unchanged. The original
failed assertion is preserved in dependency-retention-initial-check.json; the
corrected dependency-retention.json records all55 actual baseline entries. No
package or approval payload was changed. Explicit retention maintenance and
simulated acknowledgement-loss recovery remain WORK-004; no network-fault,
production deployment or release proof is inferred.
| WORK-003 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1925; failed=270; possibleTokenSavings=591; ledgerDigest=sha256:855e5e81fe411be245c7eaef0916e1b6db41097e7ca78faa9f5000b873a6dd1c |

## WORK-005 observations — actual collector checkpoint

A narrow scoped decorator observes the public Execute/GetResult boundary while
the sequential coordinator stays independent. It captures a known registered
alias, exact host bundle, immutable LogCalls setting and monotonic start before
authorization, then completes after actual method-owned cleanup. Timing remains
null when the clock is unavailable. Late attachment produces no replay and
foreign host bindings are refused. Normal calls are quiet by default; guarded
ordinary diagnostics retain original authorization, cancellation, business and
cleanup facts without request/context/result or exception histories.

Pinned scoped formatting/verify and affected Release builds passed with zero
warnings/errors. Each actual Windows/Ubuntu lane passed116 Operations Unit cases
(32 new observation cases plus84 foundation/coordinator),11 actual SDK exporter
cases and31 affected catalog regressions (10 Transaction,11 REST,10 Security),
zero failures/skips. Real exporter assertions cover mandatory identity, standard
buckets, registration order, host isolation, disabled selection, exact profile
capacity and lookup timing through held read cleanup. The two conditional timers
reserve208 execute and144 lookup combinations:352 total. With RM-02 selected,
the profile is26 streams/3808 combinations within unchanged global limits.
Prepared is measured as preparation, separately from actual transaction commit.

Receipt: .artifacts/operations/CIS-0018/WORK-005/observations-receipt.json.
Independent review aligned14 current source/doc hashes, all Core and collector
copies, Unit/Integration assemblies and six exact native TRX reports. Core SHA256
is f2576bc062d64f95180bd79e644e56865a8f69d8fe12ff97a13ac6a33f988329.
review-005.md is Complete with no remaining required finding or framework/harness
expansion. These collector cases establish no production deployment or release
proof. WORK-004 retention and WORK-006 final current-package integration remain
separate active outcomes.

WORK-006's public operation helper also passed25 focused cases on each actual
OS. The bundled skill validator passed; an independent fresh-context usage review
found no blocking link/API ambiguity and prompted the now-qualified resolved-
account example correction. The existing outside-friend Windows package case
passed once on the interim f2576bc0 candidate using a fresh local feed/cache,
actual deployed/loaded Core/CommonTesting hashes and embedded/packaged SQL checks.
That interim package result is retained; a final current-candidate package run
follows WORK-004's new Core source. Full Linux SDK-backed package execution stays
unverified under the approved profile; no SDK/tooling installation is added.
| WORK-005 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1928; failed=270; possibleTokenSavings=591; ledgerDigest=sha256:1b017a939475897a66fdfefe7cc6a376da8bf5cd36baa0fb27e8afa02b78863d |

## Final RM-03 integrated candidate and restoration

The final Core candidate passed **219 focused cases on each actual Windows and
Ubuntu24.04 runtime**, zero failures or skips:119 Operations Unit (including three
fresh-maintenance-owner guards),25 public-helper Unit,33 real PostgreSQL journal/
retention cases (19 retained plus14 new), and42 actual SDK/affected catalog cases.
The existing outside-friend Windows package consumer also passed on that same
candidate using a fresh local feed/cache, actual restore/build/child execution,
restored/deployed/loaded Core/CommonTesting hashes and embedded/packaged SQL.
Both affected Release builds passed with zero warnings/errors; all changed C#
passed scoped pinned formatting and verify-no-changes.

Final Core SHA256:
76df3a0cada0f502bd063a56509b74d684dd1e2503fca83a1990e6deb11c6340.
CommonTesting SHA256:
186e29188d446f188ff3d910c9788c27bb11659481e90bd5e6225916daa6b517.
The integrated receipt is .artifacts/operations/CIS-0018/integrated-receipt.json;
WORK-004/retention-receipt.json retains the same cohort. It records46 source/vector
hashes, source/Unit/Integration Core and helper copies, actual collector/test DLLs,
SDK10.0.401 and .NET/ASP.NET10.0.12 Disable runtime configurations, nine exact TRX
reports/classes, workload observations and owned-restoration facts.

The separate privileged maintenance service uses the actual selected journal's
captured settings, existing complete target authorization and a fresh validated
ReadCommitted owner. One deterministic CTE processes at most256 eligible rows;
SKIP LOCKED appears only there and payloads are not fetched. Disabled/Denied
disclose no counts; Executed carries candidate counts and actual completion.
Only known Committed completion exposes confirmed counts. Existing row expiry,
identity and fingerprint are retained when payloads clear; only whole-row
removal ends protection. No schedule, internal batch loop or repair worker exists.

Native cases qualify logical expiry before maintenance, immutable old-row policy,
version/mismatch precedence, tombstones/deletion, the257-row batch boundary,
skipped locks, actual deletion/lookup/arbitration races, a real claim deadlock and
explicit fresh retry, and actual commit/disposal boundaries. The narrow test-only
BCL decorator performs a real native commit and then reports failure through
RM-02: actual Unknown and independent committed-result recovery are observed.
Another native-disposal failure leaves known Committed facts intact. This is
simulated acknowledgement loss, not a real network fault or fabricated completion.

The first33-case Windows retention run passed32 and failed one blocking
observation. Its BEFORE DELETE trigger held while the original committed tuple
still existed, so requiring a unique-index wait was incorrect. The test now holds
AFTER DELETE in the same uncommitted transaction. Production code, causal server
blocking assertions, no-early-handler checks and actual cleanup are unchanged.
The focused correction and full33-case reruns passed; original failure and every
measured cost remain preserved. No assertion was relaxed or result overwritten.

Independent review-004.md aligned all46 sources, DLLs/configs and nine reports;
review-006.md independently aligned its public-helper/package scope. Neither has
a remaining required finding. The focused Operations skill, transaction route and
Metrics skill pass the bundled validator. An independent fresh-context skill use
review and55 local-link checks confirm usable routing/public API spelling.
CountryData remains the one maintained tutorial with its database-free default.

After all consumers settled, actual Docker checks found zero client sessions and
zero case schemas. Only the newly owned container, labelled volume, unshared
newly pulled image and two generated secret files were removed, without force or
engine pruning. Final data before removal was68026368bytes; restoration took
2864ms native elapsed plus a conservative8 source minutes, within WORK-004's
protected900seconds. postgres-restoration.json records exact identities/results.
Historical resources, existing databases and operator credentials were untouched.

Final storage observation is2970127365 filesystem/native bytes. Retaining the
full conservative1GiB Docker allowance and64MiB reserve still leaves2331472891
bytes inside the original6GiB ceiling; no cache reclamation or capacity increase
is claimed. All55 actual baseline package IDs/versions/content hashes and selected
SDK/runtime/source pins remain unchanged. Protected launch settings and the raw
handoff retain their original SHA256 values.

All intake, agents, documentation, review, native runs and restoration share the
approved allocation. final-external-costs.json and the ignored resource/native
journals distinguish conservative source estimates from measured native elapsed
and from CIS automatic counters, which do not observe these external tools.
Six coherent external work attempts were used; no automatic counters were reset
or invented. Full Linux SDK-backed package execution, EF, actual network faults,
external deployment, broad qualification and release remain unverified/outside
this bounded outcome. No source push, merge or release was performed.
| WORK-004 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1931; failed=270; possibleTokenSavings=591; ledgerDigest=sha256:8016a8b56437b3f07245d4c7a16ae88b015cc0d5a4d1184ca53cf6d532b4c35a |
| WORK-006 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1933; failed=270; possibleTokenSavings=591; ledgerDigest=sha256:698caaca5d158f6e6609082812eda38d361d5e65d453d1a5eb19acb1a9c69cbd |

## Human-authorized integration closeout — 21 November 2026

Andrew Spiteri explicitly instructed: "push and merge the RM-03 branch to main
and close it off before moving on to the next module." This authorizes publishing
the completed branch, merging it into main, closing this dossier and deleting the
merged feature branch. It supersedes the implementation phase's push/merge
exclusion for this exact completed work. The original implementation approval,
limits and accounting remain unchanged; no release or additional product
execution is authorized or claimed.

The integration baseline remains `8ee1858ce9d84dc4210bbfda8690f03ecb6e9fbc`;
`d18bf5669fa667d9c0eb70c7690da8588a78d54a` is the completed implementation head.
All six tasks are Complete and the canonical plan is valid. The recorded
219 focused cases on each actual Windows/Ubuntu runtime, Windows public-package
consumer, exact-candidate receipts and owned PostgreSQL restoration remain
unchanged. The Linux SDK-backed package, EF, actual network-fault and broader
qualification/release limits remain explicit. Protected untracked launch
settings and the raw handoff retain their original hashes. This closeout changes
documentation/lifecycle records only; it does not rerun completed native work or
establish a new tested binary.
