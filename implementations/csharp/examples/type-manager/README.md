# Type Manager examples

These development examples use the same `CIS.Core` project as logging. They do not
construct any discovered objects. `IDiscoveryExample` is a sample-only contract;
it is not a proposed Configuration API.

Use SDK **10.0.401**, .NET runtime **10.0.12** and ASP.NET Core shared framework
**10.0.12**. Run the source-reference examples from `implementations/csharp`:

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

Checked-in projects use source references for repository development. For an actual
package consumer, use a local feed containing one current `CIS.Core` candidate and
select its exact version as an MSBuild SDK. The SDK must participate in the first
restore so runtime assets are selected for the host RID.

For a clean example consumer, copy `Console`, `Web`, `Contracts`, `Unused` and
`Shared` into a disposable directory outside the repository. Exclude `bin`, `obj`
and old `packages.lock.json` files. Keep `Program.cs`, `CatalogConsumer.cs`, the
sample Contracts/Unused references and shared verification code. Remove each
host's Core `ProjectReference`, source target import and broad `RuntimeIdentifiers`
list. Replace the Console project with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <Sdk Name="CIS.Core" Version="0.1.0-dev.1" />
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>CIS.TypeManager.Examples.Console</AssemblyName>
    <CisTypeManagerEnabled>true</CisTypeManagerEnabled>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../Contracts/Contracts.csproj" />
    <ProjectReference Include="../Unused/Unused.csproj" />
    <Compile Include="../Shared/SampleVerification.cs" Link="SampleVerification.cs" />
  </ItemGroup>
</Project>
```

For Web, use the same project structure with `Sdk="Microsoft.NET.Sdk.Web"` and
`AssemblyName` set to `CIS.TypeManager.Examples.Web`. Use the actual candidate
version in both `Sdk` elements; `0.1.0-dev.1` is the current development version.
Do not add a Core `PackageReference`: the SDK supplies the exact matching runtime
dependency from the same package. `CIS.TypeManager.Build.dll` belongs to its build
assets and must not appear in the application's runtime output.

Place this `Directory.Build.props` beside the copied directories, so Contracts and
Unused also get explicit framework/compiler settings:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RuntimeFrameworkVersion>10.0.12</RuntimeFrameworkVersion>
    <RollForward>Disable</RollForward>
    <SelfContained>false</SelfContained>
  </PropertyGroup>
</Project>
```

Add empty `<Project />` files named `Directory.Build.targets` and
`Directory.Packages.props` at that root to stop unrelated parent imports. Copy the
repository's `global.json` there to pin SDK 10.0.401 with roll-forward disabled.
Create `NuGet.Config` there **before any consumer dotnet command**, using an
absolute native path for the feed containing the candidate:

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
</configuration>
```

The exact `CIS.Core` mapping selects the local candidate; other dependencies can
restore from nuget.org. Use a Windows absolute path on Windows. Set `NUGET_PACKAGES`
to a new cache inside the disposable consumer root **before SDK resolution**:
in PowerShell, `$env:NUGET_PACKAGES = Join-Path $PWD '.packages'`; in a POSIX shell,
`export NUGET_PACKAGES="$PWD/.packages"`. Restore its previous value when finished.
A command-line `restore --configfile` supplied after SDK evaluation is not a
replacement for the visible configuration file. No warm assets or cached Core
package should be needed for the first restore.

Run from that isolated consumer root on the actual target OS:

```text
dotnet restore Console/Console.csproj
dotnet build Console/Console.csproj -c Release --no-restore
dotnet run --project Console/Console.csproj -c Release --no-build --no-restore
dotnet publish Console/Console.csproj -c Release -o publish/console
dotnet publish Console/Console.csproj -c Release --no-build --no-restore -o publish/console-no-build
dotnet publish/console/CIS.TypeManager.Examples.Console.dll
dotnet publish/console-no-build/CIS.TypeManager.Examples.Console.dll

dotnet restore Web/Web.csproj
dotnet build Web/Web.csproj -c Release --no-restore
dotnet run --project Web/Web.csproj -c Release --no-build --no-restore -- --urls http://127.0.0.1:5080
dotnet publish Web/Web.csproj -c Release -o publish/web
dotnet publish Web/Web.csproj -c Release --no-build --no-restore -o publish/web-no-build
dotnet publish/web/CIS.TypeManager.Examples.Web.dll --urls http://127.0.0.1:5080
dotnet publish/web-no-build/CIS.TypeManager.Examples.Web.dll --urls http://127.0.0.1:5080
```

Stop each Web host before the next launch. The SDK selects the current supported
`win-x64` or `linux-x64` RID before restore; build output is under
`bin/Release/net10.0/<rid>/`. `--no-build` and `--no-restore` require matching
configuration, framework, RID and dependency inputs. Do not mix different RID
builds in one intermediate directory. Each build/publish output must carry its
own `<host>.types.json` and selected assemblies; unchanged manifests keep their
bytes and modification time. The broken-deployment demonstration above also
applies to a disposable package-consumer publish output.

These instructions describe the supported package route. Actual Windows 11 x64
and Ubuntu 24.04 x64 outcomes are recorded in [CIS-0002 verification](../../../../docs/changes/CIS-0002/verification.md).
Cross-publishing alone does not establish a platform pass, publication or full
release qualification. Visual Studio/MSBuild.exe, `PublishUrl`, custom helper
output layouts, AOT, trimming, single-file, self-contained output, plugins and
custom load contexts remain outside this task.

The sample `--verify` mode loads the separate matching test assembly from `tests/fixtures/TypeManager/ExampleLate` after preparation to demonstrate fixed catalog/query membership. It is not plugin support and is not a runtime dependency.

See the [Configuration boundary](../../../../docs/modules/type-management/configuration-boundary.md) for later consumers of these results and [current release gaps](../../../../planning/decisions.md).
