---
title: 'CountryList development example'
type: document
status: unknown
cis:
  stable_id: cislib.document.modules.healthcheck.country.list
---

# CountryList development example

The [CountryList source](../../../implementations/csharp/examples/healthcheck/CountryList/Program.cs) is an application-owned startup and shutdown composition using the real Core service, binder, collector runner and reporting adapters. It uses a deterministic local data source in place of an application's remote data client. The checked-in project references Core source for development; the isolated package route below uses the same application source.

Use SDK **10.0.401**, .NET **10.0.12** and ASP.NET Core **10.0.12**. The [integration verification record](../../changes/CIS-0004/verification.md) identifies the actual candidate and platform results; commands here do not establish a release or another platform pass.

## Run from source

First change from the repository root into the C# implementation directory so `global.json` selects the pinned SDK:

```text
cd implementations/csharp
dotnet run --project examples/healthcheck/CountryList -- --urls http://127.0.0.1:5080
dotnet run --project examples/healthcheck/CountryList -- --self-test
dotnet run --project examples/healthcheck/CountryList -- --self-test --fail-prime
dotnet run --project examples/healthcheck/CountryList -- --self-test --fail-refresh-registration
dotnet run --project examples/healthcheck/CountryList -- --self-test --fail-later-startup
dotnet publish examples/healthcheck/CountryList -c Release -o .artifacts/country-list
dotnet .artifacts/country-list/CIS.Healthcheck.Examples.CountryList.dll --urls http://127.0.0.1:5080
```

Stop a normal host before starting another on the same address. A published host
uses its own output directory and requires both shared frameworks above.
For an exact 10.0.12 source reproduction when newer patches are installed, add
`-p:RuntimeFrameworkVersion=10.0.12 -p:RollForward=Disable` to `run`/`publish`, or
start the DLL with `dotnet exec --fx-version 10.0.12 --roll-forward Disable`.
The package route below sets those runtime properties explicitly.

## Consume the exact package

Use a local feed containing the selected `CIS.Core` candidate. The current
development version is `0.1.0-dev.1`; select the actual version and retain the
package SHA-256 when comparing results. A development candidate is not a public
package release.

Create a disposable directory outside the repository. Copy `Program.cs`,
`CountryCatalog.cs` and `CountryList.csproj` into a `CountryList` subdirectory,
excluding `bin`, `obj` and the source project's lock file. Copy the repository's
`implementations/csharp/global.json` to the disposable root. Place this
`Directory.Build.props` there so settings do not depend on repository imports:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <RuntimeFrameworkVersion>10.0.12</RuntimeFrameworkVersion>
    <RollForward>Disable</RollForward>
    <SelfContained>false</SelfContained>
    <PublishTrimmed>false</PublishTrimmed>
    <PublishSingleFile>false</PublishSingleFile>
    <PublishAot>false</PublishAot>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <RestoreEnablePackagePruning>false</RestoreEnablePackagePruning>
  </PropertyGroup>
</Project>
```

Add empty `<Project />` files named `Directory.Build.targets` and
`Directory.Packages.props` at that root to stop unrelated parent imports.
The pruning setting keeps the declared package dependency closure visible for
these integration checks; it changes no Core runtime default.
In the copied CountryList project only, replace the Core `ProjectReference`
with an exact `PackageReference`. Preserve the Web SDK and application identity:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <AssemblyName>CIS.Healthcheck.Examples.CountryList</AssemblyName>
    <RootNamespace>CIS.Healthcheck.Examples.CountryList</RootNamespace>
    <IsPackable>false</IsPackable>
    <DefineConstants>$(DefineConstants);CIS_PACKAGE_CONSUMER</DefineConstants>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CIS.Core" Version="[0.1.0-dev.1]" />
  </ItemGroup>
</Project>
```

`CIS_PACKAGE_CONSUMER` labels the example's diagnostic record as package
execution. It changes no Healthcheck policy or application behavior. The actual
loaded Core path and bytes must also match the selected package; the label alone
does not prove package identity.

Create `NuGet.Config` at the disposable root before restore. Substitute an
absolute native path for the candidate feed:

```xml
<configuration>
  <packageSources>
    <clear />
    <add key="candidate" value="/absolute/path/to/candidate-feed" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="candidate"><package pattern="CIS.Core" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
  <fallbackPackageFolders><clear /></fallbackPackageFolders>
</configuration>
```

Use a Windows absolute feed path on Windows. Before any consumer command, set
`NUGET_PACKAGES` to a new cache in the disposable root: in PowerShell,
`$env:NUGET_PACKAGES = Join-Path $PWD '.packages'`; in a POSIX shell,
`export NUGET_PACKAGES="$PWD/.packages"`. Restore its earlier value after the
consumer work. A fresh cache and the exact source mapping prevent a different
cached Core from hiding a packaging fault.

Run from the disposable root:

```text
dotnet restore CountryList/CountryList.csproj --configfile NuGet.Config
dotnet build CountryList/CountryList.csproj -c Release --no-restore
dotnet CountryList/bin/Release/net10.0/CIS.Healthcheck.Examples.CountryList.dll --self-test
dotnet CountryList/bin/Release/net10.0/CIS.Healthcheck.Examples.CountryList.dll --self-test --fail-prime
dotnet CountryList/bin/Release/net10.0/CIS.Healthcheck.Examples.CountryList.dll --self-test --fail-refresh-registration
dotnet CountryList/bin/Release/net10.0/CIS.Healthcheck.Examples.CountryList.dll --self-test --fail-later-startup
dotnet publish CountryList/CountryList.csproj -c Release --no-restore -o publish/country-list
dotnet publish/country-list/CIS.Healthcheck.Examples.CountryList.dll --self-test
dotnet publish/country-list/CIS.Healthcheck.Examples.CountryList.dll --urls http://127.0.0.1:5080
```

The four self-test scenarios also apply to the folder-published DLL. Normal
operation is a long-lived host: use HTTP reporting and the matching probe as
described below, then request normal host shutdown. Keep this framework-dependent,
untrimmed build/folder-publish profile; it adds no trimming, AOT, single-file or
self-contained support. Stop hosts before removing their disposable outputs or
cache. Keep the selected candidate and necessary native results separately.

## Startup and failure behavior

Normal operation remains running until host shutdown. `--fail-prime` makes mandatory initialization fail and retains the reporting host with startup incomplete. It creates no recurring refresh registration. `--fail-refresh` allows priming to succeed but makes subsequent actual refresh fetches fail; the noncritical threshold-three refresh policy flags while the usable cache and critical availability remain intact. The five-minute interval is an example choice. Its first periodic attempt is due at activation rather than after five minutes.

`--fail-refresh-registration` selects an explicit two-monitor capacity: the startup/availability monitors consume it, so the required refresh registration fails after usable priming. `--fail-later-startup` makes the actual required country-response contract setup fail after usable priming and before refresh creation. Both keep readiness false, retain the independent critical startup condition and admit no business request. Select at most one startup-failure demonstration flag. Environment overrides still take precedence over explicit setup; clear `CIS_HEALTHCHECK_MAX_MONITORS` when running the controlled registration-failure demonstration.

The application explicitly:

1. Constructs health objects, retained startup/availability monitors, a prime binding, the owning catalog and the existing HTTP host without fetching data.
2. Installs the reserved health branch before its business request gate/middleware, then starts HTTP/optional pipe reporting and the collector runner.
3. Awaits its actual initial fetch, validates and publishes the replacement list, and synchronously reports the actual priming Outcome/Settled facts. Availability success declares usable-data publication; settlement is a separate fact. It then completes required response-contract setup before declaring startup success.
4. Only after successful priming, creates the noncritical threshold-three refresh monitor and inert registration. It marks startup complete only after required setup succeeds, then activates periodic refresh in normal operation.
5. On failed priming, stops dependent startup and retains its independent critical startup-failure monitor. No startup retry or recurring registration is invented. Reporting remains available for the supervisor's decision.
6. On shutdown, publishes terminal stopping, closes collection, awaits actual callback/lease settlement and owner-dependent handoffs, then disposes monitors/owner dependencies and closes reporting.

`CountryCatalog` retains usable data until a fully validated replacement is ready. Ordinary refresh failure cannot discard it. Its owner-held registration protects a real `IHealthCollectionLease`; component disposal closes lease admission and waits for actual acquired leases before disposing `CountrySource`. The service registry does not retain this owner while idle.

`--self-test` starts real Kestrel on an ephemeral loopback port. It asserts health/readiness and the business gate before and during actual in-progress priming, then explicit startup publication, three actual failed manual refresh attempts, retained usability, an independent critical flag/recovery, reporting bypass of business-side effects, terminal stopping, and actual owner dependency drain. The application-owned source barriers block real work until explicitly released; pending prime cannot count as completed startup, and requested shutdown cancellation cannot certify actual collection completion or release its owner lease. Failure self-tests check retained startup failure, no refresh registration and no business admission; later-step failure keeps the actual usable cache available. The failed-prime path also disposes its child availability monitor before checking the still-live startup monitor. It prints one bounded JSON success record only after its assertions pass; assertion failure returns a nonzero process exit through the runtime. Source execution and exact-package execution retain their distinct diagnostic labels.

The JSON record includes actual detached snapshot observations at each stage, including counts, policy state, decimal refresh-failure count, cached-country count and accepted business-request count. It records actual owner-disposal pending/completed observations and the runtime description. Native process tests should independently inspect those observations; `passed: true` alone is insufficient candidate evidence.

The self-test uses controlled manual collection rather than elapsed periodic timing to make assertions deterministic. Normal operation explicitly activates periodic collection after startup. Set the documented environment variables to enable a local pipe, then invoke the matching probe from the same identity/context; the self-test alone does not invoke or qualify the probe executable.

## Match the pipe probe to the package

Copy the existing `implementations/csharp/tools/CisHealthProbe/Program.cs` and
`CisHealthProbe.csproj` into a `CisHealthProbe` subdirectory of the same isolated
consumer root. Replace only its Core `ProjectReference` with the same exact
`PackageReference` used above. Keep its executable name and existing program;
the shared explicit settings, feed and cache apply to both projects. Build it
with ordinary `dotnet restore CisHealthProbe/CisHealthProbe.csproj --configfile
NuGet.Config` and `dotnet build CisHealthProbe/CisHealthProbe.csproj -c Release
--no-restore`. This uses the existing development tool; it creates no new probe
distribution.

Enable the endpoint before launching a normal CountryList process. For example,
from the disposable root in PowerShell:

```powershell
$env:CIS_HEALTHCHECK_PIPE_ENABLED = 'true'
$env:CIS_HEALTHCHECK_PIPE_NAME = 'country-list-health'
dotnet publish/country-list/CIS.Healthcheck.Examples.CountryList.dll --urls http://127.0.0.1:5080
```

In another shell with the same user identity and endpoint filesystem context:

```text
dotnet CisHealthProbe/bin/Release/net10.0/CisHealthProbe.dll --pipe country-list-health --view readiness --timeout-ms 1000
dotnet CisHealthProbe/bin/Release/net10.0/CisHealthProbe.dll --pipe country-list-health --view health --timeout-ms 1000
```

In a POSIX shell, the host variables can be scoped to its launch:

```sh
CIS_HEALTHCHECK_PIPE_ENABLED=true CIS_HEALTHCHECK_PIPE_NAME=country-list-health \
  dotnet publish/country-list/CIS.Healthcheck.Examples.CountryList.dll --urls http://127.0.0.1:5080
```

Use a short shared writable temporary directory on Unix.
Use a distinct pipe name for each concurrently running host, and restore any
PowerShell environment changes afterward.

After successful startup, readiness and health return valid positive snapshots
(probe exit `0`). Start a separate host with `--fail-prime` to observe legitimate
NotReady/Unhealthy (`1`) while its business route remains denied and health
reporting remains available. A stopped or absent endpoint is a connection error
(`2`), not a negative health observation. Inspect the returned fields as well as
the exit code. HTTP uses `/_health` and `/_health/ready`; full transport semantics
and the existing probe options are in [Reporting and deployment](transports.md).

The prime binder demonstrates real in-process fact commitment for an explicit application operation. It is not Heartbeat integration. The actual Heartbeat scheduler and future `IStartable` coordinator are absent: no scheduler/coordinator substitute is supplied, and their activation, instance-identity/at-most-once, failure/cancellation, OnError and final owner-handoff cases remain pending at their real integration boundaries.
