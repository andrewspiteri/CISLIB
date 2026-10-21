# Independent replay of automatic03 rollback/filter omissions

This is an authored verifier and execution plan. No new native build, test, mutant outcome or release pass is claimed by these notes. Root owns native execution and final integration. The preserved four manual reproductions already show that native Stryker's aggregate compiler rollback is not an adequate individual disposition.

The immutable automatic03 raw report contains 320 selected `HealthStateCoordinator` mutants: 196 Killed, nine Survived, nine Timeout, three NoCoverage, 35 CompileError and 68 Ignored. Its strict tool-classified subset score remains **196/217 = 90.3225806451613%**. Native Stryker 4.16.0 reports 94.47% because it also credits the nine timeouts; this supplement never credits a timeout. Original statuses, report bytes and scores remain unchanged.

The completed four manual native observations are recorded in `planning/healthcheck/evidence/HC12/qualification-next/compiler-collateral.json` and retained under `.artifacts/healthcheck/qualification-next/compiler-collateral-01/`. IDs 2089, 2202, 2204 and 2221 each compiled independently and failed their intended existing public capacity, synchronous disposal or measurement-limit cases. Original source and binary identities, 1,284 source inputs and all 650 baseline cases were restored. These separate observations justify replaying all 103 omitted variants; they grant no automatic score adjustment or blanket exclusion.

## Native entry point to integrate

New default-compiled source:

`implementations/csharp/eng/CIS.DependencyChecker/HealthcheckQualification/Mutation/Rollback/HealthcheckRollbackMutationVerification.cs`

Namespace/class: `CIS.Verification.HealthcheckRollbackMutationVerification`.

Root should register `healthcheck-rollback-mutation` in the existing Healthcheck command predicate and add this explicit switch branch:

```csharp
"healthcheck-rollback-mutation" => await HealthcheckRollbackMutationVerification.RunAsync(args[1..]),
```

No project or Compile link changes are needed. No new package, runtime, test family, production change, analyzer suppression or mutation exclusion is introduced. SDK 10.0.401, framework runtime 10.0.12 with disabled roll-forward, existing locked dependencies and the existing 90% mutation threshold are retained. Root may use its existing private launcher to avoid canonical output directories.

Arguments:

```text
healthcheck-rollback-mutation
  --repo-root <canonical-repo-root>
  --report <immutable-automatic03-raw-mutation-report.json>
  --output <empty-isolated-output-directory>
  [--dotnet <dotnet-executable>]
  [--ids <comma-separated-exact-native-IDs>]
```

Suggested bounded guard: `--ids 2027,2030,2089,2202,2204,2221`. It covers an actual original compiler-error variant, an originally ignored block removal, and the four independently validated comparison controls. An explicit ID list always reports `SelectedSubset`, qualification false, and the number of unselected variants. A successful subset exit only means that selected observations and restoration completed; it never qualifies the 103-row supplement. The full run must omit `--ids` and select exactly all 35 CompileError plus all 68 Ignored variants from the pinned raw report.

## Pins and source application

| Input | SHA-256 |
| --- | --- |
| `.artifacts/healthcheck/qualification-next/automatic-state-windows-03/native/reports/mutation-report.json` | `cf22c436cae175745414562918318b2e3cd23cb5e307e62c4c4cba9a448aae02` |
| `implementations/csharp/src/CIS.Core/Healthcheck/Internal/State/HealthStateCoordinator.cs` | `7e1462309c085cf952cf0bff034f37eda57108165a1ac7f348fd84142e870c1a` |
| `implementations/csharp/src/CIS.Core/Healthcheck/Pipes/HealthProbeClient.cs` | `e022538f8c4694b3b772625d58886ba9897b6fb8f075a365faf08887878f2f54` |

The verifier verifies these exact raw pins before copying or applying a mutation. It requires raw report `source` to equal the production source exactly, validates all original counts and distinct IDs, and uses the native one-based line/column, end-exclusive spans. UTF-16 string offsets match Roslyn's source coordinates; original source bytes are retained separately. Each replacement is applied alone at its exact location, including multiline blocks. There is no heuristic text search, combined instrumentation, adjacent mutant application, or assumption that all members of an old rollback group fail to compile.

`SourceInventory` captures actual maintained C# sources/tests/configuration, contracts, conformance, C# compatibility and the relevant root files. Every input is hashed and checked when copied into one reusable isolated `output/source` tree. The source copy contains no `.git`, canonical `bin`/`obj` or old native outputs. Artifacts and unrelated planning notes are outside the selected source guard. The complete selected path/hash set, including additions and deletions, is compared before every row, after restoration and against canonical inputs at the end.

## Baseline, builds and retained evidence

The unmutated source copy first undergoes locked restore, a Release `--no-restore --no-incremental -maxcpucount:1` unit-project build, and the complete existing filter:

```text
FullyQualifiedName~CIS.Tests.UnitTests.Healthcheck|FullyQualifiedName=CIS.Tests.TestAssemblySetup.SchedulingMatchesRequestedMode
```

Baseline case count is observed dynamically (650 in the retained current candidate); no authorized new cases are excluded. Every selected test must pass, the real Scheduling case must pass once, and native output must confirm `CIS mutation scheduling: serialized`. The same exact native case IDs, names and methods are required for any subsequent kill/survival claim and final restoration. No empty, skipped or incomplete native run receives a kill.

Each row restores exact source bytes, validates all source inputs, writes the one mutation, retains its changed source and patch hashes, and freshly builds the entire referenced Core/unit project. The initially locked restore is reused through `--no-restore` builds; lock/config inputs remain guarded. Shared compiler/MSBuild servers are disabled and `CisMutationTesting=true` is set for both build and execution. Build/restore commands have a 180-second process deadline. Tests have a 55-second native session limit and a 60-second outer process deadline. All deadlines are bounded and receive no kill credit.

For every successful build the actual built `CIS.Core.dll` hash must equal the copy beside the executing native unit assembly. Actual Core and test DLLs/PDBs, test dependency/runtime manifests, hashes, assembly names and module-version IDs are retained per row before executing tests. Dependency selection must name Core and runtime policy must pin 10.0.12 with roll-forward disabled. Healthcheck unit cases run in-process and have no child fixture/probe closure; the selected code is loaded from this fresh unit output. Test invocation/process logs and native TRX retain actual selected IDs, messages and source stacks. Command requests are written before process launch, including deadlines and environment overrides. The existing ProcessRunner does not return partial stdout/stderr on an outer timeout; that limitation is recorded explicitly rather than fabricating a native result.

Exact original state bytes are restored in each row's `finally`. One final unmutated locked restore, fresh full referenced build and complete native baseline run execute in the outer `finally`, including after earlier failures. Actual final Core/test binary hashes must equal their baseline identities, final native IDs must match, copied sources must match, and canonical input/report hashes must remain unchanged. No command builds or writes canonical checkout outputs.

## Supplemental dispositions and denominator

`IndependentCompileError` requires a failed standalone build with actual `error CSdddd:` diagnostics located in the one changed `HealthStateCoordinator.cs`. No native tests execute for that row. The exact diagnostics remain in its receipt/log. Under the unchanged warnings-as-errors policy, actual C# compiler diagnostics, including compiler warnings promoted to errors, are observed compilation failures. Analyzer, MSBuild, dependency, launch or unrelated-file failures remain unresolved and do not exclude a mutant.

`Killed` requires native exit 1, the complete exact baseline case set, passed serialized Scheduling, no run/infrastructure/timeout error, and actual failed Healthcheck cases. Each failed case must show an assertion in its actual native Healthcheck test source stack or an unexpected managed exception from actual Healthcheck production source with that test stack. Source stacks, complete failure messages and case identities are retained for review. This general full-closure replay does not substitute a generic nonzero exit, crash, failed launch, timeout or missing fixture for observed test detection. Unsupported failures stay `NativeFailureUnresolved`/`InfrastructureFailureUnresolved` rather than earning credit. `Survived` requires the complete case set and all native cases passing.

Compiled test deadlines/cancellation are `TestTimeout` and earn zero kills. Build deadlines, source/infrastructure failures and unknown native results remain unresolved. No Ignored block removal is treated as equivalent merely because Stryker filtered it. No production assertion is removed, no analyzer policy is relaxed, and no whole mutant kind is excluded to improve the score.

The summary preserves the original 217 tool-classified testable mutants and adds observed individual outcomes by exact original ID. Its conservative combined denominator is **320 minus independently observed source compiler errors**. The numerator is **196 plus supplemental native kills**. Original and supplemental survivors/timeouts/uncovered cases, unselected rows and all unresolved variants receive zero kill credit; only independently demonstrated source noncompilation can leave this denominator. Counts for supplemental testable, compiler-error, unresolved and unselected variants are explicit. A full supplement can pass its unchanged 90% State threshold only after all 103 dispositions are observed, baseline/restoration and canonical guards pass, and that conservative score is at least 90%. Original Stryker report/statuses remain immutable. This establishes a reconciled State subset only; automatic qualification of remaining Healthcheck sources and the other release gates remain separate work.

The only execution authority here is the user's continued parallel qualification request. No external review/transmission, production change, publication or package release is authorized or performed by this verifier authoring task.
