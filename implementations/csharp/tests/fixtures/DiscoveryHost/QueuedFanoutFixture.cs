using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;

namespace CIS.Tests.DiscoveryFixture;

internal static class QueuedFanoutFixture
{
    private sealed class Clock : TimeProvider
    {
        private int calls;
        internal int Calls => Volatile.Read(ref calls);
        public override DateTimeOffset GetUtcNow() { Interlocked.Increment(ref calls); return EventQualificationFixture.Instant; }
    }
    private sealed class Writer
    {
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource SecondWritten = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly MemoryStream Bytes = new();
        internal readonly List<AdmittedEvent> Attempted = [], Written = [];
        internal readonly List<byte[]> Original = [];
        internal readonly IOException Failure = new("controlled queue writer fault");
        internal object? Gate;
        internal bool GateHeld, OriginalFaultObserved;
        internal int Active, MaximumActive;
        internal async ValueTask WriteAsync(AdmittedEvent admitted, Task release, bool fail)
        {
            int active = Interlocked.Increment(ref Active); RecordMaximum(ref MaximumActive, active);
            try
            {
                GateHeld |= Monitor.IsEntered(Gate!); Attempted.Add(admitted); Original.Add(admitted.Event.JsonLine.ToArray());
                if (Attempted.Count == 1) { Started.TrySetResult(); await release.ConfigureAwait(false); }
                if (fail) { throw Failure; }
                await Bytes.WriteAsync(admitted.Event.JsonLine).ConfigureAwait(false); Written.Add(admitted);
                if (Written.Count == 2) { SecondWritten.TrySetResult(); }
            }
            finally { Interlocked.Decrement(ref Active); }
        }
        internal async Task Observe(Task completion)
        {
            try { await completion.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (IOException error) when (ReferenceEquals(error, Failure)) { OriginalFaultObserved = true; }
        }
    }
    internal static async Task<JsonObject> ExecuteAsync(JsonObject row)
    {
        string mode = row["mode"]!.GetValue<string>(); bool consoleEnabled = row["consoleEnabled"]!.GetValue<bool>(), fileEnabled = row["fileEnabled"]!.GetValue<bool>();
        var options = new LoggingOptions
        {
            ConsoleLevel = consoleEnabled ? Enum.Parse<CoreLogLevel>(row["consoleLevel"]!.GetValue<string>()) : CoreLogLevel.None,
            FileLevel = fileEnabled ? Enum.Parse<CoreLogLevel>(row["fileLevel"]!.GetValue<string>()) : CoreLogLevel.None,
            FilePath = fileEnabled ? Path.Combine(Directory.GetCurrentDirectory(), "cis-no-destination-created.jsonl") : null,
            QueueCapacity = row["capacity"]!.GetValue<int>()
        };
        var metadata = EventQualificationFixture.Metadata(); metadata.ApplicationName = "Fanout.App";
        var clock = new Clock(); var console = new Writer(); var file = new Writer();
        var releaseConsole = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFile = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var factoryRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var produced = new ConcurrentDictionary<int, LogWriteResult>(); var data = new ConcurrentDictionary<int, LogEventData>();
        int factories = 0, consolePendingMaximum = 0, filePendingMaximum = 0;
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(metadata.CaptureMetadata(), options,
            consoleEnabled ? admitted => console.WriteAsync(admitted, mode == "independent" ? Task.CompletedTask : releaseConsole.Task, mode is "early-fault" or "console-fault-overflow") : null,
            fileEnabled ? admitted => file.WriteAsync(admitted, mode == "independent-file" ? Task.CompletedTask : releaseFile.Task, mode is "early-fault" or "late-fault" or "file-fault-overflow") : null, clock);
        OutputQueues queues = runtime.QueuedOutputs!; object gate = typeof(CoreLogRuntime).GetField("admissionGate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(runtime)!;
        console.Gate = gate; file.Gate = gate; CoreLogger logger = runtime.CreateLogger("Fanout");
        JsonObject[] steps = row["steps"]!.AsArray().Select(step => step!.AsObject()).ToArray();
        Task[] producers = [];
        LogEventData Content(int label) { var value = new LogEventData { EventName = "fanout.event", Message = "record-" + label, Attributes = new Dictionary<string, LogValue> { ["input"] = LogValue.Integer(label) } }; data[label] = value; return value; }
        LogWriteResult Write(JsonObject step, bool holdFactory = false)
        {
            int label = step["label"]!.GetValue<int>(); CoreLogLevel level = Enum.Parse<CoreLogLevel>(step["level"]!.GetValue<string>());
            LogWriteResult result = step["lazy"]!.GetValue<bool>() ? logger.WriteLazy(level, () =>
            {
                Interlocked.Increment(ref factories); if (holdFactory) { factoryEntered.TrySetResult(); factoryRelease.Task.GetAwaiter().GetResult(); }
                return Content(label);
            }) : logger.Write(level, Content(label));
            produced[label] = result; return result;
        }
        void ObservePending()
        {
            RecordMaximum(ref consolePendingMaximum, queues.Console?.BufferedCount ?? 0);
            RecordMaximum(ref filePendingMaximum, queues.File?.BufferedCount ?? 0);
        }
        async Task Burst(JsonObject[] cohort)
        {
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); int arrivals = 0;
            producers = cohort.Select(step => Task.Run(async () => { if (Interlocked.Increment(ref arrivals) == cohort.Length) { ready.TrySetResult(); } await go.Task; Write(step); ObservePending(); })).ToArray();
            try { await ready.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
            finally { go.TrySetResult(); }
            await Task.WhenAll(producers).WaitAsync(TimeSpan.FromSeconds(10));
        }
        async Task StartedFor(LogWriteResult result)
        {
            foreach (LogOutputWriteResult output in result.Outputs.Where(output => output.Status == LogOutputWriteStatus.Accepted))
            { await (output.OutputName == "console" ? console.Started : file.Started).Task.WaitAsync(TimeSpan.FromSeconds(10)); }
        }
        try
        {
            if (mode == "multi")
            {
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); int arrivals = 0;
                producers = steps.Select(step => Task.Run(async () => { if (Interlocked.Increment(ref arrivals) == steps.Length) { ready.TrySetResult(); } await go.Task; Write(step); })).ToArray();
                try { await ready.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
                finally { go.TrySetResult(); }
                await Task.WhenAll(producers).WaitAsync(TimeSpan.FromSeconds(10)); await console.Started.Task.WaitAsync(TimeSpan.FromSeconds(10)); await file.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            else
            {
                LogWriteResult first = Write(steps[0]); await StartedFor(first); ObservePending();
                if (mode == "burst")
                {
                    foreach (IGrouping<int, JsonObject> cohort in steps.Skip(1).GroupBy(step => step["phase"]!.GetValue<int>()).OrderBy(cohort => cohort.Key)) { await Burst(cohort.ToArray()); }
                }
                if (mode == "mutate")
                {
                    options.ConsoleLevel = CoreLogLevel.None; options.FileLevel = CoreLogLevel.None; options.FilePath = null; options.QueueCapacity = 0; options.MaxEntryBytes = 1;
                    metadata.ApplicationName = "CIS_CHANGED_AFTER_CREATION"; data[1].Message = "CIS_CHANGED_AFTER_CAPTURE";
                }
                if (mode is "late-close" or "late-fault")
                {
                    Task<LogWriteResult> late = Task.Run(() => Write(steps[1], holdFactory: true)); producers = [late];
                    await factoryEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    if (mode == "late-close") { await Task.Run(runtime.CloseAdmission).WaitAsync(TimeSpan.FromSeconds(10)); }
                    else { releaseFile.TrySetResult(); await file.Observe(queues.File!.Completion); }
                    factoryRelease.TrySetResult(); await late.WaitAsync(TimeSpan.FromSeconds(10));
                    if (mode == "late-close") { Write(steps[2]); }
                }
                else if (mode == "early-fault")
                {
                    releaseConsole.TrySetResult(); releaseFile.TrySetResult(); await console.Observe(queues.Console!.Completion); await file.Observe(queues.File!.Completion); Write(steps[1]);
                }
                else if (mode != "burst")
                {
                    if (mode == "file-fault-overflow") { releaseFile.TrySetResult(); await file.Observe(queues.File!.Completion); }
                    if (mode == "console-fault-overflow") { releaseConsole.TrySetResult(); await console.Observe(queues.Console!.Completion); }
                    for (int index = 1; index < steps.Length; index++)
                    {
                        if (mode == "saturation" && index == 3)
                        {
                            releaseConsole.TrySetResult(); releaseFile.TrySetResult(); await console.SecondWritten.Task.WaitAsync(TimeSpan.FromSeconds(10)); await file.SecondWritten.Task.WaitAsync(TimeSpan.FromSeconds(10));
                        }
                        if (mode == "recover-console" && index == 3) { releaseConsole.TrySetResult(); await console.SecondWritten.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
                        if (mode == "recover-file" && index == 3) { releaseFile.TrySetResult(); await file.SecondWritten.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
                        if (mode == "independent" && index == 2) { await console.SecondWritten.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
                        if (mode == "independent-file" && index == 2) { await file.SecondWritten.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
                        Write(steps[index]);
                    }
                }
            }
            bool independentProgress = mode switch
            {
                "independent" => console.Written.Count >= 2 && file.Written.Count == 0,
                "independent-file" => file.Written.Count >= 2 && console.Written.Count == 0,
                _ => true
            };
            int consolePendingAtBarrier = queues.Console?.BufferedCount ?? 0, filePendingAtBarrier = queues.File?.BufferedCount ?? 0;
            int consoleActiveAtBarrier = Volatile.Read(ref console.Active), fileActiveAtBarrier = Volatile.Read(ref file.Active);
            runtime.CloseAdmission(); runtime.CloseAdmission(); releaseConsole.TrySetResult(); releaseFile.TrySetResult();
            if (queues.Console is not null) { await console.Observe(queues.Console.Completion); }
            if (queues.File is not null) { await file.Observe(queues.File.Completion); }
            try { await queues.Completion.WaitAsync(TimeSpan.FromSeconds(10)); } catch (IOException error) when (ReferenceEquals(error, console.Failure) || ReferenceEquals(error, file.Failure)) { }
            IEnumerable<KeyValuePair<int, LogWriteResult>> order = mode == "multi" ? produced.OrderBy(pair => pair.Value.Outputs[0].AcceptedSequence) : steps.Select(step => new KeyValuePair<int, LogWriteResult>(step["label"]!.GetValue<int>(), produced[step["label"]!.GetValue<int>()]));
            JsonArray results = new(), bindings = new();
            foreach (var pair in order)
            {
                JsonObject result = Result(pair.Value); results.Add(result); JsonObject binding = result.DeepClone().AsObject(); binding["caller"] = pair.Key; bindings.Add(binding);
            }
            var outputs = new JsonObject(); var wires = new JsonObject();
            foreach ((string name, OutputEventQueue? queue, Writer writer) in new[] { ("console", queues.Console, console), ("file", queues.File, file) })
            {
                if (queue is null) { continue; }
                outputs[name] = new JsonObject
                {
                    ["lastAcceptedSequence"] = queue.LastAcceptedSequence,
                    ["attemptedTickets"] = Numbers(writer.Attempted.Select(item => item.AcceptedSequence)),
                    ["writtenTickets"] = Numbers(writer.Written.Select(item => item.AcceptedSequence)),
                    ["filteredCount"] = queue.FilteredCount,
                    ["unavailableCount"] = queue.UnavailableCount,
                    ["faulted"] = queue.IsFaulted,
                    ["originalFaultObserved"] = writer.OriginalFaultObserved
                };
                wires[name] = new JsonObject { ["stream"] = Convert.ToBase64String(writer.Bytes.ToArray()), ["attempted"] = new JsonArray(writer.Attempted.Select((item, index) => (JsonNode)new JsonObject { ["ticket"] = item.AcceptedSequence, ["wire"] = Convert.ToBase64String(writer.Original[index]), ["after"] = Convert.ToBase64String(item.Event.JsonLine.Span) }).ToArray()), ["pendingAfterComplete"] = queue.BufferedCount };
            }
            var paired = console.Attempted.Join(file.Attempted, item => Message(item), item => Message(item), (left, right) => ReferenceEquals(left.Event, right.Event)).ToArray();
            JsonObject actual = new()
            {
                ["caseId"] = row["id"]!.DeepClone(),
                ["semantic"] = new JsonObject { ["results"] = results, ["factoryCalls"] = factories, ["clockCalls"] = clock.Calls, ["outputs"] = outputs, ["sharedCapturePairs"] = paired.Length, ["sharedCaptures"] = paired.All(value => value), ["callbackGateHeld"] = console.GateHeld || file.GateHeld },
                ["bindings"] = bindings,
                ["wires"] = wires,
                ["independentProgress"] = independentProgress
            };
            if (mode == "burst")
            {
                var bounds = new JsonObject();
                foreach ((string name, OutputEventQueue? queue, Writer writer, int pending, int maximum, int active) in new[] { ("console", queues.Console, console, consolePendingAtBarrier, consolePendingMaximum, consoleActiveAtBarrier), ("file", queues.File, file, filePendingAtBarrier, filePendingMaximum, fileActiveAtBarrier) })
                {
                    if (queue is null) { continue; }
                    bounds[name] = new JsonObject { ["pendingAtBarrier"] = pending, ["pendingMaximum"] = maximum, ["activeAtBarrier"] = active, ["maximumActive"] = Volatile.Read(ref writer.MaximumActive), ["activeAfterComplete"] = Volatile.Read(ref writer.Active), ["pendingAfterComplete"] = queue.BufferedCount };
                }
                actual["bounds"] = bounds;
                actual["workers"] = (queues.Console is null ? 0 : 1) + (queues.File is null ? 0 : 1);
            }
            return actual;
        }
        finally
        {
            factoryRelease.TrySetResult(); releaseConsole.TrySetResult(); releaseFile.TrySetResult(); await Task.WhenAll(producers).WaitAsync(TimeSpan.FromSeconds(10)); runtime.CloseAdmission();
            if (queues.Console is not null) { await console.Observe(queues.Console.Completion); }
            if (queues.File is not null) { await file.Observe(queues.File.Completion); }
            try { await queues.Completion.WaitAsync(TimeSpan.FromSeconds(10)); } catch (IOException error) when (ReferenceEquals(error, console.Failure) || ReferenceEquals(error, file.Failure)) { }
            console.Bytes.Dispose(); file.Bytes.Dispose();
        }
    }
    private static void RecordMaximum(ref int maximum, int value)
    {
        int current = Volatile.Read(ref maximum);
        while (value > current) { int observed = Interlocked.CompareExchange(ref maximum, value, current); if (observed == current) { return; } current = observed; }
    }
    private static JsonArray Numbers(IEnumerable<long> values) => new(values.Select(value => (JsonNode)JsonValue.Create(value)).ToArray());
    private static string Message(AdmittedEvent item) => JsonNode.Parse(item.Event.JsonLine.Span)!["message"]!.GetValue<string>();
    private static JsonObject Result(LogWriteResult result) => new() { ["outcome"] = result.Outcome.ToString(), ["outputs"] = new JsonArray(result.Outputs.Select(output => (JsonNode)new JsonObject { ["name"] = output.OutputName, ["status"] = output.Status.ToString(), ["ticket"] = output.AcceptedSequence }).ToArray()) };
    internal static int Run(string path, string id)
    {
        JsonObject row = JsonNode.Parse(File.ReadAllText(path))!["cases"]!.AsArray().Single(row => row!["id"]!.GetValue<string>() == id)!.AsObject(); JsonObject actual = ExecuteAsync(row).GetAwaiter().GetResult(); if (row["mode"]!.GetValue<string>() == "burst") { QueueBurstExpectations.Validate(row, actual); } else { QueuedFanoutExpectations.Validate(row, actual); }
        actual["observed"] = new JsonObject { ["runtimeDescription"] = RuntimeInformation.FrameworkDescription, ["operatingSystem"] = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "unknown", ["coreSha256"] = Hash(typeof(CoreLogRuntime).Assembly.Location), ["fixtureSha256"] = Hash(typeof(QueuedFanoutFixture).Assembly.Location), ["inputsSha256"] = Hash(path) };
        Console.WriteLine(actual.ToJsonString()); return 0;
    }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
