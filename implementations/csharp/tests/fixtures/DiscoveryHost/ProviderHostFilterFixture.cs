using System.Collections;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.MicrosoftLogger;
using Microsoft.Extensions.Logging;
using static CIS.Tests.DiscoveryFixture.FlushStressSupport;

namespace CIS.Tests.DiscoveryFixture;

internal static class ProviderHostFilterFixture
{
    private sealed class State : IEnumerable<KeyValuePair<string, object?>>
    {
        internal int Enumerations;
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            Enumerations++;
            return new[] { new KeyValuePair<string, object?>("value", 7) }.AsEnumerable().GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal static int Run(string mode, string root)
    {
        Console.WriteLine(ExecuteAsync(mode, root).GetAwaiter().GetResult().ToJsonString());
        return 0;
    }

    private static async Task<JsonObject> ExecuteAsync(string mode, string root)
    {
        Require(ProviderHostFilterExpectations.Modes.Contains(mode, StringComparer.Ordinal));
        bool off = mode == "core-off", narrow = mode is "narrow-host" or "narrow-category";
        string path = Path.Combine(root, "logs \u03a9", "active.log");
        LoggingOptions options = EnvironmentOptions.Logging;
        options.ConsoleLevel = off ? CoreLogLevel.None : CoreLogLevel.Warning;
        options.FileLevel = off ? CoreLogLevel.None : mode == "trace-file" ? CoreLogLevel.Trace : CoreLogLevel.Debug;
        options.FilePath = path;
        options.ConsoleFormat = options.FileFormat = LogOutputFormat.JsonLines;
        options.ConsoleStream = ConsoleOutputStream.StandardError;
        options.QueueCapacity = 64;
        options.MaxEntryBytes = 16384;
        options.ShutdownTimeoutMilliseconds = 5000;
        await using CoreLogRuntime runtime = CoreLogRuntime.Create();
        using var provider = new CoreLoggerProvider(runtime);
        using ILoggerFactory factory = LoggerFactory.Create(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(mode is "broad" or "provider-rule" or "narrow-host" ? LogLevel.Warning : LogLevel.Trace);
            builder.AddProvider(provider);
            if (mode == "broad") { builder.AddFilter<CoreLoggerProvider>(null, LogLevel.Trace); }
            if (mode is "provider-rule" or "narrow-category") { builder.AddFilter<CoreLoggerProvider>(null, LogLevel.Debug); }
            if (mode == "provider-rule") { builder.AddFilter("Provider.Host", LogLevel.Critical); }
            if (mode == "category-rule")
            {
                builder.AddFilter<CoreLoggerProvider>(null, LogLevel.Warning);
                builder.AddFilter<CoreLoggerProvider>("Provider.Host", LogLevel.Debug);
            }
            if (mode == "narrow-category") { builder.AddFilter<CoreLoggerProvider>("Provider.Host", LogLevel.Warning); }
        });
        ILogger logger = factory.CreateLogger("Provider.Host.Filters");
        CoreLogger direct = runtime.CreateLogger("Direct.Host.Filter");
        bool coreDebug = direct.IsEnabled(CoreLogLevel.Debug), hostDebug = logger.IsEnabled(LogLevel.Debug);
        var state = new State();
        int formatterCalls = 0;
        foreach (LogLevel level in new[] { LogLevel.Trace, LogLevel.Debug, LogLevel.Information, LogLevel.Warning, LogLevel.Error, LogLevel.Critical, LogLevel.None })
        {
            logger.Log(level, new EventId(17, level.ToString().ToLowerInvariant()), state, null, (_, _) =>
            {
                formatterCalls++;
                return "rendered";
            });
        }
        if (narrow)
        {
            Accepted(direct.Write(CoreLogLevel.Debug, new LogEventData { EventName = "direct.debug", Message = "direct" }));
        }
        LogStopResult stop = await runtime.StopAsync(CancellationToken.None).WaitAsync(Guard);
        Require(stop.Succeeded);
        bool leaseReleased = false;
        if (!off)
        {
            using var lease = new FileStream(path + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            leaseReleased = true;
        }
        JsonObject report = Common(mode);
        report["factoryVersion"] = System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(LoggerFactory).Assembly.Location).FileVersion;
        report["factorySha256"] = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(LoggerFactory).Assembly.Location)));
        report["coreDebug"] = coreDebug;
        report["hostDebug"] = hostDebug;
        report["formatterCalls"] = formatterCalls;
        report["stateEnumerations"] = state.Enumerations;
        report["consoleLevel"] = options.ConsoleLevel.ToString();
        report["fileLevel"] = options.FileLevel.ToString();
        report["fileExists"] = File.Exists(path);
        report["leaseReleased"] = leaseReleased;
        report["fileBytes"] = Convert.ToBase64String(off ? [] : File.ReadAllBytes(path));
        report["invalid"] = Number(runtime.GetDiagnostics().InvalidEventCount);
        report["outputs"] = Diagnostics(runtime);
        report["result"] = StopJoinFixture.Json(stop);
        return report;
    }
}
