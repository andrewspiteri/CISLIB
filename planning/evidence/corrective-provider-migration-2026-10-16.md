# Provider behavior verifier migration

16 October 2026. Corrective C02 / Batch B; this changes maintained verification, not shipping provider behavior. Historical WP06 reports and their original scope remain unchanged.

The former `verify-provider-behavior.py` reader had meaningful assertions missing from the existing native validator. They now live in the C# validator and native tests, rather than being discarded or wrapped. Its SHA-256 before retirement is `6b5dbf2ce28759dd359547b2075455b200b4b03156bdd2a67458400abe2ff25b`.

| Former observation | Native binding |
| --- | --- |
| Fourteen scenario IDs in shared catalog order | `ProviderBehaviorVerifierTests.NativeModesMatchTheSharedCaseCatalogInOrder` |
| Exact child PID, runtime, OS, Core/fixture hashes, physical versus controlled modes, released lease and actual file bytes | `ProviderBehaviorProcessTests.ProviderInterestAndRejectionUseActualCore` plus `ProcessResult.ProcessId` from the actual spawned process |
| Formatter/state calls, interest gates, invalid/truncated counts, exception identity | `ProviderBehaviorExpectations.Validate`, exercised in both native unit and process cases |
| Ordered outputs, exact safe fault codes, complete accounting, stop shape, fixed boundaries and flush statuses | Same validator; corruptions require the corresponding assertion reason |
| Strict UTF-8/LF/no-BOM records, byte/count bounds, schema/service/process facts, truncation marker | Same validator; blank lines cannot silently disappear during splitting |
| Exact accepted prefix, retained template tail, Single widening, duplicate-key resolution, root shape and no partial admission | Same validator; recursive JSON comparison distinguishes integer from floating-point syntax and Boolean values |
| Twenty-one original corruptions | Twenty-one native data rows, each first validating an unmodified real fixture report, then checking the exact rejection reason |
| Additional migration controls | Three native rows reject an inserted blank line, changed service and extra stop-result member; physical child tests also reject changed PID and lease state |

The original reader had no active invocation or import in current engineering entry points, verification manifests or nonhistorical guidance. Retirement follows actual native verification. Native TRX/focused summaries identify executed cases; the historical Python receipt is not relabelled as native evidence.

Windows native focused runs passed 41 unit cases at `.artifacts/csharp/focused/eae5ed180a744bc78017937fc9ae57ce` and 14 actual child scenarios at `.artifacts/csharp/focused/2f70ea075d5b49a688900deb6a98010c`, with zero failures/skips. SDK 10.0.401 and runtime 10.0.12 remain pinned. Ubuntu and final combined checkpoint results follow below.

This is one bounded migration slice. Other DiscoveryHost readers, Type Manager compatibility/package/instrumentation/mutation utilities, substantive PowerShell policy and IDE acceptance remain open. Full coverage, mutation, cumulative compatibility and release qualification are not credited by these focused tests.

Ubuntu native runs passed the same 41 unit cases at `.artifacts/corrective/batch-a/linux/.artifacts/csharp/focused/e68e0a9e2f0b44c0bd352a84b0202668` and 14 child scenarios at `.artifacts/corrective/batch-a/linux/.artifacts/csharp/focused/3045d3892fc9448dae0fdfdfadc67e3c`, with zero failures/skips. The isolated Ubuntu checkout was updated with the scoped current sources; this is not a fresh whole-repository checkout. The obsolete Python reader was then removed.

The final combined Windows Developer checkpoint also passed all 5,637 enabled native cases, including this migration's unit/process controls. Run `108018f939984364abdd859f3eaa4998`, retained artifacts and the exact result scope are recorded in the [processing checkpoint](corrective-processing-2026-10-16.md#final-combined-developer-result). This completes this provider-reader slice, not the remaining Batch B migration.
