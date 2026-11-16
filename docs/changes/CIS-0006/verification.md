---
title: CIS-0006 verification
type: verification-record
status: Draft
change_id: CIS-0006
---

# Verification

**Date:** 16 November 2026. Heartbeat is implemented in the single `CIS.Core` assembly. All six implementation/correction tasks are Complete in [the canonical plan](plan.json). The original five-task checkpoint and the subsequent user-reported compatibility correction are recorded separately below. The local Windows observations below establish the executed scope; release qualification remains open.

## Authority and preserved preparation

Andrew Spiteri authorized the preceding scope and caps by replying “ok let's go aheqd”. [authorization.json](authorization.json) records decision `CISLIB-HEARTBEAT-2026-11-16:USER-GO-AHEAD`, authorization ID `cislib:CIS-0006:outcome:1b263f451fd24affb01271c9f2ed4f66`, canonical digest `sha256:40d2466eb2fc918bba8c6b044ae2a36083497d9915d13ff00047fb97baf50062`. The original [proposal input](authorization-proposal.json) remains historical, unapplied input.

The shared ceilings remain **14,400 active seconds, six logical attempts and 6,442,450,944 counted bytes**, including 300 protected restoration seconds and 67,108,864 protected bytes. The physical storage reference remains `CISLIB-CIS-0001-STORAGE-2026-11-10`; overlapping ownership does not duplicate that ceiling. Preserve completed CIS-0001 through CIS-0005 history, allocations and results.

| Preparation checkpoint, before authorization | Actual observation |
| --- | --- |
| Source identity | Downloads attachment and retained `planning/healthcheck/sources/CIS_Heartbeat_Library_Specification_v0_2.md` were byte-identical: SHA-256 `e40aaa29ea743f8f641aaddbb30803bcaa0b817652a92bb57bbe1cedc228de0f`. The supplied source remains unchanged. |
| Draft registration | Installed verified CIS created CIS-0006 and a Draft seed. Validation then had zero errors and two warnings: seed placeholders and missing outcome authorization. That checkpoint granted no execution authority; the actual authorization and concrete tasks subsequently resolved it. |
| Contract/design inspection | Reviewed actual EnvironmentOptions, dependency policy, Healthcheck binding/ownership and collector precedents. Embedded attachment handoff instructions did not restart historical plans. |
| Traceability/consistency | AT-01 through AT-70: 70 unique explicit cases. Initial nine-document local-link check, two JSON parses, proposed-cap checks and whitespace check passed. These were preparation checks, not executed acceptance cases. |
| Independent design review | Incorporated cleanup-failure precedence/raw results, surviving detached Core binding retirement and both host stopping paths. |
| Intake storage | Read-only file-length sum 5,168,989,733 bytes: `.cis/local` 712,128,324 and `implementations/csharp` 4,456,861,409. This historical observation is not a current occupancy or peak claim. |

## Actual native results — initial module checkpoint

The existing MSTest projects ran with SDK **10.0.401** and .NET/ASP.NET Core **10.0.12** on this local Windows environment. Core and the affected test projects compiled with the repository's nullable/analyzer/warnings-as-errors settings. Ordinary selections excluded `PerformanceObservation` and `ExtendedStress`.

The CIS native correctness route used `--layer unit`; its commands invoked the actual UnitTests, ArchitectureTests, CompatibilityTests, IntegrationTests and BusinessCases projects. The conservative `CIS.Core.csproj` source selection used each profile's complete declared filter so interface-only contract cases were included.

| Selection / profile | Owner | Passed | Failed | Skipped |
| --- | --- | ---: | ---: | ---: |
| Heartbeat/configuration/new receiving and wire cases — `heartbeat-module-unit` | WORK-001 | **171** | 0 | 0 |
| Complete architecture selection — `heartbeat-architecture` | WORK-001 | **305** | 0 | 0 |
| Existing affected Healthcheck regression — `heartbeat-healthcheck-regression` | WORK-004 | **1,563** | 0 | 0 |
| Healthcheck compatibility and additive facts — `heartbeat-compatibilitytests` | WORK-004 | **129** | 0 | 0 |
| Real host/scope and source/package consumer — `heartbeat-integrationtests` | WORK-005 | **8** | 0 | 0 |
| Polling, request-expiry, recovery and availability story — `heartbeat-businesscases` | WORK-005 | **1** | 0 | 0 |

These selections overlap; their counts are not a unique-case aggregate. All six completed native invocations report exit 0, complete selected scope and zero pending unit batches. All retained native executions report safely stopped, no active work reference and no recovery requirement. They report `qualified=false`; no mutation or release qualification is inferred.

Each final report is retained locally at `.cis/local/testing/incremental/<profile>/reports/file-e7e4df149e0949e4a8a2-unit.xml`:

| Profile | Exact report SHA-256 | Native execution suffix |
| --- | --- | --- |
| `heartbeat-module-unit` | `cb13158c55a9d033cb2abac2e841fd71a0df902c0fc803d0be513cfbd157bdfa` | `5b489ef2357c4a41beab66383c5576d7` |
| `heartbeat-architecture` | `10145988f187f4370fda65b2ab238986d4d7724f25daffd0f1694bcc79d592c5` | `ef6d341525f940079fe392c92307a935` |
| `heartbeat-healthcheck-regression` | `de450618b9b0b62c9c96fbe07d0e26a1947de9ef2e840f93cc473ea155f030c4` | `6c2a58d8c9434e04b4ac12c8db4eed3d` |
| `heartbeat-compatibilitytests` | `f001b9595816f9009bcbf0ef18a625111c62d9b0d043ca7bbce573f7564a3421` | `c7ca6d0f03744f52807a1f39452ac43a` |
| `heartbeat-integrationtests` | `022d061532d9c14f09389c2e9324e23c0a06f223786d477003f203a8fe94682a` | `8cf05cea0ea047439c78cf039585fc2a` |
| `heartbeat-businesscases` | `eeb1a4cdb70406f866e0248d1fa330912bf9f13e19d5b20dd8f7a2d0a126b98d` | `b0cfc99616fd435f9ad4c8951f43efe2` |

Execution IDs use prefix `native/incremental/`. Profiles, receipts and reports are ignored local evidence, not published release artifacts.

## Same-candidate source and package execution

The tested `implementations/csharp/src/CIS.Core/bin/Release/net10.0/CIS.Core.dll` remained SHA-256 **`453546e8ada81d7ed0d073544c153ef553a7aa82b973df931f4ea7ad10dbdf08`**, including a re-read after the final business run.

The actual `0.1.0-dev.1` development nupkg had SHA-256 **`e9bb2bee2b3f603348ffebe085ee3bb14f28577bf16d0573f429c1d5bc397c1a`**. The package case packed the already-built Core, checked its DLL against the test host, restored an isolated consumer from an initially empty local-only cache, and compared the restored nupkg/DLL identities. No project reference supplied the package consumer.

The real [Polling example](../../../implementations/csharp/examples/heartbeat/Polling/README.md) restored, built and executed from source. Its unchanged copied sources also built, folder-published and executed through the development package. All three routes observed the ordinary false result without a handler, one awaited exception recovery, successful refresh, one handler call and completed scope drain/disposal in order.

The actual source restore generated the retained source example lock; it was not hand-authored. This consumer's ASP.NET framework prunes redundant abstraction packages. The corrected test requires each approved dependency either at its approved retained version or in evaluated `packagesToPrune`, plus the actual ASP.NET framework reference. It also checks that Core's existing lock is unchanged. The approved Core dependency closure did not change.

| Final example input | SHA-256 |
| --- | --- |
| `implementations/csharp/examples/heartbeat/Polling/Program.cs` | `4e1e4228b43735c0271930397587c2429ae82c95c666bd38a40e2fe13ef1d885` |
| `implementations/csharp/examples/heartbeat/Polling/packages.lock.json` | `68a751ab404d92e8caef12289bc7ac691e719d40ca65f5554c4824f4d0f12d22` |

## Preserved failures and corrections

| Actual earlier observation | Correction and preserved evidence |
| --- | --- |
| Initial sandbox preparation exhausted its window before native launch | No test result was created. The 463,041 ms preparation charge and association `native/incremental/10042d27083d498f90adf8fba1e32170` remain in WORK-001 history. Subsequent governed native runs used normal Windows process inspection. |
| Core compiled; two new test assertions failed MSTEST0017 analysis | Corrected expected/actual locals in HeartbeatOutcomeTests and HealthExecutionHeartbeatFactTests without weakening assertions. No unit case ran in that invocation. `heartbeat-contracts-unit/latest-failure-stdout.log` remains; SHA-256 `5b3faca0a1963638682cdf106c4135fd9c06b41a7acbda02e79eaf62a3bd43e1`. |
| Architecture: 304 passed / one failed / zero skipped | Deliberately appended the new suppression-reason enum to the current strict public-type census (54 to 55); frozen API fixtures were preserved. `heartbeat-architecture/latest-failure-stdout.log` remains; SHA-256 `3b9a7f18779d04e93ccc9055861093ede8033b783077645980c3a730aa3ceaaf`. |
| Healthcheck inventory refused before native launch | CLI exit 4: “Declared source, test and configuration paths must be unique.” Removed 115 duplicate declarations. Association `native/incremental/824d44e44f58408482abc5064e12e35b`, 963 ms, zero native batches remain in the ledger; no TRX is invented. |
| Initial integration: seven host/scope cases passed, package case failed at the source-lock lookup | The test incorrectly demanded packages intentionally pruned by the framework. Corrected the oracle against evaluated assets and approved versions. Original report remains at `heartbeat-integrationtests/history/first-7-passed-1-failed-unit.xml`, SHA-256 `9e373469eb19280798f1f109b35bf8d91bea0f541130ad9dbfa08447d3032871`, plus its receipt and failure stdout. That invocation stopped before example build/run; the final 8/8 report is separate evidence. |

Relative failure paths above are under `.cis/local/testing/incremental/`. The original 167-case pass remains at `heartbeat-module-unit/history/first-167-unit.xml`, SHA-256 `8d19ec94bb3e7adc3da7368aff063be4fd2c7a8af56e0dd6b6ae3d6a6cacb9ab`, with its receipt. The final 171-case report includes the four later capacity/race/detached-exception cases.

Independent review corrected condition/callback work-end timing, dispatch publication before wrapper progress, initial/final secondary statuses, true task settlement, safe clock-fault handling and retirement/reporting races. The concrete Core reporting binding survives weak-target loss without retaining subscriber/owner closures; real retained-snapshot/exception-graph collection checks passed.

Example/guide review also corrected destructive cleanup after an unsuccessful global barrier: once an engine exists, its actual `StopAsync` must succeed before host/scope/health/root cleanup begins. Later cleanup failures still permit remaining cleanup after that proof. This was a reviewed failure-path correction, not an invented earlier runtime failure. The final source/package runs used the corrected source.

The direct gate/CWT/FIFO engine and narrow adapters were judged proportionate to the required ownership, identity and phase gates. Existing Healthcheck policy logic, MSTest, controllable clocks and process fixtures were reused. No material responsibility or verification-machinery expansion remains; no new runtime package or generic startup framework was added.

## Initial module accounting and storage checkpoint

| Owner | Measured inclusive native charge | Used logical attempts | Disposition |
| --- | ---: | ---: | --- |
| WORK-001 | 2,000,450 ms | 1 | Closed after final module/architecture evidence. |
| WORK-002 | 0 separately launched | 0 | Complete using applicable actual WORK-001 scheduling evidence. |
| WORK-003 | 0 separately launched | 0 | Complete using applicable actual WORK-001 drain/race evidence. |
| WORK-004 | 712,982 ms | 1 | Closed after regression/compatibility evidence. |
| WORK-005 | 1,393,589 ms | 1 | Closed after corrected integration and business evidence, including its real failed invocation. |
| **Shared measured total** | **4,107,021 ms** | **3 of 6** | **Zero reserved; no active native executions or recovery holds.** |

Canonical remaining usable time is 9,992,979 ms, with 300,000 protected restoration ms within the original 14,400,000 ms ceiling. Native preparation, execution and result admission use inclusive accounting; nested child charges are not added again. These recorded charges do not measure total authoring effort, and no zero-cost claim is made for authoring or earlier unmeasured effort.

After the final native runs, a read-only file-length observation of declared counted roots found `.cis/local` **739,063,874 bytes**, `implementations/csharp` **4,461,479,668 bytes** and `.artifacts/heartbeat/CIS-0006` **523,854 bytes**: **5,201,067,396 bytes** combined, before final document/receipt updates. This is an occupancy observation, not a physical-deduplication or peak-storage claim. The existing six-GiB shared physical ceiling and protected reserve remain unchanged.

Package children exited with complete bounded capture; the test disposed its uniquely owned temporary root. Other native output/evidence remains counted and retained. No general cleanup/deletion authority was inferred and no earlier owner's output was removed.

## Currentness and remaining qualification

The final native receipts reported current unit evidence at their admission checkpoints. Final fresh-graph/read-only saved-result verification is recorded below. The retained native passes and later saved-result currentness are distinct observations.

The [70-case mapping](../../../planning/heartbeat/acceptance.md) remains explicit traceability, not 70 blanket acceptance passes. No Ubuntu run, complete repository regression, full platform/package release qualification, coverage/mutation gate, performance benchmark, extended stress or fractional-CPU latency measurement is established here. The accepted 95% line / 90% conservative-branch coverage and 90% conservative-mutation obligations remain open. Compatibility checks used actual frozen API/schema inputs; they do not claim execution of an unavailable historical reader binary.

The initial implementation authorization did not publish a package, merge main, transmit source externally or close the change dossier. The subsequent explicit user-authorized branch/change closeout is recorded below. Existing Logging/Type Manager/Healthcheck historical results retain their original scopes. Use the [guide](../../modules/heartbeat/README.md), [implementation scope](implementation-plan.md) and [canonical task state](plan.json) for this implemented development module.

## Initial module closeout checkpoint

At the initial module closeout, the original five canonical tasks were Complete. Logical attempts WORK-001:1, WORK-004:1 and WORK-005:1 were closed; all owned native invocations are settled with zero reservations/recovery holds. Independent final review checked 19 current documents, 173 local links, four heading anchors, all 70 unique AT rows, all six final TRX counts/hashes and unchanged supplied/retained specification bytes. `git diff --check` passed.

The final graph refresh reported zero errors/warnings, build ID `sha256:33ce71480f40bdca5c147b6da46e855cd64d1c172aa45825aa16670ed2b06996`. Read-only `test incremental verify --layer unit` was then requested for all six profiles. Every invocation returned exit 4 / `verification-failed`, with the generic diagnostic “Every declared source requires completed current unit and configured mutation evidence.” The returned receipts label their layer `all` despite the unit requests; no all-layer qualification is inferred.

The detailed reuse reasons report a changed `LOCALAPPDATA` environment input for all six profiles. The module, architecture, Healthcheck-regression and compatibility receipts additionally report the later changes to `examples/heartbeat/Polling/Program.cs` and `tests/CIS.Core.IntegrationTests/Heartbeat/HeartbeatPackageTests.cs` in their conservative shared Core-project closure. The corrected final integration and business receipts report only the environment difference. Their actual native passes used the same unchanged Core DLL hash recorded above.

Full read-only results remain at `.cis/local/testing/heartbeat-final-reuse-verification.json`. Saved-result reuse is therefore **held** at this checkpoint. No cache fingerprint, profile or report was rewritten to clear that status, and no completed owner was reopened or native test rerun for currentness. This limit does not turn the independently retained passing native observations into a failure of those executed tests.
| Task | Check | Source | Disposition | Recorded snapshot |
| --- | --- | --- | --- | --- |
| WORK-006 | CIS tool-usage snapshot | `.cis/local/feedback/tool-usage.jsonl` | Recorded | invocations=731; failed=117; possibleTokenSavings=123; ledgerDigest=sha256:37c221f5d70b27190742d1822bf633f9ff7470764a0906cda10b21b5f4e6ab96 |

## User-reported Type Manager compatibility correction — WORK-006

The user subsequently reported three IDE failures: both `OriginalConsumersRemainCompatible` variants and `PublicApiRemainsCompatible`. The original 129-case compatibility filter covered Healthcheck/Heartbeat, so it did not execute these Type Manager cases. That scoped pass remains valid history; it was not evidence for the missed full compatibility workflow. This correction records the fixture/deployment gap that escaped the earlier reviewed handoff.

Complete Type Manager discovery correctly rejected a selected Core whose hosting-interface dependency could not load. Core already declares the approved `Microsoft.AspNetCore.App` shared framework; the old raw-reference/frozen consumers declare only `Microsoft.NETCore.App`. Heartbeat's hosted service made this incomplete fixture deployment visible during `GetTypes()`. The fixture now writes a separate runtime configuration supplying the pinned .NET/ASP.NET 10.0.12 frameworks for positive consumers, preserving their original configuration properties and all frozen files. Discovery remains complete-or-fail. These passes establish consumer behavior under current approved dependency deployment, not unchanged historical deployment prerequisites.

The API failure was the selected host's missing exact .NET 8.0.31 runtime: the global installation had 8.0.34 while a private 8.0.31 host was available. API calls now prefer a nonblank explicit `CIS_COMPAT_API_DOTNET`, then the existing repository-local pinned host, then ordinary `dotnet`. Exact runtime/tool versions and disabled roll-forward remain enforced; SDK/product host selection is unchanged. Case-insensitive DLL recognition preserves these flags for uppercase tool-path extensions. Root and native-test instructions were updated.

**Actual corrective native result: 179 passed, 0 failed, 0 skipped**, with the API-host override blank. This is the full CompatibilityTests selection: the three reported cases, all frozen behavior/source/API cases and the existing Healthcheck cases. Both intentional incomplete-deployment checks passed: missing NuGet deployment still requires the original Logging.Abstractions metadata failure, while a separate new case supplies those packages but omits ASP.NET and requires the Hosting.Abstractions metadata failure.

- Profile: `heartbeat-type-manager-compatibility-correction`; native correctness layer `unit` invoking the actual CompatibilityTests project, ordinary performance/stress exclusions.
- Execution: `native/incremental/46dfff49581f4885aa7dc0eb77fd562a`; logical attempt `WORK-006:1`.
- Retained report: `.cis/local/testing/incremental/heartbeat-type-manager-compatibility-correction/reports/file-e7e4df149e0949e4a8a2-unit.xml`.
- Report SHA-256: `9d415f4b481363432562aa81f4a0f93c14d032abe59ec8ba65ca0a502c2fcc15`.
- Core DLL remains `453546e8ada81d7ed0d073544c153ef553a7aa82b973df931f4ea7ad10dbdf08`.
- Frozen baseline manifest hashes remain `4fd1124533971344d0ca362056450553c5eb0cea2c46e28780cc6301d1ed49d1` and `d5d26e9f528b3306b8437b9564da6797d660a191dc1f04162d5475ac283e4228`; runtime hash guards also verified every listed frozen input.

Independent review found no required defects or material product-responsibility/verification-machinery expansion. Only the existing fixture, one meaningful negative test and supporting instructions changed; no Core code, runtime package, partial-discovery behavior or frozen baseline was changed.

[WORK-006](agent-tasks/WORK-006.md) is Complete. It received 2,000 unused seconds from WORK-003 and WORK-005's unused second attempt under the original resource delegation; prior completed tasks, consumption and results remain intact. Current allocations are 3,900 / 2,700 / 1,000 / 2,400 / 2,400 / 2,000 seconds, one logical-attempt ceiling per task: still 14,400 seconds and six attempts total. Protected restoration stays 300 seconds and the shared physical six-GiB storage policy/reserve is unchanged.

The corrective invocation consumed **454,308 ms** inclusively and settled safely with no active execution, reservation or recovery hold. The updated measured change total is **4,561,329 ms**, **four of six attempts used**, **9,538,671 ms usable remaining** plus the existing 300,000 protected ms. WORK-006:1 is closed. Authoring remains outside the completeness claim of those measured native charges. The corrective native receipt reported complete/current selected unit evidence and `qualified=false` at admission; earlier saved-result holds remain historical observations, and no cache status was forced or completed owner replayed.

## User-authorized branch and change closeout — 16 November 2026

The user explicitly requested merging and closing the current branch before the next step. This later instruction authorizes the Git merge/publication of the reviewed repository changes and deletion of this feature branch; the original implementation-only exclusions remain historical. It does not authorize a package release or convert scoped evidence into release qualification.

All six tasks and their native executions were already complete/settled. `cis change close CIS-0006` recorded the change dossier as Closed. Merge preparation confirmed remote `main` at `060b2f593b3c99e39d3682286311cd2d9d400a9f`, an ancestor of this branch, with no required branch checks. The branch includes its 15 existing integration/closeout commits plus the Heartbeat and compatibility correction; their history and previous owners are preserved. Independent merge review found no unintended generated/binary/temp files, credentials or required issues, and whitespace checks passed. Ignored native evidence remains local and intact.

The actual merge/branch-deletion result is reported with the GitHub pull request. Full release, Ubuntu/coverage/mutation/performance obligations and the documented saved-result reuse limits remain unchanged.
