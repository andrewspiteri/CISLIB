# C# interpolation native evidence

These records come from successful Developer run `7952095e19d34f0c8eadd3a0b509897d` on Windows x64. The [execution record](../../template-api/README.md) states the implemented behavior, source point, intermediate corrections and qualification limits.

- `developer-summary.json` and `developer-validation.json` are exact copies of native checkpoint reports: 7,016 passed, zero failed/skipped, exit zero and no issues.
- `acceptance-native-results.json` is the actual native discovery/execution reconciliation subset for 21 interpolation case IDs and 22 native executions. It is not a second execution or additional test count.
- `interpolation-costs.json` is the actual attached measurement file from the successful unit report's `RecordOwnedCaptureAndConsumerFormattingCostsSeparately` result, execution ID `3f4a6c8f-77e4-43d8-b84a-5442939a04b1`. SHA-256 is `4AC992243FD64F84DC60B293719D17E11BD37C80D4B1154857F3229EDD4BDBA1`.

The tested source inventory SHA-256 is `b00855a698c82b465536586a3f493da6b89e56f5569327858428d1ef4c1c73d9`. After execution, 861 implementation/test/tool/reference/contract inputs were checked against it with zero mismatches. Completion reporting and the contract's qualification label were then updated; the archived tested contract preserves the earlier label and identical behavior fields.

Raw reports, source inventory, compiler controls, package inspection and the exact local package are retained in `.artifacts/csharp/interpolation/developer-final-7952095e19d34f0c8eadd3a0b509897d/`. Summary paths retain their native meanings and are resolved against that archived run. The archive can also contain older attachment directories retained by the runner; only attachments referenced by this run's native reports receive execution credit.

Measurements cover actual synchronous owned capture/rendering helpers, not complete caller, queue, writer or arbitrary user formatter costs. Developer's disabled mutation and compatibility families, Linux execution and release qualification receive no pass from these records. The local package was not published.
