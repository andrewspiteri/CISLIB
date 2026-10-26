using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Healthcheck.Reporting;

public sealed partial class HealthcheckPipeTransportTests
{
    private static readonly TimeSpan WireRecoveryDeadline = TransportTestSupport.Limit;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WireRecoveryObservation RunInvalidFrameScenario(string mode)
    {
        var (service, monitor) = Create();
        string pipe = TransportTestSupport.NewPipeName();
        var facts = new WireRecoveryFacts();
        var source = new WireRecoverySource(service, facts);
        var clock = new WireRecoveryClock(facts);
        var callers = new WireRecoveryCallers();
        HealthPipeServer? server = null;
        Task? body = null;
        Exception? primary = null;
        try
        {
            server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe, PipeRequestTimeoutMs = 100 }, clock);
            body = RunInvalidFrameBodyAsync(server, pipe, source, monitor, mode, callers);
            JoinWireRecovery(body);
        }
        catch (Exception error) { primary = error; }
        return ReleaseWireRecoveryOwners(server, service, monitor, source, clock, facts, body, callers, primary, 2, 2);
    }

    private static async Task RunInvalidFrameBodyAsync(HealthPipeServer server, string pipe, WireRecoverySource source,
        IConditionMonitor monitor, string mode, WireRecoveryCallers callers)
    {
        await TransportTestSupport.StartServerAsync(server, pipe);
        using var timeout = new CancellationTokenSource(TransportTestSupport.Limit);
        await using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(timeout.Token);
        byte[] payload = mode == "utf8" ? [0xff, 0xfe] : [];
        uint? announced = mode switch { "zero" => 0, "oversize" => 513, "uint-max" => uint.MaxValue, _ => null };
        await TransportTestSupport.WriteFrameAsync(client, payload, announcedLength: announced, cancellation: timeout.Token);
        try
        {
            JsonObject error = JsonNode.Parse(await TransportTestSupport.ReadFrameAsync(client, timeout.Token))!.AsObject();
            TransportTestSupport.ValidateError(error, "InvalidFrame");
        }
        catch (EndOfStreamException) { /* Damaged framing may terminate the local connection. */ }
        catch (IOException) { /* A closed pipe still fails the invalid exchange. */ }
        Assert.AreEqual(0, source.Captures);
        callers.Recovery = TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}");
        TransportTestSupport.ValidateSnapshotEnvelope(await callers.Recovery, "health", true, true);
        GC.KeepAlive(monitor);
        // Preserve the original lifetime: this raw client is disposed only
        // after recovery, by this actual async body's await-using scope.
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WireRecoveryObservation RunTruncatedFrameScenario(int headerBytes, int bodyBytes)
    {
        var (service, monitor) = Create();
        string pipe = TransportTestSupport.NewPipeName();
        var facts = new WireRecoveryFacts();
        var source = new WireRecoverySource(service, facts);
        var clock = new WireRecoveryClock(facts);
        var callers = new WireRecoveryCallers();
        HealthPipeServer? server = null;
        Task? body = null;
        Exception? primary = null;
        try
        {
            server = new HealthPipeServer(source, new HealthcheckOptions { PipeEnabled = true, PipeName = pipe, PipeMaxClients = 1 }, clock);
            body = RunTruncatedFrameBodyAsync(server, pipe, source, monitor, headerBytes, bodyBytes, callers);
            JoinWireRecovery(body);
        }
        catch (Exception error) { primary = error; }
        return ReleaseWireRecoveryOwners(server, service, monitor, source, clock, facts, body, callers, primary, 1, 2);
    }

    private static async Task RunTruncatedFrameBodyAsync(HealthPipeServer server, string pipe, WireRecoverySource source,
        IConditionMonitor monitor, int headerBytes, int bodyBytes, WireRecoveryCallers callers)
    {
        await TransportTestSupport.StartServerAsync(server, pipe);
        using var timeout = new CancellationTokenSource(TransportTestSupport.Limit);
        await using (var abandoned = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            await abandoned.ConnectAsync(timeout.Token);
            byte[] header = new byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(header, 40);
            await abandoned.WriteAsync(header.AsMemory(0, headerBytes), timeout.Token);
            if (bodyBytes > 0) { await abandoned.WriteAsync(Encoding.UTF8.GetBytes("{\"proto").AsMemory(0, bodyBytes), timeout.Token); }
            await abandoned.FlushAsync(timeout.Token);
        }
        callers.Recovery = TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}");
        TransportTestSupport.ValidateSnapshotEnvelope(await callers.Recovery, "health", true, true);
        Assert.AreEqual(1, source.Captures, "Only the later complete valid request may read the source.");
        GC.KeepAlive(monitor);
    }

    private sealed class WireRecoveryCallers
    {
        internal Task<JsonObject>? Recovery { get; set; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WireRecoveryObservation ReleaseWireRecoveryOwners(HealthPipeServer? server,
        HealthcheckService service, IConditionMonitor monitor, WireRecoverySource source, WireRecoveryClock clock,
        WireRecoveryFacts facts, Task? body, WireRecoveryCallers callers, Exception? primary, int minimumTimers, int maximumTimers)
    {
        Task? stopping = server is null ? null : BeginWireRecoveryStop(server);
        Task? disposing = server is null ? null : BeginWireRecoveryDispose(server);
        var status = new WireRecoveryStatus();
        var observers = new List<WeakReference<Task>>();
        if (stopping is not null) { observers.Add(ObserveWireRecoveryTerminal(stopping, status, false)); }
        if (disposing is not null) { observers.Add(ObserveWireRecoveryTerminal(disposing, status, true)); }
        var holds = new List<Exception>();
        try
        {
            // These are the original real helper tasks, including their frame IO,
            // await-using client disposal and caller CTS disposal. No IO catch is added.
            foreach (Task? actual in new Task?[] { body, callers.Recovery })
            {
                if (actual is not null)
                {
                    try { JoinWireRecovery(actual); Interlocked.Increment(ref facts.clientTasksJoined); }
                    catch (Exception error) when (ReferenceEquals(actual, body) && ReferenceEquals(error, primary))
                    {
                        // The same actual body task already completed, including its
                        // original await-using disposals, with this exact saved error.
                        // Preserve it as the body failure, not a second cleanup fault.
                        Interlocked.Increment(ref facts.clientTasksJoined);
                    }
                    catch (Exception error) { holds.Add(error); }
                }
            }
            try { clock.JoinActualDisposals(); }
            catch (Exception error) { holds.Add(error); }
        }
        catch (Exception error) { holds.Add(error); }
        finally
        {
            try { JoinWireRecovery(monitor.DisposeAsync().AsTask()); Volatile.Write(ref facts.monitorJoined, 1); }
            catch (Exception error) { holds.Add(error); }
            try { JoinWireRecovery(service.DisposeAsync().AsTask()); Volatile.Write(ref facts.serviceJoined, 1); }
            catch (Exception error) { holds.Add(error); }
            GC.KeepAlive(monitor);
        }
        // Keep actual swallowed source/callback causes visible even when the
        // original response assertion also failed. They remain resource holds.
        holds.AddRange(facts.Errors());
        bool eligible = primary is null && holds.Count == 0 && server is not null && body is not null && callers.Recovery is not null
            && !stopping!.IsFaulted && !stopping.IsCanceled && !disposing!.IsFaulted && !disposing.IsCanceled;
        if (eligible)
        {
            try { RequireWireRecoveryPhase(facts, minimumTimers, maximumTimers); }
            catch (Exception error) { holds.Add(error); eligible = false; }
        }
        if (eligible)
        {
            // Exact remaining-decrement leaves only: the real BeginStop still
            // cancels admission and disposes every published stream. Every actual
            // accepting/handling RunSlot still owns Server until IO and its finally
            // return. The original eight-slot or one-slot worker population
            // belongs to this whole weak graph; neither setting is changed.
            WeakReference[] graph = [new(server!), new(service), new(monitor), new(source), new(clock), new(callers), .. clock.WeakTimerOwners()];
            return new(null, graph, facts, status, [.. observers], minimumTimers, maximumTimers);
        }
        if (server is not null)
        {
            Task all = Task.WhenAll(stopping!, disposing!);
            try { JoinWireRecovery(all); }
            catch (Exception error) { holds.Add(all.Exception ?? error); }
            foreach (WeakReference<Task> observer in observers)
            {
                try { if (observer.TryGetTarget(out Task? actual)) { JoinWireRecovery(actual); } }
                catch (Exception error) { holds.Add(error); }
            }
            holds.AddRange(status.Errors());
        }
        if (holds.Count > 0)
        {
            if (primary is not null) { holds.Insert(0, primary); }
            primary = new AggregateException("The original wire-recovery body or its actual resource cleanup did not settle.", holds);
        }
        return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), [], facts, null, [], minimumTimers, maximumTimers);
    }

    private static void ReviewWireRecoveryScenario(WireRecoveryObservation observed)
    {
        if (observed.Graph.Length > 0)
        {
            var elapsed = Stopwatch.StartNew();
            while (observed.Graph.Any(reference => reference.IsAlive) && elapsed.Elapsed < WireRecoveryDeadline)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                if (observed.Graph.Any(reference => reference.IsAlive) && elapsed.Elapsed < WireRecoveryDeadline) { Thread.Sleep(1); }
            }
            if (observed.Graph.Any(reference => reference.IsAlive))
            {
                throw new InvalidOperationException("Actual accepting/request/source/clock/timer ownership remains live; elapsed time cannot certify settlement.");
            }
            RequireWireRecoveryPhase(observed.Facts, observed.MinimumTimers, observed.MaximumTimers);
            foreach (WeakReference<Task> reference in observed.Observers)
            {
                if (reference.TryGetTarget(out Task? actual)) { JoinWireRecovery(actual); }
            }
            Exception[] errors = observed.Status!.Errors();
            if (errors.Length > 0) { throw new AggregateException("An actual uncanceled terminal operation failed.", errors); }
        }
        observed.Primary?.Throw();
        if (observed.Graph.Length > 0)
        {
            Assert.IsTrue(observed.Status!.StopCompleted, "Actual terminal Stop must publish success after the real partial-frame exchange, recovery, timers and all queued workers settle.");
            Assert.IsTrue(observed.Status.DisposeCompleted, "Actual terminal Dispose must publish success after the real partial-frame exchange, recovery, timers and all queued workers settle.");
        }
    }

    private static void RequireWireRecoveryPhase(WireRecoveryFacts facts, int minimumTimers, int maximumTimers)
    {
        Exception[] errors = facts.Errors();
        if (errors.Length > 0) { throw new AggregateException("An actual source, clock or timer callback failure remains a resource hold.", errors); }
        if (maximumTimers != 2 || minimumTimers is not (1 or 2))
        {
            throw new InvalidOperationException("Only the two original method-specific timer inventories are supported.");
        }
        if (Volatile.Read(ref facts.captures) != 1 || Volatile.Read(ref facts.returns) != 1
            || Volatile.Read(ref facts.createdTimers) < minimumTimers || Volatile.Read(ref facts.createdTimers) > maximumTimers
            || Volatile.Read(ref facts.actualDrainsJoined) != Volatile.Read(ref facts.createdTimers)
            || Volatile.Read(ref facts.callbacksEntered) != Volatile.Read(ref facts.callbacksReturned)
            || Volatile.Read(ref facts.clientTasksJoined) != 2 || Volatile.Read(ref facts.monitorJoined) != 1 || Volatile.Read(ref facts.serviceJoined) != 1)
        {
            throw new InvalidOperationException("The actual raw-body/recovery tasks and every source-proven allocated System timer drain, one returned source capture, and real monitor/service joins are required; unknown phases remain holds.");
        }
    }
    private static void JoinWireRecovery(Task actual) => actual.WaitAsync(WireRecoveryDeadline).GetAwaiter().GetResult();
    private static T JoinWireRecovery<T>(Task<T> actual) => actual.WaitAsync(WireRecoveryDeadline).GetAwaiter().GetResult();
    private static Task BeginWireRecoveryStop(HealthPipeServer server)
    {
        try { return server.StopAsync(); }
        catch (Exception error) { return Task.FromException(error); }
    }
    private static Task BeginWireRecoveryDispose(HealthPipeServer server)
    {
        try { return server.DisposeAsync().AsTask(); }
        catch (Exception error) { return Task.FromException(error); }
    }
    private static WeakReference<Task> ObserveWireRecoveryTerminal(Task actual, WireRecoveryStatus status, bool disposal)
    {
        Task observer = actual.ContinueWith(static (completed, state) =>
        {
            var observed = ((WireRecoveryStatus Status, bool Disposal))state!;
            if (completed.Exception is { } error) { observed.Status.RecordError(error); }
            else if (completed.IsCanceled) { observed.Status.RecordError(new TaskCanceledException("Actual uncanceled terminal operation canceled.")); }
            else { observed.Status.RecordSuccess(observed.Disposal); }
        }, (status, disposal), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return new(observer);
    }
    private sealed record WireRecoveryObservation(ExceptionDispatchInfo? Primary, WeakReference[] Graph,
        WireRecoveryFacts Facts, WireRecoveryStatus? Status, WeakReference<Task>[] Observers, int MinimumTimers, int MaximumTimers);
    private sealed class WireRecoveryStatus
    {
        private readonly object gate = new();
        private readonly List<Exception> errors = [];
        private bool stop, dispose;
        internal bool StopCompleted { get { lock (gate) { return stop; } } }
        internal bool DisposeCompleted { get { lock (gate) { return dispose; } } }
        internal void RecordSuccess(bool disposal) { lock (gate) { if (disposal) { dispose = true; } else { stop = true; } } }
        internal void RecordError(Exception error) { lock (gate) { errors.Add(error); } }
        internal Exception[] Errors() { lock (gate) { return [.. errors]; } }
    }
    private sealed class WireRecoveryFacts
    {
        private readonly object gate = new();
        private readonly List<Exception> errors = [];
        internal int captures, returns, createdTimers, actualDrainsJoined, callbacksEntered, callbacksReturned;
        internal int clientTasksJoined, monitorJoined, serviceJoined;
        internal void RecordError(Exception error) { lock (gate) { errors.Add(error); } }
        internal Exception[] Errors() { lock (gate) { return [.. errors]; } }
    }
    private sealed class WireRecoverySource(HealthcheckService service, WireRecoveryFacts facts) : IHealthSnapshotSource
    {
        internal int Captures => Volatile.Read(ref facts.captures);
        public HealthSnapshot CaptureSnapshot()
        {
            Interlocked.Increment(ref facts.captures);
            try { return service.CaptureSnapshot(); }
            catch (Exception error) { facts.RecordError(error); throw; }
            finally { Interlocked.Increment(ref facts.returns); }
        }
    }
    private sealed class WireRecoveryClock(WireRecoveryFacts facts) : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<WireRecoveryTimer> timers = [];
        public override long TimestampFrequency => TimeProvider.System.TimestampFrequency;
        public override long GetTimestamp() => TimeProvider.System.GetTimestamp();
        public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            try
            {
                var observed = new WireRecoveryTimer(facts, callback, state);
                observed.Publish(TimeProvider.System.CreateTimer(observed.Invoke, null, dueTime, period));
                lock (gate) { timers.Add(observed); }
                Interlocked.Increment(ref facts.createdTimers);
                return observed;
            }
            catch (Exception error) { facts.RecordError(error); throw; }
        }
        internal void JoinActualDisposals()
        {
            WireRecoveryTimer[] actual; lock (gate) { actual = [.. timers]; }
            var errors = new List<Exception>();
            foreach (WireRecoveryTimer timer in actual)
            {
                try { timer.JoinActualAndReleaseOnlyObserverReferences(); Interlocked.Increment(ref facts.actualDrainsJoined); }
                catch (Exception error) { errors.Add(error); }
            }
            lock (gate)
            {
                if (timers.Count != actual.Length) { throw new InvalidOperationException("Actual request timer publication raced its cleanup inventory."); }
            }
            if (errors.Count > 0) { throw new AggregateException("Actual System request timers did not all drain.", errors); }
        }
        internal WeakReference[] WeakTimerOwners()
        {
            lock (gate) { return timers.SelectMany(timer => timer.WeakOwners()).ToArray(); }
        }
        private sealed class WireRecoveryTimer(WireRecoveryFacts facts, TimerCallback callback, object? state) : ITimer
        {
            private readonly object gate = new();
            private readonly TaskCompletionSource disposalRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private TimerCallback? observedCallback = callback;
            private object? observedState = state;
            private ITimer? actual;
            private WeakReference? originalActual;
            private Task? actualDisposal;
            internal void Publish(ITimer timer) { lock (gate) { actual = timer; originalActual = new(timer); } }
            internal WeakReference[] WeakOwners() { lock (gate) { return [new(this), originalActual ?? throw new InvalidOperationException("Actual timer owner was never published.")]; } }
            internal void Invoke(object? unused)
            {
                Interlocked.Increment(ref facts.callbacksEntered);
                try
                {
                    TimerCallback current; object? currentState;
                    lock (gate) { current = observedCallback ?? throw new InvalidOperationException("Callback entered after its actual timer drain."); currentState = observedState; }
                    current(currentState);
                }
                catch (Exception error) { facts.RecordError(error); throw; }
                finally { Interlocked.Increment(ref facts.callbacksReturned); }
            }
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                ITimer timer; lock (gate) { timer = actual ?? throw new ObjectDisposedException(nameof(WireRecoveryTimer)); }
                try { return timer.Change(dueTime, period); }
                catch (Exception error) { facts.RecordError(error); throw; }
            }
            public void Dispose()
            {
                lock (gate)
                {
                    if (actualDisposal is not null) { return; }
                    try { actualDisposal = (actual ?? throw new InvalidOperationException("Actual System timer not published.")).DisposeAsync().AsTask(); }
                    catch (Exception error) { facts.RecordError(error); actualDisposal = Task.FromException(error); }
                }
                // The real disposal task can await this callback. Never self-join here.
                disposalRequested.TrySetResult();
            }
            public ValueTask DisposeAsync() { Dispose(); lock (gate) { return new(actualDisposal!); } }
            internal void JoinActualAndReleaseOnlyObserverReferences()
            {
                JoinWireRecovery(disposalRequested.Task);
                Task drain; lock (gate) { drain = actualDisposal ?? throw new InvalidOperationException("Actual System disposal task missing."); }
                JoinWireRecovery(drain);
                lock (gate) { observedCallback = null; observedState = null; actual = null; }
            }
        }
    }
}
