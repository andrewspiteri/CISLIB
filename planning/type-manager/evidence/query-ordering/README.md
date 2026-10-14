# Culture and successful inventory ordering

14 October 2026. The TM-020 audit found that prior ordinal-order assertions did
not directly exercise culture changes and reversed valid inventories. Three new
native test cases close that specific gap.

Windows and native Ubuntu each pass 80 focused cases: 71 unit and nine runtime
architecture cases, with no failures or skips. The unchanged Core is
`d70fcfdc41b6d15829227f1cee60130ddad3f7f82cc11c7a2fcbf8d5f700fb09`.
Ubuntu executes the same managed test output under `/mnt/c`; this is not a Linux
source rebuild or the package candidate used by the separate lifecycle suite.

`CultureAndValidInventoryOrderCannotChangePreparedOrLazyResults` has en-US, tr-TR
and sv-SE rows. Each compares an invariant-culture baseline with both inventory
orders and prepared/lazy queries. The inventory contains the host and two
non-host assemblies, so reversal changes non-host order too. Assertions cover:

- exact assembly order and unchanged complete catalog/definition lists;
- explicit Zebra-before-Éclair order in global and per-assembly catalogs and
  query results, independently of the implementation's comparer;
- exact identity lookup, prepared-contract counts and generic/non-generic cache
  identity;
- stable concurrent reads of already-computed query results.

The fixture verifies that linguistic ordering differs from the required ordinal
ordering and that its accented name retains the intended Unicode scalar. It does
not claim to exercise concurrent first computation; earlier concurrency and
instrumentation evidence covers that separately.

The read-only review prompted absolute per-assembly ordering assertions and a
second non-host assembly. These exact corrections were followed by both final
80-case runs and formatting verification. `inputs/` retains canonical test and
harness bytes; `checkpoint.json` records report and managed-output hashes. Both
native reports also passed the repository's strict `Read-NativeTrx` validation.
The copied input inventory covers the named files, not every transitive build
input. No production source changed.

Reproduce from the repository root with the frozen Core and its two dependency
DLLs beside it:

```powershell
pwsh -NoProfile -File implementations/csharp/eng/verify-type-manager-runtime-tests.ps1 -CoreAssembly <CIS.Core.dll> -OutputRoot <empty-output-root>
```

For native Ubuntu replay, run `dotnet vstest` on that produced test assembly and
retain/validate its TRX. This remains development evidence. Full mutation,
behaviour/schema compatibility, performance and final release qualification are
separate roadmap requirements.
