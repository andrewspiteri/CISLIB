using System.Collections;
using System.Diagnostics;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.MicrosoftLogger;
using Microsoft.Extensions.Logging;
using static CIS.Tests.DiscoveryFixture.FlushStressSupport;

namespace CIS.Tests.DiscoveryFixture;

internal static class ProviderStateFixture
{
    internal static readonly string[] Modes = ["extensions", "scalars", "collections", "precedence", "unsupported", "bounds", "filtered", "correlation", "template-size"];
    private sealed class Bomb
    {
        internal int Calls;
        public override string ToString() { Calls++; throw new InvalidOperationException("Arbitrary ToString ran."); }
    }
    private sealed class Counting : IEnumerable
    {
        internal int Visits;
        public IEnumerator GetEnumerator()
        {
            for (int i = 0; i < 2000; i++)
            { Visits++; Require(Visits <= 1100); yield return i; }
        }
    }
    private sealed class Fields(Action? onEnumeration = null, bool fail = false) : IEnumerable<KeyValuePair<string, object?>>
    {
        internal int Calls;
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        { Calls++; onEnumeration?.Invoke(); if (fail) { throw new InvalidOperationException("Filtered state traversed."); } return new[] { new KeyValuePair<string, object?>("trace_id", "fake") }.AsEnumerable().GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    internal static int Run(string mode, string root)
    { Console.WriteLine(ExecuteAsync(mode, root).GetAwaiter().GetResult().ToJsonString()); return 0; }
    internal static async Task<JsonObject> ExecuteAsync(string mode, string? root = null)
    {
        Require(Modes.Contains(mode, StringComparer.Ordinal)); using var bytes = new MemoryStream(); bool physical = root is not null;
        string? path = physical ? Path.Combine(root!, "logs Ω", "active.log") : null;
        CoreLogLevel threshold = mode == "filtered" ? CoreLogLevel.Warning : CoreLogLevel.Information;
        CoreLogRuntime runtime;
        if (physical)
        {
            LoggingOptions options = EnvironmentOptions.Logging; options.ConsoleLevel = CoreLogLevel.None; options.FileLevel = threshold;
            options.FilePath = path; options.FileFormat = LogOutputFormat.JsonLines; options.QueueCapacity = 64; options.MaxEntryBytes = 16384; options.ShutdownTimeoutMilliseconds = 5000;
            runtime = CoreLogRuntime.Create();
        }
        else
        {
            var options = new LoggingOptions { ConsoleLevel = threshold, FileLevel = CoreLogLevel.None, ConsoleFormat = LogOutputFormat.JsonLines, QueueCapacity = 64, MaxEntryBytes = 16384 };
            runtime = CoreLogRuntime.CreateConsoleQueued(ConsoleCompositionFixture.Metadata(), options, acquireConsole: _ => bytes);
        }
        using var provider = new CoreLoggerProvider(runtime); ILogger logger = provider.CreateLogger("Provider.State");
        int calls = 0, identities = 0, stateCalls = 0; var bomb = new Bomb(); var counted = new Counting(); string? trace = null, span = null;
        try
        {
            void Log(string name, object state, string message = "supplied rendered")
            {
                logger.Log(LogLevel.Information, new EventId(17, name), state, null, (received, error) =>
                { calls++; Require(ReferenceEquals(received, state) && error is null); identities++; return message; });
            }
            switch (mode)
            {
                case "extensions": logger.LogInformation(new EventId(17, "orders"), "Orders {Count} {User}", 3, "Ω"); break;
                case "scalars":
                    Log("scalars", new Dictionary<string, object?>
                    {
                        ["nil"] = null,
                        ["bool"] = true,
                        ["text"] = "Ω 🙂",
                        ["char"] = 'x',
                        ["sbyte"] = (sbyte)-8,
                        ["byte"] = (byte)8,
                        ["short"] = (short)-16,
                        ["ushort"] = (ushort)16,
                        ["int"] = -32,
                        ["uint"] = uint.MaxValue,
                        ["long"] = 9_007_199_254_740_991L,
                        ["ulong"] = 9_007_199_254_740_991UL,
                        ["float"] = 1.5f,
                        ["double"] = 2.5,
                        ["decimal"] = 1.20m,
                        ["wholeDecimal"] = 2m,
                        ["uuid"] = Guid.Parse("11111111-2222-3333-4444-555555555555"),
                        ["offset"] = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.FromHours(2)),
                        ["utc"] = new DateTime(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc),
                        ["explicit"] = LogValue.IntegerString("9223372036854775807"),
                        ["{OriginalFormat}"] = "raw {template}"
                    }); break;
                case "collections":
                    object?[] array = [1, null, true]; var nested = new Dictionary<string, object?> { ["array"] = array, ["typed"] = new Dictionary<string, int> { ["i"] = 7 } };
                    var source = new Dictionary<string, object?> { ["nested"] = nested, ["owned"] = LogValue.Array([LogValue.String("original")]) };
                    Log("collections", source); array[0] = 999; nested.Clear(); source.Clear(); break;
                case "precedence":
                    using (logger.BeginScope(new OperationContextData { OperationId = "operation.original", Attributes = new Dictionary<string, LogValue> { ["event_id"] = LogValue.Integer(999), ["common"] = LogValue.String("outer") } }))
                    {
                        Log("precedence", new KeyValuePair<string, object?>[] { new("common", "event"), new("choice", 1), new("choice", 2), new("event_id", bomb), new("service", "fake"), new("{OriginalFormat}", "scope {choice}") });
                    }
                    break;
                case "unsupported":
                    object?[] cycle = new object?[1]; cycle[0] = cycle; var mapCycle = new Hashtable(); mapCycle["self"] = mapCycle;
                    object[] unsupported = [bomb, double.NaN, double.PositiveInfinity, long.MaxValue, ulong.MaxValue, new DateTime(2026, 10, 14), CoreLogLevel.Error, cycle, mapCycle];
                    foreach (object value in unsupported) { Log("invalid", new Dictionary<string, object?> { ["bad"] = value }); }
                    Log("invalid", new Dictionary<string, object?> { ["bad\0key"] = 1 });
                    Log("invalid", new Dictionary<string, object?> { ["bad"] = "\ud800" });
                    Log("invalid", new Dictionary<string, object?> { ["{OriginalFormat}"] = 7 }); break;
                case "bounds":
                    Log("array-bound", new Dictionary<string, object?> { ["array"] = counted, ["{OriginalFormat}"] = "large {array}" });
                    object deep = 1; for (int i = 0; i < 9; i++) { deep = new object[] { deep }; }
                    Log("depth-bound", new Dictionary<string, object?> { ["deep"] = deep });
                    Log("field-bound", Enumerable.Range(0, 2000).Select(i => new KeyValuePair<string, object?>("k" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), i))); break;
                case "filtered":
                    var skipped = new Fields(fail: true);
                    logger.Log(LogLevel.Information, new EventId(17, "filtered"), skipped, null, (_, _) => { calls++; throw new InvalidOperationException("Filtered formatter ran."); });
                    logger.Log(LogLevel.None, new EventId(17, "none"), skipped, null, (_, _) => { calls++; throw new InvalidOperationException("None formatter ran."); });
                    stateCalls = skipped.Calls; break;
                case "correlation":
                    using (var original = new Activity("original").SetIdFormat(ActivityIdFormat.W3C).SetParentId("00-11111111111111111111111111111111-2222222222222222-01").Start())
                    using (var changed = new Activity("changed").SetIdFormat(ActivityIdFormat.W3C).SetParentId("00-33333333333333333333333333333333-4444444444444444-01").Start())
                    {
                        Require(original.TraceId != changed.TraceId && original.SpanId != changed.SpanId);
                        Activity.Current = original; trace = original.TraceId.ToHexString(); span = original.SpanId.ToHexString();
                        var changing = new Fields(() => Activity.Current = changed); Log("correlation", changing); stateCalls = changing.Calls; Activity.Current = original;
                    }
                    break;
                case "template-size": Log("template-size", new Dictionary<string, object?> { ["{OriginalFormat}"] = new string('x', 9000) }); break;
            }
            LogStopResult stop = await runtime.StopAsync(CancellationToken.None).WaitAsync(Guard); Require(stop.Succeeded);
            await runtime.QueuedOutputs!.Completion.WaitAsync(Guard); byte[] written = physical ? File.ReadAllBytes(path!) : bytes.ToArray(); bool leaseReleased = false;
            if (physical) { using var lease = new FileStream(path + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None); leaseReleased = true; }
            JsonObject report = Common(mode); report["physical"] = physical; report["bytes"] = Convert.ToBase64String(written);
            report["formatterCalls"] = calls; report["stateIdentityChecks"] = identities; report["stateEnumerationCalls"] = stateCalls; report["arbitraryToStringCalls"] = bomb.Calls;
            report["arrayEnumerated"] = counted.Visits; report["expectedTrace"] = trace; report["expectedSpan"] = span;
            report["invalid"] = Number(runtime.GetDiagnostics().InvalidEventCount); report["truncated"] = Number(runtime.GetDiagnostics().TruncatedEventCount); report["result"] = StopJoinFixture.Json(stop);
            report["borrowedConsoleOpen"] = physical ? null : JsonValue.Create(bytes.CanWrite); report["leaseReleased"] = leaseReleased; return report;
        }
        finally { await runtime.DisposeAsync().AsTask().WaitAsync(Guard); }
    }
}
