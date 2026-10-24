using System.Diagnostics;
using System.Runtime.ExceptionServices;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

public sealed partial class HealthProbeCallerCompletionQualificationTests
{
    private static PendingProbeObservation ReleasePendingProbeOwners(HealthPipeServer? server, HealthcheckService service,
        ProbeStartupSource? source, ProbeStartupClock? clock, QueuedCallerContext context, Task<int>? running,
        bool endpointStarted, CancellationTokenSource cancellation, MemoryStream output, StringWriter error, Exception? primary)
    {
        var holds = new List<Exception>();
        // Preserve the original first real caller drain before terminal close.
        // The drain owns its original finite watchdog; observe its actual Task
        // directly rather than abandoning it behind another timeout wrapper.
        try
        {
            if (!endpointStarted) { cancellation.Cancel(); }
            if (running is not null) { DrainActualPendingCaller(context, running); }
        }
        catch (Exception failure) { holds.Add(failure); }
        try { cancellation.Cancel(); }
        catch (Exception failure) { holds.Add(failure); }
        Task? stopping = server is null ? null : BeginStop(server);
        Task? disposing = server is null ? null : BeginDispose(server);
        var terminal = new PendingProbeTerminal();
        WeakReference<Task>[] observers = server is null ? [] : [ObservePendingProbeTerminal(stopping!, terminal, false), ObservePendingProbeTerminal(disposing!, terminal, true)];
        bool phaseJoined = false;
        try
        {
            // The original second drain is still attempted after actual close
            // initiation, even if infrastructure interrupted the first drain.
            if (running is not null) { DrainActualPendingCaller(context, running); }
            if (server is not null && source is not null && clock is not null && endpointStarted
                && primary is null && holds.Count == 0 && running?.IsCompletedSuccessfully == true)
            {
                clock.JoinActualRequestDisposal();
                RequirePendingProbePhase(source.Phase, clock.Phase, context.Phase);
                if (context.HasQueuedWork) { throw new InvalidOperationException("Actual caller continuation work remains queued after its operation settled."); }
                phaseJoined = true;
            }
        }
        catch (Exception failure) { holds.Add(failure); }
        finally
        {
            try { JoinPendingProbe(service.DisposeAsync().AsTask()); }
            catch (Exception failure) { holds.Add(failure); }
            try { cancellation.Dispose(); }
            catch (Exception failure) { holds.Add(failure); }
            try { output.Dispose(); }
            catch (Exception failure) { holds.Add(failure); }
            try { error.Dispose(); }
            catch (Exception failure) { holds.Add(failure); }
        }
        bool candidate = server is not null && source is not null && clock is not null && primary is null && phaseJoined && holds.Count == 0
            && !stopping!.IsFaulted && !stopping.IsCanceled && !disposing!.IsFaulted && !disposing.IsCanceled;
        if (candidate)
        {
            WeakReference[] graph = [new(server!), new(service), new(source!), new(cancellation), new(output), new(error),
                new(running!), .. clock!.WeakGraph(), .. context.WeakGraph()];
            return new(null, graph, source!.Phase, clock.Phase, context.Phase, terminal, observers);
        }
        if (server is not null)
        {
            Task all = Task.WhenAll(stopping!, disposing!);
            try { JoinPendingProbe(all); }
            catch (Exception failure) { holds.Add(all.Exception ?? failure); }
            foreach (WeakReference<Task> reference in observers)
            {
                try { if (reference.TryGetTarget(out Task? actualObserver)) { JoinPendingProbe(actualObserver); } }
                catch (Exception failure) { holds.Add(failure); }
            }
            holds.AddRange(terminal.Failures());
        }
        if (source is not null) { holds.AddRange(source.Phase.Failures()); }
        if (clock?.Phase.CallbackFailure is { } callbackFailure) { holds.Add(callbackFailure); }
        holds.AddRange(context.Phase.Failures());
        if (holds.Count > 0)
        {
            if (primary is not null) { holds.Insert(0, primary); }
            primary = new AggregateException("The original pending-probe assertions or actual caller/source/timer cleanup did not settle.", holds);
        }
        return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), [], source?.Phase, clock?.Phase, context.Phase, terminal, []);
    }

    private static void ReviewPendingProbeOwners(PendingProbeObservation observed)
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
            { throw new InvalidOperationException("Actual pending-probe client/context/source/timer/slot ownership remains live; elapsed time is no settlement proof."); }
            RequirePendingProbePhase(observed.Source!, observed.Clock!, observed.Context);
            foreach (WeakReference<Task> reference in observed.Observers)
            {
                if (reference.TryGetTarget(out Task? actualObserver)) { JoinPendingProbe(actualObserver); }
            }
            Exception[] failures = observed.Terminal.Failures();
            if (failures.Length > 0) { throw new AggregateException("An actual pending-probe terminal operation failed.", failures); }
            Assert.IsTrue(observed.Terminal.StopCompleted, "Actual Stop must publish success after the real pending client/context exchange and server owner settle.");
            Assert.IsTrue(observed.Terminal.DisposeCompleted, "Actual Dispose must publish success after all actual pending-probe scenario work settles.");
        }
        observed.Primary?.Throw();
    }

    private static void RequirePendingProbePhase(PendingProbeSourcePhase source, PendingProbeClockPhase clock, PendingProbeContextPhase context)
    {
        Exception[] unknown = [.. source.Failures(), .. context.Failures()];
        if (clock.CallbackFailure is { } callbackFailure) { unknown = [.. unknown, callbackFailure]; }
        if (unknown.Length > 0) { throw new AggregateException("An actual unknown pending-probe source/context/timer fault remains a runtime hold.", unknown); }
        if (Volatile.Read(ref source.Captures) != 1 || Volatile.Read(ref source.Exits) != 1
            || Volatile.Read(ref clock.Created) != 1 || Volatile.Read(ref clock.JoinedDisposals) != 1
            || Volatile.Read(ref clock.CallbackEntries) != Volatile.Read(ref clock.CallbackReturns)
            || Volatile.Read(ref context.JoinedDrains) != 2 || Volatile.Read(ref context.PostEntries) != 0
            || Volatile.Read(ref context.PostReturns) != 0 || Volatile.Read(ref context.Enqueued) != 0
            || Volatile.Read(ref context.CallbackEntries) != 0 || Volatile.Read(ref context.CallbackReturns) != 0)
        { throw new InvalidOperationException("One actual capture/server timer drain and both real caller drains with the original no-post phase are required; raced or unknown work remains a hold."); }
    }
    private static void DrainActualPendingCaller(QueuedCallerContext context, Task running)
    {
        // This exact private method already observes its cancellation watcher
        // and joins the actual public probe Task after draining real callbacks.
        context.DrainUntilCompletedAsync(running, Deadline).GetAwaiter().GetResult();
        Interlocked.Increment(ref context.Phase.JoinedDrains);
    }
    private static void JoinPendingProbe(Task actual) => actual.WaitAsync(Deadline).GetAwaiter().GetResult();
    private static T JoinPendingProbe<T>(Task<T> actual) => actual.WaitAsync(Deadline).GetAwaiter().GetResult();
    private static WeakReference<Task> ObservePendingProbeTerminal(Task actual, PendingProbeTerminal terminal, bool disposing)
    {
        Task observer = actual.ContinueWith(static (completed, state) =>
        {
            var observed = ((PendingProbeTerminal Terminal, bool Disposing))state!;
            if (completed.Exception is { } failure) { observed.Terminal.RecordFailure(failure); }
            else if (completed.IsCanceled) { observed.Terminal.RecordFailure(new TaskCanceledException("Actual pending-probe terminal wait canceled.")); }
            else { observed.Terminal.RecordSuccess(observed.Disposing); }
        }, (terminal, disposing), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return new(observer);
    }
    private sealed record PendingProbeObservation(ExceptionDispatchInfo? Primary, WeakReference[] Graph,
        PendingProbeSourcePhase? Source, PendingProbeClockPhase? Clock, PendingProbeContextPhase Context, PendingProbeTerminal Terminal, WeakReference<Task>[] Observers);
    private sealed class PendingProbeSourcePhase
    {
        internal int Captures, Exits;
        private readonly object gate = new();
        private readonly List<Exception> faults = [];
        internal void RecordFailure(Exception failure) { lock (gate) { faults.Add(failure); } }
        internal Exception[] Failures() { lock (gate) { return [.. faults]; } }
    }
    private sealed class PendingProbeClockPhase
    {
        internal int Created, JoinedDisposals, CallbackEntries, CallbackReturns;
        private Exception? callbackFailure;
        internal Exception? CallbackFailure => Volatile.Read(ref callbackFailure);
        internal void RecordCallbackFailure(Exception error) => Interlocked.CompareExchange(ref callbackFailure, error, null);
    }
    private sealed class PendingProbeContextPhase
    {
        internal int PostEntries, PostReturns, Enqueued, CallbackEntries, CallbackReturns, JoinedDrains;
        private readonly object gate = new();
        private readonly List<Exception> faults = [];
        internal void RecordFailure(Exception failure) { lock (gate) { faults.Add(failure); } }
        internal Exception[] Failures() { lock (gate) { return [.. faults]; } }
    }
    private sealed class PendingProbeTerminal
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
}
