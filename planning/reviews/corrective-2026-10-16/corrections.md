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

Selection: IncomingRuntimeTests, IncomingEventQueueTests and ProcessingPolicyTests. These are focused development checks, not coverage, mutation or release qualification. A comprehensive Developer checkpoint remains required after the remaining fixes.

## Still open

F1, F2, F3, F5, F6 and T1–T4 from [the original summary](README.md) remain open pending implementation and verification.
