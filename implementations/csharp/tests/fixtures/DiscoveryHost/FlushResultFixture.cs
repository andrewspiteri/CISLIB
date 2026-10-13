using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.Logging;
using CIS.Logging.Runtime;

namespace CIS.Tests.DiscoveryFixture;

internal static class FlushResultFixture
{
    internal static readonly string[] Modes = ["empty", "filtered", "closed", "physical", "write-timeout", "flush-timeout", "cancel", "pre-cancel",
        "loss", "write-failure", "flush-failure", "later-failure", "exhaustion", "clock", "dual", "long-timeout", "system-timeout"];
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(15);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal sealed class Clock : TimeProvider
    {
        private long ticks;
        internal DateTimeOffset Utc = new(2026, 10, 13, 15, 16, 17, TimeSpan.Zero);
        internal int Created, Disposed, Fires;
        internal long? NextDueTicks => timer?.Due;
        private Timer? timer;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref ticks);
        public override DateTimeOffset GetUtcNow() => Utc;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (timer is not null) { throw new InvalidOperationException("Fixture expects at most one live deadline owner."); }
            Created++; timer = new Timer(this, callback, state); timer.Change(dueTime, period); return timer;
        }
        internal void Advance(TimeSpan elapsed) { Interlocked.Add(ref ticks, elapsed.Ticks); if (timer is { } current && current.Due <= ticks) { Fire(); } }
        internal void Fire()
        {
            Timer? current = timer; if (current is null) { return; }
            current.Due = long.MaxValue; Fires++; current.Callback(current.State);
        }
        private sealed class Timer(Clock clock, TimerCallback callback, object? state) : ITimer
        {
            internal readonly TimerCallback Callback = callback;
            internal readonly object? State = state;
            internal long Due = long.MaxValue;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (period != Timeout.InfiniteTimeSpan) { throw new InvalidOperationException("Only one-shot timers expected."); }
                Due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : clock.ticks + dueTime.Ticks; return true;
            }
            public void Dispose() { if (ReferenceEquals(clock.timer, this)) { clock.timer = null; clock.Disposed++; } }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
    private sealed class Writer : IDisposable
    {
        internal readonly TaskCompletionSource WriteStarted = Signal(), FlushStarted = Signal(), ReleaseWrite = Signal(), ReleaseFlush = Signal();
        internal readonly MemoryStream Bytes = new();
        internal bool HoldFlush, FailWrite, FailFlush, FailSecond;
        internal int Writes, Flushes;
        internal async ValueTask WriteAsync(AdmittedEvent admitted)
        {
            int attempt = ++Writes;
            if (attempt == 1) { WriteStarted.TrySetResult(); await ReleaseWrite.Task; }
            if (FailWrite || FailSecond && attempt == 2) { throw new IOException("private controlled write failure"); }
            await Bytes.WriteAsync(admitted.Event.JsonLine);
        }
        internal async ValueTask FlushAsync()
        {
            Flushes++; FlushStarted.TrySetResult();
            if (HoldFlush) { await ReleaseFlush.Task; }
            if (FailFlush) { throw new IOException("private controlled flush failure"); }
            await Bytes.FlushAsync();
        }
        public void Dispose() { ReleaseWrite.TrySetResult(); ReleaseFlush.TrySetResult(); Bytes.Dispose(); }
    }
    internal static int Run(string mode, string root)
    { Console.WriteLine(ExecuteAsync(mode, root).GetAwaiter().GetResult().ToJsonString()); return 0; }
    internal static async Task<JsonObject> ExecuteAsync(string mode, string root)
    {
        if (!Modes.Contains(mode, StringComparer.Ordinal)) { throw new ArgumentException("Unknown flush-result mode.", nameof(mode)); }
        if (mode == "physical") { return await PhysicalAsync(mode, root); }
        var clock = new Clock(); using var console = new Writer(); using var file = new Writer();
        bool dual = mode == "dual", empty = mode == "empty";
        var options = new LoggingOptions
        {
            ConsoleLevel = empty ? CoreLogLevel.None : CoreLogLevel.Information,
            FileLevel = dual ? CoreLogLevel.Information : CoreLogLevel.None,
            FilePath = dual ? Path.Combine(root, "unused.jsonl") : null,
            QueueCapacity = mode == "loss" ? 1 : 4,
            MaxEntryBytes = 16384
        };
        CoreLogRuntime runtime = CoreLogRuntime.CreateQueued(ConsoleCompositionFixture.Metadata(), options,
            empty ? null : console.WriteAsync, dual ? file.WriteAsync : null,
            mode == "system-timeout" ? TimeProvider.System : clock, empty ? null : console.FlushAsync, dual ? file.FlushAsync : null);
        CoreLogger logger = runtime.CreateLogger("FlushResult"); var report = Common(mode);
        using var caller = new CancellationTokenSource();
        Task<LogFlushResult>? wait = null; LogFlushResult? result = null; int heldControls = 0;
        try
        {
            if (mode == "exhaustion")
            {
                foreach (string field in new[] { "lastAcceptedSequence", "acceptedCount", "writtenCount" })
                { typeof(OutputEventQueue).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(runtime.QueuedOutputs!.Console, long.MaxValue - 2); }
            }
            console.HoldFlush = mode is "flush-timeout" or "filtered";
            if (!empty && mode is not ("filtered" or "closed"))
            {
                console.FailWrite = mode == "write-failure";
                console.FailFlush = mode == "flush-failure"; console.FailSecond = mode == "later-failure";
                Accepted(logger.Write(CoreLogLevel.Information, Data("first")));
                await console.WriteStarted.Task.WaitAsync(Guard);
                if (dual) { await file.WriteStarted.Task.WaitAsync(Guard); }
            }
            if (mode is "loss" or "write-failure" or "exhaustion")
            { Accepted(logger.Write(CoreLogLevel.Information, Data("second"))); }
            if (mode == "loss")
            {
                Require(logger.Write(CoreLogLevel.Information, Data("overflow")).Outcome == LogWriteOutcome.Rejected);
                Require(logger.Write(CoreLogLevel.Debug, Data("filtered")).Outcome == LogWriteOutcome.Filtered);
                Require(logger.Write(CoreLogLevel.Information, Data("")).Outcome == LogWriteOutcome.InvalidEvent);
                Require(logger.Write(CoreLogLevel.Information, Data("large", true)).Outcome == LogWriteOutcome.Rejected);
            }
            if (mode == "filtered") { Require(logger.Write(CoreLogLevel.Debug, Data("filtered")).Outcome == LogWriteOutcome.Filtered); }
            if (mode == "closed") { runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(Guard); }
            if (mode == "pre-cancel") { caller.Cancel(); }
            TimeSpan budget = mode == "long-timeout" ? TimeSpan.MaxValue : mode == "system-timeout" ? TimeSpan.FromMilliseconds(30) : TimeSpan.FromSeconds(5);
            wait = runtime.FlushAsync(budget, caller.Token);
            heldControls = runtime.QueuedOutputs!.Console?.RegisteredFlushCount ?? 0;
            if (mode == "exhaustion")
            {
                Require(logger.Write(CoreLogLevel.Information, Data("exhausted")).Outcome == LogWriteOutcome.Rejected);
                report["stillPendingAfterFault"] = !wait.IsCompleted;
                clock.Advance(budget);
            }
            else if (mode == "loss")
            {
                Require(logger.Write(CoreLogLevel.Information, Data("")).Outcome == LogWriteOutcome.InvalidEvent);
                Require(logger.Write(CoreLogLevel.Debug, Data("later-filtered")).Outcome == LogWriteOutcome.Filtered);
                console.ReleaseWrite.TrySetResult();
            }
            else if (mode == "later-failure")
            { Accepted(logger.Write(CoreLogLevel.Information, Data("later"))); console.ReleaseWrite.TrySetResult(); }
            else if (mode == "flush-timeout")
            { console.ReleaseWrite.TrySetResult(); await console.FlushStarted.Task.WaitAsync(Guard); clock.Advance(budget); }
            else if (mode == "filtered") { await console.FlushStarted.Task.WaitAsync(Guard); console.ReleaseFlush.TrySetResult(); }
            else if (mode == "write-timeout") { clock.Advance(budget); }
            else if (mode == "clock")
            {
                clock.Utc = clock.Utc.AddYears(20); clock.Fire(); Require(!wait.IsCompleted);
                clock.Utc = clock.Utc.AddYears(-40); clock.Fire(); Require(!wait.IsCompleted);
                clock.Advance(TimeSpan.FromSeconds(4)); clock.Fire(); Require(!wait.IsCompleted);
                report["survivedUtcCorrections"] = true; clock.Advance(TimeSpan.FromSeconds(1));
            }
            else if (mode == "dual")
            {
                clock.Advance(TimeSpan.FromSeconds(4)); console.ReleaseWrite.TrySetResult();
                await console.FlushStarted.Task.WaitAsync(Guard);
                using var guard = new CancellationTokenSource(Guard);
                while (runtime.QueuedOutputs.Console!.RegisteredFlushCount != 0) { await Task.Delay(1, guard.Token); }
                clock.Advance(TimeSpan.FromSeconds(1));
            }
            else if (mode == "long-timeout")
            {
                clock.Advance(TimeSpan.FromDays(1));
                Require(!wait.IsCompleted && clock.Created == 1 && clock.Disposed == 0 && clock.Fires == 1 && clock.NextDueTicks == TimeSpan.TicksPerDay * 2);
                report["longTimerPhase"] = new JsonObject
                { ["created"] = clock.Created, ["disposed"] = clock.Disposed, ["fires"] = clock.Fires, ["nextDueTicks"] = Number(clock.NextDueTicks!.Value) };
                caller.Cancel();
            }
            else if (mode == "cancel") { caller.Cancel(); }
            else if (mode is "write-failure" or "flush-failure") { console.ReleaseWrite.TrySetResult(); }
            try { result = await wait.WaitAsync(Guard); report["cancelled"] = false; }
            catch (OperationCanceledException failure)
            { Require(caller.IsCancellationRequested && failure.CancellationToken == caller.Token); report["cancelled"] = true; }
            string? frozen = result is null ? null : Serialize(result).ToJsonString();
            if (result is not null) { report["result"] = Serialize(result); }
            report["controlsAfterWait"] = runtime.QueuedOutputs.Console?.RegisteredFlushCount ?? 0;
            console.ReleaseWrite.TrySetResult(); console.ReleaseFlush.TrySetResult(); file.ReleaseWrite.TrySetResult(); file.ReleaseFlush.TrySetResult();
            if (mode is not ("write-failure" or "flush-failure" or "later-failure"))
            { _ = await runtime.FlushAsync(TimeSpan.FromSeconds(5), CancellationToken.None).WaitAsync(Guard); }
            runtime.CloseAdmission();
            try { await runtime.QueuedOutputs.Completion.WaitAsync(Guard); }
            catch (IOException) when (mode is "write-failure" or "flush-failure" or "later-failure") { }
            report["immutableAfterIo"] = result is null || frozen == Serialize(result).ToJsonString();
            report["heldControls"] = heldControls;
            report["timers"] = new JsonObject { ["created"] = clock.Created, ["disposed"] = clock.Disposed, ["fires"] = clock.Fires };
            report["bytes"] = Convert.ToBase64String(console.Bytes.ToArray());
            report["fileBytes"] = Convert.ToBase64String(file.Bytes.ToArray());
            report["diagnostics"] = new JsonArray(runtime.GetDiagnostics().Outputs.Select(output => (JsonNode)new JsonObject
            {
                ["name"] = output.OutputName,
                ["written"] = Number(output.Written),
                ["failed"] = Number(output.Failed),
                ["abandoned"] = Number(output.Abandoned),
                ["pending"] = Number(output.Pending),
                ["invalid"] = Number(runtime.GetDiagnostics().InvalidEventCount),
                ["filtered"] = Number(output.Filtered)
            }).ToArray());
            return report;
        }
        finally
        {
            console.ReleaseWrite.TrySetResult(); console.ReleaseFlush.TrySetResult(); file.ReleaseWrite.TrySetResult(); file.ReleaseFlush.TrySetResult(); runtime.CloseAdmission();
            try { await runtime.QueuedOutputs!.Completion.WaitAsync(Guard); } catch (IOException) { }
        }
    }
    private static async Task<JsonObject> PhysicalAsync(string mode, string root)
    {
        string path = Path.Combine(root, "flush-results.jsonl"); using var console = new MemoryStream();
        var options = new LoggingOptions { FilePath = path, QueueCapacity = 4, FileMaxBytes = 65536, ConsoleFormat = LogOutputFormat.JsonLines };
        options.Prepare(root);
        CoreLogRuntime runtime = CoreLogRuntime.CreateConfiguredQueued(ConsoleCompositionFixture.Metadata(), options, acquireConsole: _ => console);
        try
        {
            CoreLogger logger = runtime.CreateLogger("FlushResult");
            Accepted(logger.Write(CoreLogLevel.Information, Data("first"))); Accepted(logger.Write(CoreLogLevel.Information, Data("second")));
            LogFlushResult result = await runtime.FlushAsync(TimeSpan.FromSeconds(5), CancellationToken.None).WaitAsync(Guard);
            byte[] raw; using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            { using var bytes = new MemoryStream(); await input.CopyToAsync(bytes); raw = bytes.ToArray(); }
            var report = Common(mode); report["result"] = Serialize(result); report["bytes"] = Convert.ToBase64String(console.ToArray()); report["fileBytes"] = Convert.ToBase64String(raw);
            string frozen = Serialize(result).ToJsonString(); runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(Guard);
            report["immutableAfterIo"] = frozen == Serialize(result).ToJsonString(); report["cancelled"] = false;
            using var lease = new FileStream(path + ".cis.lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None); report["leaseReleased"] = true;
            return report;
        }
        finally { runtime.CloseAdmission(); await runtime.QueuedOutputs!.Completion.WaitAsync(Guard); }
    }
    private static void Accepted(LogWriteResult result) => Require(result.Outcome == LogWriteOutcome.Accepted);
    internal static void Require(bool condition) { if (!condition) { throw new InvalidOperationException("Flush-result fixture invariant failed."); } }
    internal static LogEventData Data(string name, bool large = false) => new()
    { EventName = name, Message = large ? new string('x', 20000) : "Ω 🙂", Attributes = new Dictionary<string, LogValue> { ["producer_pid"] = LogValue.Integer(Environment.ProcessId) } };
    internal static JsonObject Serialize(LogFlushResult result) => new()
    {
        ["completion"] = result.Completion.ToString(),
        ["succeeded"] = result.Succeeded,
        ["loss"] = result.LossObserved,
        ["captured"] = result.BoundaryCapturedAtUtc.ToString("O", CultureInfo.InvariantCulture),
        ["observed"] = result.ObservedAtUtc.ToString("O", CultureInfo.InvariantCulture),
        ["invalid"] = Number(result.InvalidEventCount),
        ["truncated"] = Number(result.TruncatedEventCount),
        ["outputs"] = new JsonArray(result.Outputs.Select(output => (JsonNode)new JsonObject
        {
            ["name"] = output.OutputName,
            ["boundary"] = Number(output.AcceptedSequenceBoundary),
            ["written"] = Number(output.Written),
            ["failed"] = Number(output.Failed),
            ["abandoned"] = Number(output.Abandoned),
            ["pending"] = Number(output.Pending),
            ["flush"] = output.WriterFlushStatus.ToString(),
            ["flushSucceeded"] = output.WriterFlushSucceeded,
            ["filtered"] = Number(output.Rejections.Filtered),
            ["full"] = Number(output.Rejections.QueueFull),
            ["unavailable"] = Number(output.Rejections.Unavailable)
        }).ToArray())
    };
    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static JsonObject Common(string mode) => new()
    {
        ["mode"] = mode,
        ["pid"] = Environment.ProcessId,
        ["runtime"] = RuntimeInformation.FrameworkDescription,
        ["os"] = OperatingSystem.IsWindows() ? "windows" : "linux",
        ["coreSha256"] = Hash(typeof(CoreLogRuntime).Assembly.Location),
        ["fixtureSha256"] = Hash(typeof(FlushResultFixture).Assembly.Location)
    };
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
