# Logging processing and convenience checkpoint

16 October 2026. Follows incoming commit `450a3ee3` and common-writer commit `6c346acc`. This is development implementation, not final release qualification.

User selections are implemented: duplicate suppression is opt-in with application-supplied key/window; message-only convenience calls use the stable `log.message` event name. Ordered startup interceptors, global filtering and destination routing share the incoming stage. Callback failures prevent writer admission. Callback recursion is rejected before factories run, while diagnostics stay available. Shutdown expiry abandons blocked preparation and prevents late writer admission. Duplicate keys have bounded size/capacity and monotonic non-sliding expiry; all-writer rejection does not reserve a key.

Callbacks receive immutable, entry-byte-bounded JSON views. Projection changes the retained facts themselves so later transformations cannot resurrect data an earlier redactor could not inspect. Changed attributes preserve the provider's integer event_id within normal shape bounds. Interceptor truncation is counted once. This byte-bound correction was identified during local review after the first broader runs below; final verification must include it.

## Executed checks before final consolidation

| Check | Result | Ignored artifact root |
| --- | --- | --- |
| Windows complete unit project | 3,523 passed, zero skipped/failed | `.artifacts/csharp/focused/56504fa49b2c4b4faea30c9d66c3fdca` |
| Windows complete integration, including 20 exact-package cases | 1,445 passed, zero skipped/failed | `.artifacts/csharp/focused/39f3a392dd9d4ddfb6c8d633206c101b` |
| Ubuntu focused policies, convenience and lifecycle | 306 passed, zero skipped/failed | `.artifacts/corrective/batch-a/linux/.artifacts/csharp/focused/bc5ddaf09dcb44909e2f73f4201d398f` |
| Ubuntu exact Debug package consumers | 20 passed, zero skipped/failed | `.artifacts/corrective/batch-a/linux/.artifacts/csharp/focused/56f961db471e4b0e902917e29da73a86` |
| Windows policy tests after callback byte-bound correction | 22 passed, zero skipped/failed | `.artifacts/csharp/focused/35e076edb20e4938bac7f0539418c48d` |

The initial compiler check still rejected Create(100), but newly declared overloads changed its exact diagnostic from CS1501 to CS1503; the negative control now requires CS1503. An architecture run passed 81/82 and identified three missing declarations in the compile-only incoming snapshot; those declarations were added without weakening the exact-shape assertion. Reference fixtures remain unpublished specifications, not rewritten historical release baselines.

SDK 10.0.401/runtime 10.0.12 pins and the single Core package/dependency closure remain unchanged. Linux uses the existing isolated checkout with scoped current copies, not a complete fresh HEAD checkout. Final comprehensive Developer and post-correction Linux results are recorded below when complete. Coverage, mutation, supported Visual Studio acceptance, cumulative compatibility and release/publication remain separate obligations. No Claude review was requested or performed.

## Post-correction verification

Ubuntu passed 307 focused unit cases at `.artifacts/corrective/batch-a/linux/.artifacts/csharp/focused/5d68a4d25114447d94a9f55ff7d0979b` and all 20 exact-package consumer cases at `.artifacts/corrective/batch-a/linux/.artifacts/csharp/focused/270efcffd3f541558130dd7bce070f88`. Both rebuilt the corrected source; zero cases failed or skipped.

The first comprehensive Windows attempt stopped before test execution on existing formatting in the native focused/coverage verifiers. The second passed solution build and compiler/package checks, then rejected `LogIncomingSnapshot` consuming the newly introduced `IncomingAccounting`. The latter is a readonly scalar record; adding it to the required immutable-value roots makes it subject to the existing field-closure checks rather than bypassing the contract boundary. Two negative controls reject mutable counters and retained writers. The independent full architecture inspection then passed 27 projects, 33 target graphs and both packaged assemblies at `.artifacts/corrective/incoming/architecture-processing/inspection.json`.

Those stopped attempts do not count as Developer passes. The final combined checkpoint includes the native provider-verifier migration documented separately.

The combined run `fa15cb159c6b44d295410de7c390f02f` passed all 3,549 unit and 1,445 integration cases, then passed 615/617 architecture cases. It exposed the old diagnostic IL guard rejecting the generic writer collection and a negative-fixture parser that accepted only nonpartial class declarations. Local inspection also found diagnostics could initialize the combined completion task. Diagnostics now read the existing incoming worker task's completion flag; the guard explicitly permits pure collection/accounting accessors and that status read while continuing to reject task creation. Two new controls reject Task.WhenAll and the lazy runtime Completion property. All 12 focused diagnostic cases passed at `.artifacts/csharp/focused/cfb1f03b1c734c5989ba34061fe548ed`. The fixture parser still requires exactly one intended class and closing brace, now permitting its partial modifier. A new full checkpoint follows these corrections.

The first graph refresh could not persist its SQLite transaction. Its retry succeeded with 10,727 nodes, 50,459 edges and zero warnings/errors, build `9cf1a2306bfbd30caac56a824632dda1a072abcee32589b085b557d4d1da28fc`. The later diagnostic correction requires another final refresh; graph facts do not establish semantic test completeness.

## Final combined Developer result

Windows Developer run `108018f939984364abdd859f3eaa4998` passed with exit 0 and zero validation issues, from 23:30:07 to 23:44:15 UTC on 15 October (16 October local). Executed families: **3,549 unit, 1,445 integration, 619 architecture and 24 business cases; 5,637 total, zero failures/skips**. The successful architecture run includes the corrected partial-class negative fixture and both new diagnostic controls. Solution build, formatting, compiler consumers, dependency/package boundaries, discovery reconciliation and the configured example host also passed. The host remains a configuration scaffold; this does not close the remaining runnable-example work.

Native reports, logs, source inventory and summary are retained under `.artifacts/corrective/incoming/developer-108018f939984364abdd859f3eaa4998`. Summary SHA-256: `529f4690e7239d46c631c0da9f626f55b8bda689344444ca0e10c11cef4792b7`. Source-inventory SHA-256: `696d4a60d7f58030f58611e08299b7257a4f390db99876613fd844d32b0e458e`. This is the observed working-tree candidate based on `6c346acc`, including this corrective batch and preserved unrelated workspace changes; it is not a clean release replay. Mutation and cumulative compatibility are explicitly not executed by Developer and receive no passing credit.

Final Ubuntu checks rebuilt the diagnostic correction and passed:

| Check | Passed | Ignored focused artifact ID below `.artifacts/corrective/batch-a/linux/.artifacts/csharp/focused/` |
| --- | --- | --- |
| Processing, convenience, queue and lifecycle cases | 307 | `b8b88ca9c34441399e8e2a6b880a0a42` |
| Exact-package consumers | 20 | `60753294f8fd4fbf9bc8d8fb6dc9309b` |
| Diagnostic architecture and negative controls | 12 | `f5f783054ad34c4ba2f0b5224d506d97` |

Final graph refresh succeeded and status was fresh: schema 2, 10,729 nodes, 50,467 edges, zero diagnostics, build `3f23e292ff170fdd72a7067afdd7c703a9e62cda69410c81943d5d0128b321e3`. The configured C# compiler/semantic extractors are versions 6/3. Bounded searches resolved `CIS.Logging.Runtime.EventProcessing` and `CallbackViewsAreByteBoundedAndOmittedDataCannotReappear` uniquely without truncation; these navigation observations do not claim all semantic relationships or test coverage.

Remaining obligations are the rest of Batch B's native utility migration, supported IDE acceptance, final coverage/mutation/compatibility, runnable-example and complete release/bundle qualification. Publication remains separate. No Claude review, external payload transfer or publication occurred.
