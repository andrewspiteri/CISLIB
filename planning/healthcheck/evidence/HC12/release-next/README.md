# Healthcheck mutation and Release continuation

The preceding wave was committed locally as
`8fc2f9ccb78ba948f9794cfb4c0ea37cfd2956eb`. This continuation is a separate,
unpublished working candidate. Earlier receipts retain their original inputs and
meaning. Production Core sources and public contracts are unchanged by this wave.

The coordinator owns native builds in isolated frozen copies. Parallel owners
handle rollback review, child-process proof and compatibility/release auditing.
No agent builds into another owner's outputs.

[receipt.json](receipt.json) records the scoped outcomes and remaining gates;
[artifact pins](artifact-pins.json) identify the retained workspace-local raw
evidence. [Next work](next-work.md) lists the concrete remaining qualification
tasks. A pin inventory is not a published verification kit.

| Work | Executed observation and boundary |
| --- | --- |
| Exact unpublished package | Archive SHA-256 `37961c7cd75a7c7e5af47087496f087c5f50ade228c9616be0cfc251e49bcdea`; Core entry `039007b8920929bb62e096590789325f874ec5cac969f2d5a95f29a31967e980`. Windows and native Ubuntu consumers each completed 25 observation rows, 25 owned processes and 13 HTTP observations. These are not additional MSTest counts. |
| Coverage for that Core | 2,882/2,990 lines (96.3879599%); conservative branch lower bound 1,993/2,184 (91.2545788%). Unchanged thresholds: 95%/90%. Collection passed 649 unit, 137 integration and four business cases. Later detectors receive no retroactive credit. This is Healthcheck coverage, not whole-shipping Core qualification. |
| Complete unit/runtime automatic mutation | Windows strict score 1,843/2,695 = 68.3858998%; Ubuntu 1,844/2,695 = 68.4230056%. Both fail 90%. All 41 selected source paths match the frozen inputs. Fresh restored 650-case suites passed on each system. See [the native comparison](matrix/unit-runtime-native.md). |
| Complete Windows Pipes mutation | Strict score 89/228 = 39.0350877%, below 90%. The v1 child audit rejects 271 records. Fresh 93 integration plus 183 unit baseline cases passed; all 1,321 frozen source inputs remained unchanged. See [native counts and unresolved origins](matrix/pipes-native.md). |
| Complete ASP.NET mutation | Both recorded native lanes score 21/24 = 87.5%, below 90%; seven earlier survivors are killed. Latest owner-aware Windows and earlier schema3 Ubuntu child audits each pass 1,125 records. Three survivors, 72 compiler omissions and five ignored remain. See [the exact native contexts](matrix/aspnet-native.md). |
| State compiler/ignored replay | All 103 omissions attempted: 43 independent compiler errors, 30 kills, four survivors, 17 timeouts and nine unresolved. Exact source/baseline binaries restored. Conservative supplement 226/277 = 81.5884477%, incomplete. See [the source-bound review](state-dispositions/native-replay.md). |
| New state detectors | Two real owned cancellation-resource closure tests pass. Separate replay kills survivor 2346 with 652 baseline/restored cases. Separate named-assertion replays kill 2107/2210/2216 with exact restoration. Original frozen 650-case results remain unchanged. |
| Compatibility correction | First full profiles exposed 87 inherited Type Manager fixture failures after the approved ASP.NET framework began supplying dependencies. The fixture now verifies the original approved NuGet DLL/archive/lock/restore origins and preserves the omission control. Focused Windows compatibility: 247 passes. See [the correction and trust boundary](matrix/compatibility-dependency-correction.md). |
| HTTP/lifecycle detectors | Seven additional HTTP cases and cache-header assertions passed in the 52-case HTTP run. The v2 lifecycle wiring passed 145 selected integration/scheduling cases. Mutation and full profiles keep their own input sets. |
| Finite native child proofs | Current owner-schema4 Windows and Ubuntu proofs each killed four conditional mutants and passed 100 launch records (80 instrumented), with nine derivative parser controls each. Earlier schema3 registration proofs each killed three guards with four complete launch records. Normal restoration passed. Parser controls are not process executions. See [current owner evidence](matrix/child-owner.md) and [frozen earlier evidence](matrix/child-origin.md). |
| Frozen Release-v2 profiles | The older schema2 harness overlay passed all 17 executable stages: Windows 8,235 native cases, Ubuntu 8,237; zero failures/skips. Both ended at validator exit 2 with the same 26 issues. These are not full-profile passes for the later owner-schema4 working tree. See [native inputs, results and distinct package bytes](matrix/native-release.md). |

The final owner-aware harness has separate [native proof and parser-control
evidence](matrix/child-owner.md): four real conditional kills and 100 audited
launches on each platform; nine derivative audit controls per platform; 145
current canonical integration cases passed. [Restoration records](restoration.json)
keep clean-source rebuilds separate from the state/finite proofs' exact binary
restoration checks. Overlapping native cases and parser controls are not added to
the Release totals.

The package producer remains the original clean Windows clone at the committed
revision. Its inputs are separate from later harness corrections. The initially
rejected Linux consumer attempt had different NuGet.config line endings; the
successful retry validates the exact Windows producer bytes. Native source-copy
Core assemblies retain their own origins.

## Mutation evidence rules

The [three full-scope configurations](../../../qualification/release-next/)
partition all 46 Healthcheck sources exactly once: 41 unit/runtime, one ASP.NET,
four Pipes. Declaration-only files remain included. All retain Complete mutation
and the 90% break threshold. Child lanes run all selected tests for each mutant;
parent-process coverage cannot establish actual child execution.

The harness records the actual parent Core/selector/controller and staged child
Core, MVID, deps, runtime, startup origin and parent-observed terminal state.
Missing receipts, forced termination and timeout do not become kills. Managed
registration failure requires the exact verified Core throw frame and actual
nonzero process exit. The finite launcher uses the pinned real Stryker mutators,
compiler and VSTest runner. It is a scoped diagnostic, not the broad gate.

Ordinary CLI ASP.NET compiler omissions, v1 missing-terminal failures, the failed
fresh-backup proof, incorrect working-directory attempts and successful finite
proofs have separate output roots. No timeout, ignored mutant, compiler omission
or proposed invariant equivalence is silently removed from remaining work.

## Release boundary

Both frozen Release-v2 profiles use independent full clones with the actual base
commit plus an explicitly frozen harness overlay. Clean first attempts and later
corrected attempts have distinct inputs/outputs. Actual stage/family results are
recorded by the native runner. See [the global gate inventory](matrix/README.md).
The later owner-schema4 changes have their own focused/native mutation evidence;
no full-profile pass is retroactively assigned to that newer working tree.

The inherited validator still requires eight global gates and nine planned logging
cases. Mutation has no project-backed family dispatcher, and the full matrix
validator is unimplemented. Those refusals remain open; this wave does not resume
the paused logging roadmap. Real Heartbeat/watchdog/generic IStartable integration
is absent. The publication ledger is empty; no released-history replay or
publication is invented. HC-AT-58 remains PendingRelease.
