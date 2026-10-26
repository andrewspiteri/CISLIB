# Targeted unit/runtime native mutation run u1

The retained Windows run in `.artifacts/hcg/u1/native/` tested seven selected production files against the frozen **815-case snapshot02** Healthcheck unit closure. It completed on 21 October 2026 at 19:44:36 local time after 23:21 elapsed. Native Stryker reported **88.95% and failed the unchanged 90% break threshold**. This is selected-file evidence, not whole-Healthcheck qualification.

| Selected production file | Killed | Survived | NoCoverage | CompileError | Ignored |
| --- | ---: | ---: | ---: | ---: | ---: |
| `Contracts/HealthDiagnostic.cs` | 49 | 7 | 0 | 1 | 11 |
| `Contracts/Options/HealthcheckOptions.cs` | 90 | 6 | 0 | 6 | 18 |
| `Execution/ExecutionFactRecord.cs` | 82 | 6 | 3 | 0 | 9 |
| `Execution/ExecutionFactValidation.cs` | 154 | 23 | 0 | 9 | 13 |
| `Reporting/BoundedHealthBuffer.cs` | 20 | 5 | 0 | 0 | 5 |
| `Reporting/HealthReportFormatter.cs` | 280 | 12 | 10 | 10 | 40 |
| `Reporting/HealthReportProtocol.cs` | 613 | 77 | 11 | 51 | 101 |
| **Total** | **1288** | **136** | **24** | **77** | **197** |

No selected mutant timed out. The strict observed score for this tool-classified subset is **1288 / (1288 + 136 + 24) = 1288/1448 = 88.95027624309392%**. All 1722 selected report rows remain intact. The 77 compiler classifications and 197 ignored blocks have no independent compilation/equivalence disposition in this run; their automatic omission is not release qualification. Native log summaries also mention excluded mutations elsewhere in Core; those are outside these seven selected report files and this table.

The [residual inventory](native-u1-residuals.json) maps **all 160 observed residuals** individually: original status, frozen source path and hash, one-based native location, exact original node, replacement, and source-bound detector/review rationale. IDs are local to u1; compare exact production paths/guards rather than integer IDs from an older report. Its SHA-256 is `f8ff0e23f254f2894ba46040efdfccad5dd3db2ae0e8d6bc93d71918cc774e03`.

The mapping identifies concrete receiving-boundary gaps: malformed collection booleans; execution registration and positive Int64 identity; independent policy/counter coherence; absent settlement and nonterminal statuses; named error views that avoid another rejection masking the fault; precise metric array limits; applicable policy objects; and actual formatter time/zero-duration/error behavior. Root-owned follow-up tests address normalization and execution-record merging. The later Boundary26, Coherence39, InputShape34 and Formatter27 classes, plus the later root normalization/payload/options/diagnostic cases, **receive no retroactive kill credit**. They require their own retained baseline and mutation execution. The earlier [253-case batch note](detector-batches.md) identifies the initial tranche and its retained analyzer failure; root subsequently passed the corrected 982-case full unit baseline separately.

Other residuals need bounded review rather than artificial tests. Examples include repeated object/monitor validation, two independent depth guards, an Int64 parse after canonical parsing, error-buffer constructors that independently force unhealthy selection, default Guid format, allocation-only changes, and exception prose. The inventory records the precise reason for each candidate. It grants **no exclusion, equivalence waiver or score change**. Accepted registration-scoped diagnostic visibility remains a separate contract question; no public field/schema amendment is implied here.

The frozen snapshot04 capture after later authoring contains 1379 files, digest `a061f7387d957b81fc5e690b70e1c60f7c75da302ad55b346752b76ad4c06690`, manifest SHA-256 `3129da71204ea8b18605facbec5b4af668041f10d09fa29a07743748f3d0c6d2`. Capture completion is not native execution or a passing mutation gate. The separate complete unit/runtime lane remains outside u1's scope.

Evidence pins:

| Retained input/output | SHA-256 |
| --- | --- |
| `.artifacts/hcg/u1/native/reports/mutation-report.json` | `bc6fcd8fdddad454281de939ead835bee8176b04fc43c9d3d30bbdc57482ab8d` |
| `.artifacts/hcg/targeted-runtime-01.json` | `19ece1202e524ad31852f198ce2fd82f309a39154bab841f360e804a05c1781f` |
| `.artifacts/hcg/unit-targeted-01.log` | `4cf1572b2b637034f01a156403a1ee53ceea5c7dffd438bd30429ae18b0eb71d` |
| Existing pinned `dotnet-stryker/4.16.0/tools/net8.0/any/Stryker.Core.dll` in the local NuGet tool closure | `bf02f78d3725e1243edb7a32d88dc1a02c88f1f9f74b60b18730636073f234d7` |

The native configuration uses Release/net10.0, VSTest, Complete mutation level, concurrency two, a 10000 ms additional timeout, and the explicit Healthcheck unit plus scheduling-control filter. The established toolchain is SDK 10.0.401/runtime 10.0.12 and Stryker 4.16.0. This reviewer read native artifacts and on-disk pins; root owns execution and broader tool/source restoration receipts. All seven exact frozen production hashes are retained in the residual inventory. No build, test, Stryker or production edit was performed by this reviewer for this receipt. Existing 95% line, 90% branch and mutation/release requirements remain unchanged.
