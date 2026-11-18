---
title: CISApplication and CountryData intake and verification
type: verification-record
status: Draft
change_id: CIS-0011
---

# Observations

18 November 2026 intake uses actual main
`86a97e047e2a17590c8187cfeb7f441ea0e7646f`, after the authorized RestClient merge.
The two supplied inputs were copied byte for byte; hashes are in the
[reconciliation](../../../planning/application/README.md). Repository instructions,
dependency policy, relevant current module contracts/source and the existing
Metrics skill were reviewed. Three independent read-only reviews covered core
lifecycle/defaults, CountryData/Metrics and cleanup/fixture migration.

Installed CIS remains `0.3.0+2b916962dbedef18bdde8959e3144184d37d64f9`.
Supported change creation registered CIS-0011 Proposed against that exact Git
baseline; no workspace or product foundation was recreated. The supplied feature
was presented through `plan import-spec`; it returned exit2, applied=false and
zero work items because there were no accepted impacts. It did not import an
approved executable plan. The local log is
`.cis/local/application-intake/plan-import.log`.

An initial impact call referenced the wrong ConfigurationRuntime location and
was rejected without changes. The corrected bounded depth1/four-root analysis
retained four proposed findings, none accepted, with no truncation. It warned
that derived graph inputs are stale after intake edits. These findings are
routing proposals, not complete semantic coverage or execution authority.
No CIS upgrade, broad graph-repair campaign, product reapproval or invented human
disposition was performed. A new approved outcome/scope/allocation can supply
the supported derivation path without reopening old owners.

No product source, examples, tests or active native profiles have changed and no
native build/test/restore/pack or live-provider smoke has been launched for this
delivery. Prior RestClient results remain scoped to their original inputs.
All closed CIS-0001 through CIS-0010 grants, actual costs and receipts are intact.

The 09:15 UTC storage sample in
`.cis/local/application-intake/storage-baseline.json` counts `.artifacts`,
`.cis/local` and `implementations/csharp`: 5,871,810,059 bytes under the same
6,442,450,944-byte ceiling; 503,532,021 bytes remain after the 67,108,864-byte
restoration reserve. Sampling and this proposal do not grant cleanup or native
capacity. All-agent task limits are proposed in the single bounded plan.

The three pre-existing untracked user launchSettings files still match hashes
`1d65b76547423e534c95085865c646667153c7313fbe3f2dc910ca73a8d89c71`
(startup), `b91ec52066232e9e2c7416925607c4b7aa7abbc16360d8b3fc091d6b5ef8bc64`
(Metrics), and `496e4bfdfc50d218c6811f947c26dfe72984782fe51a21916cb113f7d5f08f85`
(RestApi). None has been moved, staged or deleted. Their proposed reversible
preservation is explicit in the plan.

Final preparation review confirms six medium leaves with their own tests/docs,
complete eight-tutorial consolidation and named minimal fixture purposes. JSON
proposal parsing and arithmetic checks confirm 43,200 shared seconds, 16 attempts
and 1,200 included restoration seconds; these remain proposals, not allocations.
Strict docs validation passes 176/176 with zero warnings/errors after correcting
an unsupported catalog authority label. Both source hashes and all three user-file
hashes remain unchanged. No product implementation/native work is claimed.

## Approved implementation and AP1 checkpoint

Andrew Spiteri approved the six-task plan and 43,200 shared active seconds /
16 logical attempts, including 1,200 restoration seconds and 64 MiB within the
same physical 6 GiB ceiling. Supported CIS authorization is
`cislib:CIS-0011:outcome:ad93dd647e1f41ad9dac004ce6061dda`, decision
`CISLIB-APPLICATION-2026-11-18:USER-APPROVED-PLAN-ALLOCATION`, digest
`sha256:922e5ba2af5e498be6452cddfa843734a665da363e6b81f2eb2d936b2ba04663`.
The first request needed the existing native-scratch representation for
`.artifacts` and its actual decision reference; rejected requests granted no
authority. Paths, limits and historical protection did not change.

Six medium work owners were derived and allocated through supported commands.
AP1 adds the thin public builder/facade/integration seam, Core composition,
explicit defaults-only Configuration and internal activation-prepend hook, and
Core no-op Metrics. Public Configuration registration remains strict. Lazy module
defaults precede customization; removed Heartbeat is not restored. Independent
review identified pre-host-start route-failure ownership, which now retains and
directly cuts off/drains effective module owners before root teardown.

Native observations (Windows, SDK 10.0.401/runtime 10.0.12):

- `native/incremental/b6ec2445a6f84fdc8a123069126dbafe`: preparation exhausted
  its usable 600-second window before any build/test launch; 605,787 ms retained.
- `native/incremental/e1d00a3c8a754d5d9e48f04baecf0d9f`: Core and Unit projects
  compiled, then VSTest aborted after failing to connect to testhost. Zero tests
  executed; cost retained. The attempted native cancellation later returned
  unchanged because the original invocation had already settled safely.
- `native/incremental/aff98128b819495295f4666da67d020b`: running the same
  focused checks outside the restricted process sandbox passed **17/17**, zero
  failed/skipped/aborted. Defaults/retained-reader/activation/order cases, Core
  no-op metrics and constructor/no-build cases passed. Result admission reports
  scopeComplete=true, unitCurrent=true and zero pending batches; qualified=false
  remains the separate pilot advisory.

Evidence is `.cis/local/application-intake/core-escalated-observed.trx` and
`core-escalated-native.log`; the aborted stdout/stderr/TRX and first preparation
failure are retained separately. Inclusive measured native consumption is
2,099,203 ms in one still-open logical delivery attempt; the earlier failures
and actual overrun remain counted, with zero native reservation/recovery hold.
The focused source root was restored to the project boundary to select all 17
authored tests, rather than the graph's three constructor cases alone.

Native setup also rejected duplicate input paths, a stale graph and the relative
authority value before admission. Normal graph refresh and the exact repository
authority corrected those invocations; no CIS code, SDK or test framework changed.
An attempted narrowing of a started task's paths was rejected, so its contract
and history remain intact. AP1's original task distribution was raised to 9,000
seconds using 1,200 unreserved seconds each from AP2/AP5 under the delegated
12-hour total; attempt and restoration reserves are unchanged.

The component checkpoint does not prove full facade/web readiness, exporter or
CountryData behavior. Those actual integrated proofs remain successors. Pending
source/test/doc patches were prepared independently in ignored intake storage
while Core inputs were frozen; their authoring is not passing evidence.
| WORK-001 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1434; failed=215; possibleTokenSavings=591; ledgerDigest=sha256:5370b1567a50e3157780b8a9ef48dd590b41db5e0e549b979fe154c4f0d7c0a8 |

## Facade and optional Metrics checkpoint

Windows execution `native/incremental/d5ca5f01cd724a44a839d6a8b72461ca`
passed **29/29** cases: 17 retained lifecycle/real-host cases and 12 optional
Metrics composition cases. The source CountryData project, minimal Type Manager
Host and both optional packages compiled during this gate. Evidence is
`.cis/local/application-intake/host-observed.trx`, `host-observed-stdout.log`
and `host-native.log`. Admission reports scopeComplete=true, unitCurrent=true,
zero pending batches and no recovery hold. The separate pilot advisory remains;
this is focused native evidence, not platform/release qualification.

The checks exercise sequential startup, early real-listener operational access,
readiness/business gating, retained start/stop/dispose outcomes, actual callback
and logging-writer drain, partial preparation/Build/route failure, effective
replacement ownership and late cancellation preserving an already selected fault.
Optional integration checks use actual prepared type discovery, native scrapes,
central identity, two independent hosts, callback drain, disablement, customization
and conflicting registrations. Core no-op registration does not preempt the adapter.
Independent reviews resolved these ownership and contract issues before execution;
the Startup coordinator still has no module dependency.

This gate consumed 595,233 inclusive measured milliseconds. Cumulative native
consumption is 2,694,436 ms in two logical delivery attempts, with no active native
reservation. Source authoring, controller work and independent reviews also share
the original 12-hour total; their estimates are not a second allowance. WORK-002
and WORK-003 completion is separate from CountryData/package/consolidation proof.
| WORK-002 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1451; failed=215; possibleTokenSavings=591; ledgerDigest=sha256:e4959cac39606a98a076e36b6ae1802cc7b1c1723ddbbb2311448020ae5262ec |
| WORK-003 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1455; failed=215; possibleTokenSavings=591; ledgerDigest=sha256:7ba7b5e80c6f019152e354acb36949ccdefba344e2e85aa4eb2585c122cb608d |


## Consumer coverage after consolidation

CountryData is the maintained application example. Tests keep only distinct
assembly, process, protocol and packaging boundaries; old tutorial report fields,
monitor counts, application names and demonstration-order metrics are not public
module contracts. Actual executions and limitations are recorded in this verification record.

| Retired tutorial | Surviving proof and purpose |
| --- | --- |
| Type Manager Console/Web and support projects | Existing TypeManager Host/Contracts/Transitive/Unused/Runtime fixtures retain independently compiled, initially unloaded and missing selected assemblies, transitive membership, ordinary PackageReference, SDK build and both folder-publish modes. The Host has one startup consumer and two passive Configuration roots, rather than another application. ConfigurationSourceConsumerTests retains the exact current Core byte guard. |
| Logging ConsoleHuman | LoggingBusinessStories, CandidateLoggingProcessTests and existing DiscoveryHost retain real human/JSON output, context, flush, process exit and exclusive file-group release. No copied logging application is retained. |
| Healthcheck CountryList | Existing HealthcheckHttpHost/CisHealthProbe tests retain real HTTP, pipe, protocol and process failures. The current CountryData package consumer builds and publishes the unchanged probe against the same exact Core nupkg, comparing HTTP/pipe identity, valid negative health/readiness and unavailable transport. |
| Heartbeat Polling | In-process HeartbeatPollingStories retain false versus exception, awaited recovery, original request budget, non-overlap and owner drain. CountryData tests exercise the actual scheduled refresh and publication with the existing ManualTimeProvider. |
| Startup CountryList | Existing coordinator/hosting tests plus CISApplicationLifecycleTests retain construct-all-before-start, ordered starts, identity deduplication, readiness, failure/cancellation and retained lifetime ownership. CountryData primes real REST data before readiness. |
| Metrics MetricApi | Native Collection/HTTP/Hosting tests retain adapter semantics. MetricsApplicationCompositionTests and the actual CountryData package consumer exercise automatic discovery, scrape, central identity, independent hosts, callbacks, deployment and disabled/custom modes. |
| REST RestApi | Native REST boundary, mapping, retry, cleanup, logging and trace tests remain. A tiny Core-only public consumer proves None without optional packages; CountryData's actual package consumer proves optional retry/Metrics deployment and real local HTTP requests. |

The redundant ExampleLate fixture is retired: existing fixed-membership and
runtime deployment tests already cover its distinct requirement. Content-only
and satellite DLL exclusion remain in the minimal Type Manager package fixture.
Configuration's native retained-root/reload tests remain; the source process adds
discovery and constructor/default identity rather than duplicating those tests.

Package tests use isolated feeds/caches populated from the actual current built
candidate and approved dependency assets. They check restored nupkg and deployed
assembly hashes, pinned SDK/runtime and genuine build/publish/execute boundaries.
Test-owned temporary entry points arrange finite startup holds and observations;
the maintained application and probe acquire no self-test or failure modes.

## CountryData and consumer observations

The first CountryData build stopped at ASP.NET route analyzer AD0001 before any
case executed (`native/incremental/3d58294f556e47a9a001041d231b9674`). The
integration fixture used an external assembly method group; a typed forwarding
lambda compiled with the same analyzers/warnings enabled. This is a bounded
fixture workaround, not a claimed upstream diagnosis. The actual compile log is
`country-first-build-stdout.log` in ignored application intake storage.

The combined Windows run `native/incremental/2c7dc9eaeb604486867ab743584f2a5d`
executed55cases: **54passed,1failed,0skipped**. All29 facade/Metrics cases and all
seven source/package consumers passed; 18of19 CountryData cases passed. The one
failure waited for a cleanup wrapper after only observing a server-side flush;
stop could legitimately cancel acquisition before that wrapper existed. The
15-second timeout and completed finally joins support that fixture-phase race.
The corrected fixture waits for an actual pending native body ReadAsync before
stopping, retaining server disconnect, held cleanup, retained disposal and actual
stop/owner drain assertions. No runtime change or longer deadline was introduced.

The correction run passed **20/20**, zero failed/skipped/aborted: all19 CountryData
cases plus the strengthened same Core-only package consumer. Its normal
CISApplication web host starts and stops from the isolated exact Core package,
resolves the inert Metrics fallback, exposes no metrics endpoint and loads no
optional packages. REST still returns17 in one physical attempt with None.
The normal optional CountryData/probe case already passed genuine build and
folder publish against current Core, OpenTelemetry and Polly packages, with
isolated caches, exact nupkg/deployed-byte checks, real retrying local HTTP,
actual scrape families and central Unicode identity. The unchanged matching
CisHealthProbe observes valid negative health/readiness exit1 while startup is
held, exit0 after success, and unavailable transport exit2.

Raw reports are `.cis/local/application-intake/consumers-first-observed.trx`
and `corrections-observed.trx`, with stdout retained alongside. The55-case result
remains failed/noncurrent as a whole; its specific passing observations are not
relabeled as a successful full receipt. The20-case run separately verifies the
corrected behavior. Preparation inventory overflow and stale graph/document
warnings launched no tests; their measured costs remain retained. A supported
full graph refresh cleared the deleted-document diagnostic after strict docs
validation184/184 passed. No CIS implementation or test framework was changed.

Three user launch-settings files now have byte-identical verified copies under
`.cis/local/application-intake/user-settings/` with their full original relative
paths. `user-settings-preservation.json` records all hashes and restoration
restrictions; original files remain in place until approved example retirement.
| WORK-004 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1477; failed=220; possibleTokenSavings=591; ledgerDigest=sha256:e9193c859fa6a2e76285355914a19b4324aebc88574d0595d3cd26fef412e63b |
| WORK-005 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1480; failed=220; possibleTokenSavings=591; ledgerDigest=sha256:ea6e02a9b385bf86f1d675ba9ba82d8e2196e41050226706b18f6819e520ebfd |

## Complete example consolidation

WORK-006 removed all eight runnable tutorials, redundant Type Manager sample
support and ExampleLate after scoped migration proof. CountryData is the only
remaining example project. Eleven obsolete tutorial-specific test/helper files,
four BusinessCases project references/copy targets and the obsolete linked
expectations were removed. Existing native module tests, Logging/Heartbeat
stories, distinct discovery libraries and protocol/process fixtures remain.
The solution, active guides/routing/skill and current first-consumer reference
now use CountryData. Framework selection metadata and retention policy in
contract-targets.json remain the original decision; this changes its current
consumer reference, not a published target. The inventory build guide preserves
the SDK/first-restore recipe and now explains the current app's optional package
references without obsolete Contracts/Unused sample instructions.

All three verified user launch files were preserved outside removed directories;
no later user files were overwritten. Native preflight checked exact resolved
paths, tracked/known files and absence of reparse points before recursive removal.
Five retired lockfiles reappeared eight seconds later while Visual Studio had
background MSBuild nodes; only those regenerated lock/build remnants were removed
after another unknown-file/reparse check. No user-owned IDE process was stopped.
The subsequent example inventory contains only CountryData.

The ignored profile-disposition.json classifies old tutorial-bound profiles and
the closed Configuration preparation script as historical provenance. Their
bytes/results/accounting are preserved; no final CIS-0011 call selects them.
Earlier CIS-0011 component profiles are also retained checkpoints once source
retirement makes their broad inventories noncurrent. The final maintained profile
is application-final-consolidation. No old result is forced current and no closed
owner is reopened. Remaining old names in AGENTS/planning are explicit historical
records; the Type Manager boundary guide labels its retired parent guide as such.

After consolidation the same counted roots contain5,874,953,329bytes, leaving
500,388,751bytes after the unchanged64MiB reserve under the physical6GiB ceiling.
This is an observation, not a new capacity pool. Final native invocation uses one
ordinary restore-enabled solution build and one native VSTest command over the
existing five MSTest assemblies, with one aggregate TRX. The small ignored script
is command glue; it adds no test framework, report merger or orchestration product.
The filter covers affected module units, retained native host/module cases,
Architecture, Compatibility and the remaining BusinessCases, plus the genuine
Core-only facade package consumer on the final tree. Already verified unchanged
optional consumers are not repeated solely to refresh their bookkeeping.
## Final Windows checkpoint

`native/incremental/a5b190e240d44f0cb2e77a6979d70d8d` built the cleaned solution
in Release with **zero warnings/errors**, then passed **3,034/3,034**, zero
failed/skipped/aborted: 2,137affected unit, 361integration, 332architecture,
179compatibility and25business cases. This includes the exact current Core
source-process byte guard and genuine Core-only facade package consumer on the
final tree. All four minimal Type Manager fixture projects are explicit solution
members, so their Release configuration matches the existing helper. The earlier
normal solution build correctly failed CISTM005 when the omitted Host project
fell back to Debug; the guard was preserved and the mapping fixed. The earlier
prelaunch accounting-revision rejection and build failure launched no tests and
remain recorded, with their costs, in the same WORK-006 delivery attempt.

Admission is scopeComplete=true, unitCurrent=true, zero pending batches, with no
active native reservation or recovery hold. Qualified=false/pilot-required stays
the separate advisory; no mutation/coverage/performance, Linux, live public
provider smoke or release qualification is asserted. Local actual HTTP is the
repeatable provider boundary; the normal maintained Program has no test modes.
Raw aggregate report is `.cis/local/application-intake/final-observed.trx`, with
build/test stdout in `final-observed-stdout.log` and admission in
`final-mapped-native.log`. The earlier54/55 report remains failed as a whole.

The user's containing allocation remains43,200active seconds,16logical attempts,
1,200included restoration seconds and the same physical6GiB ceiling/64MiB reserve.
Delegated distributions now total7,200/7,800/3,600/7,200/8,400/9,000seconds for
WORK-001through006, respectively; attempt counts and all restoration reserves
are unchanged. The two later allocation payloads preserve the prior distribution
files and decision/digest. Allocation was settled before the successful native
invocation; no concurrent accounting mutation was repeated. Measured native
consumption is5,602,646ms across five native logical delivery attempts, with zero
reserved work. Controller/agent authoring, review, source planning and graph work
also share the original total; native figures are a measured subset, not a second
allowance or a claim that other work cost zero. Working estimates remain within
the original containing limit, with protected restoration capacity unused.

Both imported source hashes and all three preserved user-file hashes still match
the intake values. No old example project returned after removal of the one-shot
regenerated locks. Historical closed-owner records, original inputs and native
results remain intact. No release, merge, push, new backend or next module is
part of this completion.
| WORK-006 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1502; failed=223; possibleTokenSavings=591; ledgerDigest=sha256:6b205f90fd6e8419c849d2d46dc7a1edb37e7de174f4bde3768cb01f2600ee58 |

Final hygiene observed a newly untracked CountryData/Properties/launchSettings.json
created during IDE activity. It is outside the authored implementation and is left
untouched and unstaged; the three original retired-example files remain preserved.
