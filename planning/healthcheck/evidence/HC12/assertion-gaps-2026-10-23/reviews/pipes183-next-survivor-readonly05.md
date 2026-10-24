# Next bounded Pipes survivor experiment: read-only source selection

One actionable target is recommended. No new fixture, utility, Core variant, build, discovery or test was created or invoked for this review. Canonical sources and active campaign inputs remain unchanged. The already-proven cancellation-source leaf is excluded.

## Recommended: actual endpoint-change semaphore disposal

Exact physical source: `implementations/csharp/src/CIS.Core/Healthcheck/Pipes/HealthPipeServer.cs`, SHA-256 `809ba1a493d6176e354f84209ea556ae21eb726e626b35eb7cc777956cdba12f`.

Exact tuple: UTF-16 `[3400,3426)`, line68 columns50–76, original `endpointChanges.Dispose();`, replacement `;`, operator `Statement mutation`, static false.

The retained report `.artifacts/hcg/pipes180-reference-origins181/server-pipes-full-20-native/reports/mutation-report.json` has SHA `847327cc1bb2465664ad095ed06b3b1bd26707b5a10cc15c50cc2cee81c67836`. Its embedded Server source equals the current physical source byte-for-text (13,342 UTF-16 characters, LF), and the exact location extracts that statement. The matching historical raw outcome is Survived (alias2797, descriptive only). No current180 outcome, future kill or numeric credit is inferred.

The current `HealthPipeServerResourceDisposalTests.cs` SHA `328a83cb11aa85209fce41ed9fe8f99b8af0e0aec29f9b29cc491fdc5b0568b5` observes only the distinct `stoppingSource` resource. Searches of current Unit/Integration reporting fixtures found no observation of `endpointChanges`, `SemaphoreSlim.AvailableWaitHandle` or its disposal. Public completion/idempotence checks can pass when this adjacent Dispose call is omitted.

Proposed one public case: `HealthPipeServerResourceDisposalTests.DisposingAnInertServerReleasesItsActualOwnedEndpointChangeHandle` (name proposed; no discovered GUID exists yet).

1. Construct a disabled, unstarted server with a source/clock that record and reject any unexpected snapshot or timer work. No Start, client, endpoint, budget, worker or semaphore waiter is created.
2. Read the actual private `endpointChanges` field, requiring exact `SemaphoreSlim` metadata and a non-null actual instance. No production field is written or replaced.
3. Read that actual owner's `AvailableWaitHandle` and retain the actual `SafeWaitHandle` before disposal. This explicitly materializes a fixture-observed event owned by the SUT's semaphore. It is not evidence that the unused semaphore had already allocated an OS event. Do not dispose the borrowed handle or assert a replacement resource.
4. Join actual public server Dispose, then assert the saved actual SafeWaitHandle is closed. Source and timer counts remain zero.
5. On every exit, independently initiate and genuinely join both public Stop and Dispose. Only after both succeed, dispose the same actual semaphore as fixture cleanup and verify the real observed handle is closed. This release follows the saved assertion and cannot repair it. Preserve any API/resource/producer failure as an Aggregate hold; no unfinished task or live waiter is excused.

One fresh original positive and one fresh whole-public-method negative against only this leaf are sufficient for the small experiment. The negative must fail the resource assertion while real cleanup still succeeds. Reuse the reviewed90s session/120s process/15s reap/15s pipe-drain diagnostic envelope if root approves; expiry, unknown exception or failed cleanup remains a hold. No GC ownership exception, queued callback drain, production hook or new harness framework is needed. Future canonical integration/discovery and complete qualification bindings remain separate.

## Second candidate considered: do not pursue as an assertion gap

Same physical Server source, UTF-16 `[3259,3264)`, line65 columns50–55, `false` to `true` in `await stopCompletion.Task.ConfigureAwait(false)`, Boolean mutation/static false. The exact embedded source join has historical raw Survived (alias2790).

A real experiment could hold an actual source invocation, call public Dispose under a real queued SynchronizationContext, release the source, join actual client/handler/Stop work, and then observe Dispose or a genuine queued continuation. Cleanup could drain the actual callback and join disposal. These mechanics require no fabricated task, but alone they prove continuation placement rather than an adopted contract violation.

The existing source-bound Pipes review `planning/healthcheck/evidence/HC12/gate-closure/pipes/historical-remaining.md` SHA `e368e1bb356d685d2a508d4766edd078f477d4f0bd9f67f432fbd26ec6b0acae` explicitly records that the public contract does not promise progress without pumping a caller-supplied context. C08 requires terminal handler/resource settlement without prescribing this scheduler behavior. Therefore no non-pumping-context assertion or new fixture is recommended solely to kill this definition. It remains a raw survivor; this review changes neither its status nor denominator.

No second behavioral target is forced into the plan. Recovery/partial-start resource paths need concrete actual branch/ownership evidence; existing platform assertions already cover several older Windows-only survivors, so old Ubuntu survival alone does not establish a present assertion gap.
