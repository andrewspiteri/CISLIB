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
    private static readonly TimeSpan ExactBoundaryDeadline = TransportTestSupport.Limit;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ExactBoundaryObservation RunExactBoundaryScenario(int elapsedMilliseconds, bool replyAllowed,
        HealthcheckService? observedService = null, IConditionMonitor? observedMonitor = null)
    {
        if (!(elapsedMilliseconds == 999 && replyAllowed || elapsedMilliseconds is 1000 or 1001 && !replyAllowed)) { throw new ArgumentException("Only the three original exact-boundary row pairs are selected."); }
        if ((observedService is null) != (observedMonitor is null)) { throw new ArgumentException("Both actual control owners must be supplied together."); }
        var (service, monitor) = observedService is null ? Create() : (observedService, observedMonitor!);
        string pipe = TransportTestSupport.NewPipeName();
        var facts = new ExactBoundaryFacts(elapsedMilliseconds, replyAllowed);
        var clock = new ExactBoundaryClock(facts);
        var source = new ExactBoundarySource(service, clock, elapsedMilliseconds, facts);
        var callers = new ExactBoundaryCallers();
        HealthPipeServer? server = null;
        Task? body = null;
        Exception? primary = null;
        try
        {
            server = new HealthPipeServer(source, new HealthcheckOptions
            { PipeEnabled = true, PipeName = pipe, PipeMaxClients = 1, PipeRequestTimeoutMs = 1000 }, clock);
            body = RunExactBoundaryBodyAsync(server, pipe, source, clock, monitor, callers, facts, replyAllowed);
            JoinExactBoundary(body);
        }
        catch (Exception error) { primary = error; }
        return ReleaseExactBoundaryOwners(server, service, monitor, source, clock, facts, body, callers, primary);
    }

    private static async Task RunExactBoundaryBodyAsync(HealthPipeServer server, string pipe, ExactBoundarySource source,
        ExactBoundaryClock clock, IConditionMonitor monitor, ExactBoundaryCallers callers, ExactBoundaryFacts facts, bool replyAllowed)
    {
        await TransportTestSupport.StartServerAsync(server, pipe);
        using var timeout = new CancellationTokenSource(TransportTestSupport.Limit);
        byte[]? payload = null;
        await using (var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous))
        {
            facts.RecordActualClient(client);
            await client.ConnectAsync(timeout.Token);
            await clock.TimerCreated.Task.WaitAsync(TransportTestSupport.Limit);
            await TransportTestSupport.WriteFrameAsync(client, Encoding.UTF8.GetBytes("{\"protocolVersion\":1,\"view\":\"health\"}"), cancellation: timeout.Token);
            try { payload = await TransportTestSupport.ReadFrameAsync(client, timeout.Token); }
            catch (IOException) { Interlocked.Increment(ref facts.firstReadIoExceptions); }
        }
        Volatile.Write(ref facts.rawScopeReturned, 1);
        Volatile.Write(ref facts.firstPayloadPresent, payload is null ? 0 : 1);
        if (replyAllowed)
        {
            Assert.IsNotNull(payload, "A complete reply is permitted strictly before the total deadline.");
            TransportTestSupport.ValidateSnapshotEnvelope(JsonNode.Parse(payload)!.AsObject(), "health", true, true);
        }
        else { Assert.IsNull(payload, "A delayed timer callback cannot authorize a reply at or beyond the elapsed-time deadline."); }
        Assert.AreEqual(1, source.Captures);
        Volatile.Write(ref facts.beforeRecoveryCaptures, source.Captures);
        Task<JsonObject> recovery = TransportTestSupport.RequestAsync(pipe, "{\"protocolVersion\":1,\"view\":\"health\"}");
        callers.Requests.Add(recovery);
        TransportTestSupport.ValidateSnapshotEnvelope(await recovery, "health", true, true);
        Assert.AreEqual(2, source.Captures);
        GC.KeepAlive(monitor);
    }

    private sealed class ExactBoundaryCallers
    {
        internal List<Task<JsonObject>> Requests { get; } = [];
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ExactBoundaryObservation ReleaseExactBoundaryOwners(HealthPipeServer? server,
        HealthcheckService service, IConditionMonitor monitor, ExactBoundarySource source, ExactBoundaryClock clock,
        ExactBoundaryFacts facts, Task? body, ExactBoundaryCallers callers, Exception? primary)
    {
        Task? stopping = server is null ? null : BeginExactBoundaryStop(server);
        Task? disposing = server is null ? null : BeginExactBoundaryDispose(server);
        var status = new ExactBoundaryStatus();
        var observers = new List<WeakReference<Task>>();
        if (stopping is not null) { observers.Add(ObserveExactBoundaryTerminal(stopping, status, false)); }
        if (disposing is not null) { observers.Add(ObserveExactBoundaryTerminal(disposing, status, true)); }
        var holds = new List<Exception>();
        try
        {
            // These are the original real helper tasks, including their frame IO,
            // await-using client disposal and caller CTS disposal. No IO catch is added.
            foreach (Task? actual in new Task?[] { body }.Concat(callers.Requests))
            {
                if (actual is not null)
                {
                    try
                    {
                        JoinExactBoundary(actual);
                        if (ReferenceEquals(actual, body)) { Volatile.Write(ref facts.bodyJoined, 1); Volatile.Write(ref facts.originalBodySucceeded, actual.IsCompletedSuccessfully ? 1 : 0); }
                        else { Interlocked.Increment(ref facts.clientTasksJoined); }
                    }
                    catch (Exception error) when (ReferenceEquals(actual, body) && ReferenceEquals(error, primary))
                    {
                        // The same actual body task already completed, including its
                        // original await-using disposals, with this exact saved error.
                        // Preserve it as the body failure, not a second cleanup fault.
                        Volatile.Write(ref facts.bodyJoined, 1);
                    }
                    catch (Exception error) { holds.Add(error); }
                }
            }
            try { clock.RequireActualStubDisposals(); }
            catch (Exception error) { holds.Add(error); }
        }
        catch (Exception error) { holds.Add(error); }
        finally
        {
            try { JoinExactBoundary(monitor.DisposeAsync().AsTask()); Volatile.Write(ref facts.monitorJoined, 1); }
            catch (Exception error) { holds.Add(error); }
            try { JoinExactBoundary(service.DisposeAsync().AsTask()); Volatile.Write(ref facts.serviceJoined, 1); }
            catch (Exception error) { holds.Add(error); }
            GC.KeepAlive(monitor);
        }
        // Keep actual swallowed source/callback causes visible even when the
        // original response assertion also failed. They remain resource holds.
        holds.AddRange(facts.Errors());
        bool eligible = primary is null && holds.Count == 0 && server is not null && body is not null && callers.Requests.Count == 1
            && !stopping!.IsFaulted && !stopping.IsCanceled && !disposing!.IsFaulted && !disposing.IsCanceled;
        if (eligible)
        {
            try { RequireExactBoundaryPhase(facts); }
            catch (Exception error) { holds.Add(error); eligible = false; }
        }
        if (eligible)
        {
            // Exact remaining-decrement leaves only: the real BeginStop still
            // cancels admission and disposes every published stream. Every actual
            // accepting/handling RunSlot still owns Server until IO and its finally
            // return. The one original worker belong to this whole weak
            // graph; the original one-slot population remains unchanged.
            WeakReference[] graph = [new(server!), new(service), new(monitor), new(source), new(clock), new(callers), .. facts.WeakClients(), .. clock.WeakTimerOwners()];
            return new(null, graph, facts, status, [.. observers]);
        }
        if (server is not null)
        {
            Task all = Task.WhenAll(stopping!, disposing!);
            try { JoinExactBoundary(all); }
            catch (Exception error) { holds.Add(all.Exception ?? error); }
            foreach (WeakReference<Task> observer in observers)
            {
                try { if (observer.TryGetTarget(out Task? actual)) { JoinExactBoundary(actual); } }
                catch (Exception error) { holds.Add(error); }
            }
            holds.AddRange(status.Errors());
        }
        if (holds.Count > 0)
        {
            if (primary is not null) { holds.Insert(0, primary); }
            primary = new AggregateException("The original exact-monotonic-boundary/recovery body or its actual resource cleanup did not settle.", holds);
        }
        return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), [], facts, null, []);
    }

    private static void ReviewExactBoundaryScenario(ExactBoundaryObservation observed)
    {
        if (observed.Graph.Length > 0)
        {
            var elapsed = Stopwatch.StartNew();
            while (observed.Graph.Any(reference => reference.IsAlive) && elapsed.Elapsed < ExactBoundaryDeadline)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                if (observed.Graph.Any(reference => reference.IsAlive) && elapsed.Elapsed < ExactBoundaryDeadline) { Thread.Sleep(1); }
            }
            if (observed.Graph.Any(reference => reference.IsAlive))
            {
                throw new InvalidOperationException("Actual accepting/request/source/manual-clock/stub-timer ownership remains live; elapsed time cannot certify settlement.");
            }
            RequireExactBoundaryPhase(observed.Facts);
            foreach (WeakReference<Task> reference in observed.Observers)
            {
                if (reference.TryGetTarget(out Task? actual)) { JoinExactBoundary(actual); }
            }
            Exception[] errors = observed.Status!.Errors();
            if (errors.Length > 0) { throw new AggregateException("An actual uncanceled terminal operation failed.", errors); }
        }
        observed.Primary?.Throw();
        if (observed.Graph.Length > 0)
        {
            Assert.IsTrue(observed.Status!.StopCompleted, "Actual terminal Stop must publish success after the real exact-monotonic-boundary/recovery exchanges, manual timers and all queued workers settle.");
            Assert.IsTrue(observed.Status.DisposeCompleted, "Actual terminal Dispose must publish success after the real exact-monotonic-boundary/recovery exchanges, manual timers and all queued workers settle.");
        }
    }

    private static void RequireExactBoundaryPhase(ExactBoundaryFacts facts)
    {
        Exception[] errors = facts.Errors();
        if (errors.Length > 0) { throw new AggregateException("An actual unknown producer or manual-clock/stub failure remains a resource hold, even when an expired-response body passed.", errors); }
        bool firstOutcome = facts.ReplyAllowed
            ? Volatile.Read(ref facts.firstPayloadPresent) == 1 && Volatile.Read(ref facts.firstReadIoExceptions) == 0
            : Volatile.Read(ref facts.firstPayloadPresent) == 0 && Volatile.Read(ref facts.firstReadIoExceptions) == 1;
        if (!firstOutcome || Volatile.Read(ref facts.captures) != 2 || Volatile.Read(ref facts.returns) != 2
            || Volatile.Read(ref facts.beforeRecoveryCaptures) != 1 || Volatile.Read(ref facts.rawScopeReturned) != 1
            || Volatile.Read(ref facts.createdTimers) != 2 || Volatile.Read(ref facts.actualStubDisposeReturns) != 2
            || Volatile.Read(ref facts.stubAsyncDisposeReturns) != 0 || Volatile.Read(ref facts.advanceCalls) != 1
            || Volatile.Read(ref facts.finalTimestamp) != facts.ElapsedMilliseconds || Volatile.Read(ref facts.clientTasksJoined) != 1
            || Volatile.Read(ref facts.bodyJoined) != 1 || Volatile.Read(ref facts.originalBodySucceeded) != 1
            || facts.WeakClients().Length != 1 || Volatile.Read(ref facts.monitorJoined) != 1 || Volatile.Read(ref facts.serviceJoined) != 1)
        {
            throw new InvalidOperationException("The original row-specific reply/no-reply assertions, raw-client and recovery scopes, two actual source returns/manual stub disposals, sole original advance and state disposals must all join; unknown phases remain holds.");
        }
    }
    private static void JoinExactBoundary(Task actual) => actual.WaitAsync(ExactBoundaryDeadline).GetAwaiter().GetResult();
    private static T JoinExactBoundary<T>(Task<T> actual) => actual.WaitAsync(ExactBoundaryDeadline).GetAwaiter().GetResult();
    private static Task BeginExactBoundaryStop(HealthPipeServer server)
    {
        try { return server.StopAsync(); }
        catch (Exception error) { return Task.FromException(error); }
    }
    private static Task BeginExactBoundaryDispose(HealthPipeServer server)
    {
        try { return server.DisposeAsync().AsTask(); }
        catch (Exception error) { return Task.FromException(error); }
    }
    private static WeakReference<Task> ObserveExactBoundaryTerminal(Task actual, ExactBoundaryStatus status, bool disposal)
    {
        Task observer = actual.ContinueWith(static (completed, state) =>
        {
            var observed = ((ExactBoundaryStatus Status, bool Disposal))state!;
            if (completed.Exception is { } error) { observed.Status.RecordError(error); }
            else if (completed.IsCanceled) { observed.Status.RecordError(new TaskCanceledException("Actual uncanceled terminal operation canceled.")); }
            else { observed.Status.RecordSuccess(observed.Disposal); }
        }, (status, disposal), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return new(observer);
    }
    private sealed record ExactBoundaryObservation(ExceptionDispatchInfo? Primary, WeakReference[] Graph,
        ExactBoundaryFacts Facts, ExactBoundaryStatus? Status, WeakReference<Task>[] Observers);
    private sealed class ExactBoundaryStatus
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
    private sealed class ExactBoundaryFacts(int elapsedMilliseconds, bool replyAllowed)
    {
        private readonly object gate = new();
        private readonly List<Exception> errors = [];
        private readonly List<WeakReference> clients = [];
        internal int ElapsedMilliseconds { get; } = elapsedMilliseconds;
        internal bool ReplyAllowed { get; } = replyAllowed;
        internal int captures, returns, createdTimers, actualStubDisposeReturns, stubAsyncDisposeReturns, advanceCalls;
        internal long finalTimestamp;
        internal int firstReadIoExceptions, firstPayloadPresent = -1, rawScopeReturned, beforeRecoveryCaptures = -1;
        internal int clientTasksJoined, bodyJoined, originalBodySucceeded, monitorJoined, serviceJoined;
        internal void RecordError(Exception error) { lock (gate) { errors.Add(error); } }
        internal Exception[] Errors() { lock (gate) { return [.. errors]; } }
        internal void RecordActualClient(NamedPipeClientStream actual) { lock (gate) { clients.Add(new(actual)); } }
        internal WeakReference[] WeakClients() { lock (gate) { return [.. clients]; } }
    }
    private sealed class ExactBoundarySource(HealthcheckService service, ExactBoundaryClock clock, long firstCaptureAdvance, ExactBoundaryFacts facts) : IHealthSnapshotSource
    {
        internal int Captures => Volatile.Read(ref facts.captures);
        public HealthSnapshot CaptureSnapshot()
        {
            int ordinal = Interlocked.Increment(ref facts.captures);
            try
            {
                if (ordinal == 1) { clock.Advance(firstCaptureAdvance); }
                return service.CaptureSnapshot();
            }
            catch (Exception error) { facts.RecordError(error); throw; }
            finally { Interlocked.Increment(ref facts.returns); }
        }
    }
    private sealed class ExactBoundaryClock(ExactBoundaryFacts facts) : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<ExactBoundaryTimer> timers = [];
        private long timestamp;
        internal TaskCompletionSource TimerCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Volatile.Read(ref timestamp);
        internal void Advance(long milliseconds)
        {
            long actual = Interlocked.Add(ref timestamp, milliseconds);
            Interlocked.Increment(ref facts.advanceCalls);
            Volatile.Write(ref facts.finalTimestamp, actual);
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            // Original DelayedCallbackClock deliberately ignores callback/state.
            // This is an inert manual stub, never a System timer or callback drain.
            var timer = new ExactBoundaryTimer(facts);
            lock (gate) { timers.Add(timer); }
            Interlocked.Increment(ref facts.createdTimers);
            TimerCreated.TrySetResult();
            return timer;
        }
        internal void RequireActualStubDisposals()
        {
            ExactBoundaryTimer[] actual; lock (gate) { actual = [.. timers]; }
            foreach (ExactBoundaryTimer timer in actual)
            {
                // Client disposal can complete before the server continuation
                // reaches budget.Dispose. Observe that actual manual API call;
                // this is not a System callback or resource drain.
                JoinExactBoundary(timer.ActualDisposeObserved.Task);
                if (Volatile.Read(ref timer.disposeCalls) != 1 || Volatile.Read(ref timer.disposeReturns) != 1
                    || Volatile.Read(ref timer.asyncDisposeCalls) != 0 || Volatile.Read(ref timer.asyncDisposeReturns) != 0)
                {
                    throw new InvalidOperationException("The actual manual stub's original synchronous Dispose API must have returned once; no System or asynchronous callback drain can be inferred.");
                }
            }
            lock (gate) { if (timers.Count != actual.Length) { throw new InvalidOperationException("Manual timer publication raced its actual disposal inventory."); } }
        }
        internal WeakReference[] WeakTimerOwners() { lock (gate) { return timers.Select(timer => new WeakReference(timer)).ToArray(); } }
        private sealed class ExactBoundaryTimer(ExactBoundaryFacts facts) : ITimer
        {
            internal TaskCompletionSource ActualDisposeObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal int disposeCalls, disposeReturns, asyncDisposeCalls, asyncDisposeReturns;
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose()
            {
                Interlocked.Increment(ref disposeCalls);
                // Original Dispose has no resource/callback action.
                Interlocked.Increment(ref facts.actualStubDisposeReturns);
                Interlocked.Increment(ref disposeReturns);
                ActualDisposeObserved.TrySetResult();
            }
            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref asyncDisposeCalls);
                // Preserve original completed no-op; do not call Dispose here.
                Interlocked.Increment(ref facts.stubAsyncDisposeReturns);
                Interlocked.Increment(ref asyncDisposeReturns);
                return ValueTask.CompletedTask;
            }
        }
    }
}
