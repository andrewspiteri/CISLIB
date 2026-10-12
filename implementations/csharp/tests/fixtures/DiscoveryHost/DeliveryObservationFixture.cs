using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;

namespace CIS.Tests.DiscoveryFixture;

internal static class DeliveryObservationFixture
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);
    private sealed class ControlledStream : Stream
    {
        private readonly string mode;
        private readonly MemoryStream bytes = new();
        internal ControlledStream(string mode)
        {
            this.mode = mode;
            Error = mode == "partial-cancel" ? new OperationCanceledException("controlled terminal cancellation", Cancellation.Token) : new IOException("controlled terminal write failure");
        }
        internal readonly CancellationTokenSource Cancellation = new();
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly Exception Error;
        internal readonly List<(long Ticket, byte[] Before, byte[] After)> Attempted = [];
        internal readonly List<long> Confirmed = [];
        private int calls;
        internal bool Fails => mode is "partial-failure" or "complete-failure" or "zero-failure" or "partial-cancel";
        internal byte[] Bytes => bytes.ToArray();
        internal async ValueTask Write(AdmittedEvent item)
        {
            int index = Attempted.Count; Attempted.Add((item.AcceptedSequence, item.Event.JsonLine.ToArray(), []));
            try { await WriteAsync(item.Event.JsonLine, Cancellation.Token); Confirmed.Add(item.AcceptedSequence); }
            finally { var before = Attempted[index]; Attempted[index] = (before.Ticket, before.Before, item.Event.JsonLine.ToArray()); }
        }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (++calls != 1) { bytes.Write(buffer.Span); return ValueTask.CompletedTask; }
            int prefix = mode.StartsWith("complete", StringComparison.Ordinal) ? buffer.Length : mode == "zero-failure" ? 0 : buffer.Length / 2;
            bytes.Write(buffer.Span[..prefix]); Started.TrySetResult(); return FinishFirstAsync(buffer, prefix);
        }
        private async ValueTask FinishFirstAsync(ReadOnlyMemory<byte> buffer, int prefix)
        {
            // An adversarial stream may ignore a cancellation request while its I/O is unresolved.
            await Release.Task;
            if (Fails) { throw Error; }
            bytes.Write(buffer.Span[prefix..]);
        }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => bytes.Length;
        public override long Position { get => bytes.Position; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException("This fixture confirms writes, not flush.");
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException("Use the controlled async write.");
        protected override void Dispose(bool disposing) { if (disposing) { bytes.Dispose(); Cancellation.Dispose(); } base.Dispose(disposing); }
    }
    internal static async Task<JsonObject> ExecuteAsync(JsonObject row)
    {
        bool consoleEnabled = row["consoleEnabled"]!.GetValue<bool>(), fileEnabled = row["fileEnabled"]!.GetValue<bool>();
        using var console = new ControlledStream(row["consoleMode"]!.GetValue<string>());
        using var file = new ControlledStream(row["fileMode"]!.GetValue<string>());
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(EventQualificationFixture.Metadata().CaptureMetadata(),
            new LoggingOptions { QueueCapacity = 2, MaxEntryBytes = 16384, ConsoleLevel = consoleEnabled ? CoreLogLevel.Warning : CoreLogLevel.None, FileLevel = fileEnabled ? CoreLogLevel.Warning : CoreLogLevel.None, FilePath = fileEnabled ? "control.jsonl" : null },
            consoleEnabled ? console.Write : null, fileEnabled ? file.Write : null);
        OutputQueues queues = runtime.QueuedOutputs!; CoreLogger logger = runtime.CreateLogger("Delivery");
        var outputs = new[] { (Name: "console", Queue: queues.Console, Stream: console), (Name: "file", Queue: queues.File, Stream: file) }.Where(item => item.Queue is not null).ToArray();
        var points = new List<(string Phase, RuntimeAccounting Value)>(); var results = new JsonArray(); int originalErrors = 0;
        void Capture(string phase) => points.Add((phase, runtime.CaptureAccounting()));
        void Result(string call, LogWriteResult value) => results.Add(new JsonObject { ["call"] = call, ["outcome"] = value.Outcome.ToString(), ["truncated"] = value.IsTruncated, ["error"] = value.ErrorCode, ["outputs"] = new JsonArray(value.Outputs.Select(item => (JsonNode)new JsonObject { ["name"] = item.OutputName, ["status"] = item.Status.ToString(), ["ticket"] = item.AcceptedSequence }).ToArray()) });
        LogEventData Data(string message) => new() { EventName = "delivery.event", Message = message };
        async Task Observe(OutputEventQueue queue, ControlledStream stream)
        {
            try { await queue.Completion.WaitAsync(Limit); if (stream.Fails) { throw new InvalidDataException("Expected terminal writer error."); } }
            catch (Exception error) when (stream.Fails && ReferenceEquals(error, stream.Error)) { originalErrors++; }
        }
        JsonObject State()
        {
            var state = new JsonObject(); foreach (var item in outputs) { state[item.Name] = new JsonObject { ["bytes"] = Convert.ToBase64String(item.Stream.Bytes), ["cancellationRequested"] = item.Stream.Cancellation.IsCancellationRequested }; }
            return state;
        }
        try
        {
            Capture("initial"); Result("seed", logger.Write(CoreLogLevel.Warning, Data("seed \U0001F642\n\u0001quote\"")));
            await Task.WhenAll(outputs.Select(item => item.Stream.Started.Task)).WaitAsync(Limit);
            Result("none", logger.WriteLazy(CoreLogLevel.None, () => throw new InvalidDataException("Filtered factory ran.")));
            Result("queued-one", logger.Write(CoreLogLevel.Warning, Data("queued-one")));
            Result("queued-two", logger.Write(CoreLogLevel.Warning, Data(string.Concat(Enumerable.Repeat("\U0001F642", 10000)))));
            Result("invalid", logger.Write(CoreLogLevel.Warning, new LogEventData { EventName = "", Message = "invalid" }));
            Result("full", logger.Write(CoreLogLevel.Warning, Data("full"))); Capture("held"); JsonObject held = State();
            foreach (var item in outputs) { if (row[item.Name + "Cancel"]!.GetValue<bool>()) { item.Stream.Cancellation.Cancel(); } }
            Capture("cancellation-requested"); JsonObject cancelled = State();
            runtime.CloseAdmission(); Result("stopped", logger.WriteLazy(CoreLogLevel.Warning, () => throw new InvalidDataException("Stopped factory ran."))); Capture("closed");
            string first = row["releaseFirst"]!.GetValue<string>();
            foreach (var item in outputs.Where(item => item.Name == first)) { item.Stream.Release.TrySetResult(); await Observe(item.Queue!, item.Stream); }
            Capture("first-terminal");
            foreach (var item in outputs.Where(item => item.Name != first)) { item.Stream.Release.TrySetResult(); await Observe(item.Queue!, item.Stream); }
            Capture("final");
            var wires = new JsonObject(); foreach (var item in outputs)
            {
                wires[item.Name] = new JsonObject { ["stream"] = Convert.ToBase64String(item.Stream.Bytes), ["attempted"] = new JsonArray(item.Stream.Attempted.Select(record => (JsonNode)new JsonObject { ["ticket"] = record.Ticket, ["before"] = Convert.ToBase64String(record.Before), ["after"] = Convert.ToBase64String(record.After) }).ToArray()), ["confirmed"] = new JsonArray(item.Stream.Confirmed.Select(value => (JsonNode)JsonValue.Create(value)!).ToArray()) };
            }
            return new JsonObject { ["caseId"] = row["id"]!.DeepClone(), ["checkpoints"] = new JsonArray(points.Select(point => (JsonNode)new JsonObject { ["phase"] = point.Phase, ["observation"] = Project(point.Value) }).ToArray()), ["results"] = results, ["held"] = held, ["cancellationRequested"] = cancelled, ["wires"] = wires, ["originalErrors"] = originalErrors };
        }
        finally
        {
            console.Release.TrySetResult(); file.Release.TrySetResult(); runtime.CloseAdmission();
            try { await queues.Completion.WaitAsync(Limit); }
            catch (Exception error) when (ReferenceEquals(error, console.Error) || ReferenceEquals(error, file.Error)) { }
        }
    }
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static JsonObject Project(RuntimeAccounting value)
    {
        var outputs = new JsonObject(); if (value.Console is OutputAccounting console) { outputs["console"] = Project(console); }
        if (value.File is OutputAccounting file) { outputs["file"] = Project(file); }
        return new JsonObject { ["closed"] = value.AdmissionClosed, ["invalid"] = Number(value.InvalidEventCount), ["truncated"] = Number(value.TruncatedEventCount), ["outputs"] = outputs };
    }
    private static JsonObject Project(OutputAccounting value) => new()
    {
        ["sequence"] = Number(value.AcceptedSequence),
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
        JsonObject row = JsonNode.Parse(File.ReadAllText(path))!["cases"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == id)!.AsObject(); JsonObject actual = ExecuteAsync(row).GetAwaiter().GetResult(); DeliveryObservationExpectations.Validate(row, actual);
        actual["observed"] = new JsonObject { ["runtimeDescription"] = RuntimeInformation.FrameworkDescription, ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : "linux", ["fixtureSha256"] = Hash(typeof(DeliveryObservationFixture).Assembly.Location), ["coreSha256"] = Hash(typeof(CoreLogRuntime).Assembly.Location), ["inputsSha256"] = Hash(path) }; Console.WriteLine(actual.ToJsonString()); return 0;
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
