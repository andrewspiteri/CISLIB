# HC03 reporting codec evidence

HC03 and HC06 were dispatched together after the user requested parallel task work and the local HC02 commit `c5fd9db646aaa33c998ad93a79ce00c4dfc153dc`. The coordinator owns the reporting implementation and one build stream; separate agents own reporting tests, binding implementation and binding tests. The current committed checkout uses disjoint write sets. This is parallel authoring of two implementation tasks, with serialized combined verification.

`HealthReportFormatter` replaces its nonshipping reference facade. HTTP and pipe payloads share one detached snapshot writer, with compact health/readiness and complete details views. Pipe formatting returns the JSON envelope payload; the four-byte prefix is applied by the separate framing helper. The complete encoded payload, including envelope overhead, counts against MaxReportBytes. Overflow discards the partial candidate and returns one bounded ReportTooLarge error with SelectedHealthy=false. No formatter reads live state, starts collection, reinterprets policy outcomes or changes readiness.

The bounded writer checks committed bytes and bounds scratch-allocation hints; JSON's worst-case escaping hint is distinct from actual encoded length. Revision conversion is bounded before materializing arbitrary-precision decimal text. Buffers own copied bytes. Stable transport errors never include caught exception messages or monitor failures.

The internal `HealthReportProtocol` is the shared boundary for later HC05 server/probe integration. It checks payload size, strict UTF-8, scalar Unicode, duplicates at every object depth and nesting depth 16 before materializing JSON. Its semantic reader validates versions, selected view, error exclusivity, canonical GUID/decimal values, finite numbers, actual UTC dates, count relationships, detailed completeness/identities/measurement ordering, definite policy contradictions and secondary lifecycle/settlement facts. It preserves legitimate pending states and numeric evaluation failures retaining prior values. Compact reports cannot prove omitted per-monitor facts.

Framing helpers encode unsigned four-byte little-endian lengths, reject invalid lengths before allocating/reading bodies, assemble arbitrary stream fragments and reject truncation. Cancellation remains cancellation. Actual pipe endpoints, permissions, total connection budgets, CLI behavior and HTTP middleware remain separate tasks; helper tests do not claim transport integration.

Native verification results and candidate pins are recorded in receipt.json after coordinated execution. No project/package changes are needed. The frozen HC00/HC01/HC02 receipts remain historical evidence rather than mutable status files. This task does not qualify the whole module or a release.

The final Release focused run passed 584 Healthcheck cases: 232 retained cases, 182 reporting cases and 170 binding cases, with zero failures/skips. The complete Developer-03 checkpoint passed 7,688 native cases (4,494 unit, 2,121 integration, 1,048 architecture, 25 business), with all 16 stages passing. All 182 reporting case IDs also passed in Developer's actual unit report. All 935 captured C# inputs match the final candidate.

Attempts are retained under `.artifacts/healthcheck/HC03-HC06/`: the initial focused build found an assertion analyzer error; Developer-01 found whitespace gaps; Developer-02 passed its pre-test stages and unit cases but was intentionally stopped to close two final reader-validation gaps. Developer-03 is the complete final checkpoint. Earlier attempts are not credited as final candidate verification.

Native commands from the repository root:

```powershell
pwsh -NoProfile -File eng/test.ps1 -Project UnitTests -Configuration Release -Filter 'FullyQualifiedName~Healthcheck' -TimeoutSeconds 600
pwsh -NoProfile -File eng/verify.ps1 -Profile Developer -MaxBuildNodes 1 -OutputRoot .artifacts/healthcheck/HC03-HC06/developer-03
```

SDK 10.0.401 and test runtime 10.0.12 are repository-pinned. Builds used process-local UseSharedCompilation=false, MSBUILDDISABLENODEREUSE=1 and DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1. New work remains uncommitted; HC02 is committed separately. There is no matched comparison establishing a parallel speedup.
