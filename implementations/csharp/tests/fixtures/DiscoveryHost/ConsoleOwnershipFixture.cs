using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Logging;

namespace CIS.Tests.DiscoveryFixture;

internal static class ConsoleOwnershipFixture
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    private static readonly AsyncLocal<string?> Creator = new();
    internal static byte[] Marker(string stream) => Encoding.UTF8.GetBytes("{\"application\":\"" + stream + "-still-open\"}\n");
    private sealed class Clock : TimeProvider
    {
        internal int Calls;
        public override DateTimeOffset GetUtcNow() { Interlocked.Increment(ref Calls); return new(2026, 10, 13, 10, 11, 12, TimeSpan.Zero); }
    }
    // The real Stream is held in the middle of its first record and yields between chunks.
    // Dispose/DisposeAsync counters observe runtime ownership; only the fixture owner disposes it.
    private sealed class BorrowedStream(bool fail) : MemoryStream
    {
        private readonly object bytesGate = new();
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Calls, Active, MaxActive, Disposes, AsyncDisposes, Flushes;
        internal bool InheritedContext;
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int active = Interlocked.Increment(ref Active), call = Interlocked.Increment(ref Calls);
            int maximum;
            do { maximum = Volatile.Read(ref MaxActive); } while (active > maximum && Interlocked.CompareExchange(ref MaxActive, active, maximum) != maximum);
            InheritedContext |= Creator.Value is not null;
            try
            {
                int split = fail && call == 1 ? 5 : buffer.Length / 2;
                lock (bytesGate) { base.Write(buffer.Span[..split]); }
                if (call == 1) { Started.TrySetResult(); await Release.Task.WaitAsync(Limit); }
                if (fail && call == 1) { throw new IOException("fixture-private-console-data"); }
                await Task.Yield();
                lock (bytesGate) { base.Write(buffer.Span[split..]); }
            }
            finally { Interlocked.Decrement(ref Active); }
        }
        public override void Flush() { Interlocked.Increment(ref Flushes); base.Flush(); }
        public override Task FlushAsync(CancellationToken cancellationToken) { Interlocked.Increment(ref Flushes); return Task.CompletedTask; }
        protected override void Dispose(bool disposing) { if (disposing) { Interlocked.Increment(ref Disposes); } base.Dispose(disposing); }
        public override ValueTask DisposeAsync() { Interlocked.Increment(ref AsyncDisposes); return base.DisposeAsync(); }
    }
    internal static async Task<JsonObject> ExecuteAsync(JsonObject row)
    {
        string mode = row["mode"]!.GetValue<string>(); bool physical = row["physical"]!.GetValue<bool>(), fail = mode == "fault";
        int records = row["expected"]!["records"]!.GetValue<int>(), runs = mode == "reuse" ? 2 : 1, perRun = records / runs, selected = row["stream"]!.GetValue<int>(), selections = 0;
        var stderr = new BorrowedStream(fail); var stdout = new BorrowedStream(fail); BorrowedStream chosen = selected == 0 ? stderr : stdout;
        var clock = new Clock(); var results = new JsonArray(); var stops = new JsonArray(); JsonObject? held = null;
        Activity? previousActivity = Activity.Current; string? previousCreator = Creator.Value; Activity.Current = null;
        CoreLogRuntime? runtime = null; Task<JsonObject[]>[]? producers = null; Exception? primaryFailure = null;
        try
        {
            for (int generation = 0; generation < runs; generation++)
            {
                var options = new LoggingOptions { QueueCapacity = 64 };
                if (row["format"]!.GetValue<int>() == 1) { options.ConsoleFormat = LogOutputFormat.JsonLines; }
                if (selected == 1) { options.ConsoleStream = ConsoleOutputStream.StandardOutput; }
                Creator.Value = "creator-must-not-flow";
                runtime = CoreLogRuntime.CreateConsoleQueued(ConsoleCompositionFixture.Metadata(), options, timeProvider: clock,
                    acquireConsole: physical ? null : value => { selections++; return value == ConsoleOutputStream.StandardError ? stderr : stdout; });
                Creator.Value = previousCreator;
                CoreLogger logger = runtime.CreateLogger("BorrowedConsole");
                JsonObject Write(int index)
                {
                    LogWriteResult value = logger.Write(CoreLogLevel.Information, new LogEventData
                    {
                        EventName = "borrow." + index.ToString(CultureInfo.InvariantCulture),
                        Message = Message(index),
                        OperationId = "borrow-op",
                        Attributes = new Dictionary<string, LogValue> { ["index"] = LogValue.Integer(index) }
                    });
                    return new JsonObject
                    {
                        ["index"] = index,
                        ["generation"] = generation,
                        ["outcome"] = value.Outcome.ToString(),
                        ["truncated"] = value.IsTruncated,
                        ["outputCount"] = value.Outputs.Count,
                        ["name"] = value.Outputs[0].OutputName,
                        ["status"] = value.Outputs[0].Status.ToString(),
                        ["ticket"] = value.Outputs[0].AcceptedSequence?.ToString(CultureInfo.InvariantCulture)
                    };
                }
                if (mode == "serialized")
                {
                    var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    producers = Enumerable.Range(0, 6).Select(producer => Task.Run(async () =>
                    {
                        await start.Task; return Enumerable.Range(producer * 4, 4).Select(Write).ToArray();
                    })).ToArray();
                    start.SetResult();
                    foreach (JsonObject[] group in await Task.WhenAll(producers).WaitAsync(Limit)) { foreach (JsonObject result in group) { results.Add(result); } }
                }
                else { for (int index = generation * perRun; index < (generation + 1) * perRun; index++) { results.Add(Write(index)); } }
                if (!physical && generation == 0 && records > 0)
                {
                    await chosen.Started.Task.WaitAsync(Limit); runtime.CloseAdmission();
                    LogOutputDiagnostics output = runtime.GetDiagnostics().Outputs[0];
                    held = new JsonObject
                    {
                        ["state"] = runtime.GetDiagnostics().State.ToString(),
                        ["accepted"] = output.Accepted,
                        ["pending"] = output.Pending,
                        ["written"] = output.Written,
                        ["inFlight"] = runtime.CaptureAccounting().Console!.Value.InFlight,
                        ["calls"] = chosen.Calls,
                        ["active"] = chosen.Active,
                        ["disposes"] = chosen.Disposes,
                        ["asyncDisposes"] = chosen.AsyncDisposes
                    };
                    chosen.Release.TrySetResult();
                }
                runtime.CloseAdmission();
                try { await runtime.QueuedOutputs!.Completion.WaitAsync(Limit); }
                catch (IOException) when (fail) { }
                CoreLogDiagnostics final = runtime.GetDiagnostics(); LogOutputDiagnostics resultOutput = final.Outputs[0];
                stops.Add(new JsonObject
                {
                    ["state"] = final.State.ToString(),
                    ["outputState"] = resultOutput.State.ToString(),
                    ["accepted"] = resultOutput.Accepted,
                    ["written"] = resultOutput.Written,
                    ["failed"] = resultOutput.Failed,
                    ["abandoned"] = resultOutput.Abandoned,
                    ["pending"] = resultOutput.Pending,
                    ["inFlight"] = runtime.CaptureAccounting().Console!.Value.InFlight,
                    ["queueFull"] = resultOutput.QueueFull,
                    ["filtered"] = resultOutput.Filtered,
                    ["unavailable"] = resultOutput.Unavailable,
                    ["ticket"] = resultOutput.AcceptedSequence,
                    ["fault"] = resultOutput.LastFaultCode,
                    ["invalid"] = final.InvalidEventCount,
                    ["truncated"] = final.TruncatedEventCount
                });
            }
            // The application writes again after every runtime has completed, including terminal faults.
            if (physical)
            {
                Console.OpenStandardOutput().Write(Marker("stdout")); Console.OpenStandardError().Write(Marker("stderr"));
            }
            else { stdout.Write(Marker("stdout")); stderr.Write(Marker("stderr")); }
            var actual = new JsonObject
            {
                ["caseId"] = row["id"]!.DeepClone(),
                ["results"] = results,
                ["stops"] = stops,
                ["held"] = held,
                ["selectionCalls"] = selections,
                ["clockCalls"] = clock.Calls,
                ["stdout"] = Convert.ToBase64String(stdout.ToArray()),
                ["stderr"] = Convert.ToBase64String(stderr.ToArray()),
                ["writeCalls"] = chosen.Calls,
                ["maxActive"] = chosen.MaxActive,
                ["active"] = chosen.Active,
                ["flushes"] = chosen.Flushes,
                ["disposedBeforeOwner"] = chosen.Disposes,
                ["asyncDisposed"] = chosen.AsyncDisposes,
                ["inheritedContext"] = chosen.InheritedContext,
                ["otherWriteCalls"] = selected == 0 ? stdout.Calls : stderr.Calls,
                ["otherDisposes"] = selected == 0 ? stdout.Disposes : stderr.Disposes
            };
            if (!physical) { chosen.Dispose(); }
            actual["ownerDisposeCalls"] = chosen.Disposes;
            return actual;
        }
        catch (Exception error) { primaryFailure = error; throw; }
        finally
        {
            Creator.Value = previousCreator; Activity.Current = previousActivity;
            try
            {
                chosen.Release.TrySetResult(); runtime?.CloseAdmission();
                if (producers is not null) { await Task.WhenAll(producers).WaitAsync(Limit); }
                if (runtime is not null) { try { await runtime.QueuedOutputs!.Completion.WaitAsync(Limit); } catch (IOException) when (fail) { } }
            }
            catch when (primaryFailure is not null) { /* Preserve the primary failed test; cleanup cannot turn it into a pass. */ }
            finally
            {
                if (stdout.Disposes == 0) { stdout.Dispose(); }
                if (stderr.Disposes == 0) { stderr.Dispose(); }
            }
        }
    }
    internal static string Message(int index) => "owned caf\u00e9 \U0001F642\nend=" + index.ToString(CultureInfo.InvariantCulture);
    internal static int Run(string path, string id)
    {
        byte[] inputBytes = File.ReadAllBytes(path);
        JsonObject catalog = JsonNode.Parse(inputBytes)!.AsObject(), row = catalog["cases"]!.AsArray().Single(value => value!["id"]!.GetValue<string>() == id)!.AsObject();
        JsonObject actual = ExecuteAsync(row).GetAwaiter().GetResult(); ConsoleOwnershipExpectations.Validate(catalog, row, actual);
        actual["observed"] = new JsonObject
        {
            ["processId"] = System.Environment.ProcessId,
            ["runtimeDescription"] = RuntimeInformation.FrameworkDescription,
            ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : "linux",
            ["fixtureSha256"] = Hash(typeof(ConsoleOwnershipFixture).Assembly.Location),
            ["coreSha256"] = Hash(typeof(CoreLogRuntime).Assembly.Location),
            ["inputsSha256"] = Convert.ToHexStringLower(SHA256.HashData(inputBytes))
        };
        Console.WriteLine(actual.ToJsonString()); return 0;
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
