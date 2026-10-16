# CISLIB static review: subpass 02-values-rendering-values-1

**Scope:** the four assigned files. Bounded Core context was used only to check expectations (`LogValue`, `LogValueKind`, `Values/LogValueProjection`, `LogValueScalar`, `LogValueText`).
**Not implied:** no code was executed, no tests were run, there is no whole-change PASS and no release qualification. Line numbers are approximate, counted from the supplied text.

## Confirmed findings

### F1 — Low (Medium if no external binding covers it): Event-value rows are silently dropped from execution by kind

**Where:** `LogValueProcessTests.cs`, about L10–14 (`Kinds`, `PortableRows`), L19–22 (catalog check), L48–49 and L63 (`Verify`).

**What the code does:**
- `PortableRows` keeps only rows whose `input.kind` is in a nine-entry list. It excludes `uuid` and `timestamp`, and any other spelling, without failing.
- `Verify` still maps `uuid` and `timestamp`.
- `Verify` hard-codes `VALUE-BAD-UUID` and `VALUE-BAD-TIMESTAMP` as adapter-only.
- `Verify` has an `expectedKind is null` branch.

From this test's data source, all of these branches are unreachable. `NativeValueCatalogMembershipIsComplete` checks only that `event-value-cases.json` has 47 rows. It does not check that every row is executed.

**Concrete failure:**
- A row whose kind is mistyped (for example `"Integer"` or `"int"`), or a new `uuid`/`timestamp` row, disappears from execution.
- The 47-row count still passes and no test fails.

This file does not state whether another caller of the `internal` `Verify` executes the uuid/timestamp rows.

**Fix:**
- Assert that every row's kind is either in `Kinds` or in a named non-portable set, and name the test that executes that set.
- Assert the expected portable row count.
- Alternatively, remove the dead uuid/timestamp branches and IDs here if they belong to another test.

### F2 — Low (Medium if the fixture does not compensate): The invalid-value oracle in the integration test does not check the reason per row

**Where:** `LogValueProcessTests.cs`, about L68–69.

**What the code does:** every non-adapter invalid row accepts either `ArgumentException` or `ArgumentOutOfRangeException`. The parameter must be `"value"` for every row except `VALUE-INVALID-MAP-KEY`.

**Concrete failure:**
- An out-of-range `Integer` row should throw `ArgumentOutOfRangeException`. It would still pass if it were rejected as an `ArgumentException`, for example by a malformed-text path.
- An invalid `IntegerString`/`DecimalString` row would still pass if it threw `ArgumentOutOfRangeException`.
- All scalar factories use the parameter name `value`, so the parameter assertion cannot tell them apart.

This is the same "intended-reason" weakness that the F-series corrections fixed for the file readers.

**Dependency:** the child process calls `LogValueFixtureAdapter.Validate(row, result)` (fixture about L25), which was not supplied. If it compares the exact exception per row and exits nonzero on mismatch, this weakness is covered in the child process. If it does not, the reason is unbound.

**Fix:** compare `actual["error"]` against a per-row expected exception from the fixture data, rather than a two-type set.

### F3 — Low: The reported input hash is not the hash of the parsed bytes

**Where:** `LogValueProcessFixture.cs`, L12 (parse) vs about L35 (`Hash(inputPath)`, a second read after execution).

**What the code does:**
- `inputsSha256` comes from a separate, later file read. It does not attest to the bytes that `ReadDocument` parsed.
- On the test side, the expectation rows are also read separately from the child's read: Collection about L17, Qualification about L17, `Verify` about L34.

This is the same provenance pattern corrected for Type Manager in T1–T4 ("pin/parse one owned byte array").

**Fix:**
- In the child, read the bytes once, hash them, and parse from those same bytes.
- Have the test compare `observed.inputsSha256` with the hash of the bytes it used for expectations, unless the unsupplied `FixtureProcessChecks.RunAsync` already does this.

### F4 — Low: The OS label is wrong on any non-Windows host

**Where:** `LogValueProcessFixture.cs`, about L32.

**What the code does:** `OperatingSystem.IsWindows() ? "windows" : "linux"` records macOS, or any other platform, as `linux`. Evidence lanes then mislabel the OS.

**Fix:** report `linux` only when `OperatingSystem.IsLinux()` is true. Otherwise record or reject the actual platform.

### F5 — Low: The "without startup" check probes only one method name

**Where:** `LogValueProcessFixture.cs` about L30; asserted in `LogValueProcessTests.cs` about L80.

**What the code does:** `lifecycleApiPresent` is true only if a public method named exactly `Initialize` exists. Methods named `InitializeAsync`, `Configure`, `Reset` and similar would leave it false. Yet the test name claims "WithoutStartup".

**Fix:**
- Rename the field to state exactly what it probes.
- If the static-`EnvironmentOptions` decision needs a guard here, check the public method set against the supplied runtime surface, unless the architecture tests already enforce this. That is not visible in this bundle.

## Overlap with the eleven known fixes

None. R1/R2, F1–F6 and T1–T4 do not touch these files. The F2 and F3 issues above are analogues of those fix patterns, not regressions of them.

## Core context checked: no defect found

- **Projection budgets:** root node/depth 1; the reuse condition `depth-1+value.Depth ≤ 8`; truncation propagation; the unreachable scalar throw.
- **Text validation:** integer and decimal canonical forms (`-0` rejected, `-0.0` preserved, multiple dots rejected).
- **Formatting:** lowercase `Guid` `"D"` format; invariant UTC timestamp with seven fractional digits.
- **Collection test measurement:** `Measure` is consistent with Core node/depth semantics, including JSON `null` children counting as nodes.

## Questions and limits

1. **`FixtureProcessChecks.RunAsync` (not supplied).** Does it check:
   - the exit code and stderr;
   - a fresh process ID versus the test host;
   - `observed.coreSha256` against the Core under test;
   - `fixtureSha256`?

   The assigned tests never read these hashes. If `RunAsync` does not check them, nothing binds the child's Core to the tested artifact.
2. **Self-reported oracles.** `rejectedInputNotEchoed`, `rangeActualValueAbsent` and `metadataMatchesMeasuredShape` are computed in the child (adapters not supplied). Only node and depth are independently re-measured.
3. **Source of `json` in the collection test.** Is it rendered by production Core rendering or by the fixture's own traversal? If by the fixture, the collection test does not demonstrate production rendering.
4. **The `CORE_LOG_LEVEL` control.** Is this the variable `EnvironmentOptions` actually reads? Does an invalid value throw on read? If neither, the "must not be read" control is vacuous. Reflection via `GetMethod` does not run the static constructor.
5. **Invalid collection rows.** Does `value-collection-cases.json` contain invalid rows? If so, the unconditional `nodes`/`depth`/`json` dereferences would fail with a null reference rather than a clear assertion.
6. **Python reader parity.** No deleted Python reader text appears in this bundle, so parity was not assessed.
7. **Not reviewed.** Fixture catalogs, the adapters, required-case bindings, the full decisions/amendments, other Core files and raw logs were not supplied.