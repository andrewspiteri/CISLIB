# Review: 02-values-rendering-logger-1 (static, read-only)

**Scope:** the 14 assigned C# files, the 4 deleted Python readers, and the inlined context. No code, tests or tools were run. Line numbers are approximate, counted from the supplied text.

## Confirmed actionable findings

### F1 — Low: the foundation comparator no longer uses each row's own size cap

- **Where:** `tests/fixtures/DiscoveryHost/LoggerFoundationExpectations.cs`, about lines 26–30 (`int cap = mode == "truncate" ? 16384 : 65536;` and `GetByteCount(wire) <= 65536`) and about line 36 (truncate `<= 16384`).
- **What changed:** the deleted `verify-logger-foundation.py` checked both the raw bytes and the encoded wire against `row['input'].get('maxEntryBytes', 65536)`. The C# replacement chooses the cap from `mode` alone and never reads `input.maxEntryBytes`.
- **Concrete failure:**
  - Suppose a non-truncate row declares a smaller cap, such as 16384. If a fixture or encoder regression ignores that cap and emits a 20,000-byte line, the Python reader would have rejected it. The C# comparator accepts it.
  - Suppose the truncate row declares a cap other than 16384. The comparator then checks the wrong bound, in either direction.
  - Whether the current catalog is affected depends on `logger-foundation-cases.json`, which was not supplied. The loss of the binding to the row is confirmed either way.
- **Fix:** read `row["input"]?["maxEntryBytes"]`, defaulting to 65536. Use that value for both size checks. Keep the separate truncate prefix assertion.

### F2 — Low: the corrupted-bytes controls do not first check that the uncorrupted case passes

- **Where:** `tests/CIS.Core.UnitTests/LoggerFoundationTests.cs`, `ComparatorRejectsCorruptedOriginalBytes`, about lines 134–150.
- **What changed:** every other control in this bundle calls `Validate(row, actual)` before corrupting the data. That includes the Capture and Decision comparator tests and `ComparatorRejectsMissingWrongAndInventedRejectionTickets`. The deleted Python reader also verified the real output before each control. This method skips that step.
- **Concrete failure:** the `lf`, `bom` and `size` variants only assert that the exception type is `InvalidDataException`. Every `Require` throws that type. So if `LOGGER-DIRECT-INFORMATION` itself started failing with any `InvalidDataException`, these three controls would still pass without testing anything.
  - The separate shared-case test would still fail in the same run, so a regression would not go unnoticed.
  - The control itself is not self-contained, which falls short of the expected-reason control standard.
- **Fix:** add `LoggerFoundationExpectations.Validate(row, actual);` before the `switch`.

**Production code:** no confirmed defects in the bounded `CoreLogRuntime`, `CoreLogger` or `CoreLoggerProvider` context. Within the supplied context, these behaviors match `runtime-contract.json`:
- write order and the `None` / closed / filtered / faulted paths;
- undefined-level rejection after closure;
- saturating loss counters;
- rejection of invalid tickets and statuses;
- running factories outside the gate.

## Parity checks that hold (within the supplied scope)

- **Capture:** all 8 Python negative controls map to C# fault rows on the same case IDs. The wire-message indexing (`wireIndex - 1`) matches Python's pre-increment index. Framing checks, the 16384 cap, truncation and ambient-ID checks, and the leak check all match.
- **Decision:** all 6 Python controls are present on the same case IDs. C# adds two extra controls, `trace` and `attempt`.
- **Foundation:** the missing, wrong and invented-rejection ticket controls and the invalid-UTF-8 and LF controls are present. C# adds BOM and size controls.
- **Outcome:** the required set of 17 expected fields matches Python. The checks on fan-out byte identity, severity, owned payload and `CIS_` leaks match.
- **Case counts:** the process tests pin 41, 67, 125 and 23 cases. Python did not pin a count for foundation.

## Questions / limits (not confirmed findings)

1. **Outcome table binding (high priority to confirm).** The deleted outcome reader required `catalog['modes']` to equal the 7 listed modes. It also required the set of `OUTCOME-TABLE-*` IDs to be exactly 7×7×2. None of the supplied C# code checks either. If neither `LoggerOutcomeTests.cs` nor `FixtureProcessChecks.Cases` checks them, a catalog edit could swap table rows while keeping 125 cases and still pass. The 9 outcome negative controls are also not in this bundle.
2. **Checks on the child process that moved into `FixtureProcessChecks.RunAsync` (not supplied).** The Python readers required:
   - exit code 0 and empty stderr;
   - `.NET 10.0.12` and an OS-family match;
   - `coreSha256`, `fixtureSha256` and `inputsSha256` matching the host, Core DLL and catalog;
   - launching with `--fx-version 10.0.12 --roll-forward Disable`;
   - `schemaVersion == 1`.

   `Capture` throws away the second tuple item and asserts none of these. The test names say "IsolatedExactRuntimeChild", and that claim cannot be verified here.
3. **Source of the expected row.** The process tests send `FixtureProcessChecks.Find(name)` to the child, but parse the expected row with `LogValueProcessTests.Read(name)`. The unit tests use `ConfigurationFixtureAdapter.FindFixture()`. Python used one catalog path for both. If these resolve to different copies, the hashed input and the comparison row can diverge. It would be simpler to parse the row from `input` directly.
4. **`LoggerDecisionExpectations.cs` was not supplied.** Decision comparator parity is therefore unchecked. This includes the attribute shape `{'probe'}`, the 65536 cap, `sameCapture` when both wires exist, and the `attempts` and `unavailable` comparisons that the `closed` and `unavailable` controls depend on.
5. **Numeric type strictness.** Python's `same()` rejected int/float type swaps. `JsonNode.DeepEquals` may compare numbers by value. This is low practical risk for child output serialized by C#, but the strict-type parity is not established.
6. **Lost run artifacts.** Python wrote per-case stdout/stderr and a `summary.json` with explicit scope disclaimers, such as "not full S05/release qualification". Nothing equivalent appears in the supplied C# code. Any evidence claim should come from the native test reports, not from these tests.
7. **Execution status.** These tests are not covered by the eleven known fixes, and no execution evidence for this bundle was supplied. This review does not imply that anything passes or that the release qualifies.

## Missing context

- `FixtureProcessChecks`, `LogValueProcessTests`, `TemporaryDirectory` and `ConfigurationFixtureAdapter`.
- `LoggerDecisionExpectations.cs` and the `Logger*Fixture` / `*Sources` executors.
- The unit test file `LoggerOutcomeTests.cs`.
- All four `logger-*-cases.json` catalogs.
- `EventEncoder`, `EventProjection` and the incoming and processing partials of `CoreLogRuntime`.
- The required-case inventory bindings for these process tests.