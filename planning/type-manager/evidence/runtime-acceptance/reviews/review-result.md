# Type Manager runtime acceptance review

**Verdict: PASS once two items are fixed.** The mutants, the fixture checks and the cancellation-boundary design hold up. Neither item is a defect in Core. One is a weak assertion; the other is about which source the evidence pairs with which binary.

## Actionable defects

### 1. The "collection protected" assertions also pass for a plain array

These are at `TypeManagerCatalogTests.cs` (`ExactLookupDoesNotNormalizeOrLoadAndCollectionsRejectMutation`) and in `CheckProtectedReport` in `Runtime/Host/Program.cs`.

On a `T[]`, both `IList<T>.Clear()` and `IList<T>.Add()` throw `NotSupportedException`. So if the report stored a `string[]` (for example, a `.ToArray()` of the `Where` queries), every current assertion would still pass. Yet a consumer could still do `((IList<string>)x)[0] = "…"` or cast to `string[]` and change it.

Core's own collections use `Array.AsReadOnly`, so they are fine today. I can't see `TypeManagerPreparationReport`'s storage, and the test would not catch a regression to arrays in any of these collections.

Fix: add a control that tells the two apart. For example:
- assert `collection is not Array`, or check the exact wrapper type; and
- for non-empty collections, assert that `((IList<T>)c)[0] = c[0]` throws `NotSupportedException`. A `ReadOnlyCollection<T>` throws here; an array does not.

Note that `ICollection<T>.IsReadOnly` returns `true` for arrays too, so it is not a valid control.

### 2. The instrumented source copy is not tied to the frozen candidate

`verify-type-manager-instrumentation.py` copies every `*.cs` under `src/CIS.Core` from the working tree. It records `candidateCoreSha256` next to `sourceInputs`, but nothing links the two.

The session-start git status shows uncommitted changes inside that tree:
- modified `CIS.Core/Logging/CoreLogDiagnostics.cs` and `CoreLogRuntime.cs`
- untracked `CIS.Core/Logging/LogStopResult.cs`

So the instrumented binaries were compiled from whatever is in the working tree, which may not be the source that produced `d70fdc…`. The non-replay `verify-runtime-processes.py` path has the same issue unless `--expect-core-sha256` is passed. With that flag it fails closed, which is correct.

You already say the instrumented evidence is source-copy evidence rather than an observation of the DLL. The gap is narrower than that: the report doesn't say *which* source.

Fix, using only the verifier: record the `git rev-parse HEAD` / tree hash for `src/CIS.Core`, then do one of these:
- refuse to run when `git status --porcelain -- src/CIS.Core` is non-empty, or
- record a hash of that diff so the pairing is explicit.

## Evidence limits (not bugs)

- **Concurrent awaiters share one manager/failure.** `CreateAsync` returns a single `Task<ITypeManager>`, so every awaiter gets the same result or the same stored exception by TPL semantics. No Core change could make these `AreSame` checks fail. They are fine as regression documentation but are not evidence about Core. The useful part of that test is the post-cancel check that state is unchanged (`Types`, report reference, query, exclusive reopen).
- **Generic/non-generic agreement.** The non-generic call runs first, so the generic call always reads from the cache. The test still catches an extension that maps `T` to the wrong contract, which is the real risk, so this is adequate.
- **"Real deployed TPA assembly."** TPA membership is inferred: the file exists in `AppContext.BaseDirectory` and `Assembly.Load` by simple name succeeds without a resolver. It is never asserted directly. Checking `AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")` would make the claim explicit. The current inference is sound in practice.
- **`verify-type-manager-runtime-tests.ps1`** records the candidate's SHA but does not enforce it. The `d70fdc…` claim therefore depends on the caller reading `results.json`, unlike the runtime verifier's optional `--expect-core-sha256`.
- **Mutation coverage.** The three mutants cover one query path (`GetImplementations`) and the final cancellation check. They are targeted controls, not mutation coverage. You already scope them that way.

## Checked and sound

- **Manifest version conflicts.** `Reverse()` flips the relative order of v1 and v2. With ConflictFixture deployed as TPA, the "nothing loaded" check is meaningful. The `"Conflicting selections"` message check rules out passing via the TPA-identity-conflict path.
- **Outside-catalog late load.** It checks that the assembly is not preloaded, that the lookup is not loaded, and that after the explicit load an *uncached* `IDisposable` query, `GetImplementations(lateType)`, exact lookup and `GetDefinedTypes(outside)` all still exclude it.
- **Instrumentation anchors.** Each must match exactly once, so the build fails loudly if an anchor is missing. `read_text` normalizes CRLF. The probe sits after the report is assigned, so only the final `ThrowIfCancellationRequested` can stop publication.
- **Mutant isolation.** The query mutant cannot inflate preparation counts, because precomputation bypasses `GetImplementations`. The rollback check correctly asserts that the probe library *stays* loaded.
- **Counts.** The runtime verifier's 15 modes plus 2 generator checks make 17. The architecture class has 3 tests plus 6 DataRows, which is 9.
