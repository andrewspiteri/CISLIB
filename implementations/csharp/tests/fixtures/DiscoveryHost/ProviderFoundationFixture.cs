using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.MicrosoftLogger;
using Microsoft.Extensions.Logging;
using static CIS.Tests.DiscoveryFixture.FlushStressSupport;

namespace CIS.Tests.DiscoveryFixture;

internal static class ProviderFoundationFixture
{
    internal static readonly string[] Modes = ["forward", "extensions", "categories", "filtered", "nonowning", "stopped", "typed-scope", "formatter-error"];
    internal static int Run(string mode, string root)
    { Console.WriteLine(ExecuteAsync(mode, root).GetAwaiter().GetResult().ToJsonString()); return 0; }
    private static byte[] ReadLive(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var bytes = new MemoryStream(); input.CopyTo(bytes); return bytes.ToArray();
    }
    internal static async Task<JsonObject> ExecuteAsync(string mode, string? root = null)
    {
        Require(Modes.Contains(mode, StringComparer.Ordinal)); using var bytes = new MemoryStream(); bool physical = root is not null;
        string? path = physical ? Path.Combine(root!, "logs Ω", "active.log") : null;
        CoreLogLevel threshold = mode == "filtered" ? CoreLogLevel.Warning : CoreLogLevel.Information;
        CoreLogRuntime runtime;
        if (physical)
        {
            LoggingOptions options = EnvironmentOptions.Logging; options.ConsoleLevel = CoreLogLevel.None; options.FileLevel = threshold;
            options.FilePath = path; options.FileFormat = LogOutputFormat.JsonLines; options.QueueCapacity = 16; options.MaxEntryBytes = 16384; options.ShutdownTimeoutMilliseconds = 5000;
            runtime = CoreLogRuntime.Create();
        }
        else
        {
            var options = new LoggingOptions { ConsoleLevel = threshold, FileLevel = CoreLogLevel.None, ConsoleFormat = LogOutputFormat.JsonLines, QueueCapacity = 16, MaxEntryBytes = 16384 };
            runtime = CoreLogRuntime.CreateConsoleQueued(ConsoleCompositionFixture.Metadata(), options, acquireConsole: _ => bytes);
        }
        using var provider = new CoreLoggerProvider(runtime); ILogger logger = provider.CreateLogger("Provider.Foundation"); int calls = 0; bool ownsRuntime = false, errorPreserved = false, scopeCopied = false, exceptionReceived = false;
        try
        {
            void Log(string category, string name, string message)
            { provider.CreateLogger(category).Log(LogLevel.Information, new EventId(7, name), message, null, (state, _) => { calls++; return state; }); }
            switch (mode)
            {
                case "forward":
                    Require(logger.IsEnabled(LogLevel.Error));
                    var forwarded = new InvalidOperationException("controlled provider failure"); forwarded.Data["private_data"] = "must not be projected";
                    logger.Log(LogLevel.Error, new EventId(7, "forward"), "Ω 🙂", forwarded, (state, error) =>
                    { calls++; exceptionReceived = ReferenceEquals(error, forwarded); Require(exceptionReceived); return state; }); break;
                case "extensions":
                    logger.LogInformation("Orders {Count}", 3); break;
                case "categories":
                    Log("Orders.First", "first", "one"); Log("Orders.Second", "second", "two"); break;
                case "filtered":
                    Require(!logger.IsEnabled(LogLevel.Information) && !logger.IsEnabled(LogLevel.None));
                    logger.Log(LogLevel.Information, new EventId(1, "filtered"), "input", null, (_, _) => { calls++; throw new InvalidOperationException("Filtered formatter ran."); });
                    logger.Log(LogLevel.None, new EventId(1, "none"), "input", null, (_, _) => { calls++; throw new InvalidOperationException("None formatter ran."); }); break;
                case "nonowning":
                    provider.Dispose(); provider.Dispose(); Require(!logger.IsEnabled(LogLevel.Information));
                    logger.Log(LogLevel.Information, new EventId(1, "disposed"), "input", null, (_, _) => { calls++; throw new InvalidOperationException("Disposed formatter ran."); });
                    Require(!runtime.GetDiagnostics().AdmissionClosed);
                    Accepted(runtime.CreateLogger("Direct").Write(CoreLogLevel.Information, new LogEventData { EventName = "direct", Message = "direct remains live" }));
                    using (var second = new CoreLoggerProvider(runtime))
                    { second.CreateLogger("Other.Provider").Log(LogLevel.Information, new EventId(1, "other"), "other remains live", null, (state, _) => { calls++; return state; }); }
                    ownsRuntime = !runtime.GetDiagnostics().AdmissionClosed; Require(ownsRuntime); break;
                case "stopped":
                    await runtime.StopAsync(CancellationToken.None).WaitAsync(Guard); Require(!logger.IsEnabled(LogLevel.Information));
                    logger.Log(LogLevel.Information, new EventId(1, "stopped"), "input", null, (_, _) => { calls++; throw new InvalidOperationException("Stopped formatter ran."); });
                    logger.Log(LogLevel.None, new EventId(1, "none"), "input", null, (_, _) => { calls++; throw new InvalidOperationException("Stopped None formatter ran."); }); break;
                case "typed-scope":
                    var attributes = new Dictionary<string, LogValue> { ["scope_attribute"] = LogValue.String("original") };
                    var context = new OperationContextData { OperationId = "operation.original", Attributes = attributes };
                    using (logger.BeginScope(context))
                    {
                        context.OperationId = "operation.changed"; attributes["scope_attribute"] = LogValue.String("changed");
                        Log("Provider.Foundation", "scoped", "scope message");
                    }
                    scopeCopied = true; break;
                case "formatter-error":
                    var expected = new InvalidOperationException("private formatter failure");
                    try { logger.Log(LogLevel.Information, new EventId(1, "never"), "input", null, (_, _) => { calls++; throw expected; }); }
                    catch (InvalidOperationException actual) { errorPreserved = ReferenceEquals(actual, expected); }
                    Require(errorPreserved); break;
            }
            LogStopResult stop = await runtime.StopAsync(CancellationToken.None).WaitAsync(Guard); Require(stop.Succeeded && !stop.LossObserved);
            await runtime.QueuedOutputs!.Completion.WaitAsync(Guard); Require(runtime.GetDiagnostics().InvalidEventCount == 0);
            byte[] written = physical ? ReadLive(path!) : bytes.ToArray();
            if (physical) { using var lease = new FileStream(path + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
            JsonObject report = Common(mode); report["physical"] = physical; report["bytes"] = Convert.ToBase64String(written); report["formatterCalls"] = calls;
            report["invalid"] = Number(runtime.GetDiagnostics().InvalidEventCount); report["result"] = StopJoinFixture.Json(stop);
            report["sharedRuntimeLiveAfterProviderDispose"] = ownsRuntime; report["formatterErrorPreserved"] = errorPreserved; report["scopeCopied"] = scopeCopied;
            report["formatterExceptionReceived"] = exceptionReceived; report["borrowedConsoleOpen"] = bytes.CanWrite; report["leaseReleased"] = physical; return report;
        }
        finally { await runtime.DisposeAsync().AsTask().WaitAsync(Guard); }
    }
}
