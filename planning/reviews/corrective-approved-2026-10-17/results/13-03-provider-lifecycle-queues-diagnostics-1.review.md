# Review: 03-provider-lifecycle-queues-diagnostics-1

This is a static, read-only review of the supplied text. I executed nothing. It covers the named assigned files, the two deleted Python readers and the bounded context. It is not a whole-change PASS and does not support release qualification.

## Confirmed findings

### F-1 — Medium — Public XML doc gives the wrong meaning for the per-output `QueueFull` counter
**Location:** `src/CIS.Core/Logging/LogOutputDiagnostics.cs` ~L41–42 (context file; overlaps the R2 XML-documentation fix).

- **What the doc says:** "Cumulative **incoming** queue-full rejections."
- **What the code does:** The property copies `OutputAccounting.QueueFull`. That value comes from `OutputEventQueue.queueFullCount`, which only increases when the per-output `channel.Writer.TryWrite` fails.
- **Where incoming queue-full actually lives:** `IncomingEventQueue.queueFull`, exposed through `CoreLogDiagnostics.Incoming`.
- **Conflicts with:**
  - the runtime contract's `lossObserved` rule, which keeps "incoming queue-full" and "output queue-full" separate;
  - the R2 statement that the XML docs now separate incoming acceptance from output-stage behaviour.
- **Concrete failure:** A consumer reading the public docs would treat `Outputs[i].QueueFull` as incoming loss. They might double-count it with `Incoming.QueueFull`, or miss output-stage saturation entirely.
- **Fix:** Reword to "Cumulative output-queue-full rejections for this output, saturating at Int64.MaxValue."

### F-2 — Low — `output-queues.md` describes the retired Python comparator and pre-S03 scope as if current
**Location:** `docs/modules/logging/output-queues.md` L17, plus L3 and L11.

- **L17:** "Exact-runtime child processes **and an independent Python comparator qualify** those eleven rows on Windows and Ubuntu." L19 of the same file says the Python drivers are retired. As written, L17 claims current evidence that no longer exists.
- **L3 and L11:**
  - "There is no new public factory or lifecycle API."
  - "WP05-S03 **still owns** safe fault diagnostics, failed/abandoned/pending counters and terminal output states."

  The current `OutputEventQueue` implements state, fault codes and failed/abandoned/pending counters. `GetDiagnostics` is public.
- **Why it matters:** This is a maintained module doc, not a frozen receipt. It mixes T01 history with current-tense claims.
- **Fix:** Put the T01 statements in past tense and attribute them to the T01 receipt. Point readers to current S03 behaviour instead of saying "still owns".

### F-3 — Low — Child identity reports any non-Windows OS as `"linux"`
**Location:** the `observed` object built in `Run` in each of these fixtures:
- `DeliveryObservationFixture.cs`
- `DiagnosticsFixture.cs`
- `OutputAccountingFixture.cs`
- `OutputStateFixture.cs`

- **The defect:** These fixtures use `OperatingSystem.IsWindows() ? "windows" : "linux"`.
- **Inconsistency:** `OutputQueueFixture` and `QueuedFanoutFixture` correctly fall back to `"unknown"`.
- **Concrete failure:** A run on macOS (or any other OS) would emit `"operatingSystem":"linux"`. An evidence artifact could then carry a false Linux claim, which the docs explicitly disclaim.
- **Not new, but uncorrected:** The deleted Python check (`observed['operatingSystem'] == args.family`) would not have caught this either.
- **Fix:** Use the same three-way expression as `OutputQueueFixture`.

### F-4 — Low — Native diagnostics comparator checks the output field set less strictly than the deleted Python reader
**Location:**
- `DiagnosticsExpectations.cs` ~L41–42
- the same pattern in `OutputStateExpectations.cs` ~L30–31

- **Python (deleted `verify-diagnostics.py`):** Compared exact dict equality of every output key except `faultAtUtc`.
- **C# now:** Checks only three things:
  1. `values.Count == expected.Count + 1`
  2. `ContainsKey("faultAtUtc")`
  3. `DeepEquals(values[key], expectedValue)` for each expected key.
- **Concrete failure:** Suppose a projection renames a key whose expected value is `null`, for example `"fault": null` becomes `"faultCode": null`.
  - The count stays the same.
  - `values["fault"]` returns `null`, which equals the expected `null`.
  - Later, `values["fault"] is null` is read as "nonfaulted".
  - Result: the observation is accepted on healthy rows.
- **Why the controls miss it:** The `extra-output` control adds a key rather than substituting one, so it does not exercise this path.
- **Fix:** Require the key set to equal exactly `expected.Keys ∪ {"faultAtUtc"}`, for example with `ToHashSet(StringComparer.Ordinal).SetEquals(...)`.

## Fix checks where this bundle overlaps

- **R1 follow-up (diagnostics IL guard):**
  - `IncomingEventQueue.Capture` now builds `IncomingAccounting` through the constructor only. No `with` copy or setter remains.
  - The guard allows only `IsConstructor || get_*` for `IncomingAccounting`.
  - The 1 + 11 test rows give 12 architecture checks, matching the count in `corrections.md`.
  - The fix is consistent in the supplied text.
- **R2 (XML docs):** Not fully fixed in the overlapping context; see F-1.
- **Case bindings:**
  - The hard-coded IDs in `QueueProcessChecks.Cases` add up to 107 (12+11+15+11+23+12+11+12), matching the doc.
  - Native controls in this bundle total 98: Delivery 10, Diagnostics 13, Accounting 9, OutputQueue 16, OutputState 8, Burst 9, Overflow 3, Fanout 30. This matches the claimed 84 + 14.

## Parity of the deleted readers (only the two supplied)

**`verify-diagnostics.py` → `DiagnosticsExpectations` / `DiagnosticsProcessTests`:** Parity holds for:
- identity and source binding;
- the UTC-in-interval checks;
- shutdown and fault retention;
- monotonic and global counters;
- the saturating admission sum;
- the 12 damage controls (plus one extra-output control);
- source-catalog SHA-256 before and after the run.

Two gaps:
- the field-set check (F-4);
- the profile domain. Python raised `KeyError` for any profile other than `delivery`/`states`. C# treats any non-`"delivery"` value as the state profile. The ID and sourceCatalog binding limits the impact.

**`verify-delivery-observations.py`:**
- The 10 control names and their damage rules match `DeliveryObservationTests` exactly.
- C# requires `InvalidDataException`, which is stricter than Python's `AssertionError/KeyError/ValueError`.
- I could not check comparator parity: `DeliveryObservationExpectations.cs` was not supplied. The unverifiable checks include `pending <= 3`, the 16384 bound, ticket-1/2 messages, ticket-3 truncation, and the `held`/`cancellationRequested` prefix bytes.

## Questions and limits

- **Exact-runtime identity checks:** Python checked runtime `.NET 10.0.12`, `fixtureSha256`, `coreSha256` against the `CIS.Core.dll` beside the host, `inputsSha256`, return code 0 and empty stderr. In C#, only `sourceInputsSha256` is visibly checked. I assume the rest live in `FixtureProcessChecks.RunAsync`, which was not supplied.
- **Where corruption controls now run:** They run in-process on the test-host runtime, not on exact-runtime child stdout as in Python. Does the migration receipt say so?
- **IL guard blind spots:** The inspector does not see:
  - delegate `Invoke` calls (`System.Func`/`Action` are not forbidden);
  - virtual or override dispatch into non-Core base methods (for example `object.ToString`).

  `CoreLogRuntime.GetDiagnostics` was not supplied, so I cannot confirm whether any such call exists today.
- **Stale allowlist entry:** The allowlist entry `IncomingFlushBoundary.get_QueueFull` names a property that does not exist on the supplied type. It is harmless but out of date.
- **Expected-reason controls:** All controls in this bundle assert only the exception type, not the message. This matches the deleted readers, but it does not prove that each control is rejected for its intended reason. For example, `fault-retention` depends on when console faults in `STATE-ISOLATE-CONSOLE`, and that catalog was not supplied.

## Missing context

- **Comparators and fixtures:**
  - `DeliveryObservationExpectations.cs`
  - `OutputAccountingExpectations.cs`
  - `QueueBurstExpectations.cs`
  - `DiagnosticsProjection.cs`
  - `FixtureProcessChecks`
  - `LogValueProcessTests.Read`
  - `EventQualificationFixture`
- **Source and catalogs:**
  - `CoreLogRuntime.cs`, including `GetDiagnostics` and `CaptureAccounting`
  - `LogIncomingSnapshot.cs`
  - all eight case catalogs
- **Retired readers:** the other five deleted Python drivers.
- **Evidence and policy:**
  - required-case binding files
  - the migration receipt
  - the T01 receipt
  - `decisions.md` and the plan amendments
  - raw execution logs