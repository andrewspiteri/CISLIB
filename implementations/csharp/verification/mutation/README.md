# Mutation verification

For new Healthcheck recovery candidates, use the [grouped native route](healthcheck/README.md).
Its compact candidate-bound receiving rules supersede prospective per-file proof
catalogues. Historical commands and reports below retain their original scope.

The old Healthcheck Union/Shared, critical/rollback/omission drivers and their
workflow tests require an explicit rebuild with `-p:CisHistoricalVerification=true`.
They are excluded from default checker/test compilation after the grouped native
route was proved. Current product tests, child propagation controls and release
families remain active. Independent historical diagnostic projects/templates are
manual tools; this property does not change their source or reinterpret reports.

Mutation is the sixth family. Stryker is a pinned development CLI restored through the language-local tool manifest and dedicated tool source mapping. Core, its lockfile and its packed dependencies contain no Stryker reference.

Run from the repository root:

```powershell
pwsh -File implementations/csharp/eng/verify-toolchain.ps1
```

The driver copies maintained source into a unique `.artifacts/csharp/toolchain/<run>/source` directory and uses fresh CLI and NuGet caches. Stryker **4.16.0** runs on the explicitly selected .NET **8.0.31** tool host with roll-forward disabled, mutates the genuine Core severity parser with the `Complete` mutation level, and runs its MSTest cases through **VSTest** against `net10.0`. Local JSON/HTML reporters are enabled; no dashboard reporter is selected.

[stryker-config.json](stryker-config.json) is the retained smoke scope, not a future full-release mutation matrix. The parser run remains a smoke scope. Core now contains logging/provider and other module implementations; their full shipping scope and materially different targets must be covered by the WP07 release matrix.

The driver rejects zero valid mutants and checks `Killed / (Killed + Timeout + Survived + NoCoverage)` at 90% or above. Every testable mutant must be killed or explicitly diagnosed; native noncompiling/ignored mutants require a source/tool-bound review in `smoke-dispositions.json`. A changed source hash, tool pin or unexpected exclusion fails qualification. No excluded mutant receives kill credit. Native reports remain immutable. Source hashes must match before/after mutation, followed by a fresh unmutated build, pack and shipping dependency check in the isolated copy.

Read [toolchain qualification](../../../../docs/architecture/csharp-toolchain.md) for reports, prerequisites and retention limits. Retain the complete local run directory; generated caches and packages are ignored by Git.

## Embedded Microsoft adapter

`microsoft-logging-config.json` selects all three `Logging/MicrosoftLogger/*.cs` source files within CIS.Core at Complete mutation level and runs the actual provider unit tests. `eng/verify-microsoft-logging.ps1 -Mode Mutation` executes it in a maintained-source copy beneath the ignored .artifacts folder, with pinned tool/runtime and local JSON/HTML output. Integration child processes are measured separately by coverage; this invocation does not claim to inject mutants into those children. Native non-killed/ignored/noncompiling observations keep their statuses, without kill credit or automatic exclusions. A below90% score stays a failed mutation gate. Full source-bound dispositions, gap fixes and both recorded platform lanes remain WP07 work.

## Full Core qualification attempt

`core-release-config.json` selects every C# source file in CIS.Core at Complete level and uses the unit project through VSTest. Run in a fresh isolated checkout, from `src/CIS.Core`, with the pinned Stryker 4.16.0 DLL explicitly hosted on .NET 8.0.31:

```text
dotnet exec --fx-version 8.0.31 --roll-forward Disable <Stryker.CLI.dll> --config-file ../../verification/mutation/core-release-config.json --output <new-output> --skip-version-check --log-to-file
```

The test filter excludes only IncomingMeasurementsTests, InterpolationMeasurementsTests and LoggingReviewMeasurements, which record performance observations. Their unmutated checks remain in Developer verification; production mutation selection has no exclusions. For a direct invocation, set `CisMutationTesting=true` in that child process environment so the shared test assembly compiles with `DoNotParallelize`; include the SchedulingMatchesRequestedMode control in baseline execution. Restore the previous environment afterwards. Process concurrency does not serialize methods inside a test host. Trace logging is a command-line flag in the pinned tool. Source, config, tool and native reports must be retained independently of the mutable checkout.

This config selects the Core assembly only. It does not inject mutations into child processes or the separately packaged CIS.TypeManager.Build helper. A successful native score alone cannot qualify either missing scope. Review every non-killed/ignored/noncompiling mutant, including whole-method compiler rollbacks, against the exact source/tool inputs. Retain the stricter repository score above; do not credit timeouts as kills or waive the 90% threshold. Current run outcomes and gaps belong in the dated release qualification record.

`frontend-interpolation-config.json` is a separate six-file diagnostic scope: ring storage, batch ordering, owned interpolation capture/rendering, the handler and interpolation entry points. Its functional test closure now includes IncomingRingBufferTests, IncomingBatchIndexTests, InterpolationTests, IncomingEventQueueTests, IncomingFrontendTests, IncomingRuntimeTests, IncomingStartupTests and IncomingLifecycleTests, plus the scheduling control. This broadens test execution; it does not add the runtime queue/control source to mutation selection. Keep its native score distinct from the all-Core attempt and preserve the exact selected paths and test filter with its reports.

The [19 October triage](../../../../planning/qualification/logging-release-2026-10-19/mutation-triage.md) found false survivors when Stryker 4.16.0 captured coverage with MSTest's four-way method parallelism enabled. Its first isolated serialized rerun detected 33 old survivors with unchanged assertions but still failed at strict 74.609375%. The subsequent maintained harness and boundary-test changes are recorded in [mutation fixes](../../../../planning/qualification/logging-release-2026-10-19/mutation-fixes.md). Earlier native reports remain unchanged.

## Maintained serialized diagnostic

From `implementations/csharp`, restore the pinned tool and select a new output directory:

```powershell
dotnet tool restore --configfile eng/Tools.NuGet.config
$mutationRun = [guid]::NewGuid().ToString('N')
dotnet run --project eng/CIS.DependencyChecker/CIS.DependencyChecker.csproj -c Release --no-launch-profile -- logging-mutation --repo-root ../.. --output-root "../../.artifacts/csharp/logging-mutation/$mutationRun"
```

The C# driver copies and hashes maintained C# inputs, shared conformance cases, compatibility fixtures and the root README into an isolated source directory. It pins SDK 10.0.401, target runtime 10.0.12 and Stryker 4.16.0 on the explicit 8.0.31 host. It sets `CisMutationTesting=true` only on its children; test targets define `CIS_MUTATION_TESTS` and compile the shared assembly with `DoNotParallelize`. Ordinary Developer builds retain four-way method parallelism. The native scheduling test checks the requested environment against the compiled assembly attribute and emits a marker required in baseline/restored TRX.

The driver requires passing, nonempty baseline/restored tests, unchanged source bytes, exact selected source membership and source text in the native report. It retains native streams and checks known killing mutations for caller capture, consumer rendering, batch sorting and closed admission. `results.json` reports `Killed / (Killed + Survived + NoCoverage + Timeout)` with the unchanged 90% threshold. Timeouts, ignored blocks and compiler failures receive no added kill credit. A successful `diagnosticGatePassed` is scoped to these six files; `exclusionsReviewed=false` prevents treating the driver as a complete release disposition or shipping matrix.

The existing parser smoke runner requests this mode for its Stryker child, and the embedded adapter mutation runner requests it in its isolated mutation branch. Their historical results are not retroactively requalified by this code change. Standalone Type Manager mutation preparation already generates an explicitly nonparallel test assembly.

## Full shipping execution

The same C# driver provides complete assembly selections, using a new output directory for every invocation:

```text
dotnet run --project eng/CIS.DependencyChecker/CIS.DependencyChecker.csproj -c Release --no-launch-profile -- core-mutation --repo-root ../.. --output-root <new-core-output>
dotnet run --project eng/CIS.DependencyChecker/CIS.DependencyChecker.csproj -c Release --no-launch-profile -- build-helper-mutation --repo-root ../.. --output-root <new-helper-output>
```

Run from the language root. When executing independent scopes concurrently, build the driver once and invoke its compiled DLL; do not concurrently build the same project output. If another job will build the solution or run a Developer/Release checkpoint, first freeze the driver's complete runtime output in a separate directory and invoke that copy. An active driver in the canonical output can hold its dependency DLLs open on Windows. Each command copies its own source snapshot. Full selections reject method/mutation exclusions and verify every shipping source file against the native report, including files with no generated mutants. Shared contract inputs are copied for the complete functional baseline. Restore uses locked mode, and all children inherit `RestoreLockedMode=true`.

The Core selection retains the complete functional unit scope with the three declared measurement classes omitted. `disable-bail=true` avoids requesting early test cancellation after the first failing assertion; source coverage, Complete mutation level and thresholds stay unchanged. The helper command generates an isolated test project against the actual build-tool project and pinned SDK MSBuild references. Its canonical cases remain under `build-helper/`.

The [shipping inventory](../shipping-projects.json) lists both DLLs in the single CIS.Core package and both required lanes. [Full shipping qualification](../../../../planning/qualification/logging-release-2026-10-19/shipping-qualification.md) records actual results, compiler/filter reviews and child activation controls. Neither a native score nor this inventory supplies missing Release gates. The Release entry point now runs its implemented families and accepts a new `-OutputRoot` for retained evidence, while preserving refusal for unqualified requirements.
