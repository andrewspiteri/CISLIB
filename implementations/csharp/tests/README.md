# C# test families and shared support

The solution has five MSTest/VSTest projects and one nonpackable shared-support library. Dependencies are centrally pinned, private to test projects and locked per project.

| Project | Responsibility | Current execution |
| --- | --- | --- |
| `CIS.Core.UnitTests` | Deterministic Core behavior and test-support checks. | Implemented identity/configuration, capture, scopes, rendering, queue/file/lifecycle/provider and Type Manager cases, plus deterministic support and focused-runner rejection controls. |
| `CIS.Core.IntegrationTests` | Real consumers, streams, files and lifecycle. | Native logging discovery, configuration, capture, queues, file/console output, flush/stop and provider cases; ordinary DiscoveryHost dependency builds and copies with the project. |
| `CIS.Core.ArchitectureTests` | Dependency and public-surface boundaries. | Data-driven foundation/contract cases, including deliberately invalid project/package/framework/assembly fixtures, evidence-validator controls, startup catalog/fixture checks and reference-only API inspection. Type/API rules follow real Core types. |
| `CIS.Core.CompatibilityTests` | API/source/binary/configuration/behavior replay. | Project scaffold; no cases or historical releases yet. |
| `CIS.Core.BusinessCases` | Named developer/operator acceptance scenarios. | Implemented logging and Type Manager stories; selected cases may require fixture/package preparation. |
| `CIS.Core.TestSupport` | Owned temporary directories, bounded child processes and managed metadata inspection. | Used by actual foundation tests and the development checker. No Core or NuGet dependency. |

Mutation is the sixth family under [verification/mutation](../verification/mutation/README.md). It uses a separately pinned development CLI and is not another C# test project. WP01-S03 qualifies the pinned coverage, mutation and API compatibility tools against genuine severity parsing and public compatibility controls; see [toolchain qualification](../../../docs/architecture/csharp-toolchain.md). Full logging and historical compatibility qualification remain pending.

For everyday edits, run `pwsh -File eng/test.ps1 -Project UnitTests -Class <class>` from the repository root, or select `-Category` / native `-Filter`. This incrementally builds the selected project and rejects empty, failed, skipped or inconsistent native results. [Ordinary build and focused test guidance](../../../docs/architecture/csharp-focused-tests.md) describes SDK/runtime prerequisites, output, timing and fixture limits.

Run ordinary unit tests directly from `implementations/csharp`, without prior packaging or Developer verification:

```powershell
dotnet test tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj -c Debug --filter 'FullyQualifiedName~Severity'
dotnet test tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj -c Debug --list-tests
```

Architecture and some process/package tests have explicit fixture prerequisites. The focused launcher does not silently run the entire checkpoint to create them. See the linked guidance for standalone dependency inspection. Negative fixtures use owned temporary directories or copied assets/packages; they never modify the legitimate shipping source or candidate.

Run `pwsh -File eng/verify.ps1` at coherent checkpoints. Developer executes the populated unit, integration, architecture and business families; compatibility without historical publications is not counted as passing. Release still rejects incomplete evidence. Existing mutation/API/coverage tools and remaining Python checks are part of the broader qualification path, not prerequisites for every unit edit.

The entries below record historical roadmap additions; their statements about work then pending do not describe the current implementation status.

WP01-S04 adds manual UTC/monotonic clocks and timers, explicit metadata/UUID sequences, bounded asynchronous signals/barriers and controllable writers. The process helper supports isolated child environments and stops owned children at finite deadlines. Nine new unit cases exercise these behaviors. Architecture tests add a genuine native-report positive baseline and six independent evidence rejection controls. Read [verification and source replay](../../../docs/architecture/csharp-verification.md) for manifests, required cases, report paths and source replay limits.

WP02-S01 compiler controls run before the architecture suite in Developer verification. Run `pwsh -File implementations/csharp/eng/check-startup-contract.ps1` before individual architecture execution. These controls compile a nonshipping API specification and consumers; they do not execute a metadata resolver or static startup implementation.

WP02-S02 adds native data-driven architecture cases for every portable event record and explicitly tagged value input. The validator is development-only and never captures, truncates or writes logs; runtime expectations remain for WP04/WP05. The reference-only compiler now checks both startup and event inputs.

WP02-S03 adds 45 admission-model and 28 drain-projection cases, one lifecycle-catalog integrity check and one exact reference-metadata inspection. These development-only validators do not execute factories, queues, shutdown, provider disposal or I/O. Future runtime expectations remain visibly unexecuted.

WP02-S04-T02 adds 43 publication format/integrity/append-only architecture controls (six positive, 37 expected rejections). Every control owns its inputs and asserts the validator leaves them unchanged. Their synthetic opaque artifacts never enter the actual publication ledger and do not qualify source/binary/runtime compatibility.

WP02-S04-T04 adds 20 deterministic wire-sample architecture controls for validity versus preserved meaning. The preceding `compatibility-samples` stage compiles six consumers and eight deliberately changed reference surfaces/consumers (22 expected compiler outcomes). These are nonshipping development samples; the actual cumulative compatibility family remains pending.

WP03-S01-T01 adds 45 native unit cases for genuine internal application identity discovery. Concrete `UT-APPLICATION-*` methods/counts are required in the case inventory; the full `UT-IDENTITY` group remains Planned until process, initialization and snapshot behavior is implemented. See [discovery guidance](../../../docs/modules/logging/application-identity-discovery.md).

WP03-S01-T02 adds 55 native cases for process/runtime facts, including real current-process metadata, UTC conversion, independent OS/process architectures, optional absence and per-read native failure handling. Twelve concrete `UT-RUNTIME-*` bindings require their native methods/counts. Family/architecture injections do not advertise execution support; see [runtime discovery guidance](../../../docs/modules/logging/runtime-metadata-discovery.md).

WP03-S01-T03 adds 37 native version/initialization cases with injected single-key variables, clock and UUID sources. Selected-invalid values retain their origin for later validation; no successful static publication is simulated. See [initialization guidance](../../../docs/modules/logging/initialization-identity.md).

Ten additional controlled cases verify bounded cleanup of an owned temporary directory after transient Windows sharing/lock violations. Other failures and persistent locks remain visible; the existing isolation and real child-timeout cases also execute.

WP03-S01-T04 adds 20 portable discovery-table cases and seven real child-process integration cases. The nonshipping [DiscoveryHost](fixtures/DiscoveryHost/Program.cs) and [portable driver](fixtures/DiscoveryHost/verify-process.py) also run exact-runtime discovery scenarios on Windows and Ubuntu 24.04 under WSL2. The populated integration family executes in Developer; full logging integration and Linux SDK/full-suite qualification remain pending. See [fixture guidance](../../../docs/modules/logging/discovery-fixtures.md).

WP03-S02-T01 adds 47 native controls for recognized input capture, selection provenance, typed/absent values, invalid-input retention and source reuse. The existing version selector shares the same implementation; the real discovery host now passes one captured source. [Input guidance](../../../docs/modules/logging/configuration-inputs.md) records scope.
