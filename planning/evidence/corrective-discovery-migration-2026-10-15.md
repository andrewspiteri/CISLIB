# Batch B: native discovery-driver migration

15 October 2026, based on `e0fd6aba`. The user asked to continue the corrective implementation map. The logging chat was notified and the shared build/test/graph window reserved. This slice changes verification only; shipping logging and Type Manager behavior, runtime pins, publication records and historical evidence remain unchanged. No Claude review was run.

## Invocation inventory and retirement

The maintained `eng/` and `implementations/csharp/eng/` entry points do not invoke `tests/fixtures/DiscoveryHost/verify-process.py`. Its maintained references were the test README and discovery-fixture guidance. One historical executable reference remains in `planning/environment-options/evidence/static-constructor/native-linux.sh`; historical reports, review payloads and source snapshots also retain references. Those records are preserved and must replay against their recorded source, not today's checkout.

The driver is retired after observation reconciliation below. Its source is recoverable at base revision `e0fd6aba`. This is one of the original 92 Python files, not completion of the remaining migration or an assertion that every remaining file is active. Current entry: `pwsh -NoProfile -File eng/test.ps1 -Project IntegrationTests -Class DiscoveryProcessTests`. Linux can invoke the same C# checker directly as documented in `docs/modules/logging/discovery-fixtures.md`.

## Observation map

Native class: `CIS.Tests.IntegrationTests.DiscoveryProcessTests`. Input is the project-built nonshipping DiscoveryHost with its referenced Core assembly. Five native scenarios read real static-constructor facts; assembly-version/unavailable modes remain controlled internal projections. They do not prove those facts are unavailable on the executing machine.

| Retired driver's observation | Native destination and disposition |
| --- | --- |
| Seven mode/label combinations, canonical/alias/blank/build precedence | Existing projection and version-source cases plus all seven combinations in `ChangingChildTelemetryLabelsKeepsBuildFactsAndParentEnvironmentStable`. Explicit expected builds and labels, no tautological source-label assertion. |
| Exact application/version shape, null CommitId/BuildId, entry informational/assembly/file decoys | Shared `AssertProjection`, now applied to every scenario. |
| Core name/version, distinct informational version, actual loaded fixture/Core hashes | Shared `AssertProjection`; wrong name and Core bytes have corruption controls. |
| Exact .NET 10.0.12 and actual OS family | Explicit `dotnet exec --fx-version 10.0.12 --roll-forward Disable` and observed-runtime/OS assertions; wrong runtime has a corruption control. |
| Static-constructor versus controlled projection binding | Shared assertion; deliberately wrong binding is rejected. |
| Canonical nonempty UUID, UTC initialization inside measured interval | Shared assertions; empty UUID is rejected. The combined seven-child sweep rejects repeated IDs. |
| Native hostname, OS description, architectures, runtime, PID, UTC process start before initialization; collector hostname decoy | Shared native-field assertions. |
| All seven unavailable optional runtime values remain null | Shared unavailable-mode assertions. |
| Telemetry labels cannot change application facts | All five native combinations compared in the combined sweep. |
| Remove every configured key/alias before child overrides | Canonical configuration catalog copied by the integration project and read by the test. Windows ambient-decoy execution passes. |
| Entire parent environment unchanged | In-memory comparison around every child launch, including failure cleanup; no environment values in assertion diagnostics. Class uses `DoNotParallelize` to avoid unrelated test mutations during the comparison. |
| Nonzero exit/stderr, bounded child duration, separate streams, retained output/status | Existing bounded TestSupport.ProcessRunner, zero exit/empty stderr assertions, stdout in native TRX, focused runner's fresh summary/logs and failure policy. The old standalone JSON schema is intentionally replaced by native reports; no release gate consumes it. |

The case inventory retains the three original acceptance method mappings (3 + 1 + 3 executions) and adds `INT-DISCOVERY-OBSERVATION-CONTROLS` for five controlled observation-corruption cases. Each control first validates a real successful child, changes one observed field and requires assertion failure. They are validator checks, not mutant classifications or modified-candidate qualification.

## Executed evidence

Windows uses the private SDK 10.0.401/runtime 10.0.12 established in Batch A. Ubuntu 24.04 under WSL uses the existing native SDK 10.0.401/runtime 10.0.12 and separate checkout/output directories. Only the two changed integration source/project files were copied into that previously qualified Batch A checkout; shipping sources were unchanged. Native run metadata and report hashes are in the adjacent JSON index.

- Windows focused run: **12 passed, 0 failed/skipped**. Report root `.artifacts/csharp/focused/5fcda5fb7ce146f89415fb1a4c73b60f/`.
- Final Windows ambient configuration run: **12 passed, 0 failed/skipped** with canonical/alias service and version decoys and an invalid ambient log level in the command environment. Report root `.artifacts/csharp/focused/88218fea4fb640fdb982bf34ab4a44b5/`. Incremental focused body: 11.20 seconds, including 4.38 seconds build and 6.74 seconds native discovery/execution; bootstrap is excluded.
- Final Ubuntu native focused run: **12 passed, 0 failed/skipped**. Report root `.artifacts/corrective/batch-a/linux/.artifacts/csharp/focused/008ee2e694af4e1c92ddd95252eab7fd/`. This includes native compilation and execution, not Windows-built binaries. Final runs include the local-review correction requiring exact application/version field sets and present null-valued optional fields.
- The initial sandboxed Windows build failed without compiler diagnostics. The normal native invocation above passed; the failed attempt is retained at `.artifacts/csharp/focused/dcec1be810ce4d1caeb24f8672b321de/` and is not credited.

The normal Windows Debug solution build passed with zero warnings/errors before the final assertion tightening; both final focused runs rebuilt the affected project. The JSON index retains the build log hash and graph refresh output location. Graph navigation began fresh/valid with 10,143 nodes, 47,211 edges and zero diagnostics; the bounded test-related query returned seven nodes without truncation. A broad historical text search was output-truncated and replaced with bounded source/entry-point inspection. Full integration/Developer/release suites, IDE acceptance, coverage and mutation were not run for this isolated test migration. No claim is made for those gates. The remaining 91 Python files need individual active-invocation/observation reconciliation; C–F are still open.
