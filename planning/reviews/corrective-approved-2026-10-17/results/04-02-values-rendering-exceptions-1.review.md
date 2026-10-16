# Static review: 02-values-rendering-exceptions-1

This review is static only. Nothing was executed, and it covers only the supplied files and context. None of the eleven known fixes (R1/R2, F1–F3/F5/F6 file, T1–T4) touch the files in this bundle, so there was no fix overlap to check.

## Confirmed findings

**F1. Medium: content read expectations are no longer independent of the fixture source**
- **Location:** `ExceptionContentExpectations.cs`, `Verify`, about lines 65–83: `IReadOnlyList<ExceptionContentProbe> probes = ExceptionContentSources.Create(id).Probes;`
- **Parity loss:** The deleted `verify-exception-content.py` pinned an exact probe label sequence for each case, such as `nodes` → `root, n0..n16` and `combined` → `d4..d1, b, b0..b12, tail, root`. The C# version takes the expected label list and count from the same source builder that produces the input. It never checks that each `OmittedLabels(id)` entry exists in that list.
- **Concrete failure:** Suppose `ExceptionContentSources` builds `nodes` with only `n0..n15`, or drops `tail` from `mixed-nodes` or `combined`. The tree is unchanged because truncation still happens through other overflow. Every read still matches, so `Verify` passes. The evidence that siblings past the exhausted budget are never read silently disappears for that label.
- **Fix:** Port the Python `probes()` sequences as an independent expected label array and compare them to `read["label"]`. At minimum, assert that `OmittedLabels(id)` is a subset of the probe labels and pin the probe count for each case.

**F2. Low: a missing native error binding is silently accepted**
- **Location:** `ExceptionQualificationExpectations.cs`, `Validate`: `JsonNode? diagnostic = expected["error"] is JsonNode error ? nativeBindings[error.GetValue<string>()] : null;`
- **Problem:** If a row's `expected.error` has no `adapterObservations.csharp` entry, the indexer returns null. The expected `nativeError` and `parameter` then become null. Python raised `KeyError` in this situation.
- **Concrete failure:** A new error code with no C# binding passes whenever the fixture also emits null diagnostics. The fixture receives the same bindings object, so this is plausible.
- **Fix:** Throw `InvalidDataException` when `error` is non-null and the binding is absent.

**F3. Low: exact JSON number typing is weaker than in the deleted comparator**
- **Location:** `ExceptionQualificationExpectations.cs`, `Validate`.
- **Problem:** The integer guard covers only `nodes`, `depth`, `cachedNodes` and `cachedDepth`. Everything else relies on `JsonNode.DeepEquals`, which compares JSON numbers by numeric value. I am relying on .NET's documented `DeepEquals` semantics here and did not execute this. Python's `exact()` rejected int/float type differences everywhere.
- **Concrete failure:** A `reads.<label>.message` or `.stack` value of `1.0` is accepted. `forbidden` is only incidentally strict, because `GetValue<int>` throws.
- **Fix:** Apply the integer check to every count inside `reads` as well.

**F4. Low: the `captureCalls` evidence is tautological**
- **Location:**
  - `ExceptionProjectionProcessFixture.cs`, `Run`: `int captureCalls = 0; captureCalls++;`
  - `ExceptionProjectionProcessTests.cs`: `Assert.AreEqual(1, actual["captureCalls"]…)`
- **Problem:** The value is a source literal, not an observation. The defect is carried over unchanged from the Python version. It must not be cited as single-capture evidence; only `messageReads`/`stackReads` in `mutation` mode observe that.
- **Fix:** Remove the field and its assertion, or document it as non-evidence.

**F5. Low: the `null` projection mode accepts a missing `exception` key**
- **Location:** `ExceptionProjectionProcessTests.cs`: `JsonNode.DeepEquals(JsonNode.Parse("null"), actual["exception"])`
- **Problem:** The check also passes when the key is absent, because the indexer returns null. Python's `value["exception"]` required the key to be present.
- **Fix:** Add `Assert.IsTrue(actual.ContainsKey("exception"))`.

**F6. Low: `inputsSha256` is not pinned to the parsed bytes**
- **Location:** `ExceptionQualificationProcessFixture.cs`, `Run`.
- **Problem:** `ReadDocument(inputPath)` and `Hash(inputPath)` read the file separately. The reported hash is therefore not proof of the catalog that was actually evaluated. This is the same provenance pattern corrected in T-series for Type Manager.
- **Fix:** Read the bytes once, then hash and parse that same buffer.

No findings in the production `ExceptionProjection.cs` or `CapturedException.cs` within the supplied scope. Specifically:
- The node and depth budgets enforce 16 nodes and depth 4.
- Truncation is marked without reading clipped children.
- Children are copied into a read-only collection.
- No `Exception` or caller collection is retained.

## Questions and limits (need unsupplied context)

**Q1. Possible High: runtime, artifact and exit identity checks**

The deleted readers asserted all of the following for every child process. `Capture` checks none of them, and the second return value of `RunAsync` is discarded (`(JsonObject actual, _)`). If `FixtureProcessChecks.RunAsync` does not enforce them, all four exception process families have lost source and artifact identity verification.

| Check in deleted readers | Visible in `Capture`? |
|---|---|
| `runtimeDescription == ".NET 10.0.12"` | No |
| `operatingSystem` matches the lane | No |
| `fixtureSha256` / `coreSha256` equal the on-disk host and `CIS.Core.dll` | No |
| `inputsSha256` (qualification) | No |
| Exit code 0 | No |
| Empty stderr | No |

Supplying `FixtureProcessChecks` would settle this.

**Q2. Case completeness.**
- The deleted readers fixed the case sets at 34 boundary IDs and 20 content IDs.
- In C#, those sets now come from `ExceptionBoundarySources.Cases` and `ExceptionContentSources.Cases`, with no visible count or ID pin. By contrast, qualification is pinned with `Cases(..., 68)`.
- I need the required-case binding manifest to confirm the boundary and content sets are bound.

**Q3. Boundary expectations not supplied.** `ExceptionBoundaryExpectations.Verify` and `VerifyReads` were not provided, so I could not check parity with:
- the Python `nodes-*` source label sequence;
- the `labels.count(label)` read counts, including the `shared` case.

**Q4. Qualification sentinel.**
- The process test hard-codes `"CIS_PRIVATE_QUALIFICATION_DATA"`.
- The unit test uses `ExceptionQualificationSources.PrivateSentinel`, and the content test uses the constant.
- If the sources embed the constant rather than reading the environment variable, and the two values differ, the process absence check is vacuous.
- Suggest using the constant. I need `ExceptionQualificationSources` to confirm.

**Q5. Fixture and native bindings.** `ExceptionQualificationFixture.Execute` receives `adapterObservations.csharp`. If it echoes `nativeError` and `parameter` from those bindings instead of observing the thrown exception, the expected-versus-actual diagnostic comparison is circular.

**Q6. Catalog file resolution.** `FixtureProcessChecks.Find(...)` supplies the child's input, while `LogValueProcessTests.Read(...)` supplies the expectations. I need to know whether they resolve to the identical file. No identity link between the two is visible.

**Q7. Unicode handling.**
- `Text()` builds the remainder from `EnumerateRunes`, which substitutes U+FFFD for ill-formed UTF-16. Python sliced by code points.
- Python's strict UTF-8 encodability check on retained text was also dropped. `CheckShape`'s rune counts do not detect lone surrogates.
- Whether this matters depends on how the `utf16-code-unit-input` rows are represented in the catalog, which was not supplied.

**Q8. Per-case evidence artifacts.** Python wrote hashed per-case stdout/stderr and a summary record. Whether the C# runs retain equivalent artifacts determines what can be cited as executed evidence.

This review does not imply whole-change PASS, coverage of the other 325 unreviewed paths, or release qualification.