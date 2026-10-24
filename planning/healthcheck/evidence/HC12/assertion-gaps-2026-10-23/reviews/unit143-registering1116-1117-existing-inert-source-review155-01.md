# Existing inert registration witnesses for current Unit143 selectors 1116/1117

Bounded source-only review, 24 October 2026. No build, test, control, mutation campaign, canonical edit, policy change or credit was performed. Selector numbers below identify creation records in this exact Unit143 prefix only; no historical result or outcome is transferred.

## Physical inputs and exact subject mapping

- Shipping `implementations/csharp/src/CIS.Core/Healthcheck/HealthCollectorRunner.cs`: SHA-256 `386c3d5cd7e681ed58a2d6be564ad3c813b598b377921ff5057a61419067fec8`.
- Actual retained parent prefix `.artifacts/hcg/unit143-closed1039-1055-153-receiving01/unit143-closed1039-1055-153-prefix01/native-parent-prefix.log`: `c164ad7645a81dae63d127e74ab25b3c762165d235358f1239622b88ce0ccc42`.
- Its actual GUID/FQN/display cohort: `73f73fcd9d250b888fe68311b81551cadd44707a3ca11a05ca3009d20880d603`.
- Creation line 11828, `03:04:35.5288256Z`: selector 1116, StatementMutator, `registering++;` to `;`, shipping UTF-16 `[5266,5280)`.
- Creation line 11829, `03:04:35.5292125Z`: selector 1117, PostfixUnaryMutator, `registering++` to `registering--`, shipping UTF-16 `[5266,5279)`.
- Actual generated line 13506 independently places both selectors in the admission lock immediately after `EnsureAdmission()`. Its branches are exactly skipped increment versus decrement versus original increment. This establishes the subject identity, not its final report status.

## Terminal accounting

`Register` lines 88-102 validate the actual arguments, service ownership and any interval/clock conversion before admission. Line 103 increments `registering` under `gate`, then the actual registration/handle/retirement subscription is constructed and published at lines 108-130. Its `finally` always decrements the counter at line 135 and calls `SignalProgress` at line 136. The complete file has no other counter updates or assignments.

Starting from zero, one admitted, completed registration therefore leaves the original counter zero, selector 1116 at minus one, and selector 1117 at minus two. Both selected changes preserve the actual registration body and its retirement subscription. This statement concerns the bounded one-registration scenarios below; it is not an unbounded integer-overflow claim.

The actual registration retirement is independently joinable: `RetireAsync` awaits `state.WithdrawCollection`, marks `StateSettled`, decrements `closing`, disposes the actual retirement subscription, and calls `SignalProgress(entry)` (235-247). Entry disposal succeeds when the entry is closed, state-settled and has no current operation (297,301); that condition does not depend on `registering`.

By contrast, the sole runner completion publication requires `stopping && !starting && !pumpScheduled && timerSettled && registering == 0 && closing == 0 && operations.Count == 0` (298,304-305). `StopAsync` returns that real completion task; `DisposeAsync` awaits it before disposing the service-stopping subscription (144-156). The negative counter thus obstructs terminal publication even after actual registration retirement has completed. Further Stop/Dispose invocations do not repair the counter. No Start means the original runner has no timer, pump or acquired lease in these particular scenarios. An incomplete signal must not be generalized to scenarios with real queued work or live resource owners.

## Strongest existing assertion after real joins and whole owner release

`HealthCollectorIntervalValidationBoundaryTests.cs`, canonical and frozen143 SHA-256 **`2a9a53dcda371dacdc3768fc15708951e55af5fc6d7856974f8f9d19806888ad`**. Both physical copies were hashed and match.

Existing public method `PositiveRepresentableIntervalEndpointsAttachWithoutStartingTheOwner` has two original rows:

| Actual original GUID | Existing display/interval ticks |
| --- | --- |
| `1056895c-4140-8af5-83c3-2f27840b4949` | `PositiveRepresentableIntervalEndpointsAttachWithoutStartingTheOwner (1)` |
| `1dbe0d91-508b-8841-8a48-d3472b64ceba` | `PositiveRepresentableIntervalEndpointsAttachWithoutStartingTheOwner (42949672940000)` |

The original body (74-85) performs one real successful `Register`, preserves its clock-read/zero-acquisition/attached-collection assertions, then joins the actual registration's `DisposeAsync` in `finally`. It never Starts or Collects. The unchanged NoInlining scenario capsule then independently invokes real Stop, runner disposal and service disposal (166-168). Only a successfully completed original body with zero successful timer creations and zero owner acquisitions is eligible for owner observation (174). Actual service disposal must join (179); it is not replaced by a weak-reference check.

Static task observers record actual Stop/Dispose success or faults without retaining the runner through their state. The helper returns weak references to the actual Scope, Runner, Service and Monitor, and weak references to those observers (180-193). The retained clock/owner contain scalar fields only. The caller requires the complete weak graph to die, rechecks zero timer creation/acquisition, observes any surviving actual observers, and retains unknown fault/cancellation holds (105-134). The existing assertions then require actual `StopSucceeded` and `DisposeSucceeded` (136-141).

This is the strongest already-existing source candidate for an assertion specifically **after** the original body, actual registration/service joins and whole owner release. Under the two exact counter changes, source analysis predicts pending terminal signals rather than success. It does not establish an executed witness. A live graph, surviving pending observer, unexpected real fault, partial original body or actual timer/acquisition remains a runtime/cleanup hold under the existing code. No body, callback, clock, ownership or drain change is proposed.

The other interval rows are not substitutes: invalid intervals and unavailable-clock cases reject before the counter; the timer-factory-failure case closes admission before its attempted Register reaches the increment.

## Narrowest existing inert terminal witness

`HealthCollectorInertRegistrationStopBoundaryTests.cs`, canonical and frozen143 SHA-256 **`2f73ac96811a08bd85693f8128cabd35f24df104b1541552e6ee76f8b9950a9e`**. Both physical copies match.

Existing row `RetiredInertRegistrationLeavesNoActualWorkForTerminalStop`, actual original GUID **`1b181a18-5aaf-8d5d-91d2-0fca7f4fe5de`**:

1. Performs one actual registration and joins its real retirement before calling Stop (49,53-54).
2. Preserves zero owner/timer assertions and actual before-stop monitor/snapshot checks (55-60).
3. Calls real Stop (64), observes an actual completed fault/cancellation, and already asserts `stop.IsCompletedSuccessfully` (68-69).
4. A pending-stop assertion is only retained as such after independently starting real registration/runner/monitor/service drains, successfully joining all three actual state drains, and rechecking zero acquisition/timer and genuinely pending Stop/Dispose (89-108).
5. The NoInlining helper returns no strong runner/service/registration/task reference. The caller must prove the actual runner unreachable before rethrowing the original assertion; a live runner converts the result to an Aggregate cleanup/ownership hold (14-27).

The timing distinction matters: this row **first asserts before cleanup**, then rethrows the assertion only after its narrower weak-runner proof. It does not make the Interval fixture's whole-graph, post-owner-death success assertion. Its original after-stop monitor/snapshot assertions are retained in the source but will not execute when the terminal assertion first fails; no executed-body equivalence is claimed. It remains the narrowest existing exact no-Start/no-Collect/no-timer/no-lease registration scenario.

## Obstruction and next useful evidence

Neither candidate needs an added assertion or a broadened fixture to expose the source contract. Existing lifecycle row `ConstructionRegistrationAndSnapshotCaptureNeverAcquireOwnerDependencies` also Registers without starting, but its existing body checks snapshots/dependency calls and delegates terminal waiting to Scope cleanup; the counter defect can therefore surface there as cleanup timeout rather than the explicit terminal assertion. It is a weaker candidate for this purpose.

The outstanding question is execution/capture, not absence of a source assertion: obtain the exact current1116/1117 requested GUID/results and actual failure/cleanup evidence when those runs close. Confirm whether these three existing rows were selected, their actual final errors, live-owner/observer holds, and normal host/capture/restoration before proposing any remedy. This source review does not infer raw Killed/Survived status, direct kill credit, completed native qualification or permission to ignore another row's runtime error. Broad identifier trials remain with the Unit agent.

## Addendum: both exact source definitions already have preserved finite proof

At the parent's request, the two subject definitions were joined by source path/hash, original UTF-16 span/text, mutation operator, replacement and static flag to the retained **registration88** report. Both are already included in that completed four-definition finite packet and consequently are repeats within the maintained 37-definition finite inventory; they are not two new distinct definitions.

Original packet: `planning/healthcheck/evidence/HC12/assertion-gaps-2026-10-23/retained/registration88-complete-compact01.tar.gz`, **32,346,310 bytes**, physically rehashed **`c21da64ce4c82414126a8892f335c99a53a170279b013a5dc2bba43ddf11cd7c`**. Only selected report/metadata members were extracted into the new private `.artifacts/hcg/registering155-prior-finite-receiving01`; the retained original is unchanged.

The actual raw report member is `unit-registration-family88-run01/native/reports/mutation-report.json`, SHA-256 **`9411a9a581b6e2e77dd7ff87b7bcbe14b39a612cbea074e4845fbef9f0912931`**. Its file key is the original absolute run-source path ending `/src/CIS.Core/Healthcheck/HealthCollectorRunner.cs`. Hashing its embedded source as UTF-8 yields the exact shipping `386c3d5c…` hash above. Its actual definitions at line103, column42 have these complete identities:

| Source definition (path `Healthcheck/HealthCollectorRunner.cs`, SHA `386c3d5c…`, static false) | Actual preserved finite result | Preserved actual direct witness |
| --- | --- | --- |
| `[5266,5280)`, `registering++;`, **Statement mutation**, replacement `;` | Raw Killed, report-local ID1116 | Inert registration row GUID `1b181a18-5aaf-8d5d-91d2-0fca7f4fe5de`, actual host1548544/controller1546852 |
| `[5266,5279)`, `registering++`, **PostIncrementExpression to PostDecrementExpression mutation**, replacement `registering--` | Raw Killed, report-local ID1117 | Same exact inert row GUID, actual host1548533/controller1546851 |

The actual raw report's `killedBy` for each is that exact GUID. The saved native audit's two corresponding witness records both identify the original `Assert.IsTrue(stop.IsCompletedSuccessfully)` failure and actual OS exit0. The fixture hash in the saved direct-assertion review is the unchanged `2f73ac96…` canonical/frozen143 fixture above. The finite run originally recorded 6/6 normal execution hosts, 1/1 normal discovery, four direct witnesses and zero issues, with original/fresh one-case controls and native exit0. Its result explicitly sets `wholeUnionOrGate=false`, `completePartitionQualification=false`, `appliedKillCredit=0`.

Additional physically checked member pins:

- `actual-native-audit.json`: **`afb1d81e2f7825caca9c14d54b40920f63f2c6adda6677828cc586e8e7dfba07`**.
- `actual-scope.json`: **`84dd9b560b4248dab6baf044d6ea345bee5b68ec26d4f37a09bdfb12b15e9cfc`**; its full-tuple rows match both definitions, including the semantic mutation names above.
- `direct-assertion-review.json`: **`24a201e9bddd588579e6c070472c188668f25412ca1b4f5ffdf246f64c2df1cf`**.
- `results.json`: **`93104a5e8e9e7602ca88a94130a87db9a87b5cea2fbaf0aa221312e1f03267f8`**.
- Original payload `input.json`: **`a5ab9b128d64f8b363aa0ebfbcccf0d2befb9f38480a69c951746d0237465ecc`**; contains the exact four source-bound definitions and one original inert row.

This addendum reads the preserved raw source/definitions and original receiving records; it does not rerun Audit8, reconstruct the complete kernel/parent protocol, execute the test or create a new finite result. It establishes an existing precise assertion proof rather than a missing-fixture theory. Any current full143 failures, extra selected rows, incomplete host results or integrity issues remain entirely separate and unresolved until their own actual complete evidence is reviewed. Coincident numeric IDs in the two invocations supply no status transfer, additional credit, fourth partition or waiver.
