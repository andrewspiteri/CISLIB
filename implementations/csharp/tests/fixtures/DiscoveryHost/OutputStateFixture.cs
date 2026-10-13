using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;

namespace CIS.Tests.DiscoveryFixture;

internal static class OutputStateFixture
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    private sealed class HostileIOException : IOException
    {
        internal int Accesses;
        public override string Message { get { Interlocked.Increment(ref Accesses); throw new InvalidOperationException("Do not inspect private exception payload."); } }
        public override string ToString() { Interlocked.Increment(ref Accesses); throw new InvalidOperationException("Do not format private exception payload."); }
    }
    private sealed class Writer(string mode) : IDisposable
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly List<(long Ticket, byte[] Bytes)> Attempts = [];
        internal readonly List<long> Confirmed = [];
        internal readonly MemoryStream Stream = new();
        internal readonly Exception Error = mode == "cancel" ? new OperationCanceledException("private controlled cancellation") : new HostileIOException();
        internal bool Fails => mode is "failure" or "cancel";
        internal async ValueTask Write(AdmittedEvent item)
        {
            byte[] bytes = item.Event.JsonLine.ToArray(); Attempts.Add((item.AcceptedSequence, bytes));
            if (Attempts.Count == 1)
            {
                Stream.Write(bytes.AsSpan(0, bytes.Length / 2)); Started.TrySetResult(); await Release.Task;
                if (Fails) { throw Error; }
                Stream.Write(bytes.AsSpan(bytes.Length / 2));
            }
            else { Stream.Write(bytes); }
            Confirmed.Add(item.AcceptedSequence);
        }
        public void Dispose() => Stream.Dispose();
    }
    internal static async Task<JsonObject> ExecuteAsync(JsonObject row, bool captureDiagnostics = false)
    {
        using var console = new Writer(row["consoleMode"]!.GetValue<string>());
        using var file = new Writer(row["fileMode"]!.GetValue<string>());
        bool enabledConsole = row["consoleMode"]!.GetValue<string>() != "disabled", enabledFile = row["fileMode"]!.GetValue<string>() != "disabled";
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(EventQualificationFixture.Metadata().CaptureMetadata(),
            new LoggingOptions { QueueCapacity = 3, FilePath = enabledFile ? "control.jsonl" : null, ConsoleLevel = enabledConsole ? CoreLogLevel.Information : CoreLogLevel.None },
            enabledConsole ? console.Write : null, enabledFile ? file.Write : null);
        OutputQueues queues = runtime.QueuedOutputs!;
        var outputs = new[] { (Name: "console", Queue: queues.Console, Writer: console), (Name: "file", Queue: queues.File, Writer: file) }.Where(item => item.Queue is not null).ToArray();
        var checkpoints = new List<(string Phase, RuntimeAccounting Counts)>();
        DateTimeOffset before = DateTimeOffset.UtcNow; int errors = 0;
        var diagnostics = captureDiagnostics ? new List<(string Phase, CoreLogDiagnostics Value)>() : null;
        DateTimeOffset beforeDiagnostics = DateTimeOffset.UtcNow;
        void Capture(string phase) { checkpoints.Add((phase, runtime.CaptureAccounting())); diagnostics?.Add((phase, runtime.GetDiagnostics())); }
        async Task Join(OutputEventQueue queue, Writer writer)
        {
            try { await queue.Completion.WaitAsync(Limit); if (writer.Fails) { throw new InvalidDataException("Writer did not preserve terminal failure."); } }
            catch (Exception error) when (writer.Fails && ReferenceEquals(error, writer.Error)) { errors++; }
        }
        try
        {
            CoreLogger logger = runtime.CreateLogger("States");
            LogEventData Data(int id) => new() { EventName = "states.event", Message = "record-" + id };
            Capture("initial"); logger.Write(CoreLogLevel.Information, Data(1));
            await Task.WhenAll(outputs.Select(item => item.Writer.Started.Task)).WaitAsync(Limit);
            logger.Write(CoreLogLevel.Information, Data(2)); logger.Write(CoreLogLevel.Information, Data(3)); Capture("held");
            if (row["closeBeforeRelease"]!.GetValue<bool>()) { runtime.CloseAdmission(); Capture("stop-requested"); }
            string first = row["releaseFirst"]!.GetValue<string>();
            foreach (var item in outputs.Where(item => item.Name == first)) { item.Writer.Release.TrySetResult(); await Join(item.Queue!, item.Writer); }
            Capture("first-terminal");
            LogWriteResult result = logger.WriteLazy(CoreLogLevel.Information, () => Data(4)); Capture("after-admission");
            runtime.CloseAdmission(); Capture("closing");
            foreach (var item in outputs.Where(item => item.Name != first)) { item.Writer.Release.TrySetResult(); await Join(item.Queue!, item.Writer); }
            Capture("final"); runtime.CloseAdmission(); Capture("repeated-close");
            var wires = new JsonObject(); foreach (var item in outputs)
            {
                wires[item.Name] = new JsonObject
                {
                    ["attempts"] = new JsonArray(item.Writer.Attempts.Select(record => (JsonNode)new JsonObject { ["ticket"] = record.Ticket, ["bytes"] = Convert.ToBase64String(record.Bytes) }).ToArray()),
                    ["confirmed"] = new JsonArray(item.Writer.Confirmed.Select(ticket => (JsonNode)JsonValue.Create(ticket)!).ToArray()),
                    ["bytes"] = Convert.ToBase64String(item.Writer.Stream.ToArray()),
                    ["privateExceptionAccesses"] = item.Writer.Error is HostileIOException hostile ? hostile.Accesses : 0
                };
            }
            var actual = new JsonObject
            {
                ["caseId"] = row["id"]!.DeepClone(),
                ["beforeUtc"] = before.ToString("O", CultureInfo.InvariantCulture),
                ["afterUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                ["checkpoints"] = new JsonArray(checkpoints.Select(point => (JsonNode)Project(point.Phase, point.Counts)).ToArray()),
                ["result"] = new JsonObject { ["outcome"] = result.Outcome.ToString(), ["outputs"] = new JsonArray(result.Outputs.Select(item => (JsonNode)new JsonObject { ["name"] = item.OutputName, ["status"] = item.Status.ToString(), ["ticket"] = item.AcceptedSequence }).ToArray()) },
                ["wires"] = wires,
                ["originalErrors"] = errors
            };
            if (diagnostics is not null)
            {
                actual["diagnostics"] = new JsonArray(diagnostics.Select(point => (JsonNode)DiagnosticsProjection.Project(point.Phase, point.Value)).ToArray());
                actual["diagnosticsBeforeUtc"] = beforeDiagnostics.ToString("O", CultureInfo.InvariantCulture);
                actual["diagnosticsAfterUtc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            }
            return actual;
        }
        finally
        {
            console.Release.TrySetResult(); file.Release.TrySetResult(); runtime.CloseAdmission();
            try { await queues.Completion.WaitAsync(Limit); }
            catch (Exception error) when (ReferenceEquals(error, console.Error) || ReferenceEquals(error, file.Error)) { }
        }
    }
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static JsonObject Project(string phase, RuntimeAccounting counts)
    {
        var outputs = new JsonObject(); if (counts.Console is OutputAccounting console) { outputs["console"] = Project(console); }
        if (counts.File is OutputAccounting file) { outputs["file"] = Project(file); }
        return new JsonObject { ["phase"] = phase, ["closed"] = counts.AdmissionClosed, ["invalid"] = Number(counts.InvalidEventCount), ["truncated"] = Number(counts.TruncatedEventCount), ["outputs"] = outputs };
    }
    private static JsonObject Project(OutputAccounting counts) => new()
    {
        ["state"] = counts.State.ToString(),
        ["fault"] = counts.LastFaultCode,
        ["faultAtUtc"] = counts.LastFaultAtUtc?.ToString("O", CultureInfo.InvariantCulture),
        ["accepted"] = Number(counts.Accepted),
        ["written"] = Number(counts.Written),
        ["failed"] = Number(counts.Failed),
        ["abandoned"] = Number(counts.Abandoned),
        ["pending"] = Number(counts.Pending),
        ["inFlight"] = Number(counts.InFlight),
        ["unavailable"] = Number(counts.Unavailable)
    };
    internal static int Run(string path, string id)
    {
        JsonObject row = JsonNode.Parse(File.ReadAllText(path))!["cases"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == id)!.AsObject();
        JsonObject actual = ExecuteAsync(row).GetAwaiter().GetResult(); OutputStateExpectations.Validate(row, actual);
        actual["observed"] = new JsonObject { ["runtimeDescription"] = RuntimeInformation.FrameworkDescription, ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : "linux", ["fixtureSha256"] = Hash(typeof(OutputStateFixture).Assembly.Location), ["coreSha256"] = Hash(typeof(CoreLogRuntime).Assembly.Location), ["inputsSha256"] = Hash(path) };
        Console.WriteLine(actual.ToJsonString()); return 0;
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
