# Historical Unit survivors against frozen170: bounded assertion triage

**Queue one concrete fixture-oracle repair for source review.** The useful next case is the existing operation-plus-retirement overlap test: it proves work is pending, but after the real callback/withdrawal return it only waits on the completion signals. A missing state finalization can therefore obscure the contract defect with a timeout and another cleanup timeout. Do not add ten speculative tests to satisfy a list size. No canonical source, snapshot, utility, test, build or mutation execution was changed by this triage.

## Actual basis and exact source join

The accepted complete three-report union is the retained original40 result, `C:/Users/andrs/AppData/Local/Temp/cis-hc12-execution72/union40-original-result-diagnostic83.json`, SHA256 `496905d6f61b8dc477e96b23da2efbc1295881c7e064bab6114dce807d20a9b5`:1591/3065,51.908646%, complete=true/qualified=false. It identifies Unit raw report `056cc405715ac814bbad9ff45e276e77708c56463beea24ad76e23d19f3bf89d`, at `survivor-source-reports77/server-unit-full-18-native/reports/mutation-report.json` below that same Temp root. That report has234 raw Survived definitions. Its status/IDs describe its own original run, not current fixtures or running170.

The retained source-compatible map `.artifacts/hcg/unit-current-source-survivor-map01/source-compatible-historical-survivors01.json`, SHA `b1501ffccc3ac59329a3cdbdd44c5660094e8f67c7f517f9d85be2b38d29616a`, admits158 tuples in18 physically identical files. I independently joined every admitted full file SHA and exact UTF16 original slice against `.artifacts/hcg/unit-complete-partition170-staging01/snapshot/source/implementations/csharp/src/CIS.Core`:158/158 matched,0 mismatches. The other76 are historical Runner whole-file source mismatches and remain excluded from this join. I did not remap their numeric IDs or infer their current status. Frozen170 source-manifest SHA is `eea4cbabeb2970bbdd64ad51d6bd81e9c86cda29d6bc3f35928bc59ebc2469e3`.

The source-compatible map includes49 string mutations,14 monitor-handle KeepAlive removals,12 immutable-field coalescing reversals and numerous redundant/previously covered validation boundaries. The prior34-entry ExecutionState audit `execution-state34-source-only-review01.md` already found zero justified new public gaps; it is not repeated as new assertion work here.

## Ranked concrete candidate

**1. Assert real operation/monitor/service completion immediately after the actual retirement dispatch returns.**

Exact historical source tuple, without old numeric ID:

```text
path: Healthcheck/Internal/State/HealthStateCoordinator.Collection.cs
sourceSha256: a4d9049a2a1304a40d487584c708e85749f2e6a9f9305855bb690afa48315240
UTF16: [14250,14298)
original: { FinalizeOperationLocked(operation, effects); }
mutatorName: Block removal mutation
replacement: {}
static: false
```

This is the `CancelRetirement` finally branch at current source line270. After the real retirement observer returns, it finalizes an already-completed operation, then attachment/monitor/service settlement. Removing this dispatch can leave the operation in the live state dictionary and leave genuine completion Tasks pending. The observable obligation is the operation/monitor/service disposal barrier after all real owned work has returned; it is not an instant-Stop SLA.

The existing method `HealthCollectionStateIntegrationTests.RetirementCallbacksKeepOperationMonitorAndServiceDrainsPendingAfterConcurrentComplete` starts at line14, fixture SHA `e281d083b9b6ef935a81b7be31426c17b0c97f8cad0251fefa870ca23c20fafe`. It already enters a real reservation, joins the actual synchronous Complete call at line34, verifies the barriers remain pending while the actual retirement observer is gated, releases that observer in finally, calls actual Complete again and joins the real withdrawal at line47. After that it checks callbackReleased and merely awaits the three completion Tasks at line52. Scope.DisposeAsync subsequently calls Complete and waits for service disposal again. Neither wait supplies a direct assertion for a lost completion signal; the second wait can also mask a useful earlier assertion.

The smallest useful proposal is to retain every original pending/work assertion and real callback/Complete/withdrawal join, then assert `IsCompletedSuccessfully` on the actual state-owned operation, monitor and service Tasks **after** those real synchronous dispatches returned. A phase-aware cleanup path must be source-proven before adoption: account for the sole callback/registration disposal, both state calls, cancellation/retirement resources and absence of runner/timer/lease/user work; do not simply skip a wait because an assertion occurred. Retain unknown exceptions and every genuine pending-work hold. No completion Task may be fabricated or completed by the fixture. An entire private state-graph release may be needed if remaining managed ownership cannot otherwise be proved.

The existing `HealthCollectionRetirementOverlapBoundaryTests.MonitorDisposalRetainsAnEarlierIdleCollectionRetirementUntilItsActualObserverReturns` (fixture SHA `8fa82dff6711278dcdeea4dfc233e7e352da3fc3ee741e1b2ff5eb2fad4d8129`) has the analogous direct post-dispatch checks and narrow cleanup for an **idle** attachment. It does not enter an operation, so it does not traverse the target operation-finalization block. Reuse its causal reasoning, not a blanket cleanup exemption.

Finite de-dup limit: this exact tuple is absent from reviewed State85 three-target definitions (`bf9ee84bb3db128ab6666e8111f10b61b5a88ff37cdebf55de6df41c3c8d09dd`) and Settlement111 nine-target definitions (`00c202fa7785bb42cf5a29ef9454c4f515e1329f9136a8650f997a772d55ad21`). Those target different exact statements/flags/spans. The retained aggregate claim is37 distinct finite definitions; there is no authoritative single37-row catalog available. This bounded triage does **not** assert complete de-dup against an invented table or count this as a newly unproved mutant. Before any manual/finite execution, root should reconcile any additional earlier proof of this exact tuple. Existing135/125 repeated Runner retirement and137/126 repeated Server counters are not proposed as new work.

## Other examined families: no speculative new tests queued

| Exact historical family | Current-oracle/source conclusion |
| --- | --- |
| Collection `[13680,13799)` pending-settlement OR→AND | Existing idle retirement overlap checks genuine monitor/service Tasks while the actual retirement observer is still running. It is already an observable current oracle, not a new gap. |
| Collection `[11973,12160)` active-target guards; generation changes `[13386,13463)` | Current target reference, closed/withdrawn state and generation changes are coupled under one gate. No admitted stale-target counterexample escaping the remaining checks was established. Do not mutate private generations to manufacture public behavior. |
| Core `[7076,7103)` disposed/withdrawn OR→AND | Current `HealthcheckServicePolicyTests.AllPolicyMutationMethodsRejectWithdrawnHandles` at577 and Lifecycle qualification at67 already assert ObjectDisposedException through actual withdrawn public handles. Not new work. |
| Binder `[5581,5614)` and `[6066,6097)` equality boundaries | Current Binder tests explicitly cover previous duplicate ordering, old history, first late outcomes and max execution IDs. Public merge/high-water invariants constrain remaining boundary effects. No new counterexample was established. |
| Binder `[9567,9572)` ConfigureAwait and terminal flags | A stalled caller SynchronizationContext is not automatically an adopted context-free completion promise. Do not assert a scheduler flag or waive genuine queued continuations. Any candidate needs a real public progress-contract counterexample first. |
| Protocol `[10740,10782)` monitor GUID and `[10916,10957)` boolean-call removals | ValidateSnapshot immediately revalidates both values when counting/uniquing the same monitor. A test that only kills the first internal call is unjustified. |
| Protocol `[9012,9049)` count guard; `[13391,13444)` equality | The other count/intersection conditions constrain impossible counts; duplicate keys have their own set check. Existing boundary/details tests already exercise actual malformed counts and repeated/ordered keys. No independent accepted malformed payload was established. |
| Protocol depth/UTC/integer boundaries | Current DepthPreallocation, TimezoneBoundary and signed/unsigned Int64 tests directly cover the observable reader depth, UTC endpoint and precision obligations. The later JsonDocument depth limit is additionally dominated by the strict Utf8JsonReader prepass. Not a new batch. |
| Bounded buffer allocation/scratch `[379,441)`, `[1303,1324)` | Initial capacity and reallocation choices can differ without changing committed output; the current exact-cap scratch writer already checks all4096 published bytes. No undocumented reference/capacity identity assertion is queued. |
| KeepAlive removals and explanatory strings | Handle liveness under an injected GC/sweep is a possible separate contract investigation, but this pass established no deterministic public counterexample or complete ownership proof. Message wording, parameter text and default Guid `"D"` formatting are not grounds for new implementation-only tests. No equivalent/exclusion disposition is approved. |

The next decision is either root approval of the single operation-overlap proposal or the actual fresh170 report/capture results. No already-proved147 argument-acceptance row, finite37 result, historical raw status or diagnostic/manual outcome supplies current170/90% credit. All original evidence and denominators remain unchanged.
