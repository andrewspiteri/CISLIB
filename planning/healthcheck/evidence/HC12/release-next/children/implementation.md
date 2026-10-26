# Real child mutation propagation support

Authored against base `8fc2f9ccb78ba948f9794cfb4c0ea37cfd2956eb`. This note records source design and handoff; no native command, build, test, or Stryker run was executed by the authoring subagent. It supplies no mutation score or release qualification claim.

The installed pinned `dotnet-stryker/4.16.0/tools/net8.0/any/Stryker.Core.dll` contains the embedded resource `Stryker.Core.InjectedHelpers.MutantControl.cs`. Static PE/resource inspection confirmed `CaptureCoverage`, the lazy `ActiveMutant` field, `STRYKER_MUTANT_ID_CONTROL_VAR` environment selection, and the higher-priority `STRYKER_MUTANT_FILE` path. Prepare retains the exact tool DLL and resource hashes and copies the inspected resource text. It requires assembly version `4.16.0.0`; no tool or dependency was upgraded.

The real Healthcheck integration source has two child launch sites: HTTP host construction in `HealthcheckHttpTransportTests` and probe construction in `HealthcheckProbeProcessTests`. Both use `TransportTestSupport.StartInfo`. Prepare and audit inspect all maintained test C# paths containing `Healthcheck` and reject an unreviewed `Process.Start` or `new ProcessStartInfo` site. This is an explicitly bounded source review of the current launch sites, not a general C# call-graph analyzer.

`HealthcheckMutationChildSupport` loads the parent's actual Core through normal resolution and verifies its on-disk SHA/MVID against the loaded module. It copies the real fixture's normal output to a private stage keyed by test-host PID, exact Core SHA, and real executable. It replaces only Core/PDB in that private copy. No shared fixture output is overwritten, and the two Stryker test hosts cannot write each other's stages. All original/deployed file hashes are retained. The probe keeps its actual `CisHealthProbe` assembly identity and friend access.

Every launch receives a unique manifest and a frozen selector in a dedicated child environment variable. The parent process environment is unchanged. Both file-control and coverage-file variables are removed from the child's environment. The linked child module initializer validates the exact entry assembly, dependency/runtimeconfig pins, naturally resolved Core location, SHA/MVID, generated controller identity, selected `IsActive` behavior, actual dotnet executable SHA, and actual .NET 10.0.12 runtime/CoreLib SHA before real Main starts. It emits JSON files, preserving the actual programs' stdout/stderr protocols. A process-exit receipt follows; forced/abnormal exits or observer/staging failures invalidate the propagation audit and cannot silently supply a credited kill.

Native VSTest coverage capture does not observe child execution. This lane therefore requires explicit `coverage-analysis: off` and rejects an active parent `CaptureCoverage` flag. Every selected native test must run for each selected mutant. Child propagation receipts do not replace native Stryker reports, passing baselines, strict kill accounting, timeout classification, restoration checks, or final release gates.

## Coordinator edits

All implementation files are new under `implementations/csharp/eng/CIS.DependencyChecker/HealthcheckQualification/Mutation/Children/`; structural edits remain coordinator-owned.

1. Link `HealthcheckMutationChildProtocol.cs` and `HealthcheckMutationChildSupport.cs` into `CIS.Core.IntegrationTests`.
2. Link `HealthcheckMutationChildProtocol.cs` and `HealthcheckMutationChildObserver.cs` into each real `HealthcheckHttpHost` and `CisHealthProbe` project. Do not link the observer into the integration parent or shipping Core.
3. After constructing `start`, before adding its arguments in `TransportTestSupport.StartInfo`, add the exact hook: `path = CIS.Verification.HealthcheckMutationChildren.HealthcheckMutationChildSupport.Prepare(path, start);`.
4. Dispatch `healthcheck-mutation-children` to `HealthcheckChildMutationVerification.RunAsync`. The command class is tool-only; source-linked files use BCL APIs without Core, MSTest, or tool-project references.
5. Give the native Stryker configuration explicit `coverage-analysis: off`, `test-runner: vstest`, `target-framework: net10.0`, `configuration: Release`, and the actual integration `test-projects` entry. Preserve serialized mutation scheduling and existing thresholds.

Prepare syntax:

```text
healthcheck-mutation-children prepare --language-root <csharp> --config <actual-stryker-config.json> --stryker-core <4.16.0/Stryker.Core.dll> --dotnet <actual-dotnet-executable> --phase automatic --output <empty-session-root>
```

Set `CIS_HEALTHCHECK_MUTATION_SESSION` to that output's `session.json` for the actual native Stryker invocation. `automatic` allows only the initial uninstrumented baseline, ordered before the first instrumented launch. `--phase instrumented` forbids all uninstrumented launches. The command itself runs on the pinned target runtime; SDK 10.0.401 remains a source pin plus the coordinator's independently retained native SDK check.

Audit syntax:

```text
healthcheck-mutation-children audit --session <session.json> --output <separate-empty-audit-root> --require-kind both
```

For a finite known-mutant proof use `--require-kind http-host` or `--require-kind probe`, plus `--require-active <actual-native-mutant-id>`. Audit requires an actual selected instrumented child of each requested kind. Unknown baseline phases, missing/failed receipts, source/config/tool/runtime drift, dependency substitutions, or incorrect Core/selector identities fail the audit. Session and stage directories are retained as evidence; this helper deletes no source or output.

The coordinator must first compile the linked sources and execute one known HTTP mutation and one known probe mutation. Only actual native receipts can establish propagation or expose runtime/tool compatibility defects. No such result is asserted here.
