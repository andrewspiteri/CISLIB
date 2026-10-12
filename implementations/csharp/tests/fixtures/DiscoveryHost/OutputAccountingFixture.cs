using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;

namespace CIS.Tests.DiscoveryFixture;

internal static class OutputAccountingFixture
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    private sealed class Writer(bool fails)
    {
        internal readonly TaskCompletionSource FirstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource SecondStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource FirstRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource RemainingRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly IOException Error = new("controlled terminal writer failure");
        internal readonly List<long> Attempted = [];
        internal readonly List<long> Confirmed = [];
        internal async ValueTask Write(AdmittedEvent item)
        {
            Attempted.Add(item.AcceptedSequence);
            if (Attempted.Count == 1)
            {
                FirstStarted.TrySetResult(); await FirstRelease.Task;
                if (fails) { throw Error; }
            }
            else if (Attempted.Count == 2) { SecondStarted.TrySetResult(); await RemainingRelease.Task; }
            Confirmed.Add(item.AcceptedSequence);
        }
        internal void ReleaseAll() { FirstRelease.TrySetResult(); RemainingRelease.TrySetResult(); }
    }
    internal static async Task<JsonObject> ExecuteAsync(JsonObject row)
    {
        bool consoleEnabled = row["consoleEnabled"]!.GetValue<bool>(), fileEnabled = row["fileEnabled"]!.GetValue<bool>();
        bool consoleFails = row["consoleFails"]!.GetValue<bool>(), fileFails = row["fileFails"]!.GetValue<bool>();
        var console = new Writer(consoleFails); var file = new Writer(fileFails);
        var options = new LoggingOptions
        {
            QueueCapacity = 2,
            ConsoleLevel = consoleEnabled ? Enum.Parse<CoreLogLevel>(row["consoleLevel"]!.GetValue<string>()) : CoreLogLevel.None,
            FileLevel = fileEnabled ? Enum.Parse<CoreLogLevel>(row["fileLevel"]!.GetValue<string>()) : CoreLogLevel.None,
            FilePath = fileEnabled ? "control.jsonl" : null
        };
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(EventQualificationFixture.Metadata().CaptureMetadata(), options,
            consoleEnabled ? console.Write : null, fileEnabled ? file.Write : null);
        OutputQueues queues = runtime.QueuedOutputs!; CoreLogger logger = runtime.CreateLogger("Accounting");
        var outputs = new[] { (Name: "console", Queue: queues.Console, Writer: console, Fails: consoleFails), (Name: "file", Queue: queues.File, Writer: file, Fails: fileFails) }.Where(item => item.Queue is not null).ToArray();
        var checkpoints = new List<(string Phase, long Invalid, (string Name, OutputAccounting Counts)[] Counts)>();
        var results = new JsonArray(); int factoryCalls = 0, originalFaults = 0; bool originalFactoryError = false;
        var factoryError = new InvalidOperationException("original factory exception");
        void Capture(string phase) => checkpoints.Add((phase, runtime.InvalidEventCount, outputs.Select(item => (item.Name, item.Queue!.CaptureAccounting())).ToArray()));
        void Result(string name, LogWriteResult result) => results.Add(new JsonObject
        {
            ["call"] = name,
            ["outcome"] = result.Outcome.ToString(),
            ["outputs"] = new JsonArray(result.Outputs.Select(item => (JsonNode)new JsonObject { ["name"] = item.OutputName, ["status"] = item.Status.ToString(), ["ticket"] = item.AcceptedSequence }).ToArray())
        });
        LogEventData Data(string label) => new() { EventName = "accounting.event", Message = label };
        async Task Observe(OutputEventQueue queue, Writer writer, bool fails)
        {
            try { await queue.Completion.WaitAsync(Limit); if (fails) { throw new InvalidDataException("Expected original terminal fault."); } }
            catch (IOException error) when (fails && ReferenceEquals(error, writer.Error)) { originalFaults++; }
        }
        try
        {
            Capture("initial"); Result("seed", logger.Write(CoreLogLevel.Warning, Data("seed")));
            await Task.WhenAll(outputs.Select(item => item.Writer.FirstStarted.Task)).WaitAsync(Limit);
            Result("none", logger.WriteLazy(CoreLogLevel.None, () => throw new InvalidDataException("None factory ran.")));
            Result("low", logger.Write(CoreLogLevel.Information, Data("low")));
            Result("invalid", logger.Write(CoreLogLevel.Warning, new LogEventData { EventName = "", Message = "invalid" }));
            Result("null-factory", logger.WriteLazy(CoreLogLevel.Warning, () => { factoryCalls++; return null!; }));
            try { logger.WriteLazy(CoreLogLevel.Warning, () => { factoryCalls++; throw factoryError; }); }
            catch (InvalidOperationException error) when (ReferenceEquals(error, factoryError)) { originalFactoryError = true; }
            Result("queued-one", logger.Write(CoreLogLevel.Warning, Data("queued-one")));
            Result("queued-two", logger.Write(CoreLogLevel.Warning, Data("queued-two")));
            Result("full", logger.Write(CoreLogLevel.Warning, Data("full"))); Capture("blocked");
            foreach (var item in outputs) { item.Writer.FirstRelease.TrySetResult(); }
            foreach (var item in outputs)
            {
                if (item.Fails) { await Observe(item.Queue!, item.Writer, true); }
                else { await item.Writer.SecondStarted.Task.WaitAsync(Limit); }
            }
            Capture("progress");
            Result("next", logger.WriteLazy(CoreLogLevel.Warning, () => { factoryCalls++; return Data("next"); })); Capture("next");
            runtime.CloseAdmission();
            Result("stopped", logger.WriteLazy(CoreLogLevel.Warning, () => throw new InvalidDataException("Stopped factory ran."))); Capture("closed");
            foreach (var item in outputs) { item.Writer.RemainingRelease.TrySetResult(); }
            foreach (var item in outputs.Where(item => !item.Fails)) { await Observe(item.Queue!, item.Writer, false); }
            Capture("final");
            var projected = new JsonArray(checkpoints.Select(point =>
            {
                var observations = new JsonObject(); foreach (var item in point.Counts) { observations[item.Name] = Project(item.Counts); }
                return (JsonNode)new JsonObject { ["phase"] = point.Phase, ["invalid"] = Number(point.Invalid), ["outputs"] = observations };
            }).ToArray());
            var writers = new JsonObject(); foreach (var item in outputs)
            {
                writers[item.Name] = new JsonObject { ["attempted"] = new JsonArray(item.Writer.Attempted.Select(value => (JsonNode)JsonValue.Create(value)!).ToArray()), ["confirmed"] = new JsonArray(item.Writer.Confirmed.Select(value => (JsonNode)JsonValue.Create(value)!).ToArray()) };
            }
            return new JsonObject { ["caseId"] = row["id"]!.DeepClone(), ["checkpoints"] = projected, ["results"] = results, ["writers"] = writers, ["factoryCalls"] = factoryCalls, ["originalFactoryError"] = originalFactoryError, ["originalFaults"] = originalFaults };
        }
        finally
        {
            console.ReleaseAll(); file.ReleaseAll(); runtime.CloseAdmission();
            try { await queues.Completion.WaitAsync(Limit); }
            catch (IOException error) when (ReferenceEquals(error, console.Error) || ReferenceEquals(error, file.Error)) { }
        }
    }
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static JsonObject Project(OutputAccounting value) => new()
    {
        ["filtered"] = Number(value.Filtered),
        ["attempts"] = Number(value.AdmissionAttempts),
        ["accepted"] = Number(value.Accepted),
        ["queueFull"] = Number(value.QueueFull),
        ["unavailable"] = Number(value.Unavailable),
        ["rejected"] = Number(value.Rejected),
        ["written"] = Number(value.Written),
        ["failed"] = Number(value.Failed),
        ["abandoned"] = Number(value.Abandoned),
        ["pending"] = Number(value.Pending),
        ["inFlight"] = Number(value.InFlight)
    };
    internal static int Run(string path, string id)
    {
        JsonObject row = JsonNode.Parse(File.ReadAllText(path))!["cases"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == id)!.AsObject();
        JsonObject actual = ExecuteAsync(row).GetAwaiter().GetResult(); OutputAccountingExpectations.Validate(row, actual);
        actual["observed"] = new JsonObject { ["runtimeDescription"] = RuntimeInformation.FrameworkDescription, ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : "linux", ["fixtureSha256"] = Hash(typeof(OutputAccountingFixture).Assembly.Location), ["coreSha256"] = Hash(typeof(CoreLogRuntime).Assembly.Location), ["inputsSha256"] = Hash(path) };
        Console.WriteLine(actual.ToJsonString()); return 0;
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
