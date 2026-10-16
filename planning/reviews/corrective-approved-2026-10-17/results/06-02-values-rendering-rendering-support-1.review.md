# Review 02-values-rendering-rendering-support-1: static review of the supplied files

This review covers only the files in this bundle and its bounded context. I executed nothing, so nothing here implies test results, whole-change PASS or release qualification. I found four confirmed actionable findings, all Low or Low–Medium. I found no production defect in the supplied rendering context.

## Confirmed findings

### F1 — Low–Medium: the discovery UTC check depends on the host time zone, unlike the deleted Python reader
**Where:** `DiscoveryProcessTests.cs`, the `Time` helper at the end of the file. It is used by the UTC assertions around lines 60–63 and 84.

**Problem:** `DateTimeOffset.Parse(text, CultureInfo.InvariantCulture)` uses the default `AssumeLocal` behaviour. A timestamp with no `Z` or offset, such as `2026-10-16T17:28:37.1234567`, gets the machine's local offset.
- On a UTC-zone host, which is typical for Ubuntu/CI lanes, `Assert.AreEqual(TimeSpan.Zero, initialized.Offset)` passes for a value that is not marked as UTC at all.
- The same applies to `ProcessStartedAtUtc`.
- The deleted `verify-process.py` used `fromisoformat` on the raw text, which gives a naive datetime with `utcoffset() is None`, so it rejected such values.

**Concrete failure:** a Core regression that emits offset-less `InitializedAtUtc` or `ProcessStartedAtUtc` passes on Ubuntu but fails on a non-UTC Windows host.

**Fix:** require an explicit designator before comparing offsets. For example, use `DateTime.Parse(..., DateTimeStyles.RoundtripKind)` and assert `Kind == Utc`, or parse exactly with a `Z`/offset format.

### F2 — Low: the `runtime` corruption control does not prove the exact-runtime pin
**Where:** `DiscoveryProcessTests.cs`, around line 140 (`case "runtime"`), checked against lines around 50 and 77.

**Problem:** the control changes only `Observed.RuntimeDescription`. Two separate assertions can reject that:
- the exact pin `".NET 10.0.12"` (around line 50);
- the observed-vs-projected equality loop (around line 77).

The control uses `Assert.ThrowsExactly<AssertFailedException>` and does not check which assertion failed. If the exact pin were deleted, the control would stay green through the equality check.

**Concrete failure:** a child that genuinely ran on the wrong runtime reports the same wrong value in both `Observed` and `Runtime`. Only the pin catches that case, and nothing guards the pin.

**Fix:** corrupt both `Observed.RuntimeDescription` and `Runtime.RuntimeDescription`, or assert the expected failure reason. This matches the expected-reason standard applied in the F-series corrections.

### F3 — Low: malformed-observation handling does not produce stable `InvalidDataException` reasons
**Where:**
- `PresentationExpectations.cs`, lines 9–14 (the `Validate` catch filter).
- `HumanRenderingExpectations.cs`, `Validate` and `Read`.

**Problem in `PresentationExpectations.Validate`:** the catch filter wraps only `JsonException`, `DecoderFallbackException` and `KeyNotFoundException`. The likely malformed paths escape unwrapped:
- A missing key produces `NullReferenceException`, because the `JsonObject` indexer returns null and is then dereferenced with `!`.
- Bad base64 or a non-integer `number` produces `FormatException`.
- A wrong JSON kind passed to `GetValue<T>` or `GetString` produces `InvalidOperationException`.

**Problem in `HumanRenderingExpectations.Validate`:** it has no wrapper at all. The deleted Python `utf8` control (`\xff` prefix) reaches `UTF8Encoding(false, true).GetString` in `Read` and surfaces as `DecoderFallbackException`.

**Consequence:** a native expected-reason control for these faults cannot assert `InvalidDataException` or a reason; it can only rely on "some exception".

**Fix:** either extend the filter, or make `HumanRenderingExpectations.Validate` apply the same wrapper that `PresentationExpectations` uses.

### F4 — Low: the README discovery claim overstates what the sweep checks
**Where:** `tests/README.md`, the WP03-S01-T04 / Batch B paragraph.

**Problem:** the README says the sweep "verifies unique instance IDs and stable application facts across all seven children". The code checks `JsonNode.DeepEquals` on application facts only when `scenario.Mode == "native"`, which is five of the seven children. The `assembly-version` and `unavailable` projections deliberately have a different `BuildVersion`.

**Note:** the deleted Python reader's scope label distinguished "five actual static-constructor children plus two controlled internal … projections". The README drops that distinction.

**Fix:** reword to say "unique instance IDs across all seven; stable application facts across the five native children".

## Parity checked with no finding

**`HumanRenderingExpectations` vs `verify-human-rendering.py`.** The `verify()` logic matches on all of these:
- the cap and final LF;
- the strict UTF-8 decode;
- rejection of Cc, Cf, Zl and Zp characters;
- duplicate-field detection and field separation;
- tolerance for leading and post-`=` whitespace (via the `Utf8JsonReader` whitespace skip);
- the truncation flags and the display loss marker;
- the message prefix and the scalar-budget cost (`6 × UTF-16 length` equals the Python 6/12);
- the normal field count;
- two-way presence checks for optional context, resources and payload;
- verbose field-set equality.

**`PresentationExpectations` vs `verify-presentation.py`.** The `verify()` logic matches. The 64-row matrix and per-profile equality now live in `PresentationTests`. The six comparator faults are ported, but they now run against in-process `Execute` instead of physical child output. The comparator code is identical, so detection power is unchanged.

**`DiscoveryProcessTests` vs `verify-process.py`.** All projection checks are preserved apart from F1. Application-fact stability is strengthened from one native pair to all native children. Five corruption controls are added. Python's unused `source` column is not lost.

**Process identity in `FixtureProcessChecks.RunAsync`.** It checks:
- the exact runtime;
- the OS family;
- the actual child PID;
- fixture, Core and input SHA-256 before and after the run;
- a strict UTF-8 decode and empty stderr.

`Capture` also checks that the owned directory stays empty and that the collector marker is not echoed. This covers the Python process checks for jsonl-reuse, human-rendering, presentation and rendering-story.

**Production rendering** (`EventRenderer`, `HumanRenderer`, `HumanText`, as context):
- Measurement and writing share one escaper.
- Array and object size accounting includes separators and short-circuits at the limit.
- The reserved marker, LF and empty message keep `Finish`'s size invariant.
- An optional field cannot fit after a clipped message, because the leftover space is below 12 and the smallest optional field needs 14 bytes.
- JSONL reuse returns the original memory.

## Questions and limits (missing context)

1. **Deleted negative and positive controls.** I cannot see where the native versions of these deleted Python controls live:
   - human rendering: 11 negative controls (`missing`, `control`, `utf8`, `unexpected-loss`, `type`, `exception`, `split`, `extra`, `missing-loss`, `underfill`, `resource`) plus the order/spacing positive control;
   - JSONL reuse: 9 negative controls (`bom`, `crlf`, `banner`, `ansi`, `utf8`, `type`, `stream`, `raw-ls`, `underfill`) plus the member-order/whitespace positive control;
   - rendering story: 11 faults, 4 positive controls (`json-layout`, `human-layout`, `integer-float`, `number-notation`) and the AC01–AC04 criteria check.

   The supplied process tests are positive-only. `JsonlReuseExpectations`, `RenderingStoryExpectations` and any `HumanRendering`/`JsonlReuse`/`RenderingStory` unit test classes were not supplied, so parity is unconfirmed.
2. **Log-value readers.** `LogValueProcessTests` and the native replacements for `verify-log-values`, `verify-log-qualification` and `verify-log-collections` were not supplied. I cannot confirm these behaviours were preserved:
   - adapter-only vs Core attribution (`factoryCalls == 0` exactly for the named IDs);
   - the error parameter name;
   - `rejectedInputNotEchoed` and `rangeActualValueAbsent`;
   - the native exception category;
   - independent node/depth shape measurement (≤1024 nodes, depth ≤8);
   - `lifecycleApiPresent == false`. `FixtureProcessChecks` does not check this field.
3. **Windows 8.3 alias control.** I cannot see a native replacement for `verify-path-tildes.py`, which used P/Invoke `GetShortPathNameW`. `ConfigurationPathProcessTests` was not supplied.
4. **Required-case bindings.** The required-case inventory bindings for these classes were not supplied.
5. **Ubuntu evidence.** Is there Ubuntu evidence for `DiscoveryProcessTests`? The README says "runs on Windows and Ubuntu", but the supplied evidence is Windows-only and full Ubuntu Developer is still pending.
6. **Encoding.** `DiscoveryProcessTests` does not request strict UTF-8 decoding, unlike `FixtureProcessChecks`. This looks benign if the child emits ASCII-escaped JSON, but I have not confirmed that.
7. **Test parallelization.** The assembly parallelization settings were not supplied. The cwd and parent-environment assertions in `FixtureProcessChecks` would be flaky if other tests mutate the process environment concurrently.
8. **Required-field overflow.** Whether a capture that fits as JSONL can still overflow a required verbose human field (raising `EventEnvelopeTooLargeException`) at the 16 KiB minimum depends on field limits that were not supplied.

Within the supplied scope there are no further findings.