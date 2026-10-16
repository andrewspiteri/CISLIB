# Corrections to the partial 16 October review

The original logging roadmap goal remains paused. These are corrections authorized by the user's subsequent fix request. Raw review responses, source submissions and historical receipts are unchanged. The three uncompleted Claude scopes remain pending, and Claude has not reviewed these corrections.

## R1 and R2 — incoming observations and current contract

Fixed caller loss snapshots for flush/stop. Each caller retains its own incoming queue-full and early output rejection facts even when worker coordination is shared. Worker invalid/truncation and rejection effects are frozen through the covered incoming prefix; unrelated later caller activity is excluded. Protected-envelope preparation failure is committed under the same gate as its incoming disposition, so abandoned preparation cannot add that loss later. Recursive processing remains bounded and is counted against its originating runtime's worker when applicable.

The portable development contract now describes incoming acceptance/tickets, empty acceptance output lists, queue-full rejection and worker observations. Both safe error-code catalogs include IncomingQueueFull and RecursiveProcessing. XML documentation distinguishes public incoming acceptance and internal output-stage qualification.

Four deterministic native regression tests cover pending flush snapshots, coalesced rejection snapshots, stop versus an already-running truncated capture, and late early-output decisions versus covered worker rejections. Each has a required-case binding for Windows and Ubuntu.

Executed with SDK 10.0.401 / runtime 10.0.12:

| Lane | Selected native cases | Result | Report |
| --- | ---: | --- | --- |
| Windows | 38 | Passed; zero failed/skipped | .artifacts/csharp/focused/3293d1df99014c73ac6cf8eea0fb4f1e/summary.json |
| Ubuntu 24.04 | 38 | Passed; zero failed/skipped | .artifacts/corrective/batch-a/linux/.artifacts/csharp/focused/b8ffa6c7d98c4591a690014b537a168d/summary.json |

Selection: IncomingRuntimeTests, IncomingEventQueueTests and ProcessingPolicyTests. These are focused development checks, not coverage, mutation or release qualification. The subsequent comprehensive Developer checkpoint below covers the completed fixes.

## F1, F2, F3, F5 and F6 — file verification and evidence labels

Fixed the required inventory binding for all 23 physical JSONL boundary modes on both lanes; the separate twelve-corruption binding remains. The selector's size corruption is now a valid metadata document padded one byte beyond the limit, with independent parsing proving that JSON syntax is not its rejection reason. Exact-bound positives remain.

Restored stable intended-reason comparisons for all original corruptions in the six named transition/boundary/rotation/retention/inventory/acquisition readers. They still validate actual physical positives before each corruption. No corruption ID was retired. The inventory holder now uses the same acquired metadata/member/hash checks as the ordinary probe and explicitly checks creation, empty active length and initial segment identity.

Current rotation/recovery maps and the policy guide now distinguish unreadable retirement-member preflight from actual post-intent deletion denial. Historical cases/receipts retain their original wording. LoggingReviewProcessTests rotation-denied/recovery-denied remain the actual deletion-denial evidence.

| Lane | Selected native cases | Result | Report |
| --- | ---: | --- | --- |
| Windows | 152 | Passed; zero failed/skipped | .artifacts/csharp/focused/f324b8abadd248c68fbd750036c6e50b/summary.json |
| Ubuntu 24.04 | 153 | Passed; zero failed/skipped | .artifacts/corrective/batch-a/linux/.artifacts/csharp/focused/bc4841e8c3c84b16ab87e6b82ecc047e/summary.json |

Selection: FileTransitionProcessTests, JsonlBoundaryProcessTests, FileRotationProcessTests, FileRetentionProcessTests, FileInventoryProcessTests, FileAcquisitionProcessTests and JsonlInputSelectorTests. Ubuntu executed the additional symlink selector control; Windows error 1314 remains an explicitly unexecuted prerequisite case, not a skip or passing rejection.

## T1–T4 — Type Manager verification and current guidance

Corrected the maintained acceptance map to native runtime/inventory commands, and documented the example cache's two runtime archives plus all seven pinned SDK runtime/host packs on both operating systems. The examples verifier now reads those seven pack identities/versions from the existing frameworkDownloads policy and records its policy hash; it adds no dependency or network fallback.

The measurement guide and reports explicitly define processWallMilliseconds as verifier elapsed time including child execution, strict output decoding, output hashing and log writes. It is not directly comparable with the retired Python child-only timing. Child-reported metrics keep their own boundaries, no threshold is added, and reports remain unqualified.

Mutation preparation and measurement pin/parse one owned byte array. Measurement preparation records the hash of the same bytes it parsed. Their guard drivers use the same pin checks. The compatibility reader also parses and pins the same producer bytes, while retaining later unchanged-input checks. These are local provenance corrections, not a security exploit claim or a new verification framework.

Verification uses frozen checker SHA-256 66b8b339713a1a7e68c5f515bee6439c76919ce627d37dcfceb9a5ce234c6fc3 and producer Core SHA-256 e4fcbc57aa14864ae021b39ccc1eefe4e2da3a1bb9056d7a6a21e1f57e169298. Producer report SHA-256 e53db55083a1d1ac16e82e031fbe9dd3c4fcd5410819f290067289ab7a34ac24. Both OSes reuse these exact managed fixture/tool bytes where replay is declared. A subsequent explanatory Core comment cleanup does not change Type Manager module sources; the later Developer checkpoint has its own complete source inventory.

| Verification | Windows | Ubuntu 24.04 |
| --- | --- | --- |
| Runtime producer/replay logical checks | 17 passed | 17 passed |
| Mutation preparation provenance controls | 4 passed | 4 passed |
| Prepared isolated native baseline | 100 passed; zero failed/skipped | 100 passed; zero failed/skipped |
| Frozen-fixture measurement | 52 fresh processes; 48 measured and four warmup | Same frozen fixtures; 52 fresh processes |
| Measurement provenance/sample controls | 7 passed | 7 passed |
| Source/exact-package example checks | 66 passed | 66 passed |
| Selected native compatibility cases using explicit producer/Core inputs | 5 passed; zero failed/skipped | 5 passed; zero failed/skipped |

Raw reports/logs and the frozen tool are under .artifacts/corrective/claude-fixes. Windows compatibility report: .artifacts/csharp/focused/94ea2705c2fc4907984b29e9c981172f/summary.json. Ubuntu: .artifacts/corrective/batch-a/linux/.artifacts/csharp/focused/6ef49375fb9947598a4cec251dcbff9c/summary.json. The example package is the earlier exact development archive with SHA-256 6ebf90713b29ec2608a4d32e3593de76c6662aad5146d13847aef95518be6e54; these checks do not qualify a newly released package. Its source/package lanes have separately recorded Core hashes.

All 3,710 current Windows unit cases also passed with zero failed/skipped: .artifacts/csharp/focused/520874c5139040159c849ff03395310b/summary.json. Utility logical checks, baseline cases and process observations are separate measures and must not be added into one invented native case count.

## R1 follow-up — readonly diagnostics construction

The first combined Developer run, 0127538b333f4c458ebbd307ae471370, failed one architecture case: DiagnosticIlDoesNotRouteDoIoQueryCallerSourcesOrCreateTasks. Its diagnostic IL traversal rejected the generated IncomingAccounting.set_QueueFull call used by a record-struct copy. Unit (3,710) and integration (2,085) passed; architecture passed 618 of 619; business did not run. This run is not a passing checkpoint.

Replaced that copy with direct readonly value construction. The existing diagnostic guard and all its negative controls are unchanged. Twelve DiagnosticsArchitectureTests pass on Windows and Ubuntu, and all 38 incoming/processing cases pass again on each lane. Eighty-two Windows foundation/runtime-contract architecture cases also pass. The portable Create and thread-safety descriptions were aligned with the static environment and incoming-admission API while reconciling R2.

Reports: Windows diagnostics .artifacts/csharp/focused/c9388f2511344c29b7a0fbb3b4fddc37/summary.json; Windows runtime .artifacts/csharp/focused/5bcb0b21c6c14ec995b8e0340f9b81dc/summary.json; Windows contract .artifacts/csharp/focused/f351891139a4489db81fbff20e9f8a9b/summary.json; Ubuntu diagnostics .artifacts/corrective/batch-a/linux/.artifacts/csharp/focused/80f06b4484d04f2ebb90f8cc97cf3e20/summary.json; Ubuntu runtime .artifacts/corrective/batch-a/linux/.artifacts/csharp/focused/b18c5c528de040069b65d3fa39ad3998/summary.json.

The failed Developer output and log are preserved under .artifacts/corrective/claude-fixes/failed-0127538b333f4c458ebbd307ae471370. Failed summary SHA-256: 142c527ca4855f6ad0a44bba7ab91194866c51ce6077feaa9340c8e6c58783ff. Its source inventory remains 85094fa4a55a85600587d944da69071fe119e6ee78c9e8a092e0a10dfd52b9da. The subsequent fresh frozen-source Developer run below verifies the corrected implementation.

## Remaining verification and review

All eleven supported findings are fixed and verified. Final Windows Developer run ac73d96d939c4f2d819463df71ba9f36, at source 7a606607, passes all 6,438 native cases with zero failures/skips/issues, including the unchanged diagnostics guard. See the [verified checkpoint](../../evidence/corrective-claude-findings-checkpoint-2026-10-16.md) for exact source/artifact pins and the separately preserved failed first attempt. The three uncompleted Claude scopes (325 assigned paths at the original snapshot) and a review of these fixes remain unexecuted. Prepared old continuation bundles do not cover this new source delta. Coverage, mutation, complete Ubuntu Developer, release/package history, supported IDE acceptance, teaching examples/skills and publication remain separate pending work. The original roadmap goal remains paused.
