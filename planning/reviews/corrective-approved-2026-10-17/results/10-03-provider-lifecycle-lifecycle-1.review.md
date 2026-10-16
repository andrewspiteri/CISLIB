# Review 03-provider-lifecycle-lifecycle-1: provider lifecycle, stop/cleanup/creation reader migration

**Scope:** the assigned docs, integration tests, DiscoveryHost expectations and fixtures, the five deleted Python readers, and the bounded Core context supplied. I executed nothing. Line numbers are not available in the bundle, so locations are given by member and code.

## Findings

### F1 — Medium — The migrated readers lost the original intended-reason checks

**Where**
- `StopReaderMigrationTests.MigratedShutdownReaderRejectsTheOriginalCorruption`, which uses `Assert.ThrowsExactly<InvalidOperationException>(...)`.
- The `Require` helper in each of `StopStartExpectations`, `StopDrainExpectations`, `StopJoinExpectations`, `StopCleanupExpectations` and `RuntimeCreateExpectations`. Each one throws a single generic message, such as "Cleanup expectation failed.".
- `StopLifecycleReaderControls.Reject`, which uses `ThrowsExactly<InvalidDataException>` against the generic message "Lifecycle expectation failed.".

**What changed.** Every deleted Python reader required each corruption to fail for one exact reason (`require(str(error) == reason, ...)`). The C# migration checks only the exception type. That type is also what System.Text.Json throws for type and shape mismatches (`JsonValue.GetValue<T>`, `AsObject`, `AsArray`). So a control can "pass" through an unrelated check or a framework conversion error.

**Concrete cases where the intended check is never the one that rejects:**
- **stop-start `partition` and stop-drain `partition` (pending set to "0" on blocked-write).** Python rejected these as "boundary partition". In C#, the per-field pending check fails first, so the boundary partition identity in `LifecycleReportChecks.Result` is never the rejecting check. No control proves that identity check.
- **runtime-create `delivery` (written set to "0").** This is rejected by the partition identity inside `LifecycleReportChecks.Result`, before the explicit `Number(output["written"]) == 1` check is reached. That delivery check has no proving control.
- **stop-join `pre-type` (`deadlineCreated = true`).** This is rejected by `GetValue<int>()` throwing `InvalidOperationException` on a boolean value, not by any expectation check. Python had an explicit int-type check for this.

**Consequence.** Removing or weakening those specific checks would leave all 74 stop controls and 42 cleanup/creation controls green. That undercuts the docs' statement that C# "retains" the former corruptions.

**Fix (no new framework needed).**
- Give each `Require` a stable reason string; `LifecycleReportChecks.Require` already takes one.
- Assert the exception message per control, as F2 in `corrections.md` did for the file readers.
- Either restore the Python check order (partition before field equality) or retarget the affected controls.

### F2 — Medium — The stop-drain evidence and claims cover only the internal output-stage path, not the public incoming pipeline

**Where**
- `StopDrainFixture.ExecuteAsync`, which calls `CoreLogRuntime.CreateConfiguredQueued(...)` with no incoming pipeline.
- The kernel and crash modes in `StopLifecycleFixture`, which use the internal constructor with `OutputQueues`.
- The `docs/modules/logging/stop-drain.md` paragraphs on expiry and abandonment.

**What differs.** Public `Create()` always passes `incomingPipeline: true`. So production stop runs through a different implementation: `StopIncomingAsync`, `RunIncomingStopAsync` and `CaptureIncomingDeadline`. That path includes `incoming.Abandon` and output boundaries captured at incoming resolution.

**What the supplied tests cover.**
- The public-path modes in this bundle are all successful, `Completed` stops: runtime-create, and StopLifecycle `normal`, `normal-dual`, `producer-stop-race` and `pre-cancelled`.
- The expiry, held-I/O, abandonment and cancellation behavior that `stop-drain.md` describes is evidenced here only on the output-stage seam.
- `stop-drain.md` says the cases "exercise actual Core" and describes abandonment purely at output level. On the public path, records still in the incoming queue are abandoned in `LogIncomingSnapshot.Abandoned` instead.

**Fix.**
- Label the drain cases as internal output-stage composition.
- State where public incoming-pipeline expiry is evidenced. If it is not evidenced, mark it unqualified.
- Alternatively, add a public-`Create` expiry case.

### F3 — Low — `stop-drain.md` contains stale present-tense claims that contradict `lifecycle-cleanup.md`

**Stale statements:**
- "Public creation, DisposeAsync and complete cleanup-failure coordination remain following lifecycle work."
- "Full primary-error preservation and cleanup-fault diagnostics remain T04; this task does not claim…"
- "Independent Windows/Ubuntu readers validate exact DLL identities…" These readers are the deleted Python readers; the work is now done by C# integration tests.

`lifecycle-cleanup.md` documents all of this as implemented. This is a current module doc, not a frozen receipt. **Fix:** explicitly mark these sentences as historical T02 scope, or update them.

## Questions and limits

1. **Unit case count.** `lifecycle-cleanup.md` gives "166 integration cases … plus 15 cleanup unit cases".
   - 166 matches 18 + 42 + 32 + 74 process rows.
   - The "Thirteen controlled kernel cases" are `StopCleanupProcessTests` integration rows, which are already inside the 166.
   - It is unclear whether the 15 unit cases are separate unit-level kernel tests or a double count. I need the migration receipt and the unit test sources to tell.
2. **Missing runtime-create Python reader.** It was not supplied, so I cannot verify parity of its 13 controls or 5 modes.
3. **Process-level parity checks not visible.** `FixtureProcessChecks.RunAsync` was not supplied. Python pinned exit code 0, empty stderr, `os`, `coreSha256` and `fixtureSha256` per child; I cannot confirm C# does the same outside `StopLifecycleProcessTests`.
4. **Catalog order.** `LifecycleProcessChecks.Modes` compares catalog ids order-insensitively (`AreEquivalent`), whereas Python required exact order. Required-case binding files were not supplied.
5. **Production behavior I could not check:**
   - Whether `LogFlushResult.LossObserved` counts incoming abandonment. `LogFlushResult`, `OutputQueues`, `FlushDeadline` and `LoggingOptions.Prepare` were not supplied.
   - Whether every invalid setting surfaces as `CoreConfigurationException` from `Prepare` rather than `ArgumentOutOfRangeException` from the factory.
6. **Startup rollback edge cases.** `new OutputQueues(...)` sits outside the rollback `try` in `ComposeQueued`, and `IncomingEventQueue` launches its worker before it can throw. Either could leave a worker un-joined, but only through internal launch seams. I am not claiming this as a public defect.
7. **Overlap with the eleven known fixes.** The supplied R1/R2 incoming snapshot code (`RegisterIncomingBoundary`, `CapturePipelineResult`, and direct `IncomingAccounting` construction) is consistent with the correction notes. I found no regression within this scope.

No whole-change PASS, release qualification or execution evidence is implied.