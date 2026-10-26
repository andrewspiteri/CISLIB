using System.Diagnostics;
using System.Runtime.ExceptionServices;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

public sealed partial class HealthPipePostCaptureClockFailureTests
{
    private static PostCaptureObservation ReleasePostCaptureOwners(HealthPipeServer? server, HealthcheckService service,
        CapturingSource source, OneShotFaultClock clock, HealthcheckOptions options, CancellationTokenSource cancellation,
        List<Task<byte[]>> requests, Exception? primary)
    {
        Task? stopping = server is null ? null : BeginStop(server);
        Task? disposing = server is null ? null : BeginDispose(server);
        var terminal = new PostCaptureTerminal();
        WeakReference<Task>[] observers = server is null ? [] : [ObservePostCaptureTerminal(stopping!, terminal, false), ObservePostCaptureTerminal(disposing!, terminal, true)];
        var holds = new List<Exception>();
        bool phaseJoined = false;
        try
        {
            // Each original RequestAsync owns its real client until complete
            // frame IO and using Dispose. Retain/rejoin even a failed wait.
            foreach (Task<byte[]> actual in requests) { _ = JoinPostCapture(actual); }
            if (server is not null && primary is null && requests.Count == 3 && requests.All(actual => actual.IsCompletedSuccessfully))
            {
                clock.JoinActualRequestDisposals();
                RequirePostCapturePhase(source.Phase, clock.Phase);
                phaseJoined = true;
            }
        }
        catch (Exception error) { holds.Add(error); }
        finally
        {
            try { JoinPostCapture(service.DisposeAsync().AsTask()); }
            catch (Exception error) { holds.Add(error); }
            finally { cancellation.Dispose(); }
        }
        bool candidate = server is not null && primary is null && phaseJoined && holds.Count == 0
            && options.PipeEnabled && options.PipeMaxClients == 1 && options.PipeRequestTimeoutMs == 5000
            && !stopping!.IsFaulted && !stopping.IsCanceled && !disposing!.IsFaulted && !disposing.IsCanceled;
        if (candidate)
        {
            // Only after the actual three clients, source, server System
            // timers and service join can the real slot owner be released.
            // The original pending-stop milestone is not changed or invented.
            WeakReference[] graph = [new(server!), new(service), new(source), new(cancellation), .. clock.WeakGraph()];
            return new(null, graph, source.Phase, clock.Phase, terminal, observers);
        }
        if (server is not null)
        {
            Task all = Task.WhenAll(stopping!, disposing!);
            try { JoinPostCapture(all); }
            catch (Exception error) { holds.Add(all.Exception ?? error); }
            foreach (WeakReference<Task> reference in observers)
            {
                try { if (reference.TryGetTarget(out Task? actualObserver)) { JoinPostCapture(actualObserver); } }
                catch (Exception error) { holds.Add(error); }
            }
            holds.AddRange(terminal.Failures());
        }
        holds.AddRange(source.Phase.Failures());
        holds.AddRange(clock.Phase.UnexpectedFailures());
        if (holds.Count > 0)
        {
            if (primary is not null) { holds.Insert(0, primary); }
            primary = new AggregateException("The original post-capture assertions or actual client/source/timer cleanup did not settle.", holds);
        }
        return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), [], source.Phase, clock.Phase, terminal, []);
    }

    private static void ReviewPostCaptureOwners(PostCaptureObservation observed)
    {
        if (observed.Graph.Length != 0)
        {
            var elapsed = Stopwatch.StartNew();
            while (observed.Graph.Any(reference => reference.IsAlive) && elapsed.Elapsed < Deadline)
            {
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                if (observed.Graph.Any(reference => reference.IsAlive) && elapsed.Elapsed < Deadline) { Thread.Sleep(1); }
            }
            if (observed.Graph.Any(reference => reference.IsAlive))
            { throw new InvalidOperationException("Actual post-capture accepting/source/timer owner remains live; elapsed time never certifies settlement."); }
            RequirePostCapturePhase(observed.Source, observed.Clock);
            foreach (WeakReference<Task> reference in observed.Observers)
            {
                if (reference.TryGetTarget(out Task? actualObserver)) { JoinPostCapture(actualObserver); }
            }
            Exception[] failures = observed.Terminal.Failures();
            if (failures.Length > 0) { throw new AggregateException("Actual post-capture terminal operation failed.", failures); }
            Assert.IsTrue(observed.Terminal.StopCompleted, "Actual Stop must publish success after all three real requests and the actual slot owner settle.");
            Assert.IsTrue(observed.Terminal.DisposeCompleted, "Actual Dispose must publish success after all original post-capture work settles.");
        }
        observed.Primary?.Throw();
    }

    private static void RequirePostCapturePhase(PostCaptureSourcePhase source, PostCaptureClockPhase clock)
    {
        Exception[] failures = [.. source.Failures(), .. clock.UnexpectedFailures()];
        if (failures.Length > 0) { throw new AggregateException("An unknown actual source/clock/callback failure remains a cleanup hold.", failures); }
        if (Volatile.Read(ref source.Captures) != 3 || Volatile.Read(ref source.Exits) != 3
            || Volatile.Read(ref clock.Created) != 3 || Volatile.Read(ref clock.JoinedDisposals) != 3
            || Volatile.Read(ref clock.Arms) != 1 || Volatile.Read(ref clock.ArmedCapture) != 2
            || Volatile.Read(ref clock.Faults) != 1 || !clock.ExactOriginalFaultProduced()
            || Volatile.Read(ref clock.CallbackEntries) != Volatile.Read(ref clock.CallbackReturns))
        { throw new InvalidOperationException("Three actual captures/timer drains and only the original second-capture one-shot fault are required; raced or unknown phases remain holds."); }
    }
    private static void JoinPostCapture(Task actual) => actual.WaitAsync(Deadline).GetAwaiter().GetResult();
    private static T JoinPostCapture<T>(Task<T> actual) => actual.WaitAsync(Deadline).GetAwaiter().GetResult();
    private static WeakReference<Task> ObservePostCaptureTerminal(Task actual, PostCaptureTerminal terminal, bool disposing)
    {
        Task observer = actual.ContinueWith(static (completed, state) =>
        {
            var observed = ((PostCaptureTerminal Terminal, bool Disposing))state!;
            if (completed.Exception is { } failure) { observed.Terminal.RecordFailure(failure); }
            else if (completed.IsCanceled) { observed.Terminal.RecordFailure(new TaskCanceledException("Actual post-capture terminal wait canceled.")); }
            else { observed.Terminal.RecordSuccess(observed.Disposing); }
        }, (terminal, disposing), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return new(observer);
    }
    private sealed record PostCaptureObservation(ExceptionDispatchInfo? Primary, WeakReference[] Graph,
        PostCaptureSourcePhase Source, PostCaptureClockPhase Clock, PostCaptureTerminal Terminal, WeakReference<Task>[] Observers);
    private sealed class PostCaptureSourcePhase
    {
        internal int Captures, Exits;
        private readonly object gate = new();
        private readonly List<Exception> faults = [];
        internal void RecordFailure(Exception error) { lock (gate) { faults.Add(error); } }
        internal Exception[] Failures() { lock (gate) { return [.. faults]; } }
    }
    private sealed class PostCaptureClockPhase
    {
        internal int Created, JoinedDisposals, Arms, ArmedCapture, Faults, CallbackEntries, CallbackReturns;
        private readonly object gate = new();
        private readonly List<Exception> clockFaults = [], callbackFaults = [];
        private ClockSampleUnavailableException? produced;
        private int producedAtCapture, producedCount;
        internal void RecordProduced(ClockSampleUnavailableException actual, int capture)
        { lock (gate) { produced = actual; producedAtCapture = capture; producedCount++; } }
        internal void RecordClockFailure(Exception actual) { lock (gate) { clockFaults.Add(actual); } }
        internal void RecordCallbackFailure(Exception actual) { lock (gate) { callbackFaults.Add(actual); } }
        internal bool ExactOriginalFaultProduced()
        {
            lock (gate)
            {
                return producedCount == 1 && producedAtCapture == 2 && produced?.GetType() == typeof(ClockSampleUnavailableException)
                    && clockFaults.Count == 1 && ReferenceEquals(clockFaults[0], produced);
            }
        }
        internal Exception[] UnexpectedFailures()
        {
            lock (gate)
            {
                // Sole producer identity, original capture/arm and exact type
                // matter; another exception with the same shape is unknown.
                bool original = producedCount == 1 && producedAtCapture == 2 && produced?.GetType() == typeof(ClockSampleUnavailableException);
                return [.. clockFaults.Where(actual => !original || !ReferenceEquals(actual, produced)), .. callbackFaults];
            }
        }
    }
    private sealed class PostCaptureTerminal
    {
        private readonly object gate = new();
        private readonly List<Exception> faults = [];
        private bool stop, dispose;
        internal bool StopCompleted { get { lock (gate) { return stop; } } }
        internal bool DisposeCompleted { get { lock (gate) { return dispose; } } }
        internal void RecordFailure(Exception error) { lock (gate) { faults.Add(error); } }
        internal void RecordSuccess(bool disposing) { lock (gate) { if (disposing) { dispose = true; } else { stop = true; } } }
        internal Exception[] Failures() { lock (gate) { return [.. faults]; } }
    }

    private sealed class ObservedPostCaptureTimer(OneShotFaultClock clock, TimerCallback callback, object? state) : ITimer
    {
        private readonly object gate = new();
        private readonly TaskCompletionSource disposalRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TimerCallback? observedCallback = callback;
        private object? observedState = state;
        private ITimer? actual;
        private Task? actualDisposal;
        internal void Publish(ITimer timer) { lock (gate) { actual = timer; } }
        internal void Invoke(object? unused)
        {
            Interlocked.Increment(ref clock.Phase.CallbackEntries);
            try
            {
                TimerCallback current; object? currentState;
                lock (gate) { current = observedCallback ?? throw new InvalidOperationException("Callback entered after actual System disposal drain."); currentState = observedState; }
                current(currentState);
            }
            catch (Exception error) { clock.Phase.RecordCallbackFailure(error); throw; }
            finally { Interlocked.Increment(ref clock.Phase.CallbackReturns); }
        }
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            ITimer timer; lock (gate) { timer = actual ?? throw new ObjectDisposedException(nameof(ObservedPostCaptureTimer)); }
            return timer.Change(dueTime, period);
        }
        public void Dispose()
        {
            lock (gate)
            {
                if (actualDisposal is not null) { return; }
                try { actualDisposal = (actual ?? throw new InvalidOperationException("Actual System timer not published.")).DisposeAsync().AsTask(); }
                catch (Exception error) { actualDisposal = Task.FromException(error); }
            }
            // The actual callback may Dispose; never self-wait inside it.
            disposalRequested.TrySetResult();
        }
        public ValueTask DisposeAsync() { Dispose(); lock (gate) { return new(actualDisposal!); } }
        internal void JoinActualDisposal()
        {
            JoinPostCapture(disposalRequested.Task);
            Task drain; lock (gate) { drain = actualDisposal ?? throw new InvalidOperationException("No actual System disposal task was returned."); }
            JoinPostCapture(drain);
            lock (gate) { observedCallback = null; observedState = null; actual = null; }
        }
    }
}
