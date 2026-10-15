# Batch B: native shipping coverage summary

15 October 2026, base `62ab9b99`. Continued under the user's instruction to implement remaining corrective items. No shipping code/dependency or coverage threshold changes. The logging chat was contacted and remains paused; shared build outputs are owned by this chat during the work. No Claude reviews.

## Migration and observations

The active `eng/verify-shipping-coverage.ps1` invocation now calls `CIS.DependencyChecker shipping-coverage`, replacing the retired Python summarizer. The original script remains recoverable from the base revision and historical evidence snapshots. PowerShell still owns the existing collection/provenance orchestration; its later substantive-policy migration remains open.

| Original responsibility | Native implementation/control |
| --- | --- |
| Source hashes, declared non-executable files, actual evaluated Compile set | Same inventory in ShippingCoverage; missing declarations, unmeasured files, compile drift, foreign owners/paths fail. MSBuild evaluation uses bounded ProcessRunner. |
| Passing/nonempty native TRX and unique Cobertura attachment | Missing/failed/skipped tests, multiple counters, absent/multiple/conflicting attachments fail. Byte-identical duplicate attachment copies remain permitted. |
| Native assembly/class/source/line identities | Same identities retained; duplicate point and unknown assembly/source controls. |
| Native totals and branch denominator integrity | Malformed branch counts, mismatched native totals and cross-report denominator drift fail. |
| OR line execution and MAX covered branch counts | Positive complementary-report control proves no invented branch union; distinct classes on one physical line remain separate points. |
| Per-source, per-assembly, aggregate counts and rates | Same observation schema, hashes, uncovered/partial lines and explicit declaration entries. Per-assembly failure remains failure even when aggregate meets thresholds. |
| SeverityTokenParser branch calibration | Missing covered calibration branches and zero assembly branch denominator fail. |
| Immutable output and status | Existing output cannot be overwritten; invalid evidence returns 1, measured threshold failure 2, qualified measurement 0. Invalid input does not produce an output report. XML DTDs are rejected. |

`ShippingCoverageTests` has 28 native cases: three positive aggregation/threshold scenarios, 23 invalid-evidence scenarios, missing/DTD report control, and command/output preservation control. These are parser controls with synthetic reports and explicit Compile observations, not production coverage measurements.

## Native checks and real-report equivalence

- Windows: 28 focused coverage cases passed; final combined selection passed **48** (28 coverage + 20 Type Manager input cases). Final report `.artifacts/csharp/focused/829719c23cb647efa66fc9719af544ff/`.
- Ubuntu 24.04 native SDK/runtime in the existing isolated checkout: **28 passed**, zero failures/skips. Report `.artifacts/corrective/batch-a/linux/.artifacts/csharp/focused/3312874550e5459392f4cbf9a8a22728/`.
- Windows Type Manager startup stories: **10 passed**, report `.artifacts/csharp/focused/6a98a51adcf648709ac66f51611d13e1/`. User confirmed the reported defaults issue was already resolved; current behavior is preserved.
- Existing four-family native reports under `.artifacts/csharp/tasks/wp07-s02-t01/coverage-current/` were replayed through both implementations against `.artifacts/csharp/tasks/wp07-s02-t01/windows-work/implementations/csharp`. All **78** inventoried shipping C# files match that run's recorded source hashes. Both summaries report 2,891/2,996 lines and 2,112/2,331 conservative branches; both correctly return **2** with `qualified:false` because not every assembly passes.
- Complete observation comparison has **zero differences** after sorting source arrays by path, comparing JSON numeric values and using attachment hashes in place of locators among byte-identical duplicate files. The C# implementation uses ordinal ordering, while Python Windows Path ordering was case-insensitive; one of the identical attachment copies was also selected at a different path. No count, rate, source hash, declaration reason, threshold result, report hash or uncovered/partial line observation is omitted from the comparison.

Replay files are retained under `.artifacts/corrective/batch-b-coverage/` (`legacy-matching.json`, `native-matching.json`, `parity.json`). The first attempt against current sources correctly rejected missing measurement for newer `ElapsedBudget.cs`; it is not credited as coverage. That attempt also exposed an uncaught InvalidDataException in the new CLI boundary, which was fixed and receives a native command failure control.

No fresh full coverage collection, full Developer/release suite, IDE check or mutation run is credited. This is migration equivalence and native control evidence, not final candidate qualification. Remaining script migrations and logging corrections continue separately.
