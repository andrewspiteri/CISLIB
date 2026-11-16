# Console logging example

This application reads a small owned order stream asynchronously, calculates a
quote and writes `order.started` and `order.completed` records using `CIS.Core`.
The `Orders` logger borrows the application's runtime. The operation's scope
supplies `quote-order-1042`, `request_id=request-1042` and `channel=console`;
the completed record adds the order ID, item/unit counts and exact decimal total.
The method that opens the scope also disposes it after the awaited work.

Use SDK **10.0.401** and the **.NET and ASP.NET Core 10.0.12** shared frameworks.
The ASP.NET requirement comes from the approved single Core package, even for a
console application. This is a development example, not a package-release claim.

## Run from source

From `implementations/csharp`, the checked-in project uses its existing source
reference and repository build settings:

```text
dotnet run --project examples/logging/ConsoleHuman/ConsoleHuman.csproj -c Release
dotnet publish examples/logging/ConsoleHuman/ConsoleHuman.csproj -c Release -o .artifacts/logging-console
dotnet .artifacts/logging-console/CisLib.Examples.ConsoleHuman.dll
```

Default logging is human-readable stderr. Messages include the order and total;
application/lifecycle summaries use stdout. A normal run includes:

```text
LOGGING max_entry_bytes=65536 console_format=Human file_enabled=False
ORDER order_id=order-1042 item_count=2 unit_count=3 total=16.25
ADMISSION outcome=Accepted incoming_sequence=<ticket> truncated=False
ADMISSION outcome=Accepted incoming_sequence=<ticket> truncated=False
FLUSH completion=Completed succeeded=True loss_observed=False error=none
STOP completion=Completed succeeded=True loss_observed=False error=none
RUNTIME_COMPLETED
```

Tickets correlate incoming admissions; they do not confirm delivery or define a
flush prefix. Output depends on the selected settings: intentional filtering is
not an error, while rejected/truncated writes or observed lifecycle loss make
this example return a nonzero exit code.

## Select human detail, JSONL or file output

Set environment variables **before starting a new process**. First access to
`EnvironmentOptions` applies defaults and known overrides once. The example sets
only its application-owned environment kind; it preserves the selected logging
settings and the default **64 KiB maximum entry size**.

| Setting | Example value | Effect |
| --- | --- | --- |
| `CORE_LOG_HUMAN_DETAIL` | `verbose` | Human records include operation, identity and structured attributes. |
| `CORE_LOG_CONSOLE_FORMAT` | `jsonl` | Console records use the existing JSON Lines representation. |
| `CORE_LOG_CONSOLE_STREAM` | `stderr` | Keeps log records separate from stdout application summaries. |
| `CORE_LOG_FILE_PATH` | `./order-example.jsonl` | Enables the runtime's managed file output, relative to the startup working directory. |
| `CORE_LOG_FILE_FORMAT` | `jsonl` | Selects JSONL file output; this is already its default. |
| `CORE_LOG_CONSOLE_LEVEL` | `None` | Disables console records independently of file output. |

For example, in PowerShell:

```powershell
$env:CORE_LOG_HUMAN_DETAIL = 'verbose'
dotnet run --project examples/logging/ConsoleHuman/ConsoleHuman.csproj -c Release

$env:CORE_LOG_CONSOLE_FORMAT = 'jsonl'
$env:CORE_LOG_FILE_PATH = './order-example.jsonl'
dotnet run --project examples/logging/ConsoleHuman/ConsoleHuman.csproj -c Release
```

In a POSIX shell, the JSONL/file configuration can be local to one process:

```sh
CORE_LOG_CONSOLE_FORMAT=jsonl CORE_LOG_FILE_PATH=./order-example.jsonl \
  dotnet run --project examples/logging/ConsoleHuman/ConsoleHuman.csproj -c Release
```

Restore or unset PowerShell environment changes afterward. Keep console logging
on stderr to separate JSONL records from this application's stdout summaries;
selecting stdout for logs mixes both. The
runtime manages its configured file group and retention; do not delete its files
while it is running. See [environment configuration](../../../../../docs/modules/logging/environment-options.md)
for the existing settings and limits.

## Use the exact package

For a standalone consumer, copy this unchanged `Program.cs` to a fresh directory
outside the checkout. Replace the source project reference with an ordinary exact
`PackageReference`; Logging does not require the Type Manager SDK or opt-in.
Keep the example's assembly name and supply the settings it normally inherits:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <AssemblyName>CisLib.Examples.ConsoleHuman</AssemblyName>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <RuntimeFrameworkVersion>10.0.12</RuntimeFrameworkVersion>
    <RollForward>Disable</RollForward>
    <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CIS.Core" Version="[0.1.0-dev.1]" />
  </ItemGroup>
</Project>
```

Save this as `ConsoleHuman.csproj` and copy the repository's pinned
`implementations/csharp/global.json` alongside it. Put the verified candidate
`CIS.Core.0.1.0-dev.1.nupkg` and its approved
`Microsoft.Extensions.Logging.Abstractions` and
`Microsoft.Extensions.DependencyInjection.Abstractions` **10.0.12** packages in a
local feed. Use a visible `NuGet.Config`, replacing the absolute feed path:

```xml
<configuration>
  <packageSources>
    <clear />
    <add key="candidate" value="/absolute/path/to/feed" />
  </packageSources>
  <fallbackPackageFolders><clear /></fallbackPackageFolders>
  <auditSources><clear /></auditSources>
</configuration>
```

Use a fresh `.packages` directory so another cached Core package cannot supply
the runtime. From this standalone directory:

```text
dotnet restore ConsoleHuman.csproj --configfile NuGet.Config --packages .packages --no-cache
dotnet build ConsoleHuman.csproj -c Release --no-restore
dotnet bin/Release/net10.0/CisLib.Examples.ConsoleHuman.dll
dotnet publish ConsoleHuman.csproj -c Release --no-restore -o published
dotnet published/CisLib.Examples.ConsoleHuman.dll
```

Reuse the exact verified candidate while shipped inputs are unchanged. Source
execution alone does not establish package or platform acceptance; actual outcomes
belong to the focused task's completion record. No new package is published here.

## Observe shutdown and ownership

`TryCreate` returns a safe startup result. Static environment configuration can
fail even before that call, so the application also reports recognized
configuration issues directly. Startup/application/shutdown failures go to stderr
and produce exit code **1**; the healthy normal path returns **0**. Logging failures
are not recursively logged through the failing runtime.

Each write observes incoming admission only. `FlushAsync` waits up to five seconds
for its captured work and writer-buffer flushes; its `Completion`, `Succeeded`
and `LossObserved` values remain distinct. Writer-buffer confirmation does not
promise durable disk storage or collector acknowledgment.

The `finally` block observes `StopAsync` using the configured shutdown budget.
`DisposeAsync` joins that same bounded stop. The application then awaits
`Completion` **without a timeout**, keeping runtime-owned resources alive until
workers and cleanup actually finish; it does not dispose borrowed console streams.
A timed-out or failed stop stays unsuccessful even if cleanup later finishes, and
actual completion can itself fault. No success is reported solely because the
bounded stop returned. Managed log files remain as output after their handles close.
