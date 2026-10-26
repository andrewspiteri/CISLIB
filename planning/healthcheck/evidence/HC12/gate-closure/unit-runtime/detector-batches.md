# Unit/runtime detector batches

This wave adds six native C# unit test classes, 253 expanded cases in 60 test methods. All use `CIS.Tests.UnitTests.Healthcheck`; no production code, project, dependency, SDK or qualification threshold was changed by this owner.

| Class | Cases | Observable behavior |
| --- | ---: | --- |
| `HealthReportProtocolMutationQualificationTests` | 113 | Each optional text, diagnostic, timestamp and duration field; canonical enum/counter representations; secondary lifecycle contradictions; framing and stable error codes. |
| `HealthReportFormatterMutationQualificationTests` | 4 | Real bound execution, monitor and collection state populate the complete report; exact committed-byte writer limits. |
| `HealthCollectionMutationQualificationTests` | 14 | Exact retained metric capacity; required/optional error classification; one accepted evaluation; stale errors; entry/completion boundaries; real foreign identities; snapshot revisions and immutability. |
| `HealthCollectorRunnerMutationQualificationTests` | 25 | Clock-unit and interval endpoints; checked arithmetic; single start; actual timer creation/disposal barriers; diagnostic categories; cleanup/clock precedence and retirement without invented outcomes. |
| `HealthExecutionStateMutationQualificationTests` | 71 | The receiving state boundary independently validates ordinary detached scalar/identity input using actual issued targets; malformed input cannot partially commit. Also covers valid zero durations/delay, diagnostic-only lifecycle, replacement identity and real foreign targets. |
| `HealthReportProtocolBoundaryMutationQualificationTests` | 26 | Coherent compact count endpoints and readiness truth table, stable error view/message rules, mandatory nonblank Unicode text. |

The first two classes passed as part of root's isolated snapshot02 full Healthcheck unit baseline: 815 passed, zero failed/skipped and zero native build warnings/errors. Retained log: `.artifacts/hcg/unit-baseline-02.log`; selected TRX: `.artifacts/hcg/inputs-02/source/.artifacts/csharp/focused/bd4753a36ab748e4a350491ff9472c19/selected.trx`. The initial hostname boundary expectation was corrected to the actual 255-scalar contract before this pass; its earlier failed attempt remains retained.

Snapshot03's subsequent build stopped at the single `MSTEST0052` analyzer error in the execution-state class: an explicit `DynamicDataSourceType.Method` argument was redundant. The canonical correction removes that argument only. Root retains `.artifacts/hcg/unit-baseline-03.log`; it is build-failure evidence, not executed-case evidence. The later four classes and the corrected combined closure require root's fresh snapshot04 native baseline. No passing case, kill or score is inferred from source inspection.

Frozen source SHA-256 values at this note:

| File below `implementations/csharp/tests/CIS.Core.UnitTests/Healthcheck/` | SHA-256 |
| --- | --- |
| `Reporting/HealthReportProtocolMutationQualificationTests.cs` | `434b0458f31109b1b16e9f9895d2e8db9128dc4882d7961f45aa011cf979afe8` |
| `Reporting/HealthReportFormatterMutationQualificationTests.cs` | `efbdf0b901e0d4c03304a7753f23d5d0910331eb4c20510f3d818f5c2d5454f2` |
| `Collection/HealthCollectionMutationQualificationTests.cs` | `ec694dde22c7be4767caa9e9bf93905edcc78f4ea595132f620e02f294685377` |
| `Collection/HealthCollectorRunnerMutationQualificationTests.cs` | `bc46841707f49779eac278284ca6cc1685846b2f4615710062d581d18853e483` |
| `Execution/HealthExecutionStateMutationQualificationTests.cs` | `c0b02016052e4d28b4b80e7e51cd8545aeeeba4e23f7e6553e6b5b4c18a2957d` |
| `Reporting/HealthReportProtocolBoundaryMutationQualificationTests.cs` | `c41187ea60b8525b47f2cd501846997625baaf7e3b39e083e41eea7066926741` |

The immutable starting full report remains `.artifacts/healthcheck/release-next/unit-runtime-windows-02/native/reports/mutation-report.json`, SHA-256 `c07ea375e6b0495d9929283ab962b4ae2a03d9502d6991c41b003f8849b9fd6e`. Its 649 Survived, 128 NoCoverage and 75 Timeout statuses are unchanged. Root's targeted seven-file Stryker run under `.artifacts/hcg/u1/` is a separate, incomplete experiment as of this note; its native classifications and original report must remain distinct. No compiler rollback, timeout, ignored block, proposed equivalence or selected-file result grants whole-Healthcheck qualification or lowers any gate.

Registration-scoped integration diagnostics have a separately reviewed visibility question: the state retains them in `RegistrationDiagnostic`, with no current public projection. The existing `Diagnostic` field has health-observation semantics, and execution-specific diagnostics require a real execution identity. HC-HB-05 is in the Heartbeat integration scope, while the current fact matrix accepts registration-scoped errors. Root is obtaining the user's explicit contract decision; this note makes no API/schema change and grants no equivalence waiver for the affected raw mutants.
