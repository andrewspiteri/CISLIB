# Review 03-provider-lifecycle-flush-1: flush readers, controls and documentation

**Scope:** static review of the 11 assigned files, the 4 baseline-deleted Python readers and the bounded context. Nothing was executed. Line numbers are approximate because the files were supplied without numbering.

## Findings

### F-1 (Medium): corruption controls no longer check the expected reason

**Where:** `FlushReaderMigrationTests.cs`, the last two statements of `OriginalFlushReaderCorruptionsAreRejectedAfterPositivePhysicalOutput` (about L203–204). Also `FlushResultExpectations.cs` L7 and `FlushStressExpectations.cs` L8.

**Comparison with the deleted readers:** each Python `corruption_controls` required `str(error)==reason`, otherwise it raised `unexpected corruption reason`. The C# test only asserts the exception type: `Assert.ThrowsExactly<InvalidDataException>` or `<InvalidOperationException>`. It never compares the message.

**Why the reason cannot be recovered:**
- The two Expectations classes always throw one generic message ("Public flush expectation failed." / "Flush stress report expectation failed.").
- Those classes run after the reason-bearing reader checks. Many controls are rejected only by them: for example results `immutable`, `timer`, `cancel`, `later-facts`, and stress `cancel-count`, `loss`, `owners`, `callback`, `notification`, `source`.
- For results and stress, `InvalidOperationException` is also what System.Text.Json throws on node-kind mismatch (`GetValue<T>`, `AsObject`, `AsArray`). LINQ `Single` throws it too.

**Concrete failure:**
- A control passes when any check of the same type rejects the corruption. A shape or accessor error also satisfies it.
- If two layers both reject a corruption, deleting or breaking the intended check goes undetected. Examples: boundary `global` is rejected both by `FlushReaderChecks` and by `FlushBoundaryExpectations` "initial global facts".
- The F2 correction restored intended-reason comparisons for the six file readers. The flush readers now have the same defect.

**Fix:**
- Make each control row carry its expected C# reason.
- Assert `exception.Message == reason` on the exception returned by `ThrowsExactly`.
- Give the Expectations `Require` helpers distinct reasons.

### F-2 (Medium): current documentation makes untrue verification claims

1. **`flush-coordination.md`, "Verification scope", paragraph 1.** It says the independent native reader validates "sixteen exact-reason corruption controls". That reader is deleted, and the C# replacement does not check reasons (F-1).
2. **`flush-stress-qualification.md`, the paragraph beginning "The C# fixtures make live assertions…".**
   - It says, in the present tense, that "The independent native Python reader checks…" and that the "34 intentional corruptions must each fail for the exact declared reason". Both statements are false at the current head.
   - Unlike `flush-results.md`, this file has no preface marking the narrative as historical.
3. **`flush-stress-qualification.md`, table row `repeat-progress`.**
   - It says the waits detach "while no flush control remains".
   - Current `FlushStressReaderChecks.Repeat` requires `before.controls == afterDetach.controls == 1` for progress. The deleted Python reader required 0.
   - The file's own final paragraph agrees with the current behaviour, so the table contradicts it.
4. **`flush-results.md`, final paragraph.**
   - It says "its unchanged imported read helpers temporarily remain for six pending stress/file/timer migrations."
   - In this bundle, `verify-flush-results.py` is listed as baseline-deleted, so those helpers no longer exist in that file.

**Fix:** correct or label as historical each of these statements, and reword the reason claims to match whatever F-1 resolves to.

## Overlap with the eleven known fixes

- **F2 (intended-reason comparisons):** that fix covered the file readers only. The flush readers in this bundle have the same gap (see F-1 above).
- **R1, R2 and the other fixes:** they do not overlap the assigned files. `LogFlushResult` includes incoming loss in `LossObserved`, which matches R1.

## Parity of the deleted Python readers (positive checks)

Each Python mode, expectation and the control counts (12+16+26+34 = 88; 41 modes) is mirrored. That matches the documented 44 and 129 integration totals.

There are two intentional divergences:
- The `exhaustion` result now expects `controlsAfterWait` = 1.
- The `repeat-progress` scenario now expects 1 retained control.

Both match the documentation's "registration behind active I/O remains" rule. No production defects were confirmed in `FlushBoundary.cs`, `FlushDeadline.cs` or `LogFlushResult.cs`.

## Questions and limits

1. **Physical, identity and input-parsing checks.** These depend on the unsupplied `LifecycleProcessChecks.Run`/`Validate` and `LifecycleReportChecks`. The deleted readers required:
   - on-disk file bytes equal to the reported bytes;
   - the exact inventory document and directory membership in boundary `physical`;
   - the raw `flush-results.jsonl` content;
   - `os`/`coreSha256`/`fixtureSha256` bindings;
   - rejection of duplicate keys and non-finite numbers in child stdout.

   Parity cannot be confirmed without those files.
2. **Unsupplied checks behind specific controls.** The results `UTC`/`framing` controls and the stress `framing` control rely entirely on `LifecycleReportChecks`. That code must throw `InvalidOperationException` for these controls to pass.
3. **Stress case-map binding.** The deleted stress reader bound `conformance/logging/flush-stress-cases.json` case IDs to its modes and hashed the map. No supplied C# file does this. Where is the bidirectional binding the documentation claims?
4. **Remaining Python importers.** Do any remaining Python file or timer drivers still load `verify-flush-results.py` via `spec_from_file_location`? If so, they now fail at import.
5. **Summary formula in `FlushStressExpectations.Result`.** It requires Completed exactly when there is no pending work and the flush is terminal. The current contract allows "TimedOut can have zero Pending". This appears unreachable under the controlled clock, but the oracle encodes the pre-amendment rule.
6. **Required-case inventory.** I could not check the inventory entries for the 88 `DynamicData` rows or the unit counts (31/63).

Missing context: `LifecycleProcessChecks`, `LifecycleReportChecks`, fixture catalogs, migration receipts, unit tests and the required-case inventory.