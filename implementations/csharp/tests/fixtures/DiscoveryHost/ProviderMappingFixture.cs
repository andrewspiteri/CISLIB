using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.MicrosoftLogger;
using Microsoft.Extensions.Logging;
using static CIS.Tests.DiscoveryFixture.FlushStressSupport;

namespace CIS.Tests.DiscoveryFixture;

internal static class ProviderMappingFixture
{
    internal static readonly string[] Modes = ["levels", "names", "ids", "payload", "structured"];
    internal static int Run(string mode, string root)
    { Console.WriteLine(ExecuteAsync(mode, root).GetAwaiter().GetResult().ToJsonString()); return 0; }
    internal static async Task<JsonObject> ExecuteAsync(string mode, string? root = null)
    {
        Require(Modes.Contains(mode, StringComparer.Ordinal)); using var bytes = new MemoryStream(); bool physical = root is not null;
        string? path = physical ? Path.Combine(root!, "logs Ω", "active.log") : null;
        CoreLogRuntime runtime;
        if (physical)
        {
            LoggingOptions options = EnvironmentOptions.Logging; options.ConsoleLevel = CoreLogLevel.None; options.FileLevel = CoreLogLevel.Trace;
            options.FilePath = path; options.FileFormat = LogOutputFormat.JsonLines; options.QueueCapacity = 64; options.MaxEntryBytes = 16384; options.ShutdownTimeoutMilliseconds = 5000;
            runtime = CoreLogRuntime.Create();
        }
        else
        {
            var options = new LoggingOptions { ConsoleLevel = CoreLogLevel.Trace, FileLevel = CoreLogLevel.None, ConsoleFormat = LogOutputFormat.JsonLines, QueueCapacity = 64, MaxEntryBytes = 16384 };
            runtime = CoreLogRuntime.CreateConsoleQueued(ConsoleCompositionFixture.Metadata(), options, acquireConsole: _ => bytes);
        }
        using var provider = new CoreLoggerProvider(runtime); int calls = 0, noneCalls = 0; bool exceptionReceived = false;
        try
        {
            void Pair(LogLevel level, string category, EventId id, string message, Exception? error = null, object? state = null, Dictionary<string, LogValue>? attributes = null, string? template = null)
            {
                ILogger logger = provider.CreateLogger(category); Require(logger.IsEnabled(level));
                logger.Log(level, id, state ?? "unrendered input", error, (original, received) =>
                { calls++; Require(ReferenceEquals(original, state ?? "unrendered input")); if (error is not null) { exceptionReceived = ReferenceEquals(error, received); Require(exceptionReceived); } return message; });
                Accepted(runtime.CreateLogger(category).Write((CoreLogLevel)level, new LogEventData
                {
                    EventName = string.IsNullOrEmpty(id.Name) ? "log.message" : id.Name,
                    Message = message,
                    Exception = error,
                    MessageTemplate = template,
                    Attributes = attributes ?? new Dictionary<string, LogValue> { ["event_id"] = LogValue.Integer(id.Id) }
                }));
            }
            switch (mode)
            {
                case "levels":
                    foreach (LogLevel level in new[] { LogLevel.Trace, LogLevel.Debug, LogLevel.Information, LogLevel.Warning, LogLevel.Error, LogLevel.Critical })
                    { Pair(level, "Map.Levels", new EventId(17, "level"), "level " + (int)level); }
                    ILogger disabled = provider.CreateLogger("Map.None"); Require(!disabled.IsEnabled(LogLevel.None));
                    disabled.Log(LogLevel.None, new EventId(19, "none"), "input", null, (_, _) => { noneCalls++; throw new InvalidOperationException("None formatter ran."); }); break;
                case "names":
                    Pair(LogLevel.Information, "Map.First", new EventId(9, "named.event"), "named");
                    Pair(LogLevel.Information, "Map.Second", new EventId(-4), "absent");
                    Pair(LogLevel.Information, "Map.Empty", new EventId(0, ""), "empty"); break;
                case "ids":
                    foreach (int id in new[] { 0, 1, -1, int.MinValue, int.MaxValue })
                    { Pair(LogLevel.Information, "Map.Ids", new EventId(id, "id"), id.ToString(CultureInfo.InvariantCulture)); }
                    break;
                case "structured":
                    using (var activity = new Activity("structured-pair").SetIdFormat(ActivityIdFormat.W3C).SetParentId("00-11111111111111111111111111111111-2222222222222222-01").Start())
                    {
                        var at = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.FromHours(2)); var uuid = Guid.Parse("11111111-2222-3333-4444-555555555555"); object?[] items = [1, null, true];
                        var input = new Dictionary<string, object?> { ["Count"] = 3, ["User"] = "Ω", ["Money"] = 1.20m, ["At"] = at, ["Items"] = items, ["Uuid"] = uuid, ["Number"] = 0.1f, ["Large"] = LogValue.IntegerString("9223372036854775807"), ["{OriginalFormat}"] = "Work {Count} {User}" };
                        var expected = new Dictionary<string, LogValue> { ["event_id"] = LogValue.Integer(42), ["Count"] = LogValue.Integer(3), ["User"] = LogValue.String("Ω"), ["Money"] = LogValue.DecimalString("1.20"), ["At"] = LogValue.Timestamp(at), ["Items"] = LogValue.Array([LogValue.Integer(1), LogValue.Null(), LogValue.Boolean(true)]), ["Uuid"] = LogValue.Uuid(uuid), ["Number"] = LogValue.Number(0.10000000149011612), ["Large"] = LogValue.IntegerString("9223372036854775807") };
                        var structuredError = new InvalidOperationException("controlled mapping failure"); structuredError.Data["private_data"] = "must not be projected";
                        Pair(LogLevel.Error, "Map.Structured", new EventId(42, "structured"), "Work 3 Ω", structuredError, input, expected, "Work {Count} {User}");
                        items[0] = 999; input.Clear(); expected.Clear(); Require(activity.TraceId.ToHexString() == "11111111111111111111111111111111");
                    }
                    break;
                case "payload":
                    var error = new InvalidOperationException("controlled mapping failure"); error.Data["private_data"] = "must not be projected";
                    Pair(LogLevel.Error, "Map.Payload", new EventId(42, "payload"), "rendered {x} Ω 🙂", error); break;
            }
            LogStopResult stop = await runtime.StopAsync(CancellationToken.None).WaitAsync(Guard); Require(stop.Succeeded && !stop.LossObserved);
            await runtime.QueuedOutputs!.Completion.WaitAsync(Guard); Require(runtime.GetDiagnostics().InvalidEventCount == 0);
            byte[] written = physical ? File.ReadAllBytes(path!) : bytes.ToArray();
            if (physical) { using var lease = new FileStream(path + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
            JsonObject report = Common(mode); report["physical"] = physical; report["bytes"] = Convert.ToBase64String(written);
            report["formatterCalls"] = calls; report["noneFormatterCalls"] = noneCalls; report["formatterExceptionReceived"] = exceptionReceived;
            report["invalid"] = Number(runtime.GetDiagnostics().InvalidEventCount); report["result"] = StopJoinFixture.Json(stop);
            report["borrowedConsoleOpen"] = bytes.CanWrite; report["leaseReleased"] = physical; return report;
        }
        finally { await runtime.DisposeAsync().AsTask().WaitAsync(Guard); }
    }
}
