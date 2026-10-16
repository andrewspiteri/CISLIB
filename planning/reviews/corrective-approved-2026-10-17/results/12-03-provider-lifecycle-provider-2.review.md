# CISLIB static review: subpass 03-provider-lifecycle-provider-2

**Scope:** the three baseline-deleted Python readers (`verify-provider-ownership.py`, `verify-provider-scope.py`, `verify-provider-state.py`), checked against the supplied provider sources and context. This was a static read only. I executed nothing, and this is not a whole-change PASS or release qualification.

**Main limit:** none of the native C# replacements for these readers was supplied. The case catalogs and required-case bindings were not supplied either. So I cannot confirm parity, case bindings or expected-reason controls. Most of this report is a parity checklist for whoever reviews those replacements.

## Confirmed findings

### F-1 (Low): the contract says provider disposal stops scopes, but the code does not

- **Where:**
  - `contracts/logging/v1/runtime-contract.json`, `ownership.provider`: "IDisposable stops only provider-owned registrations/scopes, never the shared runtime."
  - `CoreLoggerProvider.cs`, `Dispose()` (about lines 44–47).
- **What the code does:** `Dispose()` only sets `disposed` and clears `externalScopes`. It does not end, pop or invalidate any scope.
- **What else supports that reading:**
  - The provider's own XML says only that disposal "disables this provider's future calls."
  - The user decision is that the creator disposes scopes.
  - The `scope-handles` expectations in the deleted ownership reader (operation IDs `inner`/`outer` on rows, `lifoErrors == 1`) fit scopes that outlive provider disposal.
- **Failure:** a consumer reading the contract will expect provider-created scopes to end on `Dispose()`. They do not. They stay active until the creator disposes them.
- **Fix:** reword the contract to match the code. For example: "IDisposable disables provider-owned registrations and releases the borrowed host scope source; provider-created scopes remain creator-disposed; never stops the shared runtime." This only removes a promise; it does not add one.

### F-2 (Low): nested maps can turn truncation into InvalidEvent depending on key order

- **Where:** `ProviderStateCapture.cs`, `Map()`, which calls `ValidateKey(key)` (about line 130) before `Value(...)` checks the budget.
- **Why it is inconsistent:** `CaptureRoot` checks `capture.remaining == 0` *before* `ValidateKey` (about lines 59–60). Keys past the budget are therefore truncated without being validated. `Map()` validates exactly one key past the exhausted budget (or past the depth limit) before `Value` returns null.
- **Failure:** take a nested dictionary whose entry at the budget boundary has an invalid key.
  - Today it throws `ArgumentException`, so the whole event becomes InvalidEvent.
  - The same data with that key one position later is truncated.
  - The contract says "ordinary oversize -> bounded truncation," so the outcome should not depend on key order.
- **Fix:** in `Map()`, check `remaining == 0 || depth + 1 > MaximumDepth`, set truncated and break, before calling `ValidateKey`. This mirrors the root loop.

## Parity checklist for the native replacements (unverified)

The deleted readers define what the C# replacements must at least preserve.

**Case membership** (each reader required exact equality with its catalog):

| Reader | Catalog | Cases |
| --- | --- | --- |
| Ownership | `provider-ownership-cases.json` | 4: `shared`, `resources`, `scope-handles`, `in-flight` |
| Scope | `provider-scope-cases.json` | 13 |
| State | `provider-state-cases.json` | 9 |

**Expected-reason controls** (each must be rejected with the exact intended reason string):

| Reader | Controls |
| --- | --- |
| Ownership | 13 (10 report edits plus 3 `operation-*` wire edits) |
| Scope | 25 |
| State | 34 |

Points to check that no control was retired or replaced with a generic rejection:
- State has two duplicate-reason pairs: `scalar-type`/`decimal` and `trace`/`span`.
- Scope uses one wire-edit helper for its record-level controls; those controls still need their exact reasons.

**Source and artifact identity:**
- Runtime launched with `--fx-version 10.0.12 --roll-forward Disable`, and the reported runtime is checked.
- `CORE_*` environment variables are scrubbed.
- Exit code 0 and empty stderr.
- Reported PID equals the child PID.
- `os`, `coreSha256` and `fixtureSha256` equal the actual family and the hashes of `CIS.Core.dll` and the fixture.
- The on-disk `logs Ω/active.log` bytes equal the reported bytes.
- The report pins the verifier and catalog hashes.

Weaknesses in the deleted readers that the replacements should not inherit:

| ID | Reader | Gap | Concrete consequence |
| --- | --- | --- | --- |
| P-1 | Scope, state | No `\r` check; only ownership checks it. `bytes.splitlines()` also splits on CRLF. | CRLF-framed output would pass both readers. |
| P-2 | State | `row['schema_version']==1` with no type check, and no `schema-type` control; scope has both. | `"schema_version": true` is accepted. |
| P-3 | Ownership | `row.get('operation_id') == None` | An explicit JSON `null` passes where the field should be absent; scope uses `'operation_id' not in row`. |
| P-4 | Scope, state | Fixed `len(line)+1 <= 16384` bound | Does not match the 64 KiB default entry cap you decided. Either the readers predate that decision, or 16 KiB is a fixture setting. The native check should derive the bound from the configured cap, not a stale constant. |

## Questions (need Core source or fixtures)

- **Q-1:** In `ProviderScopeCapture.Capture`, the first incompatible host frame throws `ArgumentException` from inside `ForEachScope`.
  - The `host-incompatible` expectations need such events delivered as truncated, with invalid count 0.
  - Does Core drop only the bad frame, or every host frame for that event? If it drops every frame, one library calling `logger.BeginScope("text")` loses all compatible host scope data, including ASP.NET `operation_id`, for every event in that scope.
- **Q-2:** Integer ranges are handled differently.
  - `ulong` above 2^53−1 throws, so the event becomes InvalidEvent.
  - Any `long` goes straight to `LogValue.Integer`. Does that method range-check, or does it emit values outside the safe range?
- **Q-3:** Do the Core event-name rules accept PascalCase `EventId.Name` values, such as the method names produced by the `LoggerMessage` source generator? If not, every named generated event is InvalidEvent.
- **Q-4:** The root loop's `visited > MaximumNodes` cap counts `{OriginalFormat}`. With 1024 or more state entries, a trailing template is lost but still marked truncated. Please confirm this is intended.

## Missing context

- Native replacements for the three readers, and their test classes.
- Required-case binding files.
- The three provider case catalogs.
- `verify-flush-results.py` (`number`, `decoded`, `load`).
- The fixture `DiscoveryHost` provider modes.
- The Core files: `CoreLogRuntime`, `Scopes`, `BeginCopiedScope`, `WriteLazy` capture and exception classification, `LogValue`, `LogValueProjection`, `CapturedOperationContext`.
- `planning/decisions.md` and the plan amendments.
- Raw execution reports.

None of the eleven known fixes (R1/R2, F1–F6 minus F4, T1–T4) touches the supplied provider files or these readers, so I made no fix-verification claim.

Within the supplied scope there are two Low findings. Everything else above is a limit or a question, not a confirmed defect.