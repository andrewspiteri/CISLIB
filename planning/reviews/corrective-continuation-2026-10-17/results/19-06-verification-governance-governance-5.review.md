# Static review: 06-verification-governance-governance-5

This review covers only the supplied map, the evidence records and the bounded context files. I executed nothing and checked no hashes against artifacts. Line numbers are approximate because the bundle has no line numbers.

## Confirmed findings

### F1 (Medium): Discovery corruption controls accept any assertion failure
**File:** `planning/evidence/corrective-discovery-migration-2026-10-15.md`, the paragraph after the observation-map table (around L29).

The record says each of the five `INT-DISCOVERY-OBSERVATION-CONTROLS` cases "changes one observed field and requires assertion failure." It names no specific rejection reason.

**Concrete failure:** a control could pass because an unrelated assertion failed first. For example, a corrupted runtime or Core hash could be rejected by a shape or field-set assertion. If the intended runtime or hash assertion were deleted or weakened, the control would still pass.

This is the same defect class that the F2/F3 fixes corrected for six file readers ("restored stable intended-reason comparisons"). This reader was not in that fix set. Other migrations in this bundle state reason binding explicitly ("requires its specific rejection reason" for provider scopes/hosts, mapping/state and provider behavior).

**Fix:** bind each of the five controls to its expected assertion reason, then update the record.

### F2 (Medium): The map's "Current position" is stale and directs agents to redo finished work
**File:** `planning/corrective-implementation-map-2026-10-15.md`, "Current position — 16 October 2026" (around L9–L13).

- **What it says:** "B's native verification migration is the next corrective work." It also says the record "does not declare all remaining native utilities ... complete."
- **What later rows in the same file say:**
  - "Maintained Python cleanup complete — Zero maintained implementation Python remains."
  - A full 6,434-case DiscoveryHost checkpoint.
  - Retirement of all Type Manager utilities.
- **What the section omits:** what actually remains of Batch B's acceptance:
  - Supported IDE runs.
  - Separately measured discovery and execution timing. Batch A states the launcher does not time these separately.
  - Migration of the policy-bearing PowerShell modules. The checkpoint lists "substantive PowerShell policy assessment" as pending.

The section claims to supersede the historical status, so an agent following it would restart completed Python migration and miss the real open items.

**Fix:** restate the current position as Python migration done, with the remaining Batch B acceptance items listed explicitly.

### F3 (Low): Table rows sit outside the table, after a now-misleading "no edits" claim
**File:** same map, end of file (after "Source and review record").

- **Orphaned rows:** the rows from "File-failure Python driver (retired)" through "Maintained Python cleanup complete" come after the closing paragraph. They have no table header or delimiter, so in GitHub-flavoured Markdown they render as raw pipe-delimited text rather than as part of the Batch B table.
- **Misleading claim:** they directly follow "No implementation, test, dependency ... has been edited," which was true of the original map's creation but now reads as a current claim beside rows that describe test changes.
- **Unmarked retirements:** the in-table rows for `verify-type-manager-examples.py`, `verify-type-manager-package.py`, the inventory/runtime matrix utilities, `verify-type-manager-instrumentation.py` and `verify-provider-behavior.py` carry no "(retired)" marker. The appended rows say they are retired.

**Fix:**
- Move these rows into the Batch B table.
- Scope the "no edits" sentence to the original map's creation.
- Mark the in-table rows as retired.

### F4 (Low): The map omits that the fixes are unreviewed and that Claude review has resumed
**File:** same map, "Known Claude findings" section (the fix delta) and the intro paragraph.

- **Fixes unreviewed:** the new section says the findings are "fixed and verified" and that "Three Claude scopes ... remain pending." It omits that the fixes themselves have not been reviewed by Claude. `corrections.md` and the checkpoint both state this explicitly.
- **Review resumption not recorded:** the intro still records only the instruction to stop Claude reviews. Neither the map nor AGENTS.md records the later authorization that resumed them.

A reader of the map alone could take "verified" to mean reviewed, and could believe Claude review is still prohibited.

### F5 (Low): The five SDK-origin controls don't match the stated required minimum
**File:** `planning/evidence/corrective-batch-a-2026-10-15.md`, Implementation, bullet 2.

The bullet now lists five controls, including the mismatched SDK-version directory control that the R1 follow-up added. It still says "the required negative-case minimum rises from 20 to 24," which is the pre-review state with four controls.

R1 raises the architecture selection to 29 but never says whether the minimum became 25. If it stayed at 24, removing the fifth control would not trip the minimum gate.

**Fix:** reconcile the minimum, or state explicitly that the fifth control is protected by an exact binding rather than the minimum.

### F6 (Low): Gaps in artifact identity for the R1/R2/R4 follow-up claims
**Files:** `corrective-batch-a-2026-10-15.json` (`followupEvidence`) and the matching md.

- **Unpinned evidence:** the md cites `review-architecture-tests.log` (for the 29-case R1 result), `review-pack.log` and `hung-test.log` as evidence. None of them is hash-pinned in the JSON. The 29-case architecture run also has no focused run ID, unlike the pre-review run `d34ddd03…`.
- **Indistinguishable controls:** `override-Developer.log` and `override-Release.log` have the identical SHA-256 `e196f5f5…`. The retained evidence therefore cannot show that two distinct profile entry points were each rejected.

**Fix:** pin the missing logs and the run ID. Make the override logs record which profile was invoked.

## Questions (not confirmed)

1. **IncomingQueueFull error code (overlaps the R2 fix).** `runtime-contract.json` `writeResults` says "Rejected/IncomingQueueFull has no ticket or output entries." It then says "ErrorCode is null for other normal outcomes." If IncomingQueueFull is reported as an ErrorCode on Rejected, the two statements conflict.

2. **Expected-reason binding in other migrations.** These records are silent on whether controls bind to an expected reason:
   - Lifecycle records only "rejected IDs".
   - Restart, file-group, file-startup, timers, flush (coordination, result, stress), cleanup/creation, events, exceptions and loggers say nothing.
   - Coverage's 23 invalid-evidence cases may reject on exit code alone.

   Given F1 and the F2/F3 fix class, please confirm reason binding for each.

3. **cleanup-denied characterisation.** The file-failure record says the corrective cleanup-denied consumer applies "actual OS deletion denial after intent publication." `corrections.md` names only rotation-denied and recovery-denied as actual deletion-denial evidence. Is the cleanup-denied claim accurate?

4. **Python-cleanup checkpoint.** The "Maintained Python cleanup complete" row cites the same 6,434 total as the DiscoveryHost checkpoint `22fefe6c`. Was the cleanup checkpoint a fresh run after the Type Manager utility retirements? Its record was not supplied.

5. **Head versus tested source.** The current head is `396e87d6`; the tested source is `7a606607`. Is the delta truly report-only?

6. **Documentation selector.** The documentation JSONL selector now requires the nonshipping `CIS.DependencyChecker`. Is that acceptable for documentation consumers?

## Checked and consistent

- **Python file inventory:** the remaining-file counts are consistent across the supplied records, from 92 originally to 0 maintained.
- **Developer totals:** every total adds up: 5,637, 5,666, 5,747, 5,896, 5,947, 6,260, 6,434 and 6,438.
- **Migration counts:** these match the map:
  - Provider scopes/hosts: 91, 139, 43 and 192.
  - Compatibility: 86 + 1 + 24 = 111.
  - Loggers: 28 legacy controls within 32 comparator cases, and 98 outcome pairs.
  - Flush, cleanup and lifecycle modes and controls.
  - Timers: 97 controls and 130 cases.
  - Restart: 47 and 93 children.

## Limits

- I saw no test source, case catalogs, TRX or summary files, so I cannot confirm reason binding, catalog membership or any parity claim.
- I did not verify pinned hashes, including the map and md hashes frozen in the Batch A JSON.
- The value, scopes, rendering, queues, stop-reader, retention, transition, rotation-policy, Type Manager utility and python-cleanup records are referenced but not supplied.
- AGENTS.md's stale guidance to stop Claude reviews is outside the assigned scope; I note it only as context.
- NO FINDINGS is not claimed for this scope.