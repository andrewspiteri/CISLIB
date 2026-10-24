# HC12 score and failure ledger183

The accepted aggregate remains **1591/3065=51.908646%, unqualified**. Its actual source is the original22October16:27:38 union40 result, not active143/170/180 progress. Input38492223bytes/SHA `496905d6f61b8dc477e96b23da2efbc1295881c7e064bab6114dce807d20a9b5` at `C:/Users/andrs/AppData/Local/Temp/cis-hc12-execution72/union40-original-result-diagnostic83.json` was hash-stable before/after this read-only projection.

| Original accepted partition | Accepted kills | Held raw kills | Survived | NoCoverage | Timeout | Denominator |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| ASP.NET | 72 | 0 | 8 | 0 | 0 | 80 |
| Pipes | 111 | 42 | 72 | 0 | 3 | 228 |
| Unit/runtime | 1408 | 1027 | 220 | 34 | 68 | 2757 |
| Total | **1591** | **1069** | **300** | **34** | **71** | **3065** |

The table preserves the original37 accepted equivalent exclusions (22Pipes Survived,14Unit Survived,1Unit NoCoverage);306CompileError/719Ignored are outside this denominator. Eight ASP survivors have reviewed noncritical dispositions and still contribute zero numerator. Unit's accepted finite Timeout→AssertionKill accounts for its raw69 versus effective68 timeouts. No disposition was added here. Even hypothetical recovery of all1069held kills gives2660/3065, below90%; at least99 further accepted kills would still be needed with this fixed denominator. That arithmetic is not a forecast of recoverable credit.

## Actual held-definition causes

The tiny C# projector joins issue selectors to each original group's source-bound `definitionIdentity` and original report SHA, deduplicating1069definitions. It does not treat93780 repeated issue rows as93780mutants. Exact per-definition codes/identity/report bindings are in `projection01/definitions.json`; no mutation IDs are transferred between reports.

| Observed receiving category | Unit | Pipes | Distinct held definitions |
| --- | ---: | ---: | ---: |
| Abnormal/incomplete protocol only | 782 | 22 | 804 |
| Normal-host failed result lacks direct/accepted semantic assertion only | 227 | 15 | 242 |
| Both categories in original records | 18 | 5 | 23 |
| Unaccounted in these two sets | 0 | 0 | 0 |

Thus827held definitions touch abnormal/incomplete hosts,265touch nondirect failed results, with23overlap. These are affected source definitions, not827independent root defects or265new assertion bugs. Abnormal set uses the exact original AbortOrCancel/CompletionFailure/ResultSetIncomplete/CompletionMissing/TerminationMissing/AbortedResultTail/ControllerStopped codes;827have ResultSetIncomplete,822have AbortOrCancel. Every1069has NativeAssertionWitnessMissing. Global StatisticsMismatch and child-propagation issues also remain, even though their selector sets intersect no held definition. Counts do not waive them.

The accepted report's nondirect issue details contain host/GUID, not actual exception/stack text. Accordingly this ledger does not invent counts for ArgumentException versus cleanup/timeout subtypes. Concrete runtime attribution must come from the corresponding actual source-pinned result/error records. The current1389-case identity catalog is used only to name historical GUID references; none of its current outcomes is imported.

## Two repair priorities

1. **Finish real host/resource closure for the827affected set.** Reuse current whole-owner fixtures rather than create another broad campaign: Unit `HealthCollectorRunnerLifecycleTests.RunAndReleaseScenario`/`AssertLifecycleOwnership` and MutationQualification owned-Start handling; Pipes `OwnedTimer`, `PendingDispose` and `Concurrent` companions. They retain actual IO/timer/service joins and terminal assertions after real owners settle. First inspect a bounded closed current-host/failure batch and repair only a source-proven fixture/capture obstruction. Current170's known1038 null-entry invariant failure and older143's two SignalProgress background SIGABRTs remain holds; they do not justify weakening cleanup, relabeling a compiler outcome or catching arbitrary worker failures. The older count is exposure, not a demand for827fixture changes.

2. **Resolve genuine completed-result assertion gaps for the265affected set, starting with shared valid-input boundaries.** Historical nondirect GUID1841d608… (`DetailedFormattingPreservesEveryActualExecutionCollectionAndIdentityField (True)`) touches143held definitions;1074229f… (`RegistrationDiagnosticFixturesAndEarlierOmissionRemainCompatibleAndMalformedUnicodeIsRejected`) touches97. These sets overlap and must not be added. Current Formatter source already uses narrow `AcceptedDiagnostic` calls, with reserved Collection construction INSIDE the original `try/finally state.Complete` (178 repair). Current RegistrationDiagnostic source has narrow constructor/projection and actual valid-fact reporting assertions; intentional invalid checks remain. Verify the corresponding current campaign results before proposing any further edits. A remaining runtime error needs its exact original phase, real cleanup and narrow acceptance premise; broad catch/reclassification would hide it. The successful176diagnostic proves one identifier variant's229direct rows, not265new mutation kills or every historical failure cause.

Use an initial20minute source/evidence triage budget per new failure family, then propose one bounded target/cohort and one meaningful original-control/actual-error control if needed. This is an investigation-work budget, not a changed test/controller deadline or permission to terminate an active campaign. Do not repeat successful controls just to spend the budget. Keep143/170/180 running/unfinished evidence separate until their actual reports, source scope, original/fresh cohorts, capture/kernel/child/restoration audits close. ASP93's later clean full partition is completed independent evidence; it has not updated this old accepted three-report aggregate.

## Current source references and proof limits

All references are under `implementations/csharp/tests/` in the shared repo, read only:

- Unit `Healthcheck/Reporting/HealthReportFormatterMutationQualificationTests.cs`: d27976c43b7ee3eef9ef5b594ac09ec97fa638517d937cf773ee225a9acc3bca; valid constructors40–43, protected reservation123–127, helper279.
- Unit `Healthcheck/Execution/HealthRegistrationDiagnosticTests.cs`:564d3c843ce6342dd5a034f51a30b7cb160d995d4ff567c3515d82abc94a23fd; historical named case337, valid-fact boundary360, constructors434/projection445.
- Unit Lifecycle source10792596d8d8e864a6e4467f866703ad9504873189090ae4dc29144179a7deff and MutationQualification8baa72ebcd28cb88eefe70b053d6ab43cd307f040f2656070e824fcb1972c867.
- Integration OwnedTimer5247c5c867d1933e83d8ad366d88a621bb58e3f1f150619d838fdf663b0bfe71; PendingDispose05ed1baebd747ac74de63ca5165796605f604a45d3b53d48bfd061984c7a43a2; Concurrent e91a01009c15f7369ee8c28dd19ec699c731c0c4491da3cc5c59b02c9e38e889 (filename `HealthcheckPipeTransportTests.Concurrent.cs`).

Projector Program SHAa88245014d2ec9c4c5759884fee7a971501c803a1f258a78d581ab52ed863874, DLLc83eafecfe713d6cdd6b897a8be34c41a656bc4075b1993f329105fbf0f6364b. Private SDK10.0.401 build01 passed0warnings/errors; entry01 exited0. Exact arguments/logs/exits are retained. `projection01/ledger.json` SHA7b57b618d65cf3b1132064695cb0535e1e1444a9f967c0cc61468ef934112ffb; per-definition rows SHA2726e9f31e1beafa61a1641ed26e39b4d79f0b75efd0ec98d56c006093d25167. The projector rejects unknown/missing/duplicate fields in the consumed union root/partition/raw-count/issue/unresolved shapes and duplicate source identities; unconsumed original audit/disposition proof bodies remain original authority. It does not re-execute the union validator/classifier, rederive source identities from reports or make a new release decision. No shared build, native subject, source/policy change or credit.
