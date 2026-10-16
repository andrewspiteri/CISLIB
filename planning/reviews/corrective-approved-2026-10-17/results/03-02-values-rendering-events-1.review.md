# Review result: 02-values-rendering-events-1 (static, read-only)

I read the assigned files and the bounded context only. I executed no code, tests or tools. Whole-change PASS and release qualification are not implied.

## Confirmed findings

**F-1 — Low — Correlation leak check dropped**
- **Location:** `EventProjectionProcessTests.cs` lines 36–40, the shared `Capture`.
- **Original behaviour:** The deleted `verify-event-correlation.py` rejected child stdout containing `CIS_NEVER_CAPTURE_ACTIVITY`, along with the projection and collector tokens.
- **Current behaviour:** `Capture` checks only `CIS_NEVER_CAPTURE_COLLECTOR` and `CIS_NEVER_CAPTURE_PROJECTION`. `EventCorrelationProcessTests` adds no further check.
- **Failure:** Suppose activity baggage or tag content leaks into a part of the child output that `EventCorrelationExpectations` does not compare exactly. The Python reader would have rejected it; the C# replacement would pass.
- **Severity reasoning:** This is only defence in depth if `EventCorrelationExpectations` keeps Python's exact whole-object comparison. That file is not supplied.
- **Fix:** Add a `CIS_NEVER_CAPTURE_ACTIVITY` absence assertion to `Capture`. It is harmless for the other commands.

**F-2 — Low — Inconsistent operating-system observation**
- **Location:**
  - `EventEncodingProcessFixture.cs` line 19
  - `EventQualificationProcessFixture.cs` line 21
- **Failure:** These two fixtures report macOS as `"unknown"`. The correlation, precedence and projection fixtures report `"macos"`. Any parent check comparing `observed.operatingSystem` to a host family would therefore behave differently across commands.
- **Severity reasoning:** This is not a parity regression, because the Python readers accepted only windows and linux. It does make the observation untruthful on macOS.
- **Fix:** Use the same three-way expression in all five fixtures.

## Questions with severity if confirmed

**Q-1 — Medium if absent — Source, artifact and runtime identity checks**
- **Location:** `EventProjectionProcessTests.Capture`, lines 36–40, which serves all five process tests.
- **What Python checked for every case:**
  - `observed.runtimeDescription == ".NET 10.0.12"`
  - `operatingSystem == family`
  - `inputsSha256 == sha(inputs)`
  - `fixtureSha256 == sha(host)`
  - `coreSha256 == sha(host.parent/CIS.Core.dll)`
  - child exit code 0 and empty stderr
  - the `--fx-version 10.0.12 --roll-forward Disable` invocation
- **What the supplied C# asserts:** Only `caseId`, `lifecycleApiPresent == false`, two token absences and an empty owned directory.
- **What I need:** Confirmation that `FixtureProcessChecks.RunAsync` (not supplied) performs every check listed above.
- **If it does not:** The test names claim "IsolatedExactRuntimeChild" without any parent-side evidence of the runtime or of artifact identity. The fixtures' hash fields would then be emitted but never checked.

**Q-2 — Low/Medium — Expectations may not be bound to the executed catalog**
- **Location:** for example `EventCorrelationProcessTests.cs` lines 17 and 19, and the same pattern in the other four tests.
- **Issue:** The child receives `FixtureProcessChecks.Find(name)`. The parent reads its expected row through `LogValueProcessTests.Read(name)`.
- **Why it matters:** Python used one path for both. Unless the two resolvers provably return the same file, or the parent compares the child's `inputsSha256` against the bytes it read, the parent may validate against a different catalog than the child executed.
- **Mitigation:** The child self-validates against its own input, which reduces the risk.
- **Related:** `ValidateSchema` (qualification test, lines 27–28) derives the contracts root from `LogValueProcessTests.Find` plus `../..`. Whether that resolves to the repository root depends on the unsupplied fixture location.

**Q-3 — Comparator negative controls**
- **Python controls:**
  - correlation: 8 mutations
  - precedence: 8 mutations
  - projection: 6 mutations
  - qualification: a mandatory, gating corrupted-null control ("Independent negative control did not run")
- **What the supplied C# shows:** Only the encoding controls (`EventEncodingFixtureTests`, five faults). These are additive, since the Python encoding reader had none.
- **What I need:** Confirmation that equivalent expected-reason controls exist for:
  - `EventCorrelationExpectations`
  - `EventProjectionExpectations` (projection and precedence)
  - `EventQualificationExpectations`
- **If they are absent:** This is a Medium parity loss for the expected-reason controls.

**Q-4 — Qualification "none" rows**
- **Location:** `EventQualificationProcessTests.cs` line 23.
- **Issue:** The line reads `row["input"]["maxEntryBytes"]` unconditionally. Python read it only after the `none` early return.
- **What I need:** Confirmation that `none`-mode rows carry `maxEntryBytes`. If they do not, the test fails with a NullReferenceException.

**Q-5 — Exact denominators**
- **Python check:** `caseCount == len(ROWS) == len(cases) == N`.
- **What I need:** Confirmation that `FixtureProcessChecks.Cases(name, N)` checks both the declared `caseCount` and the actual row count. Duplicate IDs would still fail at `Single`.

## Parity checks with no defect found

**Encoding comparator (`EventEncodingExpectations` against `verify-event-encoding.py`):**
- Framing, the LF-inclusive cap, `fillsLimit`, truncation marker, protected identities, build resource and every mode branch are covered.
- `GetValue<T>` on parsed JSON is type-strict.
- `JsonNode.DeepEquals` for the array is stricter than Python's `[True, 1.25] == [1, 1.25]`.
- The five fault controls each fail through `Require`, which throws `InvalidOperationException`, matching the `ThrowsExactly` assertion.

**Precedence and projection native binding:** Both pass `nativeBindings`, consistent with Python's `@exception-type` substitution. The binding logic itself is not supplied.

**Qualification:** The test adds producer-schema validation beyond what Python did. This is additive.

**R1 follow-up overlap (`IncomingEventQueue.Capture`):** The frozen-observation path builds a new readonly value directly instead of using a record `with` copy. This matches the stated fix for the diagnostics IL guard.

**Context production code (`EventWireFormat`, `JsonTextBytes`, `EventByteProjection`, `EventEncoder`, `EventProjection`, `EventProcessing`):**
- Size accounting matches between property, comma, LF and brace counting.
- The encoder re-measures and throws on mismatch.
- No overflow path was identified.
- I report no findings here; these files were context, not assigned.

## Out-of-scope observation (not assigned)

`EventMetadata` XML docs describe `InstanceId` and `InitializedAtUtc` as "generated by the container" and "container construction instant". Under the static `EnvironmentOptions` decision, this wording may be stale. Please verify against the decisions file.

## Limits and missing context

These files were not supplied:
- `FixtureProcessChecks` (`RunAsync`, `Cases`, `Find`)
- `LogValueProcessTests.Read` and `Find`
- `EventCorrelationExpectations`, `EventProjectionExpectations` and `EventQualificationExpectations`
- the `Event*Fixture` executors and the DiscoveryHost `Program` dispatch
- `EventRecordValidator`
- all five case catalogs
- the required-case bindings
- `HumanRenderer`, `LogValue`, `LogValueProjection` and `CapturedException`
- `OutputEventQueue.cs` (listed in the identities but not inlined)
- `decisions.md` and `plan-amendments.md`
- raw execution reports

Because of these gaps, I cannot confirm:
- required-case bindings for these process tests
- complete Python-to-C# parity
- execution of any case

**NO FINDINGS beyond F-1 and F-2 within the supplied scope**, subject to Q-1 through Q-5.