---
title: Workflows Pipeline intake observations
type: verification-record
status: Draft
change_id: CIS-0013
---

# Current intake evidence

The user requested specification import, reconciliation and bounded planning.
The exact source hash, baseline, preserved user-file hash and read-only/native
distinction are recorded in [reconciliation](../../../planning/workflows/README.md)
and ignored workflows-intake/baseline.json. Supported CIS change create registered
CIS-0013 as Proposed against 22902b1f1176a59086fc0bfba2df3d789136dd64.

Three independent bounded read-only reviews covered execution/DI/cancellation,
current Logging/Metrics composition, and CountryData/public consumers/repository
skills. Root read the 625-line source and the actual integration code. Findings
include dynamic name/capacity admission, Info logs versus frequent callbacks,
actual host shutdown/drain placement, a shared reference context and accurate
cleanup-budget reporting. P01–P12 proposes their concrete resolution.

Current APIs include framework activation, existing metrics attachment/handles,
scoped/owned provider cleanup, the native facade, CommonTesting and one maintained
CountryData project. No Workflows API/skill exists yet. The source templates are
future deliverables, not passing consumer examples. Platform references were
checked; the source's API doc link currently shows preview versions, so pinned
.NET10 source/current dependency policy governs instead of an upgrade.

No runtime/catalog/example/skill source changed. No native build/test/restore/
pack/formatter, personal skill installation, push, merge or release ran. Earlier
results, including the documented failed CIS capture, retain their original scope
and do not verify this new module. No closed work owner or allocation was reused.

Intake/source/review effort must be counted in the proposed containing allocation
if approved; it is not a zero-cost or separate pool. The next action is review of
the concrete profile/plan and finite allocation. Supported authorization/task/work
commands will run only after an actual decision.

Final independent plan review confirmed the 48/16/384 reservation mathematics and
fixed the host ordering proposal: Stopped alone cannot protect dependencies torn
down in another service's Stop. The plan now validates the cooperating bridge
position, drains after the listener and before ordinary business teardown, retains
final/failure-path drains, and limits the ownership guarantee to DI/provider-owned
resources. Caller-owned early teardown must await its actual executions.

Preparation strict documentation validation passed 196/196 with zero warnings and
errors. Current CIS skill inventory finds one valid cislib-metrics skill; the new
workflows skill has deliberately not been generated from a proposed API. Source
and user launch-settings hashes match intake. Counted storage is 5,901,073,063 bytes,
leaving 474,269,017 bytes beyond the existing 64 MiB reserve under 6 GiB. These are intake
observations, not native implementation or skill-use passes.

## Implementation authority and first observations

Andrew Spiteri answered “Approve profile and allocation” to the concrete P01–P12,
four-task, 12-hour / 16-attempt proposal, including 1,200 restoration seconds and
the unchanged shared 6 GiB ceiling / 64 MiB reserve. Supported CIS commands
recorded that authority, derived WORK-001–WORK-004, allocated finite capacity and
started WORK-001. The generated path list was corrected to include the existing
optional collector and its documentation, which the approved P09–P10 conditional
catalog/attachment work already requires. This introduced no new profile choice,
dependency, limit or external publication authority.

The incremental planner rejected the first focused profile before native launch
because its derived graph was not fresh/complete. No native invocation or result
is attributed to that command. Ordinary native commands proceed under the same
approved active owner and finite capacity; their measured costs and results are
kept separately. No forced reuse or tooling repair is undertaken.

The first ordinary Release Core build passed with zero warnings/errors. This is
compiler evidence only; the held-operation tests and real integrations remain
pending. Ignored workflows-intake/core-build-1.log and its cost JSON retain the
actual observation.

## Delivered behavior

The existing CIS.Core assembly now provides CIS.Workflows single-use builders,
fresh DI activation, sequential forward work, failing-step-first reverse handlers,
reverse owned disposal, independent cooperative cleanup and detached reports.
The original failure/cancellation policy survives secondary cleanup faults.
Default owned scopes and explicit borrowed caller scopes use the actual provider.

AddCisWorkflows opts into a validated native bridge and a factory on that provider.
Admission closes early; accepted public completions settle before drain releases.
Actual listener stop precedes the workflow barrier and ordinary business Stop
follows it. Failed native Build/start cleanup captures the actual owner. Startup's
coordinator remains independent and existing relative module cutoffs are preserved.

Automatic logs include originating exception diagnostics once and terminal counts.
Captured LogCalls=false suppresses only these automatic events. Three reusable
metrics handles capture one binding per accepted run; reports and metrics share
measured durations. The existing catalog reserves 48/16/384 combinations only for
explicit opt-in, with transactional 16-name / 64-pair metadata admission and
unchanged global capacity. Unavailable final duration does not leave active work
unbalanced or invent a histogram value.

CountryData now uses three cached lookup steps with explicit aliases and a fresh
context/request scope. Existing 200/400/404 responses, aliases, readiness, provider
I/O boundaries, hourly refresh and quiet callback diagnostics remain intact.
The portable contract, consumer guide, metrics catalog, repository skill, generated
UI metadata and AGENTS routing are present. No runtime dependency/package was added.

## Native evidence and corrections

All observations below are actual Windows native runs using the pinned toolchain
and ordinary projects/fixtures. No tests were skipped in these selections. Ignored
paths below are rooted at `.cis/local/workflows-intake/` and remain local receipts.

| Boundary | Actual observation | Retained receipt |
| --- | --- | --- |
| Core execution checkpoint | 28 passed initially; final focused scope 45 passed | `core-first-results/unit.trx`, `core-final-results/unit.trx` |
| Real host ownership | 9 passed: web/nonweb, held unwind/disposal, failed Build/start, caller wait cancellation, borrowed scope and actual native listener abort | `host-first-results/unit.trx` |
| Automatic observations | 19 unit cases passed | `instrumentation-first-results/unit.trx` |
| Collector/exposition | Initial 5 passed / 3 failed; after correction all 8 passed | `collector-corrected-results/unit.trx` |
| Existing affected regression | 134 unit, 141 integration and 50 compatibility passed; architecture initially 329 passed / 1 failed | `regression-results/unit.trx` |
| Reconciled architecture | Corrected architecture group 5 passed, completing the 330-case affected architecture scope | `architecture-corrected-results/unit.trx` |
| CountryData and public packages | 35 passed, including actual local package build/folder-publish and outside-friend public consumption | `consumer-results/unit.trx` |
| Generated consumer whitespace | Only the affected public package case reran after whitespace-only source-literal cleanup; 1 passed | `public-consumer-final-results/unit.trx` |
| Final expiry correction candidate | Rebuilt solution: zero warnings/errors; 64 unit + 19 integration/package cases passed | `expiry-final-results/unit.trx`, `expiry-final-build.log` |

These scopes cover 771 distinct selected cases across retained runs and corrected
groups; this is not a claim of one unfiltered full-solution run. The final 83-case
run repeats the affected execution, observation, host, collector and current package
gates after the last runtime correction. Broader retained regressions verify the
unchanged public surface/composition; they are not forced CIS cache reuse.

The three collector failures were real: MetricDefinition captures dimension keys
in ordinal order, but the initial new fixed catalog supplied another order. Only
the new workflow catalog arrays were corrected; capacities/policy stayed intact.
Schema assertions and real exposition then passed.

The one architecture failure was an older stale allowlist: merged baseline
22902b1 already exposes public DisabledMetrics from the accepted prior follow-up.
The expected set now includes that existing contract and still checks every public
type for forbidden platform/collector leakage. No production Metrics API changed.

Independent final review also found that the expiry diagnostic was delayed until
unwind finished. WORK-005 moves the isolated observation to actual expiry, outside
the report gate, before cancellation delivery; its existing held-handler test now
asserts one event before release and exactly one afterward. Every handler remains
awaited, and all original cancellation, failure, report and ownership rules remain.

The first sandboxed multi-project build failed without compiler diagnostics. The
ordinary serial build succeeded; the failed command and measured cost are retained.
The CIS planner's prelaunch graph rejection launched no native work. No graph/tool
repair or forced result refresh was attempted. A rejected attempt to close a
nonexistent CIS-managed logical attempt was corrected to a factual progress record;
no managed attempt or consumption counter was invented.

## Skill and independent consumption

The installed skill tooling generated `.github/skills/cislib-workflows/agents/openai.yaml`.
Quick validation and strict CIS validation pass: two discoverable repository skills,
zero errors/warnings. The frontmatter initially used generic angle-bracket notation;
the supported validator required plain wording, which was corrected without changing
the API examples or metadata prompt. No personal skill was installed.

A fresh independent agent read only the skill, linked guide/contract/API and actual
CountryData source, then authored a three-step public consumer without seeing tests
or this implementation discussion. Static review clarified that the hosted bridge
needs ordinary Generic Host services when build validation is enabled. The guide
and story now use that composition, with awaited start/stop/async disposal.
The existing outside-friend fixture compiles that story against actual packages,
checks middle failure/reverse handlers, fresh instances, reverse async disposal,
borrowed scope identity and report retention, and passes. The test-owned entry is
component/package evidence, not unchanged production-Main smoke evidence.

## Scope and resource limits

All agent/intake/source/review estimates and measured ordinary native costs share
the approved containing allowance. Ignored `resources-final-effort.json` records
the separate source estimates, actual native durations, conservative attempt charges
and protected reserve. CIS's automatic native counters remain separate because the
planner stopped before launch; no claim of CIS incremental qualification is made.
The correction's capacity came from existing unused instrumentation capacity and
preserved every protected restoration allocation.

Changed C# passed the pinned ordinary whitespace formatter and scoped verification.
Strict docs and skill checks passed. The exact source and existing launch-settings
hashes remain protected; final storage and status observations are recorded locally.
No Linux, coverage/mutation/performance, live-provider, unfiltered-suite or release
qualification is claimed. No push, merge, release, personal installation, second
worktree or historical cleanup occurred.

Final conservative accounting totals 31,960,137 ms (about 8 h 53 min), including
31,200,000 ms of aggregated source/intake/controller/review estimates and
760,137 ms of measured native commands. Five coherent delivery/correction cycles
have nine conservative attempt charges, within the approved 16. The existing
1,200,000 ms restoration reserve remains protected; 10,039,863 ms remain beyond it.
These estimates include final local administration/closeout and do not replace
or reset any CIS counter or historical cost.

Final counted storage observed 5,897,978,177 bytes under 6,442,450,944, leaving
477,363,903 bytes beyond the protected 67,108,864-byte reserve. The source SHA-256
remains 92778b882a2eaeefc5693a18b85f0294258c20a73790b58f1b4f87f8b191b968;
the user's untracked launch settings remain
ea7e7219222183193f0361ae674998f1edc43a3789f118748c59f166a4d29784.

All five canonical implementation/correction tasks are Complete, with no pending
native operation or recovery hold. Strict final documentation validation passes
204/204 documents with zero warnings/errors; strict skill validation finds two
valid skills. The branch is ready for review; this record grants no human product
acceptance, remote publication or release qualification.
| WORK-001 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1608; failed=235; possibleTokenSavings=591; ledgerDigest=sha256:948ae6d03622b67d4ca5e69139d3ea28db0aa74c49a270ed53f62973e0e90555 |
| WORK-002 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1616; failed=235; possibleTokenSavings=591; ledgerDigest=sha256:a193c8674f2cb2ea9a165ab33f1c0cb1ce4fb683ad8b60c6cbcaefcf60884264 |
| WORK-003 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1619; failed=235; possibleTokenSavings=591; ledgerDigest=sha256:87b90a3482ab0336d4b0827168ef5e66badac79d69ab8ff30086a82408369db5 |
| WORK-004 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1635; failed=238; possibleTokenSavings=591; ledgerDigest=sha256:f0eabef395dbb9d5ede66c2bdabda7a6d93e5ffd5d32abc5f7f44391be58ceb9 |
| WORK-005 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=1638; failed=238; possibleTokenSavings=591; ledgerDigest=sha256:01431db1ad42201a9148ba701a75e1f3135c9ac8f04457458be74876ba962123 |
