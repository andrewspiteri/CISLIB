# Native C# tests

The ordinary [CisLib.slnx](../CisLib.slnx) contains five MSTest/VSTest families and shared test support. Use the same projects in the IDE or through `dotnet test`; no preparation checkpoint, package campaign or custom runner is required.

| Project | Retained purpose |
| --- | --- |
| UnitTests | Logging, environment, Type Manager and Healthcheck behavior, bounds, ownership, errors and deterministic concurrency |
| IntegrationTests | Real consumers, streams, files, process/transport boundaries and lifecycle |
| ArchitectureTests | Actual shipping dependency/API/ownership boundaries and Type Manager MSBuild task regressions |
| CompatibilityTests | Accepted development API/configuration/wire behavior and frozen source/binary consumers; no released baseline is invented |
| BusinessCases | Meaningful application/developer/operator stories |
| TestSupport | Deterministic clocks, signals, owned temporary directories and bounded child execution with drained output/cleanup |

Run from `implementations/csharp`, with the pinned SDK 10.0.401 and .NET/ASP.NET runtime 10.0.12 installed:

```powershell
dotnet test CisLib.slnx -c Release
dotnet test tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj -c Release --filter FullyQualifiedName~Severity
dotnet test tests/CIS.Core.IntegrationTests/CIS.Core.IntegrationTests.csproj -c Release --filter FullyQualifiedName~Healthcheck
```

Projects build their real fixture dependencies directly. Shared setup stages the built product and fixture files needed by those tests; it does not create producer reports or claim another verification pass. Product-specific mutation child staging remains necessary when a real child must execute Stryker's instrumented Core and active-mutant selector. It is inactive in ordinary execution.

The Type Manager compatibility fixture tests the executing Core against specific frozen development API/source/binary consumers. Restore the pinned native tools with `dotnet tool restore --configfile eng/Tools.NuGet.config` before its API cases; ApiCompat 10.0.401 requires the pinned .NET 8.0.31 tool-host runtime. `CIS_COMPAT_API_TOOL` may select that exact tool entry DLL. When the pinned runtime is installed privately, `CIS_COMPAT_API_DOTNET` may select its `dotnet` host for API calls only; SDK and product execution still use ordinary `dotnet`. Without this override, API calls also use ordinary `dotnet`. The exact runtime pin and disabled roll-forward apply to either host. Other producer/report/Core-source overrides are retired. Tests do not download tools internally.

[Native quality checks](../verification/mutation/README.md) describe coverage and mutation commands and the accepted release thresholds. Complete current release qualification and actual external integrations remain open in [current decisions](../../../planning/decisions.md). A focused pass establishes only its executed scope; performance/stress and platform claims require their actual selected runs.
