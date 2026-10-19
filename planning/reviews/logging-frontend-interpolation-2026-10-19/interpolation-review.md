# CISLIB interpolation change: review findings

I found no confirmed critical or high-severity defects in the supplied text. The main gaps are bounded-work and contract mismatches, test quality, and one lifecycle-accounting question I could not check because the queue source wasn't supplied.

## Confirmed findings

### 1. Medium: the test for invariant culture on the consumer would not catch a regression
- **Where:** `tests/.../InterpolationTests.cs:55-72` and `:392-403`. The behavior it should guard is `CapturedInterpolation.cs:54`.
- **Scenario:** The test sets `CurrentCulture` to fr-FR only on the caller. The consumer worker was launched earlier by `Create`, so it keeps the process default culture. Usually that is en-US or invariant, and `{amount:N2}` gives `1,234.50` there either way.
  - Suppose someone changes `TryFormat(..., CultureInfo.InvariantCulture)` to `null` or `CurrentCulture`.
  - On a typical CI host, the test still passes.
  - The defect would only appear on a host whose worker culture is fr-FR or de-DE.
- **Fix:** In the test's `Launch` wrapper, set `CultureInfo.CurrentCulture` and `CurrentUICulture` to fr-FR inside the worker delegate before `await worker()`. Then assert `1,234.50` and `1234.5`.

### 2. Medium: the test helper relies on worker launch order
- **Where:** `InterpolationTests.cs:394-398`, `bool incoming = ++launches == 2;`
- **Scenario:** The helper assumes the second launched worker is the incoming consumer. It also uses a non-atomic counter.
  - If the factory launches the incoming worker first, or launches workers concurrently, the `start` gate holds the output writer instead.
  - Several tests then stop exercising the window they claim to test:
    - `UserFormattingRunsOnce…`
    - `SpanAndCopiedHandlers…`
    - `CallerBoxAliases…`
    - `UserObjectCanBeCollected…`
  - In those tests the consumer may already have rendered before the mutation or GC check, and the tests can still pass.
  - `EvictedInvalidFormatNeverRunsInTheConsumer` depends on the consumer being gated. Without that gate, the `Q` format would reach rendering, and the test's `Failed == 0` assertion would fail.
- **Fix:** Identify the incoming worker explicitly. For example, add an internal launch hook parameterized by stage, or check a worker-kind tag. Use `Interlocked.Increment` if counting is kept.

### 3. Low to medium: the precision bound doesn't cover all numeric types formatted on the caller
- **Where:** `InterpolationCapture.cs:103-107` and `:79`. Documented at `interpolation.md:46` and `interpolation-contract.json` `bounds.standardNumericPrecision`.
- **Scenario:** `ExcessivePrecision` only checks the listed built-in types.
  - A manual `handler.AppendFormatted(bigInteger, "D100000000")` reaches `IFormattable.ToString` on the caller. The same applies to `NFloat`, and to a format string supplied at runtime.
  - That allocates a string of roughly 100M characters, and only then does `OwnText` clip it.
- **Conflict:** The docs say "standard numeric precision greater than 8,192 omit that formatted part" with no type qualifier. The doc text about user-formatter work only covers user types, not BCL numerics.
- **Fix:** Pick one:
  - Apply the format-shape guard (letter followed by digits greater than 8192) to any `IFormattable` before calling `ToString`.
  - Or narrow the docs and contract to "listed built-in scalars".

### 4. Low: every render zeroes a fixed 32 KB stack buffer
- **Where:** `CapturedInterpolation.cs:40`, `stackalloc char[16384]`.
- **Scenario:** Every interpolated event pays a 32 KB stack reservation and memset, even `Info($"hi {id}")`.
  - On the consumer, this is per-event CPU work at high throughput.
  - On the legacy and internal caller path (`CoreLogRuntime.cs:527`), the 32 KB comes from the caller's stack.
- **Caveat:** This is conditional. The zeroing doesn't happen if the assembly uses `[SkipLocalsInit]`, which I couldn't see.
- **Fix:** Try a small stack buffer first (for example 256 characters). Fall back to an `ArrayPool<char>` rental of 16384 only when `TryFormat` fails. Alternatively, mark `Render` with `[SkipLocalsInit]`.

### 5. Low: template clipping can produce malformed metadata and report truncation that didn't happen
- **Where:** `CapturedInterpolation.cs:13-28`, plus `InterpolationCapture.cs:47` and `:23`.
- **Scenario:** The capture budget does not count template overhead: `{n}`, `,align`, and the doubling of escaped `{{`/`}}`.
  - Example: an 8,100-scalar literal plus 20 arguments, or about 4,500 literal `{` characters.
  - The full message renders fine.
  - But the template exceeds 8,192 scalars and is cut mid-token, leaving `…{1` or a lone `{`.
  - `IsTruncated` and `TruncatedEventCount` are then set even though the message itself is complete.
- **Related:** At line 47, a part omitted because its format was clipped still emits the clipped format into the template. The omission path at line 45 emits no format, so the two paths are inconsistent.
- **Fix:** Clip the template at the last complete token boundary. Drop the format for omitted parts. Consider tracking template truncation separately from message truncation.

### 6. Low: the legacy internal path classifies render failures differently and is untested
- **Where:** `CoreLogRuntime.cs:525-531`.
- **Scenario:** On output-only internal seams, a render `FormatException` returns `InvalidEvent`/`InterpolationFormatFailed` and counts as invalid. The contract's `backendFormatFailure` says it should count as `Incoming.Failed`.
  - The `when (data.Interpolation is not null)` filter also captures any `FormatException` thrown from `output.Prepare` for interpolated events.
  - None of the supplied tests cover this path.
- **Fix:** Wrap only the `Render` call in the catch. Add a test through `CreateQueued`, or document the divergence in the contract.

### 7. Low: the first capture error code can be overwritten
- **Where:** `InterpolationCapture.cs:33`, `:85`, `:96`.
- **Scenario:** Invalid Unicode in one part followed by a throwing formatter in a later part reports `InterpolationCaptureFailed` instead of `InvalidUnicode`.
- **Fix:** Use `errorCode ??= …`.

## Unverified (needs the queue source)

### 8. Medium if real: render failure may race with stop-deadline abandonment
- **Where:** `CoreLogRuntime.Incoming.cs:66-103`.
- **Concern:** A `FormatException` from `Render` (line 70) is thrown directly out of `PrepareIncoming`. In contrast, `EventEnvelopeTooLargeException` is deliberately turned into a deferred commit action so its accounting happens under the gate ("abandoned preparation adds no loss", line 95).
  - If `IncomingEventQueue` counts `Failed` when preparation throws, rather than at commit under `admissionGate`, there is a race with `CaptureIncomingDeadline` → `incoming.Abandon` (line 298).
  - The same event could then be counted as both `Abandoned` and `Failed`, or `Failed` could change after the stop observation was frozen.
- **Status:** I can't confirm this without `IncomingEventQueue`.
- **Fix:** Either confirm that the queue counts preparation exceptions under the gate and excludes abandoned items, or catch `FormatException` here and return a commit action that records the failure, mirroring the envelope path. Add a test where the deadline expires while a failing render is being prepared.

## Compatibility notes (mostly design-accepted)

When existing `log.Info($"…")` and `log.Info("name", $"…")` call sites are recompiled, they silently bind to the new handler overloads. That changes several things:
- Culture switches from current to invariant, including user `IFormattable` types, which now receive `InvariantCulture`.
- A `message_template` field now appears in output.
- `LogWriteResult.IsTruncated` no longer reflects truncation that happens during rendering.

Your decisions accept the culture change. Two items are not called out in the docs:
- **Expression trees:** a call site inside an expression tree, such as `() => log.Info($"x {id}")`, now fails to compile, because handler conversions aren't allowed in expression trees.
- **Schema change:** the new `message_template` field affects downstream JSONL consumers.

Consider a release note for both.

## Remaining test gaps
- Format strings longer than 256 characters being omitted.
- Format text sharing the scalar budget with message text.
- Exhausting `MaximumParts` with literals.
- Template clipping.
- Caller-side precision for `BigInteger`.
- The legacy and internal render path.
- A render failure overlapping the stop deadline.

## Coverage limits
- **Source I didn't have:** `IncomingEventQueue`, `OutputQueues`, `EventEncoder`, `EventProcessing`, `QueuedRuntimeFactory`, the existing (non-interpolated) `CoreLogger` overloads, `LogWriteResult`, and `UnicodeScalarText`. I don't know the base diff, so I couldn't tell which runtime and lifecycle lines are new.
- **What that means:** I did not evaluate the P01 eviction, publisher enrollment, final empty pass, or batch-capture logic. Those live in the source I didn't have.
- **Compile-time behavior:** Overload-resolution and compiler conclusions are reasoned from C# rules, not compiled.
- **Test results:** The 7,016 passing tests don't address findings 1, 2 or 8.