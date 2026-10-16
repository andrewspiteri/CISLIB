# Review: 02-values-rendering-scopes-1 (static, read-only)

**Scope:** the 8 assigned C# files, the 4 baseline-deleted Python readers, and the supplied bounded context. No code was executed. Line numbers are approximate, counted from the supplied text.

**Result:** 4 findings (1 Medium, 3 Low), no confirmed production defects in the supplied Core context. This is not a whole-change pass.

## Confirmed findings

### F1 — Medium — Qualification lost its comparator corruption controls
**Location:** `OperationScopeQualificationProcessTests.cs` (whole file; Validate call ~l.34)

- **Parity loss:** The deleted `verify-scope-qualification.py` ran six controls (`controls()`) against `SCOPE-CONCURRENT-2`. Each mutation had to be rejected:
  - `operation-id`
  - `attribute`
  - `order` (reversed children)
  - `depth`
  - `bool-number` (`parentHeadsUnchanged = 1`)
  - `missing` (dropped `parentBefore`)
- **Current state:** The three sibling process tests port their controls (`ScopeReportCorruptionsAreRejected...`). The qualification test has none.
- **Concrete failure:** The parent and the child both call `OperationScopeQualificationExpectations.Validate`. This is the same fixture-assembly comparator, called in the child at `OperationScopeQualificationProcessFixture.cs` ~l.15. A regression that makes it lenient would pass all 14 cases on both sides. Examples: ignoring child order, using numeric/bool equality, or ignoring missing keys. The deleted Python reader used an independent `exact()` and would have caught these.
- **Fix:** Add a `DataRow` test that:
  1. captures `SCOPE-CONCURRENT-2`;
  2. validates the original report;
  3. applies each of the six mutations to `result`;
  4. asserts `ThrowsExactly<AssertFailedException>` for each.
- **Caveat:** This may already exist in an unsupplied unit test of `OperationScopeQualificationExpectations`. If so, point its required-case binding at this parity.

### F2 — Low — Input hash does not attest the parsed bytes, and the parent catalog source is not bound
**Location:** `OperationScopeQualificationProcessFixture.cs` ~l.12 and ~l.25; `OperationScopeQualificationProcessTests.cs` ~l.18 and ~l.31–34

- **Child side:** The child parses `File.ReadAllText(path)` but hashes a second, separate `File.ReadAllBytes(path)`. So `inputsSha256` is not proof of the bytes that drove execution. This is the same defect class that T1–T4 fixed for Type Manager (pin and parse one owned byte array).
- **Parent side:** The parent validates against `LogValueProcessTests.Read("scope-qualification-cases.json")`. That is a third read, possibly resolved differently from `FixtureProcessChecks.Find`. In the supplied text, the parent never compares `observed.inputsSha256` before `result.Remove("observed")`.
- **Python behaviour:** The deleted reader required `observed["inputsSha256"] == sha(inputs)` on the same resolved file it loaded.
- **Fix:**
  - In the child, read the bytes once, then hash and parse that array.
  - In the parent, load `catalog` from `input` and assert that `observed.inputsSha256` equals the hash of those bytes before removing `observed`.
  - Skip the parent part only if the unsupplied `RunAsync(..., input, ...)` already does this check.

### F3 — Low — Depth rejection reason is recorded by exception type only
**Location:** `OperationScopeMergeProcessFixture.cs` Depth ~l.133; `CountingMap` members ~l.160–165

- **Problem:** `rejection` stores only `error.GetType().Name`.
- **Why that is ambiguous:** `CountingMap.Count`, `Keys`, `TryGetValue` and the other members throw the same `InvalidOperationException` without incrementing `traversals`. So `"rejection":"InvalidOperationException","traversals":0` cannot tell apart:
  - the depth guard firing; and
  - capture touching caller content by a non-enumerating member.
- **Why it currently holds:** The lone-surrogate ID is validated before attributes are read. That masks the gap today, but only through ordering inside `CapturedOperationContext.Capture`.
- **Fix:** Do one of the following:
  - Record `error.Message` and expect `"The maximum operation scope depth is 32."`.
  - Make the `CountingMap` sentinels throw a distinct exception type, or count them in `traversals`.
- **Note:** The Python reader had the same weakness, so this is not a regression. It is an open expected-reason control gap.

### F4 — Low — OS observation is mislabelled on non-Windows hosts
**Location:**
- `OperationScopeDisposalProcessFixture.cs` ~l.27
- `OperationScopeMergeProcessFixture.cs` ~l.29
- `OperationScopeProcessFixture.cs` ~l.26

- **Problem:** These three fixtures report `OperatingSystem.IsWindows() ? "windows" : "linux"`, so macOS and other hosts are recorded as `linux`. The qualification fixture correctly distinguishes `macos` and `unknown`.
- **Python behaviour:** The deleted readers compared this field against an explicit `--family` restricted to windows/linux.
- **Concrete failure:** If the unsupplied `RunAsync` derives the expected family the same way, a run on an unsupported host produces a falsely labelled "linux" observation.
- **Fix:** Use the same four-way mapping as the qualification fixture.

## Checked within scope with no finding

- **Expected values:** Disposal, merge and foundation expectations match the deleted `EXPECTED` tables exactly, field for field. The corruption control triples also match the deleted `controls()` triples. Mutating the actual report after it validates is at least as strong as the Python approach of mutating `EXPECTED`.
- **Store semantics:** `OperationScopeStore.Handle.Dispose` matches the `runtime-contract.json` BeginScope and scope ownership text:
  - LIFO ancestor rejection;
  - the absent-and-never-completed guard;
  - harmless repeat in the creator flow;
  - completion state not treated as proof of ownership.
- **Fixture results traced against Core code:**
  - The fixture flows trace to the expected values: `lifo`, `repeat` (stale handle vs same-input replacement), `await`, and `absent` (via `SuppressFlow`).
  - The merge cases trace correctly: `precedence`, the `candidates` key cap (1023 + root), `inherited-loss` provenance, and `depth` (the guard fires before capture).
- **Provider scopes:** `ProviderScopeCapture.Capture` builds frames innermost-last, consistent with the outermost-first `LoggerExternalScopeProvider` ordering.
- **Overlap with the eleven known fixes:** None. R1/R2, F1–F6 and T1–T4 do not touch these files, so there was nothing to re-verify here.

## Questions

1. **Re-entrant `BeginScope` during capture:** If a cooperative enumerator calls `BeginScope` and does not dispose it, `BeginCopiedScope` throws "Scope state changed during input capture." The nested frame stays installed, because the synchronous AsyncLocal write persists. This looks intentional ("never overwrite its newer head"), but the contract does not state it. Should the contract document it?
2. **Tokens in raw output:** The private-token checks run on re-serialized parsed JSON (`ToJsonString()`), not on raw stdout or stderr. Does `RunAsync` require empty stderr and check the raw bytes, as the Python readers did?

## Limits and missing context

Parity with the deleted readers depends on unsupplied code:

- **`FixtureProcessChecks.RunAsync`, `Cases` and `Find`:**
  - exit code and empty-stderr checks;
  - `--fx-version 10.0.12 --roll-forward Disable`;
  - the `runtimeDescription`, `fixtureSha256` and `coreSha256` comparisons;
  - environment merge vs replace, and cwd = owned directory;
  - parent environment/cwd unchanged;
  - report and hash retention;
  - the `caseCount == 14` check.
- **Qualification support:** `OperationScopeQualificationExpectations`, `OperationScopeQualificationFixture`, `scope-qualification-cases.json` and `nativeBindings`.
- **Value merging:** `LogValue`, `LogValueProjection` and `EventProjection.Merge`, needed to confirm that the host+core overlay obeys the same key cap as `FromHead`.
- **Wiring:** the DiscoveryHost mode dispatcher, and the required-case inventory bindings for these test rows.

No execution evidence is implied. No whole-change pass or release qualification is implied.