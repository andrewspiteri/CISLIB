# Microsoft host filtering

Core's console and file thresholds are independent. The Microsoft host applies its own provider/category filter before calling `CoreLoggerProvider`. Set that host filter at least as verbose as the most verbose enabled Core destination. A Warning host filter silently removes Debug file diagnostics; Core cannot recover or count events it never receives.

For a file at Debug and console at Warning, configure Core before creating its runtime, then register the provider with a broad provider-specific filter:

```csharp
using CIS;
using CIS.Logging;
using CIS.Logging.MicrosoftLogger;
using Microsoft.Extensions.Logging;

EnvironmentOptions.Logging.ConsoleLevel = CoreLogLevel.Warning;
EnvironmentOptions.Logging.FileLevel = CoreLogLevel.Debug;
EnvironmentOptions.Logging.FilePath = "logs/application.jsonl";
await using CoreLogRuntime runtime = CoreLogRuntime.Create();
using var provider = new CoreLoggerProvider(runtime);
using ILoggerFactory factory = LoggerFactory.Create(builder =>
{
    builder.ClearProviders(); // Standalone example chooses only the Core provider.
    builder.SetMinimumLevel(LogLevel.Warning);
    builder.AddProvider(provider);
    builder.AddFilter<CoreLoggerProvider>(null, LogLevel.Trace);
});
ILogger logger = factory.CreateLogger("Application.Work");
logger.LogDebug("Loaded {Count} items", 7); // File only.
logger.LogWarning("Retrying {Count} items", 7); // File and console.

// Finish application/background logging work before stopping its shared runtime.
LogStopResult result = await runtime.StopAsync(CancellationToken.None);
if (!result.Succeeded || result.LossObserved)
{
    // Application handles the actual drain observation.
}
```

The host application supplies `Microsoft.Extensions.Logging` (or its existing host/framework); `CIS.Core` continues to require only its approved logging abstractions closure. In an existing application, configure its existing `ILoggingBuilder` rather than creating a second factory. Keep its other providers unless intentionally replacing them; `ClearProviders` above is specific to the standalone example.

Trace at the Core provider's host boundary is a simple choice that also permits a future Core Trace destination. If all enabled Core destinations start at Debug, Debug is sufficient. Ignore disabled (`None`) destinations when selecting the most verbose threshold. When every Core destination is disabled, Core skips formatting and state capture even if the host boundary permits Trace. No automatic host-filter rewriting or minimum-level helper is added.

Microsoft selects provider-specific rules before general rules, then the longest matching category prefix, then the last equally specific rule. Consequently `SetMinimumLevel(Trace)` alone cannot override an existing narrower provider/category rule. The provider-wide filter above overrides a general category Warning rule, but a matching `AddFilter<CoreLoggerProvider>("Application.Work", LogLevel.Warning)` still suppresses that category's Debug events. Review effective rules for every category whose diagnostics must reach Core, including configuration loaded by the application. See [Microsoft's filtering rule selection](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/overview#how-filtering-rules-are-applied).

Core checks destination interest before invoking the supplied formatter or copying structured state. This avoids that callback work for Trace with a Debug file/Warning console. Ordinary argument expressions run before the logging call: guard expensive argument construction with `logger.IsEnabled(level)` or use the application's existing generated logging pattern. Neither the guard nor a successful Core stop proves that host-suppressed events were delivered.

Seven fresh-process cases use the actual Microsoft `LoggerFactory` and real Core public runtime, JSONL console on stderr and physical JSONL file. They cover broad host rules, provider-specific override of a general category rule, a broad provider/category rule, deliberately narrow host and category rules, a Trace file, and all Core outputs disabled. Narrow cases also write Debug directly through Core to prove the destination is interested despite host suppression. Formatter and enumerable traversal counts assert skipped work; exact console/file records and stop counters assert separate delivery. These are filter checks; [host qualification](microsoft-host-qualification.md) binds them to complete asynchronous scope, provider ownership and application shutdown story acceptance. The fixture's four additional Microsoft packages are development-only and separately inventoried. Full release qualification remains pending.

The guidance snippet mirrors the factory configuration exercised by `ProviderHostFilterFixture` in its `broad` case. It is not an independently packaged/activated skill example; candidate-package example verification remains WP08.
