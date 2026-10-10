# C# test families and shared support

The solution has five MSTest/VSTest projects and one nonpackable shared-support library. Dependencies are centrally pinned, private to test projects and locked per project.

| Project | Responsibility | Current execution |
| --- | --- | --- |
| `CIS.Core.UnitTests` | Deterministic Core behavior and test-support checks. | Severity parsing tests cover names, abbreviations, trimming, absence, invalid values and culture independence. Eleven support tests exercise deterministic clocks/inputs/concurrency/writers, cleanup isolation and bounded child processes, including independent stream drainage. |
| `CIS.Core.IntegrationTests` | Real consumers, streams, files and lifecycle. | Project scaffold; no cases yet. |
| `CIS.Core.ArchitectureTests` | Dependency and public-surface boundaries. | Data-driven foundation/contract cases, including deliberately invalid project/package/framework/assembly fixtures, evidence-validator controls, startup catalog/fixture checks and reference-only API inspection. Type/API rules follow real Core types. |
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

WP01-S04 adds manual UTC/monotonic clocks and timers, explicit metadata/UUID sequences, bounded asynchronous signals/barriers and controllable writers. The process helper supports isolated child environments and stops owned children at finite deadlines. Nine new unit cases exercise these behaviors. Architecture tests add a genuine native-report positive baseline and six independent evidence rejection controls. Read [verification and source replay](../../../docs/architecture/csharp-verification.md) for manifests, required cases, report paths and source replay limits.

WP02-S01 compiler controls run before the architecture suite in Developer verification. Run `pwsh -File implementations/csharp/eng/check-startup-contract.ps1` before individual architecture execution. These controls compile a nonshipping API specification and consumers; they do not execute a metadata resolver or static startup implementation.

WP02-S02 adds native data-driven architecture cases for every portable event record and explicitly tagged value input. The validator is development-only and never captures, truncates or writes logs; runtime expectations remain for WP04/WP05. The reference-only compiler now checks both startup and event inputs.

WP02-S03 adds 45 admission-model and 28 drain-projection cases, one lifecycle-catalog integrity check and one exact reference-metadata inspection. These development-only validators do not execute factories, queues, shutdown, provider disposal or I/O. Future runtime expectations remain visibly unexecuted.
