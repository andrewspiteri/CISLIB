# Why Healthcheck mutation work has not translated into qualification

26 October 2026. Investigation only: no mutation campaign, native build, test,
classifier or new verification utility was run. No production/test code,
threshold, raw report or acceptance rule was changed. Three parallel read-only
reviews covered test design, qualification rules and execution/workflow cost.

**The main finding is a mismatch between the mutation engine, our custom
qualification rules and the work selected to improve the score. There are real
test weaknesses and unresolved execution failures, but the retained evidence
does not establish that the production Healthcheck library is fundamentally
broken. The current workflow is inefficient and needs correction before more
broad mutation runs.**

## 1. We have conflated three different measures

| Retained evidence | What it actually establishes |
| --- | --- |
| Last complete 191 union: 2,111/3,104 = 68.009021% | The custom auditor's accepted-evidence score for that frozen source/test context. It is not a continuously updated measurement of current source. |
| Same 191 reports: 2,754 raw Killed / 3,104 = 88.724227% | The guide's Killed-only arithmetic before validating all those kills. This is an upper bound on recoverable evidence, not permission to credit all 643 rejected kills. Even that upper bound is 40 kills short of the 2,794 needed for 90%. |
| Latest Options197 pilot: 91/96 = 94.791667% accepted direct kills | A one-file diagnostic result already above 90%. It is not complete Healthcheck qualification. Five aborted hosts and other audit issues remain. |

The 191 deficit includes 643 raw Killed definitions without accepted witnesses,
23 Timeouts, 301 Survived and 26 NoCoverage. Those categories require different
work. The 94,215 issue observations are overlapping diagnostics, not another
94,215 mutants or an additional arithmetic deduction. Many rejected raw kills
have genuinely incomplete/aborted evidence; this investigation does not assume
all 643 are classifier mistakes.

Pilot197 explicitly hardcodes `completePartitionQualification=false`,
`wholeUnionOrGate=false` and `appliedKillCredit=0`. Its four-worker profile is
also outside the maintained configuration allowlist. Even a completely clean
100% result through this diagnostic route could not update the maintained
full-scope score. Repeatedly presenting 68.01% as the current result hid both
useful local progress and this missing path to a qualifying full campaign.

## 2. The auditor demands more than the documented mutation gate

The adopted [Core guide](../../../../logging/sources/core-library-implementation-guide-v1.md)
requires full source scope, a 90% conservative Killed-only score, immutable
reports, source/candidate identity, meaningful survivor dispositions and
resolution of unexplained timeout/harness errors. It does not require every
normally failed mutant test to emit a particular assertion string.

The custom [NativeAudit](../../../../../implementations/csharp/eng/CIS.DependencyChecker/HealthcheckQualification/Mutation/Union/HealthcheckMutationNativeAudit.cs)
instead applies an assertion-message/stack whitelist and a text blacklist.
Each failed row outside that whitelist becomes `NativeFailureNotDirectAssertion`.
The [union evaluator](../../../../../implementations/csharp/eng/CIS.DependencyChecker/HealthcheckQualification/Mutation/Union/HealthcheckMutationUnionPure.cs)
retains those rows as global qualification blockers even if the same mutant
already has another accepted direct assertion.

**Concrete result:** all 5,167 non-direct failure observations in Options197
belong to 31 normally completed mutant hosts whose 31 definitions are already
in the accepted set of 91. Repairing those rows adds **zero distinct kills**
to that pilot. It can only satisfy the additional zero-issues rule.

The failures include 4,283 configuration exceptions, 789 null references,
93 LINQ cardinality errors and two platform exceptions. Six mutations account
for 4,780 repeated rows. These are mostly cascades from changing shared
validation, not thousands of independent production defects. A normally
completed test failing because mutation makes valid SUT input throw must be
distinguished from a runner crash, incomplete cleanup or infrastructure error.
Unknown origins still require investigation; blanket acceptance is not proposed.

[Stryker's documented classification](https://stryker-mutator.io/docs/mutation-testing-elements/mutant-states-and-metrics/)
uses a failed test to detect a mutant and distinguishes engine runtime errors
from that failure. Our extra assertion-only/all-row rule needs an explicit
requirements review rather than more test rewrites merely to change error text.

## 3. We deliberately run too much work after a mutant is already detected

`disable-bail=true` makes Stryker continue the selected suite after a failure.
The pinned runner's normal early-bail implementation uses `CancelTestRun()`
once the mutant is resolved. Our auditor rejects the resulting intentional
cancellation, so configuration and validator together force full continuation.
This is a pipeline design choice, not a consequence of the 90% threshold.
The [official configuration documentation](https://stryker-mutator.io/docs/stryker-net/configuration/#disable-bail-flag)
describes the tradeoff. Enabling bail alone would fail the existing auditor;
its known cancellation and real process/child cleanup would need a narrow,
verified acceptance path first.

Actual Options197 request metadata contains:

- 96 active mutation requests and **21,325 requested test-case slots**;
  aborted hosts did not necessarily execute every requested case.
- 23 requests selecting 811–850 cases; these account for **19,449 / 21,325
  requested case slots (91.20%)**.
- 19 requests selecting exactly 850 cases.
- Real per-test coverage selections, rather than a whole-family fallback.
  Shared valid-options construction connects many otherwise unrelated tests.

Pipes runs coverage off because actual subprocess execution must be accounted
for. It also disables bail and runs two test assemblies per mutant:

| Recorded Pipes run | 191 | 194 |
| --- | ---: | ---: |
| Selected cases per complete mutant | 622 | 624 |
| Eligible definitions | 250 | 250 |
| Execution hosts | 502 | 502 |
| Distinct positive child PID observations | 13,101 | 7,696 |
| Native elapsed | 3 h 52 m 38 s | 1 h 52 m 03 s |
| Normal execution hosts | 398 | 257 |
| Diagnostic direct IDs | 119 | 98 |
| Raw Timeout definitions | 5 | 80 |

The faster run produced fewer normal hosts and accepted diagnostic witnesses.
Source/tests/budgets changed alongside worker count, so this is not a causal
concurrency benchmark. It does show that faster raw completion did not produce
more usable evidence. Switching Pipes to per-test coverage alone is not a safe
solution: child IPC/coverage limitations remain real.

## 4. Some tests are not suitable for this mutation feedback loop

The suite has useful contract checks, but several weaknesses are demonstrated:

- A fixture assumed successful Start implied an enabled endpoint; mutation
  disabled the copied options and caused a 15-second connection wait. The
  [repaired control](../capture-runtime-followup-2026-10-25/disabled-endpoint199/README.md)
  now detects that prerequisite violation in 0.106/0.003 seconds of test-body
  time, with normal shutdown.
- Actual Pipes cases waited about 14 seconds after their child had already
  exited. This is missing progress observation, rather than slow business work.
- Some tests access `Issues.Single()` before checking cardinality; mutation
  therefore causes a LINQ exception instead of the intended structural assertion.
- Broad fixture construction causes repeated failures before the unrelated test
  body is reached. Rewriting every such exception as an assertion does not
  necessarily add a new behavioral check or a new killed mutant.
- Across 100 Healthcheck test source files in the frozen Unit cohort, 33 have `GC.Collect`,
  74 such source sites, 37 finalizer-wait sites and 22 literal 15-second
  deadlines. These are static counts, not measured pause totals. Ownership
  tests remain necessary, but this is a costly mixture of unit, lifetime and
  transport behavior rather than an entirely cheap isolated unit suite.

There are also real assertion gaps: the 191 complete reports retain 301
survivors and 26 uncovered definitions, and earlier empty-explanation mutants
survived until explicit checks were added. One original collector
exception-preservation defect was fixed in the preceding work. Neither the
ordinary passing suite nor this audit proves the implementation bug-free.

## 5. Several apparent hangs are deadline/calibration failures

Of the five abnormal Options197 hosts, one has the demonstrated connection
wait above. Another returned **all 850 requested results before the deadline**
but raced aborted and normal completion messages. Two others were still
completing individually finite GC/lifetime cases near their 32.185-second
session boundary. One last-started case had run only 36 ms when cutoff occurred.
These observations do not establish four more deadlocked test bodies.

Startup, adapter/collector work and completion reporting consume the session
budget too. One 850-case host took roughly 8.6 seconds to reach its first case,
then about 23.7 seconds to reach completion. Calling its last case a hang or
blindly reducing every wait attacks the wrong cause.

CPU pressure, GC contention, cold startup and instrumentation are plausible
contributors. Their individual causal shares have not been measured. A matched
instrumented inactive-cohort measurement is needed before another parallelism
or timeout change; uninstrumented original-suite duration alone is not enough.
The retained native initial hosts illustrate the distinction: both passed
1,396 cases, but first-test-to-completion was 14.896 seconds in the plain initial
host and 20.263 seconds in the instrumented coverage host. Different binaries,
collectors, order and load prevent treating this as an isolated performance
benchmark.

## 6. The preparation and reporting loop has also become expensive

Options197 took 802.518 seconds overall; native Stryker took 538.532 seconds.
Its explicit checker/original/fresh builds took 97.898 seconds and ordinary
original/fresh execution 24.582 seconds. The remainder includes controller
validation, source/evidence work and gaps not separately timed; it is not all
known parser overhead.

For the recent eight-line startup prerequisite, the Windows focused run
finished around 07:07:49 UTC and Ubuntu healthy control began at 07:31:04:
about **23 minutes 15 seconds between them**, followed by a 7.387-second
healthy and 7.774-second mutant control. That gap includes preparation,
source/metadata review and orchestration; there is no instrumented breakdown
assigning every second to a particular tool. It illustrates the latency of
the current loop, not an isolated native test-performance measurement.

Multiple frozen source copies, literal controller derivatives, new metadata
helpers, repeated builds, artifact transfer/receiving and late capture failures
have turned small fixes into substantial engineering exercises. Source and
subprocess provenance are necessary; the particular amount of bespoke machinery
is not itself a product requirement. Native execution still dominates the full
Pipes runs, so removing packaging alone will not solve their runtime.

## Recommended correction before more mutation fixes

1. **Reconcile the gate with the documented requirements.** Keep 90%, complete
   mutation source scope, no timeout credit, immutable reports, source/child
   identity and genuine runtime/cleanup holds. Separate normal mutant-induced
   test failures, meaningful per-mutant kill evidence and infrastructure faults.
   Do not require thousands of redundant assertion-formatted rows after a
   mutant already has a valid kill. This is a proposed rule change, not one
   applied by this investigation.
2. **Prove one reusable qualifying execution path.** Reuse the current runner
   and a frozen representative slice; demonstrate both a legitimate test
   failure and a genuine infrastructure/cleanup failure are classified correctly.
   Review deliberate early-bail cancellation with actual resource closure.
   Avoid another per-fix controller/metadata framework.
3. **Calibrate before increasing concurrency.** First run the same instrumented
   850-case cohort inactive with one host; run its matched active mutation only
   if that baseline completes normally. If both fit, compare four concurrent
   healthy hosts. Measure startup, cases, completion and closure separately;
   causal CPU/GC attribution needs actual event measurements, not static counts.
   Keep a finite diagnostic budget and retain failed measurements. This
   experiment was designed here, not executed.
4. **Prioritize distinct source-bound unresolved mutants.** Separate real
   survivors/no-coverage, timeouts, evidence-only rejections and already-killed
   duplicate failures. Add tests for missing semantics, rather than changing
   assertion wording merely to satisfy the classifier.
5. **Run another complete campaign only when its output can be consumed.**
   First verify the actual maintained profile and ingestion path accept the
   frozen input. Then run the full source scope once and report raw Killed-only
   score, evidence-qualified score and release blockers separately. Leave full
   ordinary/release tests and critical invariant obligations intact.

Until that correction is agreed, continuing the same mutation-fix loop is not
the recommended next step. No long campaign or further implementation work was
started during this investigation.

## Evidence and limitations

The retained [191 union review](../parallel-campaigns-2026-10-25/retained/union191-actual-peer01.md),
[197 pilot](../capture-runtime-followup-2026-10-25/options-pilot197/README.md),
[non-direct census](../capture-runtime-followup-2026-10-25/options-pilot197/nondirect-review.md)
and [abnormal-host timelines](../capture-runtime-followup-2026-10-25/options-pilot197/abnormal-host-review.md)
provide the main original observations. Independent read-only reviews are
retained with this investigation. They do not rewrite the old reports or
reclassify any outcome.

| Frozen independent review | SHA256 |
| --- | --- |
| [Gate and score audit](capture-audit.md.txt) | `642eab49bd731532b373e5f1e681838422b40775758b32f67934fb7af0a5d849` |
| [Unit test design and timing](unit-test-audit.md.txt) | `72b31d6c7c7d6b6278d7b4595edaad7407b3f9167acf1c9c05702419a111b900` |
| [Pipes and workflow cost](pipes-workflow-audit.md.txt) | `778216435d323e530e640633c1cf25b709593c5b6cf83d1725bda782fd740ef8` |

These are byte-identical text copies of the private reviews. Their internal
relative references retain the original `.artifacts/hcg/` working locations;
the links in this main report resolve from this canonical evidence folder.

Graph context at investigation start was fresh build
`e59b1eb6716f75756134660e1e9aceddd51284f7487bc657ba250afbbcc13c4a`,
24,494 nodes/119,286 edges, zero errors and 264 extraction warnings.
Exact DirectAssertion lookup and a four-node bounded caller query were not
truncated; canonical source was read directly. C# compiler-call observations
are partial (`cis.csharp.semantic-facts/3`); schema/capability metadata is not
reported by the status API. No blanket semantic coverage is claimed.
Final refresh and status records are retained under
`.artifacts/hcg/mutation-process200-final-graph-*`.
