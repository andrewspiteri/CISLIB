using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Events;
using CIS.Logging.Runtime;
using CIS.Logging.Scopes;

namespace CIS.Tests.DiscoveryFixture;

internal static class OutputQueueFixture
{
    private static readonly AsyncLocal<string?> Caller = new();
    private sealed class Writer
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource SecondWritten = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly MemoryStream Bytes = new();
        internal readonly List<EncodedEvent> Events = [];
        internal int Calls, Active, MaximumActive;
        internal bool AmbientSeen, InlineSeen;
        internal async ValueTask WriteAsync(EncodedEvent captured, Task release, ThreadLocal<bool> producer)
        {
            int active = Interlocked.Increment(ref Active); RecordMaximum(ref MaximumActive, active);
            AmbientSeen |= Caller.Value is not null; InlineSeen |= producer.Value;
            try
            {
                if (Interlocked.Increment(ref Calls) == 1) { Started.TrySetResult(); await release.ConfigureAwait(false); }
                await Bytes.WriteAsync(captured.JsonLine).ConfigureAwait(false); Events.Add(captured);
                if (Events.Count == 2) { SecondWritten.TrySetResult(); }
            }
            finally { Interlocked.Decrement(ref Active); }
        }
    }
    internal static async Task<JsonObject> ExecuteAsync(JsonObject row)
    {
        int capacity = row["capacity"]!.GetValue<int>(); bool consoleEnabled = row["consoleEnabled"]!.GetValue<bool>(), fileEnabled = row["fileEnabled"]!.GetValue<bool>();
        bool independent = row["mode"]!.GetValue<string>() == "independent";
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var producer = new ThreadLocal<bool>(() => false);
        var console = new Writer(); var file = new Writer();
        EncodedEvent[] captured = Enumerable.Range(0, capacity + 2).Select(index => EventEncoder.Encode(EventProjection.Capture(EventQualificationFixture.Metadata().CaptureMetadata(), new OperationScopeStore().Capture(), "Queue", CoreLogLevel.Information,
            new LogEventData { EventName = "queue.event", Message = "record-" + index }, new LoggerFoundationFixture.Clock())!, 65536)).ToArray();
        byte[][] originalBytes = captured.Select(item => item.JsonLine.ToArray()).ToArray();
        string? previous = Caller.Value; Caller.Value = "CIS_CREATOR_CONTEXT";
        var queues = new OutputQueues(capacity, consoleEnabled, fileEnabled,
            consoleEnabled ? item => console.WriteAsync(item, independent ? Task.CompletedTask : release.Task, producer) : null,
            fileEnabled ? item => file.WriteAsync(item, release.Task, producer) : null);
        try
        {
            var observations = new JsonObject();
            foreach ((string name, OutputEventQueue? queue, Writer writer) in new[] { ("console", queues.Console, console), ("file", queues.File, file) })
            {
                if (queue is null) { continue; }
                producer.Value = true; bool admitted;
                try { admitted = queue.TryEnqueue(captured[0]); } finally { producer.Value = false; }
                if (!admitted) { throw new InvalidDataException("Empty queue rejected initial event."); }
                await writer.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
                observations[name] = new JsonObject { ["admissions"] = new JsonArray(true) };
            }
            if (independent)
            {
                if (!queues.Console!.TryEnqueue(captured[1]) || !queues.File!.TryEnqueue(captured[1])) { throw new InvalidDataException("Independent queue admission failed."); }
                observations["console"]!["admissions"]!.AsArray().Add(true); observations["file"]!["admissions"]!.AsArray().Add(true);
                await console.SecondWritten.Task.WaitAsync(TimeSpan.FromSeconds(10));
                observations["consoleProgressWhileFileBlocked"] = file.Events.Count == 0 && file.Calls == 1 && console.Events.Count == 2;
                observations["file"]!["pendingAtBarrier"] = queues.File.PendingCount;
                observations["console"]!["pendingAtBarrier"] = queues.Console.PendingCount;
            }
            else
            {
                foreach ((string name, OutputEventQueue? queue) in new[] { ("console", queues.Console), ("file", queues.File) })
                {
                    if (queue is null) { continue; }
                    for (int index = 1; index < captured.Length; index++)
                    {
                        producer.Value = true;
                        try { observations[name]!["admissions"]!.AsArray().Add(queue.TryEnqueue(captured[index])); } finally { producer.Value = false; }
                    }
                    observations[name]!["pendingAtBarrier"] = queue.PendingCount;
                }
            }
            queues.Complete(); queues.Complete();
            foreach ((string name, OutputEventQueue? queue) in new[] { ("console", queues.Console), ("file", queues.File) })
            { if (queue is not null) { observations[name]!["admittedAfterComplete"] = queue.TryEnqueue(captured[^1]); } }
            release.TrySetResult(); await queues.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            foreach ((string name, OutputEventQueue? queue, Writer writer) in new[] { ("console", queues.Console, console), ("file", queues.File, file) })
            {
                if (queue is null) { continue; }
                JsonObject output = observations[name]!.AsObject(); output["maximumActive"] = writer.MaximumActive; output["activeAfterComplete"] = writer.Active;
                output["ambientSeen"] = writer.AmbientSeen; output["inlineSeen"] = writer.InlineSeen; output["pendingAfterComplete"] = queue.PendingCount;
                output["ownedReferences"] = writer.Events.Select((item, index) => ReferenceEquals(item, captured[index])).All(value => value);
                output["stream"] = Convert.ToBase64String(writer.Bytes.ToArray());
                output["expectedStream"] = Convert.ToBase64String(originalBytes.Take(writer.Events.Count).SelectMany(item => item).ToArray());
                output["messages"] = new JsonArray(writer.Events.Select(item => JsonNode.Parse(item.JsonLine.Span)!["message"]!.DeepClone()).ToArray());
            }
            return new JsonObject
            {
                ["caseId"] = row["id"]!.DeepClone(),
                ["outputs"] = observations,
                ["workers"] = (queues.Console is null ? 0 : 1) + (queues.File is null ? 0 : 1),
                ["distinctWorkers"] = queues.Console is null || queues.File is null || !ReferenceEquals(queues.Console.Completion, queues.File.Completion),

                ["completed"] = queues.Completion.IsCompletedSuccessfully
            };
        }
        finally { release.TrySetResult(); queues.Complete(); await queues.Completion.WaitAsync(TimeSpan.FromSeconds(10)); Caller.Value = previous; console.Bytes.Dispose(); file.Bytes.Dispose(); }
    }
    internal static void RecordMaximum(ref int maximum, int value)
    {
        int current = Volatile.Read(ref maximum);
        while (value > current)
        {
            int observed = Interlocked.CompareExchange(ref maximum, value, current);
            if (observed == current) { return; }
            current = observed;
        }
    }
    internal static int Run(string path, string id)
    {
        JsonObject row = JsonNode.Parse(File.ReadAllText(path))!["cases"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == id)!.AsObject();
        JsonObject actual = ExecuteAsync(row).GetAwaiter().GetResult(); OutputQueueExpectations.Validate(row, actual);
        actual["observed"] = new JsonObject
        {
            ["runtimeDescription"] = RuntimeInformation.FrameworkDescription,
            ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "unknown",
            ["fixtureSha256"] = Hash(typeof(OutputQueueFixture).Assembly.Location),
            ["coreSha256"] = Hash(typeof(LogEventData).Assembly.Location),
            ["inputsSha256"] = Hash(path)
        };
        Console.WriteLine(actual.ToJsonString()); return 0;
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
