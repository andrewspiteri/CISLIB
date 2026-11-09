# CisLib

CisLib implements Logging, Type Manager and Healthcheck in one C# `CIS.Core` package and assembly. It targets .NET 10; Core uses the approved Microsoft runtime dependencies and `Microsoft.AspNetCore.App`. Applications own runtime startup, shutdown and resources. The library does not depend on Change Impact Studio.

Run commands from `implementations/csharp` so `global.json` selects SDK **10.0.401**. The selected qualification runtime is **10.0.12**; install its ASP.NET Core shared framework as well.

The full Compatibility suite also needs **.NET 8.0.31** for the pinned ApiCompat tool. For a private installation, set `CIS_COMPAT_API_DOTNET` to that runtime's `dotnet.exe`; this machine's verified host is `.artifacts/tool-host-8.0.31/dotnet.exe` under the repository root. SDK and product commands continue to use ordinary `dotnet`.

```powershell
cd implementations/csharp
dotnet tool restore --configfile eng/Tools.NuGet.config
dotnet restore CisLib.slnx --locked-mode
dotnet build CisLib.slnx -c Release --no-restore
dotnet test CisLib.slnx -c Release --no-build --no-restore --filter "TestCategory!=PerformanceObservation&TestCategory!=ExtendedStress"
```

This ordinary regression selection excludes performance observations and extended stress tests. An unfiltered solution run or IDE Run All includes those categories; performance/stress and full release qualification remain separate open work.

Open the same solution in the IDE. To run a focused test selection:

```powershell
dotnet test tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj -c Release --filter FullyQualifiedName~Healthcheck
```

Pack the library and its Type Manager build assets directly:

```powershell
dotnet pack src/CIS.Core/CIS.Core.csproj -c Release -o ../../.artifacts/packages
```

A local development package is not a published release. Scoped checks and current cleanup results are recorded in [cleanup disposition](planning/cis/cleanup-disposition.md). Direct coverage/mutation commands are documented in [native verification](implementations/csharp/verification/mutation/README.md). Complete platform, coverage, mutation, compatibility and release qualification remain separate obligations in [current decisions](planning/decisions.md). The removed preparation, custody, replay and recovery command protocols are retired; they do not require reimplementation in CIS.

| Product | Current reference |
| --- | --- |
| Environment and Logging | [EnvironmentOptions](docs/modules/logging/environment-options.md), [runtime ownership](docs/modules/logging/runtime-contract.md), [incoming frontend](docs/modules/logging/incoming-pipeline.md), [processing policies](docs/modules/logging/processing-policies.md), [interpolation](docs/modules/logging/interpolation.md) |
| Type Manager | [Definition](planning/type-manager/sources/CIS_Type_Manager_Definition_v1.0.md), [inventory build integration](implementations/csharp/build/type-manager/README.md), [examples](implementations/csharp/examples/type-manager/README.md) |
| Healthcheck | [Usage and transports](docs/modules/healthcheck/README.md), [technical intent](planning/healthcheck/technical-intent.md), [contracts](planning/healthcheck/contract-catalog.md) |
| Shared behavior | [Logging contracts](contracts/logging/README.md), [Healthcheck contracts](contracts/healthcheck/v1/README.md), native fixtures under `conformance/` |
| Architecture | [Dependencies](docs/architecture/csharp-dependency-policy.md), [compatibility](docs/architecture/compatibility-policy.md), [assembly identity](docs/architecture/csharp-assembly-policy.md) |

`contracts/` and `conformance/` hold portable specifications and behavioral fixtures. C# source, tests, examples and build assets live under `implementations/csharp/`. `planning/` retains authoritative definitions and concise decisions; historical campaign payloads are available only where Git history still contains them. Existing historical reports establish no current acceptance claim.

CIS adoption comes next and should connect to the native commands and retained product obligations. It is not implemented by this cleanup. Later languages and modules remain specifications rather than active scaffolding.

CisLib uses the [MIT License](LICENSE.md). Andrew Spiteri maintains the official [repository](https://github.com/andrewspiteri/CISLIB); external contributions and publication are governed by [maintenance policy](CONTRIBUTING.md).
