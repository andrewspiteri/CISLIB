# Review: `TypeManagerOrderingTests.cs` (TM-020 ordering draft)

**Verdict:** No defects or false-positive passes found. It closes TM-020 for the main risk, which is query and catalog order depending on culture or manifest order. Two gaps leave parts of the claim weaker than the description suggests, and both are cheap to close.

## What holds up

- **The expected order is set independently.** In the nested-type names, `Z` (U+005A) is less than `É` (U+00C9) ordinally, so the result `[Zebra, Éclair]` is ordinal. The linguistic order is the reverse in invariant, en-US, sv-SE and tr-TR. If `CompareTypes` were changed to any culture or invariant-linguistic comparer, every row would fail the `expected` assertion.
- **The guard assertion fails loudly when globalization is broken.** Under `InvariantGlobalization`, `GetCultureInfo("tr-TR")` throws, and an ordinal fallback would fail `IsLessThan`. The test cannot pass silently for that reason.
- **Culture is restored correctly.** `CurrentCulture` is async-local in .NET Core. It flows into `CreateAsync` and the `Task.Run` lambdas, and the `finally` restore is correct. `[DoNotParallelize]` is not strictly needed for culture, but it does no harm.
- **`selected.Reverse().ToArray()` is safe.** On .NET 10 it binds to `Enumerable.Reverse(T[])`, not the in-place `MemoryExtensions.Reverse(Span<T>)` from the C# 14 first-class-span change. `selected` is not modified, and the later `foreach (var assembly in selected)` is fine.
- **Failures surface.** `Assert` failures inside `Task.Run` propagate through `await Task.WhenAll`.
- **Type identity is pinned.** `AreSame(type, found)` and `CollectionAssert` against `typeof(...)` confirm the loader reuses the already-loaded Default-ALC assemblies, not the temp copies. If it ever loaded the copies, these assertions would fail; they would not pass by mistake.

## Gaps against the stated claim

1. **`GetDefinedTypes` and `Assemblies` are only compared to the baseline, never to an absolute order.**
   - `Types` is covered indirectly because `GetImplementations` iterates `Types` in order. The per-assembly `Array.Sort(types, CompareTypes)` is a separate sort.
   - Suppose only that sort were changed to `StringComparer.CurrentCulture`. The baseline (invariant-linguistic) and the en-US/sv-SE rows agree on É < Z, so the baseline comparison passes. Whether tr-TR catches it depends on incidental `I`/`i` names in the host assembly.
   - **Fix:** assert absolute relative position in both lists. For example, `IndexOf(typeof(ZebraCandidate)) < IndexOf(typeof(ÉclairCandidate))` on `manager.GetDefinedTypes(host)` and on `manager.Types`. This stays independent of how the implementation sorts.

2. **The reversed inventory may not permute anything meaningful.**
   - With `[host, core]`, reversal only swaps the host entry with the single other entry.
   - If `AssemblyInventory` or `SelectedAssemblyLoader` pins the host first or otherwise special-cases it, the reversed rows exercise nothing new and still pass.
   - Also, `allTypes` is globally sorted after loading, so manifest order can only affect `Assemblies` and the per-assembly traversal.
   - **Fix:** confirm the loader does not special-case the host entry, or add at least two non-host assemblies so the reversal permutes non-host entries. Any stable framework assembly already in the Default ALC would do.

## Minor points (not defects)

- **"Concurrently cached results" is literally accurate, but concurrent first computation is not tested.** Both `ICultureMarker` and `object` are computed on the main thread before `Task.WhenAll`, so the 16 tasks only read the cache. That is fine for TM-020, but don't cite this test as covering lazy-computation races.
- **Cleanup can hide failures or leak the temp directory.**
  - `Directory.Delete` in `finally` would mask the original failure if the directory were ever locked. On Windows that would happen if a future loader change loaded the copied DLLs from path.
  - `CreateDirectory` and `File.Copy` run before `try`, so a failed copy leaks the temp directory.
  - Consider moving setup inside `try` and wrapping the delete in a best-effort catch.
- **The source file's encoding matters.** If an editor ever saves it in NFD, `É` becomes `E` + U+0301, and the fixture stops distinguishing ordinal from linguistic order. The test would then fail on `expected` with a confusing message. A one-line `Assert.AreEqual('\u00C9', nameof(ÉclairCandidate)[0])` makes that failure self-explanatory.
- **Confirm `Assemblies` should be manifest-order-independent.** The test asserts it is. If the spec instead defines `Assemblies` as manifest order, that assertion encodes behavior the spec doesn't require.

With gap 1 fixed and gap 2 confirmed or fixed, I'd consider the TM-020 culture and inventory-order requirement closed. Mutation and release qualification remain separate, as you noted.
