# CISLIB review — pass 04 (files and timers)

## Verdict: FINDINGS

These are evidence and verification-quality defects. I found no confirmed shipping-code correctness bug in the file or timer runtime paths reviewed.

## Findings (prioritized)

### F1 — The 23-mode JSONL boundary/recovery matrix is not a required case, yet acceptance evidence depends on it (confirmed)

- **Where:** `JsonlBoundaryProcessTests.ManagedJsonlInspectionAndStartupPreserveActualPhysicalBoundary`. It runs all 23 modes from `JsonlBoundaryExpectations.Modes`.
- **The gap:** `conformance/logging/case-inventory.json` binds only `OriginalBoundaryReaderCorruptionsAreRejectedAfterPhysicalPositive`, as `INT-JSONL-BOUNDARY-READER-MIGRATION` (12 executions). That control test runs positive physical children for only six modes:
  - object
  - wrong-format
  - invalid-json
  - partial-json
  - repeat
  - partial-utf8
- **Why it matters:** `file-recovery-qualification-map.json` cites the `boundary` lane's `retention-full` mode as WP05-S07-AC03 evidence. That mode is the full-group intent-first recovery correction. The JSONL boundary doc also claims "35 integration" cases. If the matrix method were filtered, renamed or not discovered, discovery reconciliation would not fail. The 17 unbound modes include:
  - retention-full and identity-exhausted
  - both collision modes
  - bom, crlf and array
  - with-retained
- **Fix:** Add a case-inventory binding for the matrix method with `minimumExecutions: 23` on both lanes. This matches the pattern already used for `INT-FILE-TRANSITION-NATIVE-MODES`.

### F2 — The selector `inventory-bound` control no longer isolates the size guard (confirmed lost negative-control isolation)

- **Where:** `JsonlInputSelectorTests.OriginalSelectorCorruptionsAreRejectedAfterPositive`, case `inventory-bound`.
- **Native version:** it writes `InventoryBytes + 1` spaces, which is not valid JSON. The selector would reject it even if the 2 MiB bound check were deleted.
- **Python version:** in `select-jsonl-inputs` qualification, it first padded the valid document to exactly 2 MiB (a positive case). It then appended one space, producing valid JSON one byte over the bound. Only the bound guard could reject that input.
- **Note:** the native `entry-bound` case is still a valid JSON object, so it remains isolated.
- **Fix:** Pad the serialized valid inventory document with spaces to `InventoryBytes + 1`, matching the exact-bound positive case.

### F3 — Several migrated readers dropped the "rejected for the intended reason" meta-check (confirmed weakening; no wrong-reason pass found)

- **What Python did:** every corruption control asserted `str(error) == reason`, so a control could not pass by tripping an earlier, unrelated assertion.
- **Native readers that keep it:** the restart and timer readers (`Assert.AreEqual(control.Reason, error.Message)`).
- **Native readers that only assert some exception type:**
  - `FileTransitionProcessTests` and `JsonlBoundaryProcessTests` (`ThrowsExactly<InvalidDataException>`).
  - `FileRotationProcessTests`, `FileRetentionProcessTests`, `FileInventoryProcessTests` and `FileAcquisitionProcessTests` (`AssertFailedException`).
- **Impact:** I traced each current control and found none that is rejected by the wrong assertion today. Future reordering or loosening of the readers could hide a dead guard.
- **Fix:** Pair each control ID with the expected `InvalidDataException` message. Where a reader uses `Assert`, switch to the `Require(..., reason)` style, as `FileTransitionExpectations` already does.

### F4 — Possible lost raw-format-character check in human startup records (uncertain)

- **Python behavior:** `verify-file-runtime-startup.py::records` rejected any raw Unicode `Cc`/`Cf`/`Zl`/`Zp` character in human lines.
- **Native behavior:** `FileRuntimeStartupExpectations.CheckRecords` only rejects `\r` and ESC, then parses via `HumanRenderingExpectations.Read`.
- **Why it matters:** for `file-display-loss` (a message of 8,192 ZWJ characters, which are `Cf`), a renderer regression that emitted ZWJ unescaped would still pass the prefix and truncation checks once JSON-decoded.
- **What decides it:** `HumanRenderingExpectations.Read` was not in the payload. If it enforces escaping, this is not a defect.
- **Fix if needed:** Add the category check to `CheckRecords` for human records.

### F5 — "delete-*-directory" cases no longer exercise deletion failure (confirmed evidence mislabel, low)

- **Current code path:** `FileGroupTransition.Begin` opens and hashes each planned retirement before publishing intent. A directory victim therefore fails there, with `UnauthorizedAccessException` mapped to `RetentionDeleteFailed`. This happens before intent is published, and `File.Delete` is never reached.
- **Evidence that confirms it:** `FileFailureExpectations` requires `before == after`, meaning no transition file exists.
- **Impact:** the acceptance maps cite these modes under WP05-S06-AC03 and WP05-S07-AC03 ("failed required deletion"). The only real after-intent delete denial is in the corrective `rotation-denied` and `recovery-denied` cases, whose source was not in this pass.
- **Fix:** Relabel these modes in both maps and in `file-policy-failures.md` as "unreadable or uninspectable retirement member (pre-intent)". Keep the corrective cases as the delete-failure evidence.

### F6 — Holder report in the two-process inventory test is under-validated (confirmed, low)

- **Where:** `FileInventoryProcessTests.TwoProcessesHoldOneInventoryIdentityAndCannotOverwriteEachOther` checks the holder's `ready` report with `Validate(ready, "acquired")` only.
- **What is unchecked:** `created == true`, format, length, segments and inventory hash. Python's `check(..., created=True, path=path)` checked all of these.
- **Partial mitigation:** the later reopen asserts group and metadata equality.
- **Fix:** Check the acquired fields on `ready`, as `Probe` already does.

### Q1 — The Developer run's commit is not the reviewed HEAD (provenance question)

- `summary.json` records `source.commit = 1de526ed…`, but the reviewed HEAD is `54e01194…` and there are five uncommitted files.
- Changes after `1de526ed` are not covered by run `bfce3f24…`.
- **Action:** Either state that limit wherever "6,434 passed" is cited for this submission, or rerun the Developer profile on the frozen HEAD.

## Checked and found consistent

Each deleted Python reader in this pass was compared with its native replacement.

- **Acquisition:** includes the dot-alias, kill/release and disabled-locking cases.
- **Inventory:**
  - all 20 physical modes
  - bounds at 1000 and 1001
  - depth limit 14
  - bad UTF-8 and BOM
- **Group consumers:**
  - real OS denials with exact ACL and mode restoration
  - six controls
- **Retention:** the preimage overlap at segment 5 is a valid positive.
- **Rotation:** LF-inclusive arithmetic.
- **Rotation policy:**
  - intent schema 2 with retirements
  - candidate-cleanup supersession, which is documented
  - Linux `prlimit` on the owned child only
- **Runtime startup:** all 27 modes and the observed identity.
- **JSONL boundary:** `retention-full` now succeeds, consistent with the documented full-group recovery change.
- **Transition:**
  - schema-2 retirement expectations match fixture arithmetic for both rotation and recovery
  - restart matrix plus native selector, with expected selected inputs of 3, 2 and 1
- **Timer readers:** every one of the 97 corruptions maps one-to-one, with reasons preserved.
- **Timer map test:** catalog and fixture mode equality.
- **Core paths checked for defects, none found:**
  - `FileGroupTransition.Reconcile` stage logic
  - `FileOutputOwner.CommitTransition`
  - `FileMetadataPublication` candidate cleanup
  - the `JsonlRecordBoundary` exact-cap scan
  - `StartFileFlushTimer` detach and arm races

## Limits and unreviewed context

- No execution took place; all conclusions come from static reading.
- **Source not in the payload, so not reviewed:**
  - `HumanRenderingExpectations`
  - `FlushStressSupport`
  - the `verify-flush-results.py` helper
  - `JsonlInputSelector.cs`
  - `LoggingReviewProcessTests` (the rotation-denied and recovery-denied cases)
  - `FileFailureTests` and `FileRotationPolicyTests`
- I did not independently verify that the corrective after-intent delete-denial cases exist or are correct.
- Windows ACL restore semantics with inherited ACEs were reasoned about, not observed.
- The Developer run predates HEAD (Q1).
- Other passes' scopes were used only as boundary context.