using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Configuration;
using CIS.Logging;
using CIS.Logging.MicrosoftLogger;
using Microsoft.Extensions.Logging;

namespace CIS.Tests.DiscoveryFixture;

// Application-level observations through the public runtime. Assertions belong to the business tests.
internal static class LoggingBusinessFixture
{
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    internal static int Run(string mode, string root)
    {
        try { Console.WriteLine(ExecuteAsync(mode, root).GetAwaiter().GetResult().ToJsonString()); return 0; }
        catch (TypeInitializationException error) when (error.InnerException is CoreConfigurationException configuration)
        { Console.WriteLine(Error(configuration, error).ToJsonString()); return 0; }
        catch (CoreConfigurationException error) { Console.WriteLine(Error(error, error).ToJsonString()); return 0; }
    }
    private static JsonObject Error(CoreConfigurationException error, Exception original) => new()
    {
        ["configurationError"] = true,
        ["message"] = original.Message,
        ["exceptionText"] = original.ToString(),
        ["runtime"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        ["fixtureSha256"] = Hash(Assembly.GetExecutingAssembly().Location),
        ["coreSha256"] = Hash(typeof(CoreLogRuntime).Assembly.Location),
        ["issues"] = new JsonArray(error.Issues.Select(issue => (JsonNode)new JsonObject
        { ["key"] = issue.Key, ["code"] = issue.Code, ["explanation"] = issue.Explanation }).ToArray())
    };
    private static async Task<JsonObject> ExecuteAsync(string mode, string root)
    {
        string path = Path.Combine(root, "events.log");
        await using CoreLogRuntime runtime = CoreLogRuntime.Create();
        CoreLogger logger = runtime.CreateLogger("Business");
        var receipts = new JsonArray(); int factories = 0;
        void Write(CoreLogLevel level, LogEventData data)
        {
            LogWriteResult result = logger.Write(level, data);
            receipts.Add(new JsonObject { ["outcome"] = result.Outcome.ToString(), ["truncated"] = result.IsTruncated });
        }
        void Lazy(CoreLogLevel level, string name)
        {
            LogWriteResult result = logger.WriteLazy(level, () => { factories++; return new() { EventName = name, Message = "deferred " + name }; });
            receipts.Add(new JsonObject { ["outcome"] = result.Outcome.ToString(), ["truncated"] = result.IsTruncated });
        }
        string? trace = null, span = null;
        switch (mode)
        {
            case "verbose":
                using (runtime.BeginScope(new OperationContextData { OperationId = "business.operation", Attributes = new Dictionary<string, LogValue> { ["experiment"] = LogValue.String("A") } }))
                { Write(CoreLogLevel.Error, new() { EventName = "business.failed", Message = "operation failed", Exception = new InvalidOperationException("controlled failure") }); }
                break;
            case "levels": Lazy(CoreLogLevel.Trace, "trace"); Lazy(CoreLogLevel.Debug, "debug"); Lazy(CoreLogLevel.Warning, "warning"); break;
            case "concurrent":
                int arrivals = 0; var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                async Task Experiment(string id)
                {
                    using (runtime.BeginScope(new OperationContextData { OperationId = "operation." + id, Attributes = new Dictionary<string, LogValue> { ["experiment"] = LogValue.String(id) } }))
                    {
                        if (Interlocked.Increment(ref arrivals) == 2) { ready.TrySetResult(); }
                        await ready.Task.WaitAsync(TimeSpan.FromSeconds(5)); await Task.Yield();
                        LogWriteResult result = logger.Write(CoreLogLevel.Information, new() { EventName = "experiment." + id, Message = id });
                        if (result.Outcome != LogWriteOutcome.Accepted) { throw new InvalidDataException("Experiment admission failed."); }
                    }
                }
                await Task.WhenAll(Experiment("A"), Experiment("B"));
                Write(CoreLogLevel.Information, new() { EventName = "after", Message = "no inherited operation" }); break;
            case "mutation":
                var nested = new Dictionary<string, LogValue> { ["value"] = LogValue.String("before") };
                var attributes = new Dictionary<string, LogValue> { ["owner"] = LogValue.String("before"), ["nested"] = LogValue.Map(nested) };
                var data = new LogEventData { EventName = "captured", Message = "before", Attributes = attributes };
                Write(CoreLogLevel.Information, data); data.Message = "after"; attributes["owner"] = LogValue.String("after"); nested["value"] = LogValue.String("after"); break;
            case "provider":
                using (var provider = new CoreLoggerProvider(runtime))
                {
                    using var activity = new Activity("business").SetIdFormat(ActivityIdFormat.W3C).Start();
                    trace = activity.TraceId.ToHexString(); span = activity.SpanId.ToHexString();
                    ILogger adapted = provider.CreateLogger("Business.Provider");
                    using (adapted.BeginScope(new OperationContextData { OperationId = "provider.operation" }))
                    { adapted.LogError(new EventId(7, "provider.failed"), new InvalidOperationException("provider failure"), "Failure {Code}", 42); }
                    provider.Dispose();
                    Write(CoreLogLevel.Information, new() { EventName = "after-provider", Message = "runtime remains live" });
                }
                break;
            case "bounded":
                Write(CoreLogLevel.Information, new() { EventName = "supported", Message = "\u03a9\U0001f642", Attributes = new Dictionary<string, LogValue> { ["decimal"] = LogValue.DecimalString("1.2300") } });
                using (runtime.BeginScope(new() { OperationId = "bounded.operation" }))
                { Write(CoreLogLevel.Information, new() { EventName = "oversized", Message = string.Concat(Enumerable.Repeat("\U0001f642", 10000)) }); }
                Write(CoreLogLevel.Information, new() { EventName = "malformed", Message = new string((char)0xD800, 1) });
                using (var provider = new CoreLoggerProvider(runtime))
                { provider.CreateLogger("Unsupported").Log(LogLevel.Information, new EventId(9), new Dictionary<string, object?> { ["bad"] = new object() }, null, static (_, _) => "unsupported"); }
                break;
            case "disabled": Lazy(CoreLogLevel.Information, "filtered"); Lazy(CoreLogLevel.None, "none"); break;
            case "configured":
                Write(CoreLogLevel.Information, new() { EventName = "configured", Message = "\u03a9\U0001f642 \"quoted\"\nnext\tline\u001b", Attributes = new Dictionary<string, LogValue> { ["decimal"] = LogValue.DecimalString("1.2300") } }); break;
            default: throw new ArgumentException("Unknown business scenario.", nameof(mode));
        }
        LogStopResult stop = await runtime.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        LogWriteResult stopped = logger.WriteLazy(CoreLogLevel.None, () => { factories++; throw new InvalidDataException("Stopped factory ran."); });
        await runtime.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        return new JsonObject
        {
            ["mode"] = mode,
            ["pid"] = System.Environment.ProcessId,
            ["instanceId"] = EnvironmentOptions.InstanceId,
            ["runtime"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            ["fixtureSha256"] = Hash(Assembly.GetExecutingAssembly().Location),
            ["coreSha256"] = Hash(typeof(CoreLogRuntime).Assembly.Location),
            ["factories"] = factories,
            ["receipts"] = receipts,
            ["stoppedOutcome"] = stopped.Outcome.ToString(),
            ["completion"] = stop.Completion.ToString(),
            ["succeeded"] = stop.Succeeded,
            ["loss"] = stop.LossObserved,
            ["invalid"] = stop.InvalidEventCount,
            ["truncated"] = stop.TruncatedEventCount,
            ["trace"] = trace,
            ["span"] = span,
            ["file"] = Convert.ToBase64String(File.Exists(path) ? File.ReadAllBytes(path) : []),
            ["fileExists"] = File.Exists(path)
        };
    }
}
