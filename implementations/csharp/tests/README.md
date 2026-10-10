# C# test families and shared support

The solution has five MSTest/VSTest projects and one nonpackable shared-support library. Dependencies are centrally pinned, private to test projects and locked per project.

| Project | Responsibility | Current execution |
| --- | --- | --- |
| `CIS.Core.UnitTests` | Deterministic Core behavior and test-support checks. | Severity parsing tests cover names, abbreviations, trimming, absence, invalid values and culture independence. Two support tests exercise cleanup isolation and independent stream drainage. |
| `CIS.Core.IntegrationTests` | Real consumers, streams, files and lifecycle. | Project scaffold; no cases yet. |
| `CIS.Core.ArchitectureTests` | Dependency and public-surface boundaries. | Ten foundation cases, including deliberately invalid project/package/framework/assembly fixtures. Type/API rules follow real Core types. |
| `CIS.Core.CompatibilityTests` | API/source/binary/configuration/behavior replay. | Project scaffold; no cases or historical releases yet. |
| `CIS.Core.BusinessCases` | Named developer/operator acceptance scenarios. | Project scaffold; no cases yet. |
| `CIS.Core.TestSupport` | Owned temporary directories, bounded child processes and managed metadata inspection. | Used by actual foundation tests and the development checker. No Core or NuGet dependency. |

Mutation is the sixth family under [verification/mutation](../verification/mutation/README.md). It uses a separately pinned development CLI and is not another C# test project. WP01-S03 qualifies the pinned coverage, mutation and API compatibility tools against genuine severity parsing and public compatibility controls; see [toolchain qualification](../../../docs/architecture/csharp-toolchain.md). Full logging and historical compatibility qualification remain pending.

Run `pwsh -File eng/verify.ps1` from the repository root. It runs the available UnitTests/ArchitectureTests and rejects empty or skipped results for those runs. The three empty families are not executed or counted as passing. Release verification still rejects incomplete evidence.

Run individual populated projects from `implementations/csharp` after Release build and Core packaging:

```powershell
dotnet test tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj -c Release --no-build --no-restore
dotnet test tests/CIS.Core.ArchitectureTests/CIS.Core.ArchitectureTests.csproj -c Release --no-build --no-restore
```

Architecture tests need the Release checker and the current locally packed Core package. Negative fixtures use owned temporary directories or copied assets/packages; they never modify the legitimate shipping source or candidate.

Test-only fake clocks, controlled writers and complete candidate-process/replay support will be added when their behavior exists, under WP01-S04 and later stories. The current process helper has finite deadlines, argument-list escaping and concurrent independent stream capture.
