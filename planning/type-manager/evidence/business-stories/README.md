# Type Manager business startup stories

14 October 2026. Ten named MSTest cases now populate the previously empty
`CIS.Core.BusinessCases` project. The same managed test output passes all ten on
Windows x64 and native Ubuntu x64 under .NET 10.0.12, with no failures or skips.
This closes the missing development business-suite execution, not TM-026's full
acceptance reconciliation or the final six-family release gate.

The frozen Core is
`d70fcfdc41b6d15829227f1cee60130ddad3f7f82cc11c7a2fcbf8d5f700fb09`.
Both lanes use SDK 10.0.401. Ubuntu runs the Windows-built managed output through
its native SDK under WSL from `/mnt/c`; this is not a Linux source rebuild.

| Named story | Cases | Acceptance covered |
| --- | ---: | --- |
| `PreparationHandsUsableCatalogToApplicationBeforeItCreatesServices` | 2, prepared/lazy | A08–A10, A13, A21, A26: inherited nonpublic concrete discovery, exact identity, application-owned activation and report reconciliation before application startup. |
| `FailedPreparationNeverInvokesApplicationFactory` | 4 | A05, A15, A21, A26: malformed/missing inventory, missing selected file and corrupt selected image retain failure context and prevent the application factory. |
| `PreCanceledPreparationNeverInvokesApplicationFactory` | 1 | A17, A21: already-canceled preparation prevents startup and activation. |
| `ConcurrentConsumersReusePreparedCatalogWhileApplicationOwnsObjectLifetime` | 1 | A19–A21: prepared result/manager/report reuse while application consumers construct and own their own objects; mutation of the shared query result is rejected. |
| `RunningApplicationKeepsSnapshotWhileNextStartupValidatesChangedInventory` | 1 | A14, A22: the first lazy query and exact lookup remain usable after deployment files change; a separate preparation validates the changed inventory. |
| `FailedStartupCanBeRepairedWithIndependentPreparation` | 1 | A18, A21: correcting an invalid inventory allows a new application startup; the original failure remains observable. |

These stories use an explicitly authored inventory selecting the already-loaded
test host. Its copied DLL is validated and the existing assembly is reused by
identity; they do not test an initially unused assembly load. The application
helper owns startup ordering and activation. No public Configuration contract,
container abstraction or lifetime management was added to Type Manager.

The concurrent story exercises prepared reads, not a guaranteed first-query race.
The cancellation story covers pre-cancellation only. Existing
[runtime acceptance](../runtime-acceptance/README.md),
[query ordering](../query-ordering/README.md) and
[console/Web integration](../host-integration/README.md) retain separate evidence
for unused/broken libraries, lazy concurrency, mid/final cancellation, later loads,
actual DI scopes and HTTP requests. The business suite supplements those checks.

## Reproduction and evidence

From the repository root, run:

```powershell
pwsh -NoProfile -File implementations/csharp/eng/verify-type-manager-business.ps1 -CoreAssembly <frozen-CIS.Core.dll> -CandidateReport <runtime-producer-results.json> -ExpectedCoreSha256 d70fcfdc41b6d15829227f1cee60130ddad3f7f82cc11c7a2fcbf8d5f700fb09 -OutputRoot <empty-directory>
```

Core's two approved dependency DLLs must be beside it. The verifier checks the
independent Core pin, producer record, exact TypeManagement source set/hashes and
dependency bytes before building a private project. It uses the existing trusted
NuGet cache with package sources cleared; this is not a clean-cache qualification.
The central versions, generated lock, copied configuration and complete managed
output hash inventory are retained. No package dependency or shared build file is
changed by this checkpoint.

For Ubuntu replay, use the pinned native `dotnet vstest` on the generated
`bin/Release/net10.0/CIS.Core.BusinessCases.dll`, with `--logger:trx` and an empty
`--ResultsDirectory:<directory>`. The runtime configuration disables roll-forward.
Both original TRX reports pass the repository's strict `Read-NativeTrx` reader.
Per-case output also proves the executed runtime, OS, process architecture and
loaded Core hash; the verifier checks the named methods and their row counts.
The checkpoint records the exact native case names, output hashes and final
replay checks. No result is inferred from a test label or an empty project.

Two controls reject an incorrect Core pin and an altered module-source producer
record before building. Formatting passes. The initial failed story expectation
is retained: the existing contract classifies a missing selected file as
`InvalidInventory`; a corrupt image is `AssemblyLoadFailure`. A subsequent native
command quoting failure and a report-grouping error are retained as superseded
attempts and excluded from final results.

This commit owns only Type Manager tests, verifier and evidence/docs. The shared
checkout contains coordinated logging edits; these tests ran against the earlier
frozen Core, not a new combined working-tree build. The latest combined Developer
result remains separately dated in the implementation status. Final exact-package
replay, complete acceptance mapping, mutation, architecture and maintainability
qualification remain open.
