# Type Manager examples

These development examples use the same `CIS.Core` project as logging. They do not
construct any discovered objects. `IDiscoveryExample` is a sample-only contract;
it is not a proposed Configuration API.

Use the pinned .NET 10 SDK from `implementations/csharp`:

```powershell
dotnet run --project examples/type-manager/Console/Console.csproj
dotnet run --project examples/type-manager/Web/Web.csproj -- --urls http://127.0.0.1:5080
```

The console requires only the .NET shared framework. The Web example also needs
the ASP.NET Core shared framework. Neither adds a runtime dependency to Core.

Both hosts explicitly supply their own assembly, await `TypeManager.CreateAsync`,
print the completed preparation report, and show each candidate's type and defining
assembly identity. The implementation library is referenced for deployment but
none of its classes is referenced by host code. It is initially unloaded and its
two concrete candidates are discovered, including an inherited, nonpublic class
with a private constructor. Such types are valid discovery results, not a promise
that a consumer can construct them.

The console reaches `APPLICATION_READY` only after preparation. The Web sample
creates even its builder after preparation, registers the completed manager with
`AddSingleton<ITypeManager>(manager)`, then builds the container. Two different
scoped consumers and the root provider must resolve that exact instance and the
same cached query result. `GET /catalog` demonstrates the same relationship from
a real request scope. No discovered class is automatically registered in DI.

## Folder publication and broken deployment

```powershell
dotnet publish examples/type-manager/Console/Console.csproj -c Release -o .artifacts/type-manager-console
dotnet .artifacts/type-manager-console/CIS.TypeManager.Examples.Console.dll

dotnet publish examples/type-manager/Web/Web.csproj -c Release -o .artifacts/type-manager-web
dotnet .artifacts/type-manager-web/CIS.TypeManager.Examples.Web.dll --urls http://127.0.0.1:5080
```

The generated `<host>.types.json` must travel with the selected managed assemblies.
To demonstrate useful startup diagnostics, publish a separate disposable output:

```powershell
dotnet publish examples/type-manager/Console/Console.csproj -c Release -o .artifacts/type-manager-broken
Remove-Item -LiteralPath .artifacts/type-manager-broken/CIS.TypeManager.Examples.Unused.dll
dotnet .artifacts/type-manager-broken/CIS.TypeManager.Examples.Console.dll
```

This exits with code 1 and prints `PREPARATION_FAILED: InvalidInventory`, the selected
file path and original `FileNotFoundException`. It never reaches `APPLICATION_READY`.
The Web sample uses the same catch before builder/container creation. Missing or
malformed manifests likewise prevent startup. Diagnostics go directly to standard
error and do not require a DI logger. Republish that disposable output to repair it.

Supported output is framework-dependent, untrimmed .NET 10 on Windows x64 and Linux
x64 with conventional build output or folder publication. Single-file, trimming,
Native AOT and self-contained publication are explicitly rejected by enabled
inventory integration (see the [package profile checks](../../../../planning/type-manager/evidence/package-delivery/README.md)). This example does not add support for runtime plugins or
custom assembly load contexts.

## Exact-package variant and verification

These checked-in projects use explicit source references for repository development.
The example verifier creates a second, isolated variant from the identical sample
source: it replaces only the Core project reference and source target import with
the optional SDK in the supplied exact `CIS.Core` package. It uses a private local
feed/cache, verifies Core bytes in consumer outputs and verifies helper bytes in
the private package cache. It also checks that the helper is absent from deployment.
For a package consumer, the relevant project shape is:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Sdk Name="CIS.Core" Version="0.1.0-dev.1" />
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <CisTypeManagerEnabled>true</CisTypeManagerEnabled>
  </PropertyGroup>
  <!-- Keep the sample Contracts/Unused references; do not add a second Core reference. -->
</Project>
```

Use `Microsoft.NET.Sdk.Web` for the Web host. The development package is supplied
through a local feed; these instructions do not claim it is published. The SDK
`<Sdk Version>` must equal the exact `CIS.Core` package version; do not add a separate
Core `PackageReference`. The private build helper is not a runtime
dependency or deployed application file.

From the repository root, with the pinned SDK available:

```powershell
python implementations/csharp/eng/verify-type-manager-examples.py --repo-root . --package <exact-CIS.Core.nupkg> --dependency-cache <nuget-packages-directory> --output-root <empty-output-directory>
```

The verifier runs Debug/Release builds, Release folder publish and publish without
building, for both hosts in source and package mode. Each layout runs in a fresh
process and gets separate missing-manifest, malformed-manifest and missing-library
controls. The Web Release build output in each lane also serves two real HTTP
requests, proving distinct scoped consumers reuse the prepared manager and cached
query. Its `--verify`
sample mode loads a separate, matching test assembly after preparation and checks
that catalog membership and both cached and previously uncached queries stay fixed.
This intentionally demonstrates exclusion of later assemblies, not plugin support.
That assembly lives under `tests/fixtures/TypeManager/ExampleLate` and is not a host
project dependency. Test instrumentation is not added to Core.

See the [Configuration boundary](../../../../docs/modules/type-management/configuration-boundary.md)
for what a later consumer can do with these results.
