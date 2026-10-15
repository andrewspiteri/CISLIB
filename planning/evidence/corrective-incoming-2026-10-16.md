# Bounded incoming logging checkpoint

16 October 2026. Development evidence following registered-writer commit `74469ffd`; this is a partial Batch D checkpoint, not release qualification.

Public runtime creation now uses a bounded incoming memory queue with reject-new overflow. Caller capture owns mutable values and scope/correlation before returning. One background processor encodes and prepares events for independent writer queues. Successful calls receive an incoming sequence; later writer delivery is observed through diagnostics and flush/stop. Startup registration remains fixed.

Flush captures an incoming prefix and registers downstream boundaries before later input can pass it. Stop uses one monotonic budget across both stages. At expiry, queued and still-preparing input is abandoned and cannot start writer I/O later; already-started writer operations remain pending until they settle. Caller cancellation detaches the waiter. Completed observations remain immutable, and Completion retains actual worker/cleanup completion.

Incoming accounting preserves `Accepted = Processed + Failed + Abandoned + Pending`. Writer rejection is a subset of processed input and prevents a successful covered flush/stop. Incoming queue overflow remains a separately observed rejection. Internal output-only qualification seams remain available for existing queue tests.

## Executed native checks

All paths below are repository-relative ignored artifacts. No skipped or failed cases remain in these final runs.

| Run | Passed | Artifact root |
| --- | ---: | --- |
| Windows complete unit project | 3,491 | `.artifacts/csharp/focused/9741acd4973b49108abc9f4eb5fa3af0` |
| Windows complete integration project | 1,444 | `.artifacts/csharp/focused/c6adc59fc7294ff9be81bc366450e3e3` |
| Windows complete business project | 24 | `.artifacts/csharp/focused/e6b5ef242f1346c48ebf7d7aff58e3fc` |
| Windows architecture: LoggerFoundationArchitectureTests and RuntimeContractTests | 82 | `.artifacts/csharp/focused/156c1ab620f649949cbba14aee516d92` |
| Exact Debug package consumers (also included in final integration) | 19 | `.artifacts/csharp/focused/a941f8b5defe4b8883e9af1034947860` |
| Ubuntu incoming, registered writer, stop/flush and output-queue cases | 197 | `.artifacts/corrective/batch-a/linux/.artifacts/csharp/focused/0bb218962a424523be7e2976a783833a` |

The pinned Windows SDK is 10.0.401, runtime 10.0.12. Ubuntu runs use the retained isolated checkout with scoped current source/test copies and the pinned Linux SDK, not a fresh complete HEAD checkout. The startup reference contract was regenerated and its compile-only consumers passed; it is an unpublished reference, not a rewritten historical baseline.

Initial failures exposed assertions tied to synchronous writer receipts: four shutdown cases now assert incoming tickets and exact input settlement; the package backpressure case now correctly expects unsuccessful drain when accepted input is rejected downstream. Exact accounting assertions remain. Diagnostic/contract shape checks now include the immutable incoming snapshot. A cancelled-wait test was corrected to the actual OperationCanceledException contract. Final runs above supersede those intermediate failures without hiding their causes.

Local review covers gate ownership, prefix ordering, timeout abandonment, worker startup rollback, caller cancellation and immutable counters. Whitespace-only formatting followed the behavioral runs. No fresh coverage, mutation, complete Developer/release profile, supported Visual Studio IDE qualification or publication is claimed. Common built-in writer composition, Batch E policies and the remaining native verification migration remain open.
