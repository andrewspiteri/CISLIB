# C# recovery progress — 1 November 2026

## Closure continuation after the reviewed checkpoint

The sections below this continuation retain the published Source35 checkpoint. Current work follows the supplied 1 November closure goal; the [canonical recovery plan](verification-remediation-plan.md) records the latest execution status. Full regression, both full-run 300-second targets and current module acceptance remain unmet.

The recorded pressure failure is resolved at the fixture boundary. The original deadline reply reported pending0, accepted2, written2 and queueFull3, with no pending output boundary. The fixture now waits for an actual pending writer flush and publishes one consistent observation. A contended four-child comparison and the complete 20-case Candidate logging class pass with the original accounting, pressure, stop and late-drain assertions preserved.

Measured reductions since the checkpoint are:

| Work | Executed evidence |
| --- | --- |
| Compiler preparation | Source39 passed all eleven stages in397.1862427 command seconds /432.8102881 including the observer, versus Source38's571.0903355 /584.388922. It reused47 matched compiler outcomes, made zero new Csc calls and captured four current SDK argument sets. The entire wall-time reduction also includes changed build state. |
| Stop fixtures | All162 selected cases passed in14.4477671 native-command seconds. Actual child launches fell from84 to61, preserving45 physical functional cases and116 readers. |
| Dependency checks | Fresh private-project validators run in-process with private writers; explicit CLI parity and compiled-checker/mutation controls remain. The139-case affected selection passed in79.1719713 command seconds. |
| Architecture | All18 selected native cases passed in108.3299806 command seconds. Five real Core compiler calls, one foreign SDK build and17 inspections remained; redundant Core build/pack work was removed. The observer's incorrect zero-restore expectation remains a recorded failure, with a separate read-only reconciliation. |
| Fast loop | Source39 passed5,190 cases in each phase:29.739007 seconds warm and49.2243705 seconds with an unchanged build. Its retention fixture avoided192 redundant timestamp writes per phase while retaining248 checks. |

Source40 adds one conditional `RestoreConfigFile` default in the C# build props. Ordinary incremental restore had added a user-level NuGet config path to prepared assets metadata. Three actual SDK queries verify the canonical default, copied props without an adjacent config, and an explicit caller override. Source40 has1,605 maintained inputs, manifest `05a540234b3d0b021db318ede59c9466630a583a474a5a96c4923a53f873ad9c`; matching preparation passed all11 stages in695.2039822 command seconds /721.2343773 whole, with47 reused compiler outcomes,0 new Csc calls and4 current captures (receipt `e4d31b5292895558ac3bc52d48178f1b82b356940039fd70073f16a285b175a4`). The shared props change triggered a rebuild; all56 affected dependency cases passed in57.1088835 command seconds /96.7213849 whole (receipt `8d0342d2bef469a6627c06affc53b5e5a854826fec3aca7ad9bf32ee754bf256`), with unchanged source and all13 prepared output roots. Fresh Source40 Fast measurements pass both targets: **27.6391419 seconds warm /43.6666978 seconds unchanged build plus tests**, with all5,190 cases passing in each run, eight fresh native hosts and all128 inspection-bound inputs unchanged after both supported commands (receipt `c5cbacb42d8515bbb9e2105c8380e3c7ab9bfd46e07774d71b6f99bb4592562e`;115.3059851 seconds including the observer). The Source39 measurements above retain their original candidate identity.

The maintained grouped-mutation receiver now accepts candidate-bound policy and source/tool identities without another campaign-specific C# receiver. Focused receiver controls pass; a fresh native pilot, complete current mutation, coverage, package/API/compatibility and mandatory platform checks remain required. Historical accepted09g results do not qualify this candidate. Windows prerequisites and real future Heartbeat/startup-provider integration remain separately identified.

All current SDK, build and native execution uses the Ubuntu server, with SDK10.0.401/runtime10.0.12 and normal CPU/GC settings. The1.5-GiB full/performance owned limit,2-GiB free reserve and separate mutation limits remain enforced. The closure goal's one permitted longer correctness diagnostic remains unused; a result above300 seconds would still fail the performance target.

The current local evidence is retained outside Git: [source manifest and archive identities](../../.artifacts/csharp/performance-20261031/source40-freeze.json), [exact measured source ZIP](../../.artifacts/csharp/performance-20261031/batch40-source.zip), [matching preparation](../../.artifacts/csharp/recovery/remote/linux/performance31-preparation40-control/finished.json), [affected56](../../.artifacts/csharp/recovery/remote/linux/performance31-source40-affected-results/finished.json), and [Fast40](../../.artifacts/csharp/recovery/remote/linux/performance31-fast40-results/finished.json). The ZIP preserves measured working-tree bytes; normal Git text normalization means a fresh checkout must establish its own matching identity. These local payloads are not supplied by a Git clone.

## Published Source35 checkpoint

The ordinary Fast loop meets both current timing targets. The full regression target remains unmet, and an unfinished Integration log contains a real pressure-boundary failure. This checkpoint preserves that state for continued work; it does not claim complete recovery or release qualification.

The base commit before this checkpoint is `153c898bd2fdfe02801a306fca5149d0acb05ba3`. The current measured candidate is **Source35**, with 1,596 maintained inputs and manifest SHA-256 `cf4e961d1e40caf3897b0b13f8c77d461b6e66f610578c210d9d35d47865813d`. Its nine-file delta is relative to Source34, rather than the entire checkpoint diff. Current compilation and native measurements were performed on Ubuntu with SDK 10.0.401 and runtime 10.0.12, default CPU/GC settings, and no Windows SDK or test execution for Source35.

## Current measurements

| Check | Actual Source35 result | Qualification |
| --- | --- | --- |
| Warm supported Fast command | **29.7584908 s**, 5,190 passed | Meets 30 s |
| Unchanged build plus Fast tests | **40.5645642 s**, 5,190 passed | Meets 60 s |
| Matching preparation | All 11 stages passed; **552.9179129 s** command, 563.9718712 s whole | Current source, package, inspection and native inputs bound |
| Selected verification | **127 passed**: Verification106 + Integration21; 80.4817825 s shared native, 97.5761693 s whole | Current affected checks; no full-run credit |
| Full regression | **315.5101186 s** pure command; **304.9384419 s** native; 323.9188325 s whole | **Fails both 300 s limits and complete scope** |

The full run finalized seven hosts with **5,898 physical / 5,894 logical passing cases**, zero failures or skips in those finalized reports. Residual Architecture148, Integration2,152 and Verification1,257 did not finalize: **3,557 required cases remain unqualified**. The unfinished Integration console also records a failure in `PackedPipePressureIsBoundedAndStopReportsActualDeadlineBeforeLateDrain(True)`: the pending-count assertion at line237 was false. Therefore the full attempt must not be described as having zero observed failures.

The complete required route is 9,451 logical / 9,455 physical cases in ten fresh hosts: all original9,439 cases, 12 partition controls and four explicit extra Fast host-identity controls. No historical passes are substituted, and no raw reports are rewritten.

## Delivered work and evidence

The four-batch recovery plan now has these distinct statuses:

1. **Ordinary development:** four existing Fast owners, supported independent commands, current exact scope and both timing targets pass. The full five-minute target remains open.
2. **Repeated work:** compiled-method partitioning preserves partial or differing methods in their original owners. Physical fixture batching retains per-row inputs and assertions. Source35 executes the same29 metadata variants and40 real validators inside one physical SDK input entry/exit boundary, with29 distinct private input paths. All11 per-request prepared-input guard cases pass. The same84 diagnostic cases span55.9318 s versus151.719463 s on Source34; these spans are not whole-command timings. The full runner releases heavy owners after residual Unit completes while retaining one shared clock and at most four active projects.
3. **Grouped mutation — historical accepted09g:** the fixed whole-group Linux pilot/runtime/HTTP plus Windows Pipes recipe qualified **2,754 / 3,026 = 91.0112%** conservatively. All346 misses received bound reviews; all99 timeouts retained zero kill credit. This is accepted09g evidence, not a new Source35 mutation run or blanket release acceptance.
4. **Acceptance/CIS — historical accepted09g:** exact-package/API/compatibility25, observations/stress52, coverage7,973 and CIS cadence10 checks passed in their declared scopes. Core/helper coverage qualified **96.494% lines / 93.705% conservative branches**. Broader release/platform obligations and real Heartbeat/startup-provider integration remain pending.

Source35 also removes Candidate logging's class-level serialization after source independence review and a passing isolated current21-case selection. Its real full-run pressure failure is unresolved; that review and isolated pass do not override the failure. Assertions, actual GC/lifetime/concurrency checks, mutation-fresh paths, product clocks, the shared300-second full limits, owned1.5GiB ceiling and free2GiB reserve remain unchanged.

## Reproduce and continue

The repository-root supported commands are:

```powershell
pwsh -NoProfile -File eng/test.ps1 -Fast -Configuration Release -NoBuild
pwsh -NoProfile -File eng/test.ps1 -Fast -Configuration Release
pwsh -NoProfile -File eng/verify.ps1 -PrepareOnly -OutputRoot <fresh-preparation>
pwsh -NoProfile -File eng/regression.ps1 -PreparationRoot <matching-preparation> -OutputRoot <fresh-output> -NativeWorkers 4 -NativeBudgetSeconds 300 -PreparationTimeoutSeconds 900
```

Next, diagnose the actual pressure failure and the three timed-out owners from their retained normal-console progress before another source batch or full attempt. Current selected127 confirms the metadata change but does not close full correctness or performance. Do not raise budgets, sweep worker profiles, weaken assertions or repeat a measurement without a concrete cause or declared hypothesis.

The [canonical recovery plan](verification-remediation-plan.md) records current guidance and historical checkpoints. Local ignored evidence includes [Fast35](../../.artifacts/csharp/recovery/remote/linux/performance31-fast35-results/finished.json), [preparation35](../../.artifacts/csharp/recovery/remote/linux/performance31-preparation35-control/finished.json), [corrected selected127](../../.artifacts/csharp/recovery/remote/linux/performance31-selected35-corrected-results/finished.json), [failed Full35](../../.artifacts/csharp/recovery/remote/linux/performance31-regression35-control/finished.json) and the [accepted09g composite](../../.artifacts/csharp/recovery/remote/linux/final-composite09g-complete/acceptance/results.json). Those payloads are retained separately and are not supplied by a Git clone. Original failed attempts, timing misses and observer/preentry failures remain unchanged. Successful disposable outputs were retired only after complete Windows byte/hash custody; unresolved evidence remains preserved.
