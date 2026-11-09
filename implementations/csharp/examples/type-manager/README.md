# Type Manager examples

These development examples use the same `CIS.Core` project as logging. They do not
construct any discovered objects. `IDiscoveryExample` is a sample-only contract;
it is not a proposed Configuration API.

Use the pinned .NET 10 SDK from `implementations/csharp`:

```powershell
dotnet run --project examples/type-manager/Console/Console.csproj
dotnet run --project examples/type-manager/Web/Web.csproj -- --urls http://127.0.0.1:5080
```

Both examples require the .NET and ASP.NET Core shared frameworks because the single `CIS.Core` package includes the approved Healthcheck framework reference. The Web example also uses ASP.NET directly.

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
inventory integration (see the package profile checks). This example does not add support for runtime plugins or
custom assembly load contexts.

## Package consumers

Checked-in projects use explicit source references for repository development. A package consumer uses the optional SDK from a local feed containing the exact packed `CIS.Core` version:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Sdk Name="CIS.Core" Version="0.1.0-dev.1" />
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <CisTypeManagerEnabled>true</CisTypeManagerEnabled>
  </PropertyGroup>
</Project>
```

Use `Microsoft.NET.Sdk.Web` for the Web host. Keep the sample Contracts/Unused references, remove the source Core reference/target import, and do not add a second Core PackageReference. The SDK version must equal the package version; the helper is a build asset and must not appear in runtime deployment. Run ordinary native restore/build/publish and execute the resulting consumer on its actual platform. This is not a published-package or complete release claim.

The sample `--verify` mode loads the separate matching test assembly from `tests/fixtures/TypeManager/ExampleLate` after preparation to demonstrate fixed catalog/query membership. It is not plugin support and is not a runtime dependency.

See the [Configuration boundary](../../../../docs/modules/type-management/configuration-boundary.md) for later consumers of these results and [current release gaps](../../../../planning/decisions.md).