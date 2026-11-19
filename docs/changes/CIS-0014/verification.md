---
title: State Machine implementation observations
type: verification-record
status: Draft
change_id: CIS-0014
---

# Intake and authority

The user supplied the State Machine specification, asked to move to this next
Workflows topic, and delegated required-time approval while unavailable. The exact
source is preserved through CIS-0014 at Pipeline commit 1e0595f2. Root read all 701
lines and repository/native/Workflows/Metrics instructions; three independent
read-only reviews covered runtime/DI/gate/results, finite telemetry/host wiring,
and actual consumer/skill/package boundaries.

The selected 14-hour/16-attempt containing allocation includes 1,500 restoration seconds,
all agents and already-performed intake, tests, docs, skill and review. The existing
6 GiB/64 MiB physical ceiling/reserve is unchanged. Source instructions alone do not
authorize execution, and no prior completed owner/allocation is reopened.

Pipeline's local branch and all current contracts remain intact. The new local
State Machine branch has no push/merge/release grant. The exact source/hash and
preserved launch-settings hash are recorded in ignored statemachine-intake/baseline.json.

No State Machine runtime, native tests or passing consumer exists at this intake
checkpoint. The required metadata caps were absent from the exact 701-line source;
16/512/64 and 2560/16/384 are explicit bounded controller implementation choices.

## First core checkpoint — 19 November 2026

The first ordinary Windows Release Core/Unit project build passed with zero
warnings/errors. The focused `CIS.Tests.UnitTests.Workflows.StateMachine` run
passed 22 cases with zero failures/skips. Receipts and measured native costs are
under ignored `.artifacts/statemachine/CIS-0014/core-initial` and adjacent logs.
This is an initial execution boundary check, not final module acceptance.

Independent review checked the shared hosting/provider/owner adapter, finite
telemetry and actual consumer source. Core review identified two narrow issues
before final verification: cleanup cancellation must be requested before a
blocking diagnostic while its callback task remains joined, and guarded handler
reports must reject compensation after an unentered Execute. The runtime now
tracks `CancelAsync` callback settlement; guard/boundary proof and formatting are
still pending at this checkpoint. Constructor/disposal cancellation exceptions
remain phase faults and Pipeline's outward contract is unchanged.

## Completed core boundary

After those corrections and the remaining boundary cases, the frozen core scope
passed **55/55** cases with zero failures/skips and a clean Release build. The
scoped pinned whitespace formatter and `--verify-no-changes` also passed. The
held callback proof observes cancellation requested at expiry, then allows Error
to return while a registered callback is still held; handler/scope disposal and
the public result remain pending until that callback ends. Its original aggregate
fault remains a secondary lifecycle diagnostic without replacing the business cause.

Enum tests cover all eight integral underlyings, negative/gapped/large unsigned
members, aliases/Flags, invalid mappings, terminal/empty-trigger definitions,
self-transitions and frozen builder/options metadata. Public guard tests reject
contradictory results and detach supplied diagnostic collections. A retained child
Pipeline exception wrapper remains the actual diagnostic cause. The final core
receipt is `.artifacts/statemachine/CIS-0014/core-final/core.trx`; the earlier
22-case and 23-case checks remain supporting observations, not additional distinct
cases. Ignored resources-checkpoint.json preserves separate source estimates,
measured native costs, conservative logical charges and the protected reserve.

The already-selected P07/A08 real-child proof then passed **3/3** new native-host
cases, for **58 distinct core cases** overall. It uses the actual factories and
borrowed caller scope: reported child failure unwinds second/first exactly once;
successful child plus later outer failure compensates retained application data
without invoking disposed child steps; foreign child cancellation followed by late
caller cancellation remains Failed and retains the Pipeline wrapper/original cause.
Child disposal precedes outer Error, and the borrowed dependency remains live until
the test explicitly disposes its scope. No production change was needed.

An initial administrative Complete transition correctly refused unresolved task
checklists. The task stayed InProgress; after completing this actual-child proof,
the checklists/evidence were resolved before retrying through the supported lifecycle.

## Actual host and telemetry checkpoint

The actual-host gate passed **22/22** cases: 13 State Machine cases and 9 retained
Pipeline cases. It covers both opt-in orders/State-only registration, one owner and
bridge, mixed accepted work, borrowed scopes/fresh children, web listener abort,
cancelled Stop waits, real Error/disposal settlement and late Build/startup failures.
The existing facade capture/order logic needs no second registry/provider or new
Startup dependency. Independent review found no additional lifetime blocker.

The first Integration build compiled the actual CountryData project but stopped
on MSTEST0017 in a newly authored test's expected/actual argument order. The single
symmetric assertion was corrected; the retained second build has zero warnings/errors.
Both receipts/costs remain under ignored .artifacts/statemachine/CIS-0014.

Telemetry passed **39/39** unit cases (20 State Machine, 19 retained Pipeline) and
**31/31** collector cases (12 State Machine, 8 Pipeline, 11 REST). One actual scrape
observes all five outcomes, exact runner/report seconds, entered phase counts and
active balance through held compensation/disposal. Tests cover both registration
orders, full-domain transactional alias union/rollback, invalid-label collision,
three-handle capture, opt-out/failing/disabled observations and inadequate overrides.
The explicit State marker preserves all old Pipeline/REST opt-out profiles; combined
defaults are 33 streams/7,824 combinations under unchanged global limits. Metric catalogs
and guide match the implementation. These are focused Windows observations, not
footprint, broad stress, platform or release qualification.

## Consumer, shared skill and final focused evidence

The selected consumer gate passed **41/41** cases: 40 CountryData cases plus the
exact Core-byte source fixture. It preserves 200/400/404, identifier normalization,
readiness, cached lookup/no provider I/O and bounded refresh diagnostics. Real
source is built/published with local packages and a test-owned entry against a
controlled provider; this is component/package evidence, not an unchanged production
Main smoke claim. Pipeline observations remain 5 calls/12 Execute/3 Error; the new
State Machine observations are 8 calls/7 entered handler phases with balanced active.

The existing outside-friend consumer now runs public success/failure/cancelled
mock results and public caller-scope capability dispatch. A fresh agent, without
the implementation discussion, read the shared skill and linked actual materials
and authored a Draft/Submit/Approve story. The same package consumer compiles and
executes it: success, a separate rejected machine, normal failed Submit retaining
Draft, prompt overlapping/reentrant Busy, fresh child retry and exact borrowed
scope ownership. Controlled holds stay entirely in test source. The child is never
compensated twice and old results remain unchanged. No unsupported skill guidance
was found. No new project/framework, personal installation or usage harness exists.

The installed skill generator produced the updated metadata; its quick validator
passed. Strict CIS validation reports two valid/discovered skills and 214/214 valid
documents with zero warnings/errors. The pinned scoped formatter/verification and
ordinary Release solution build pass with zero build warnings/errors. Affected
retained Pipeline execution passes 45/45 and Core architecture boundaries 21/21.

| Final distinct scope | Passed | Failed / skipped |
| --- | ---: | ---: |
| Core execution/enum/public guards | 55 | 0 / 0 |
| Actual child Pipeline ownership | 3 | 0 / 0 |
| Shared host integration | 22 | 0 / 0 |
| State Machine/retained Pipeline telemetry unit | 39 | 0 / 0 |
| State Machine/Pipeline/REST collector | 31 | 0 / 0 |
| CountryData/public packages/exact source | 41 | 0 / 0 |
| Retained Pipeline execution | 45 | 0 / 0 |
| Affected Core architecture | 21 | 0 / 0 |
| **Total** | **257** | **0 / 0** |

Ignored native-summary.json identifies these eight final receipts. Earlier 22-case
and 23-case runs are supporting checks and are not double-counted. The first
Integration compiler/analyzer failure and initial incomplete administrative close
check remain preserved; neither is represented as a passing observation.

## Scope and finite accounting

The final conservative containing account is **39,236,957ms** (about 10 h 54 min):
38,880,000ms of source, intake, all agents, controller, review and local closeout estimates
plus 356,957 ms of measured ordinary native commands, including the failed build.
Nine conservative logical charges remain within 16. The shared 50,400,000 ms maximum
and 1,500,000 ms restoration reserve leave 9,663,043 ms beyond the protected reserve.
Every task remains within its own redistributed finite allowance and reserve.
Ignored resources-final-effort.json records the components; CIS's automatic native
counters stay separate because no managed native attempt was opened. No incremental
currentness, full platform, coverage/mutation/performance or release qualification
is claimed, and no unchanged test was rerun merely to clear a CIS status.

Counted storage observed 5,910,418,112 bytes under 6,442,450,944, leaving 464,923,968 bytes
beyond the protected 67,108,864-byte reserve. The imported source hash remains
c68790990bc6912fc6a5839c6bd287dd6cf8832d5499ec4b8ded067a9aa8f8f0.
The user's untracked launch-settings hash remains
ea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784.
No dependency/SDK/runtime pin, new storage pool/worktree, historical cleanup,
push, merge, release or prior completed owner was introduced/reopened.
| WORK-001 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1657; failed=240; possibleTokenSavings=591; ledgerDigest=sha256:f92169fbd5d75d078fb2cb2a1479d42f2c173bb6e197045d6fa8dd00f6026b80 |
| WORK-002 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1662; failed=240; possibleTokenSavings=591; ledgerDigest=sha256:b022b6a678c8ac903e7bedbd060eb6dd6b170f0c02e1dc65b28e499c53705a71 |
| WORK-003 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1665; failed=240; possibleTokenSavings=591; ledgerDigest=sha256:a6d7a2e9854ab6ee802196c74295c25524267826b1079147a82abe7717cb17a9 |
| WORK-004 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1672; failed=240; possibleTokenSavings=591; ledgerDigest=sha256:06727acab81719a24c398c8e07f32c2de547876709379c3a060cfdfae6f58078 |

All four canonical tasks are Complete. No native operation, active reservation or
recovery hold remains. The local State Machine branch is ready for review; this
record grants no remote publication or human product acceptance.

## Authorized push and merge on 19 November 2026

After the implementation checkpoint, Andrew Spiteri explicitly requested:
"ok, let's push and merge to main". This subsequent instruction authorizes
publication and merge of the current Workflows branch, including its unmerged
Pipeline parent. The earlier no-publication statements remain historical limits
on implementation authority; this closeout does not rewrite those approvals.

The reviewed implementation commits are Pipeline `1e0595f2` and State Machine
`ffa00ffb`, above remote main `22902b1f`. Fetch confirmed that main had not advanced,
with no tracked working changes and the protected launch settings unchanged.
GitHub permits merge commits; main reports no required status checks and the
repository contains no Actions workflow files. The pull request will be checked
again before merging and will preserve implementation commit ancestry.

Only closeout documentation changes after the retained native evidence. No new
native execution or implementation owner is opened, and the prior test and resource
observations retain their stated scope. No release or tag is authorized.
