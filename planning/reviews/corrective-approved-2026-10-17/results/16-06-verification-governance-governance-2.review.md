# CISLIB static review: subpass 06-verification-governance-governance-2

**Scope:** `conformance/logging/case-inventory.json` (current file plus fix delta), checked against the supplied context files. This is a static read only. I ran no code or tests.

**Line references:** The current file was supplied as a single minified line, while the diff shows a pretty-printed file of about 11,000 lines. Findings therefore cite case `id` values. Only fix-delta lines can be located by the diff hunks.

## Checks on the known fixes that overlap this file

- **F1 (23-mode JSONL boundary binding): verified as present.**
  - `INT-JSONL-BOUNDARY-NATIVE-MODES` binds `JsonlBoundaryProcessTests.ManagedJsonlInspectionAndStartupPreserveActualPhysicalBoundary`, with `minimumExecutions: 23` on both lanes.
  - The separate 12-control binding, `INT-JSONL-BOUNDARY-READER-MIGRATION`, is kept.
- **R1 (flush/stop caller snapshots): verified as present.**
  - All four regression methods named in `corrections.md` have bindings: `UT-REVIEW-R1-*`, each with a minimum of 1 on both lanes.
- **Other fix-delta changes:** These are whitespace and indentation only (lane arrays expanded, two `source` lines re-indented). They change no values.

## Confirmed findings

### 1. Medium: the same methods are required on Linux in one place and Windows-only in another

**Where:**
- `UT-FILTERING` bindings versus the rows `UT-LOGGER-OUTCOME-TABLE` and `UT-LOGGER-OUTCOME-CONTROLS`.

**What the file says:**
- `UT-FILTERING` lists `windows-x64` and `linux-ubuntu2404-x64`. Its bindings require:
  - `LoggerOutcomeTests.CatalogIncludesEachOrderedOutputPairForDirectAndLazyCalls` (minimum 1);
  - `LoggerOutcomeTests.ComparatorRejectsAlteredOutcomeAdmissionAndWireObservations` (minimum 1).
- The dedicated rows for those exact two methods list only `["windows-x64"]`.
- Their sibling row `ARCH-LOGGER-QUALIFICATION`, which covers the same WP04-S05-T04 task, is also Windows-only.

**Concrete failure:**
- If the Windows-only rows reflect reality (for example, the tests are platform-guarded or excluded on Linux), the first complete Ubuntu Developer run will fail required-case reconciliation for `UT-FILTERING`. Skipped or missing results are rejected, per `ARCH-EVIDENCE-NEGATIVE`.
- If the tests do run on Linux, the dedicated rows understate the lanes, and the inventory contradicts itself.

**Note on evidence:** The last full checkpoint was Windows-only. Full Ubuntu Developer is listed as pending, so this contradiction has not yet been exercised.

**Action:** Make the lanes agree in both places.

### 2. Low/Medium: `source` text says Windows-only execution while `lanes` require Linux

**Where:**
- `UT-DIAGNOSTICS-SURFACE`, `-READS`, `-CLOCK`, `-LIMITS`, `-CONCURRENT`, `-GUARD`, `-ENUM`, `AT-DIAGNOSTICS-IL`, `AT-DIAGNOSTICS-CONTROLS`. Their source says: *"Extra unit/IL controls execute on the Windows Developer lane."*
- `UT-CONSOLE-COMPOSITION-ENUMS`, `-STREAM`, `-INDEPENDENCE`. Their source says: *"Additional C# unit checks execute on Windows."*
- `UT-CONSOLE-COMPOSITION-LOSS-FULL`, `-LOSS-LATE`, `-LOSS-FAULTED`. Their source says: *"Windows C# controls … no release or all-platform unit claim."*

**Concrete failure:**
- Every one of these rows lists `linux-ubuntu2404-x64`.
- Either the claim in the text is stale, or Linux reconciliation will demand cases the text says do not run there.
- In both cases a reader cannot tell what Linux evidence is actually required.

**Partial supporting evidence:** `corrections.md` reports the 12 `DiagnosticsArchitectureTests` passing on Ubuntu. That suggests the diagnostics wording is stale, not the lanes. The console loss controls have no comparable evidence in the supplied context.

**Action:** Change the text, or the lanes, row by row.

### 3. Low: current-row descriptions still refer to Python controls that have been migrated or deleted

**Where:**
- `UT-CONSOLE-COMPOSITION-ROWS` / `-MATCHER` / `-ENUMS` / `-STREAM` / `-INDEPENDENCE` and `IT-CONSOLE-COMPOSITION-ROWS`. Their source says *"native vectors and Python controls on both lanes."*
- `UT-EXCEPTION-T04-01` through `-03`, `IT-EXCEPTION-T04-04`, `AR-EXCEPTION-T04-05` and `-06`. Their source says *"exact native/Python observations."*

**Why it matters:**
- The Python readers were replaced by C# migration rows, for example `UT-CONSOLE-COMPOSITION-FIELDS`.
- These are required-case rows in the current inventory, not frozen receipts. Other rows were updated additively, such as the T02 note on `AR-LOG-VALUE-T01-17`.
- As written, the descriptions say Python controls still run on both lanes.

**Action:** Append a migration note, in the same additive way, rather than rewriting history.

### 4. Low: corrective and R1 bindings carry no requirement traceability

**Where:**
- The four `UT-REVIEW-R1-*` rows, all `UT-CORRECTIVE-*` / `IT-CORRECTIVE-*` rows and the provider verifier rows have `"requirements": []`.
- The following rows omit `requirements` (and usually `title`) entirely:
  - `UT-ENV-CONTROLLED-INPUTS`, `IT-ENV-STATIC-CONSTRUCTOR`, `ARCH-ENV-READONLY-STATIC`;
  - the `UT-ENV-*` metadata rows and `UT-ENV-CTOR-*`;
  - `UT-EVENT-ENCODING-*`, `UT-LOGGER-FOUNDATION-SHARED`;
  - the native migration rows, including `INT-JSONL-BOUNDARY-NATIVE-MODES`.

**Why it matters:**
- The R1 rows test REQ-004 flush/stop accounting.
- The environment rows test REQ-002 static constructor and overrides.
- If any requirement-coverage view is derived from this file, these behaviors show as untraced.
- I cannot confirm from the supplied context whether a validator enforces this field.

## Questions and limits

1. **`ARCH-IMMUTABLE-CONFIG` versus writable static properties.**
   - `ARCH-IMMUTABLE-CONFIG` is Planned with the title *"No mutable static config…"*.
   - `ARCH-ENV-READONLY-STATIC` records *"three caller policy properties remain writable"* on the static `EnvironmentOptions`.
   - Is the Planned row amended by a user decision I was not given? If not, it cannot be satisfied as worded.
2. **Two superseded assertions in `INT-STOP-READER-MIGRATION-CONTROLS`.** The row's minimum of 116 equals the sum of all original corruptions (30+21+23+29+13), and the text says *"retains all."* It also says two stale Python assertions are *"superseded."*
   - Are those two corruptions still rejected for their original intended reason, or for a new one?
   - I did not have the reason catalog to check this against the expected-reason controls.
3. **Forty legacy corruptions in `UT-STOP-LIFECYCLE-KERNEL-MIGRATION`.** The row says 11 process cases *"retain forty legacy report/console/restart corruptions."*
   - Those 11 cases are bound only through `INT-BURST-STOP` (minimum 11).
   - No row enforces the forty individually, so per-corruption parity cannot be confirmed from the inventory.
4. **The 26-case minimum in `INT-JSONL-INPUT-SELECTOR-CONTROLS-WINDOWS`.** It depends on the symlink control being *"unexecuted"* without counting as a skip. I could not verify that mechanism without the test source and validator.
5. **How lane requirements are reconciled.** Is a method's required lane set the union across all rows that bind it? The answer determines how Finding 1 fails in practice.
6. **Missing context:**
   - the required-case reconciliation validator and its schema;
   - `planning/decisions.md` and `plan-amendments.md`;
   - the test sources for the bound methods;
   - the fixture catalogs and reason catalogs;
   - the raw Windows and Ubuntu execution reports;
   - `compatibility/csharp/replay-rules.json`.

   I did not verify that any bound method exists, that any execution count is achievable, or that any case passed.

There are no other findings within this file. This is not a whole-change PASS and does not qualify a release.