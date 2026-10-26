# Complete Pipes mutation: native Windows 01

`pipes-windows-01` fails mutation qualification and child-evidence validation.
The retained exit record is `nativeExit: 2, auditExit: 1`. Native Stryker displays
50.00%, while the strict score without timeout kill credit is
**89 / 228 = 39.0350877193%**. The actual v1 child auditor reports
`InvalidChildEvidence` with 271 failures. No result is exempted, reclassified or
promoted to qualified by this audit.

The frozen configuration selects all four Healthcheck/Pipes sources at
`Complete`, with explicit `coverage-analysis: off`, two sessions,
`disable-bail: true`, Release/net10.0, VSTest and a 90% break threshold. Its mixed
IntegrationTests/UnitTests filter includes the PipeTransport and ProbeProcess
classes, the unchanged Protocol and Formatter test closure, and each assembly's
scheduling control. The native log reports **276 tests found** for the initial
run. This discovery count does not assert completion of the separately retained
restored build/TRX checks.

## Selected four-source outcomes

Paths below are relative to `implementations/csharp/src/CIS.Core`.

| Source | Killed | Survived | Timeout | NoCoverage | CompileError | Ignored | Total |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `Healthcheck/Pipes/HealthPipeBudget.cs` | 4 | 5 | 0 | 0 | 0 | 3 | 12 |
| `Healthcheck/Pipes/HealthPipeEndpoint.cs` | 13 | 13 | 1 | 0 | 1 | 6 | 34 |
| `Healthcheck/Pipes/HealthPipeServer.cs` | 34 | 61 | 23 | 0 | 49 | 54 | 221 |
| `Healthcheck/Pipes/HealthProbeClient.cs` | 38 | 35 | 1 | 0 | 10 | 12 | 96 |
| Total | **89** | **114** | **25** | **0** | **60** | **75** | **363** |

The strict denominator is Killed + Survived + Timeout + NoCoverage = 228. The
native score credits timeouts: `(89 + 25) / 228 = 50%`. This audit gives timeouts
no kill credit. CompileError and Ignored remain outside that numeric denominator
and require their own review/replay; neither status is an equivalence waiver.

All four expected report source objects are present. Their original report text,
normalized from CRLF to LF, matches the frozen scope inventory with zero
omissions or mismatches. The corresponding four files in the retained
`pipes-inputs-01/source` snapshot also have those same hashes. This four-file
comparison does not replace the complete before/after source, project, fixture,
toolchain and candidate-binary inventory. The v1 session lists 861 source inputs.

The report contains 146 source objects, with no retained mutant records in the
142 objects outside this configuration. The native whole-Core generator log
records 10,241 mutants created: 1,490 CompileError, 75 block-covered Ignored and
8,448 mutate-filter Ignored, leaving 228 mutants tested after 10,013 were
skipped. Only **60** of those prefilter CompileError statuses are retained in
the selected four-source report; the difference is 1,430 outside the selected
scope. These generator totals do not establish whole-Core mutation execution.

All selected CompileError records have reason `Mutant caused compile errors`;
all selected Ignored records have reason `Removed by block already covered
filter`. The native log includes Safe Mode rollback of `HandleClientAsync`.
Compiler rollback does not prove each affected mutation independently
uncompilable. No rollback or block-covered disposition is approved here.

## Actual v1 child audit

This run used the retained `private-launcher-05` auditor and the session schema
`cis.healthcheck.stryker-children/v1-development`. Its result requires kind
`probe`, has no required single active-mutant ID, and records coverage analysis
off. It is separate from later v3 helper proofs and their different inputs.
Those later results do not repair this run's lifecycle evidence.

The session has **9,814 launch directories**. The auditor accepts 9,543 rows:
55 uninstrumented baseline probe rows and 9,488 instrumented selected probe
rows. Their committed exit codes are 1,099 zero, 938 one and 7,506 two. Probe
negative-control exit codes are recorded outcomes; this note does not treat all
nonzero probe exits as audit failures.

The remaining **271 directories** are the actual audit failures:

| Retained files / recorded condition | Count |
| --- | ---: |
| Only `parent-failure.json`; each records `System.InvalidOperationException: A private child stage unexpectedly already exists.` | 165 |
| `launch.json` and `receipt.json`, without committed `exit.json` | 105 |
| `launch.json`, `receipt.json` and an uncommitted `exit.json.*.tmp`, without committed `exit.json` | 1 |
| Total | **271** |

The 165 parent failures divide into 55 records for each of three parent PIDs
(12428, 67716 and 84676). Those records do not identify an active mutant or prove
a child launch, so this audit makes no mutation-status attribution for them.

All 106 incomplete-exit records have an instrumented child receipt and a null
receipt `Failure`. They span 17 native-Killed IDs: Budget ID 2708 has one record;
the remaining 105 are ProbeClient IDs 2985:20, 2986:20, 3009:9, 3011:9, 3012:7,
3022:10, 3023:9, 3025:2, 3026:1, 3027:1, 3028:2, 3029:2, 3033:10, 3043:1,
3053:1 and 3057:1. These native Killed labels remain unchanged in the report;
the incomplete lifecycle evidence cannot independently qualify their
child-origin/exit closure. Missing committed exits do not establish a specific
exit code, forced-termination cause or expected-failure classification. The
temporary file supplies no committed-exit credit.

## Exact retained pins

The following paths are repository-relative. SHA-256 values are of actual file
bytes, without line-ending normalization.

| Artifact | SHA-256 |
| --- | --- |
| `.artifacts/healthcheck/release-next/pipes-windows-01/config.json` | `1a8f81da060bbcfd22a58fe5ea99aa2307a33c6a466700a5c9a2103a1baff532` |
| `.artifacts/healthcheck/release-next/pipes-windows-01/native/reports/mutation-report.json` | `29424c1ebfea6a595163a04c67941d2ae71ddf7321fb6e798f61eb0aa17fd8fb` |
| `.artifacts/healthcheck/release-next/pipes-windows-01/native.log` | `0d966833f6ba9f8b15a2293579d32035703270d7df0357dcaae110533da381a7` |
| `.artifacts/healthcheck/release-next/pipes-windows-01/audit/results.json` | `cadfdebaa5222b7574357192a82ccb50973d4a7a5ab928f28fc10ad6908d9cb2` |
| `.artifacts/healthcheck/release-next/pipes-windows-01/audit.log` | `b9786318b2e2d794f62740476898087bab9c7db5433750a89df96149597ed91c` |
| `.artifacts/healthcheck/release-next/pipes-windows-01/session/session.json` | `3a0ee95661cfd9dbce5182b0ebf9ada70dbca46a7f8b0f0dc4dbd4b52b354f16` |
| `.artifacts/healthcheck/release-next/pipes-windows-01/exits.json` | `1cb28caaa8a50e8d317bf91262c1dfc334a7003dffce1ba851359123390b05b6` |
| `.artifacts/healthcheck/release-next/private-launcher-05/CIS.DependencyChecker.dll` | `46c516b458887f790216b03f53c46c5f1072d1de86f360ffe8c833188fc84e17` |
| `planning/healthcheck/qualification/release-next/scope-inventory.json` | `a5f2c814e27d87fa714bbe83e8495c63396c017801182c2ee9450cb98eff9df3` |

The auditor's `sessionSha256` equals the inspected session-file hash above. That
session pins Stryker.Core 4.16.0 to
`bf02f78d3725e1243edb7a32d88dc1a02c88f1f9f74b60b18730636073f234d7`,
the selector source to
`8b6bcf99503e313227f4436db335cf8fa01406d2633387a6c77a911c3f11eaf5`,
and the old observer/protocol/support/auditor source inputs. Its pinned source
inputs describe this v1 execution, not the current helper implementation. The
report's project root is
`C:/miscwork/portfolio/cislib/.artifacts/healthcheck/release-next/pipes-inputs-01/source/implementations/csharp/src/CIS.Core`.

## Remaining qualification

The 114 survivors, 25 timeouts, 60 compiler rollback outcomes and 75 block-covered
outcomes require contract-based review and retained replay/disposition. The
failed v1 child audit requires a fresh execution with complete verified child
origin and lifecycle evidence; no current helper fix supplies retroactive
credit. Coordinator-owned fresh restored build, 276-test native results and
complete source checks remain separate receipts.

Full Linux Pipes automatic mutation remains unrun. The other mutation
partitions, complete Healthcheck closure, whole-Core Release gates, external
integration matrices and actual publication-history obligations remain
independent. This failed Windows run establishes no release qualification or
published-history replay.
