# Review: 02-values-rendering-console-1 (static, read-only)

**Scope.** This review covers only the 13 current files and 4 deleted readers in this bundle, plus the bounded context files. I executed nothing. The verdict does not mean the whole change passes or that the release qualifies. None of the eleven listed fixes (R1/R2, F1–F6, T1–T4) touches these files, so no fix verification applies here. One fix (T-series "hash the bytes you parsed") sets a precedent that Finding 2 relies on.

## Confirmed findings

### 1. Low: the composition negative controls do not check the rejection reason, so the "stream" control can pass for the wrong reason

**Location.** `ConsoleCompositionTests.cs`, `ConsoleMatcherRejectsChangedStreamFactsAndAccounting`, about line 34. Related check: `ConsoleCompositionExpectations.cs` about line 31, `Require(actual[other]… == "", "Records reached the unselected console stream.")`.

**What happens.** The seven controls only assert that some `InvalidDataException` is thrown. For row `CONSOLE-CONSOLE-110` (stderr selected), the `stream` damage swaps the two streams, which leaves stderr empty.

- If the route check at line 31 were deleted, `CheckLines` would still reject the swapped output with "Console line count differs." (0 lines where 3 are expected).
- The control would therefore still pass, even though the route check it is meant to protect is gone.

**Context.** This matches the deleted Python behaviour, which also did not compare reasons for composition. However, every other family in this bundle now pins reasons:
- ownership: 14 controls
- startup: 10 controls
- consumer: 13 controls

**Fix.** Add a reason column to the composition `DataRow`s and use `Assert.AreEqual(reason, error.Message)`, as the sibling tests do.

### 2. Low: `inputsSha256` hashes a second read of the file, not the bytes that were parsed

**Location.** The `Run(string path, string id)` method in each of:
- `ConsoleCompositionFixture.cs`
- `ConsoleOwnershipFixture.cs`
- `ConsoleStartupFixture.cs`

**What happens.**
1. Each `Run` parses the catalog with `File.ReadAllText(path)`.
2. It later reports `inputsSha256 = Hash(path)`, which reads the file again.
3. The child process therefore attests to a separate read, not to the catalog that drove execution and its own validation.

This is the same provenance gap that the T-series correction closed for Type Manager ("records the hash of the same bytes it parsed"). It is a local provenance issue, not a security exploit claim.

**Fix.** Call `File.ReadAllBytes(path)` once, parse from that buffer, and hash that same buffer.

## Parity of the deleted Python readers (within visible code)

**Verified equivalent:**
- **Exact case sets.** 41, 22, 36 and 38 cases, enforced by count, distinct count and set equivalence.
- **Composition validation.** Output field set, filtered/accepted/written counts, the LF/CR/ESC/BOM/cap checks, the JSONL capture-loss protected keys and unknown-key rule, and the human normal/verbose field-set, template and attribute omission rules.
- **Ownership validation.** Canonical tickets, cohorts, stop dispositions, the held seam, the physical early return, the failed prefix, and record order and facts.
- **Ownership and startup controls.** Same faults and reason strings, with message wording updated. Ownership adds a `noncanonical-ticket` control.

**Not verifiable here.** These checks depend on files that were not supplied:
- `ConsoleStartupExpectations`
- `ConsoleConsumerExpectations`
- `ConsoleConsumerFixture`
- `HumanRenderingExpectations`
- `FixtureProcessChecks`

## Questions and limits

1. **Are child identity checks preserved?** The Python readers required, per child:
   - `runtimeDescription == ".NET 10.0.12"`
   - `operatingSystem ==` the lane
   - `fixtureSha256`, `coreSha256` and `inputsSha256` matching the host, the `CIS.Core.dll` beside it, and the catalog
   - for consumers, `observed.processId == process.pid`

   None of these comparisons is visible. `ConsoleProcessChecks.Capture` (about line 38) discards the second tuple element from `RunAsync`. Consumer `Run` only compares the in-child `processId` with the resource `process_id`, and the OS with the resource OS, not with the lane. Please confirm `FixtureProcessChecks.RunAsync` performs these checks and fails on mismatch.

2. **Are controlled-stream and physical-stream rules preserved?** Python required that non-physical children emit exactly one stdout line and empty stderr. For physical children, it rebuilt `stdout`/`stderr` from the real process streams. `physicalBytes: true` validation depends on `RunAsync` doing the same; this is not visible.

3. **Is the consumer environment isolated?** Python stripped inherited `CORE_*` and `DD_SERVICE`/`DD_ENV`/`DD_VERSION`, set decoy `DD_*` aliases plus `TERM` and `NO_COLOR`, then applied the row's environment. Does `EnvironmentFor(row)` together with `RunAsync` remove inherited `CORE_*` variables (for example, via null values) and add the decoys?

4. **Do the parent and child read the same catalog?** Python passed one catalog path to both. Here the parent reads with `LogValueProcessTests.Read` while the child receives `FixtureProcessChecks.Find`. The unit tests use yet another lookup: `ConfigurationFixtureAdapter.FindFixture` for composition, and a walk upward from `AppContext.BaseDirectory` for ownership and startup. Please confirm all of these resolve to the same file.

5. **Where did the per-case evidence go?** The Python readers wrote per-case `.stdout.bin`/`.stderr.bin` files and a `summary.json` with scope disclaimers (for example, "not … release"). I cannot tell where, or whether, the native lane records equivalent artifacts and scope limits.

6. **Process cost (observation only).** The consumer integration launches about 89 children:
   - 38 for the case rows
   - 38 again for the uniqueness check
   - 13 for the controls

   Python launched 38. This affects runtime, not correctness.

7. **Other limits.**
   - Production behaviour is only partly visible: `ConsoleOutputWriter` is supplied, but `CoreLogRuntime.CreateConsoleQueued`, rendering and queues are not.
   - I could not verify that a null `acquireConsole` result is rejected explicitly. Otherwise `Initialize` may map a `NullReferenceException` to `ConsoleInitializationFailed`.
   - The catalogs, required-case bindings (Windows/Ubuntu) and execution logs were not supplied.

## Missing context

- `FixtureProcessChecks.cs`
- `LogValueProcessTests.cs`
- `TemporaryDirectory`
- `ConsoleStartupExpectations.cs`
- `ConsoleConsumerExpectations.cs`
- `ConsoleConsumerFixture.cs`
- `HumanRenderingExpectations.cs`
- `ConfigurationFixtureAdapter.cs`
- `console-*-cases.json`
- the required-case binding files
- the Core runtime, rendering and queue sources
- `planning/decisions.md`
- the plan amendments