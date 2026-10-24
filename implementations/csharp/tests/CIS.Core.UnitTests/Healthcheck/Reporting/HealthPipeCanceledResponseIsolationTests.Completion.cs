using System.Diagnostics;
using System.Runtime.ExceptionServices;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

public sealed partial class HealthPipeCanceledResponseIsolationTests
{
    private static CanceledObservation ReleaseCanceledOwners(HealthPipeServer? server, HealthcheckService service,
        CancelingSource source, CallbackControlledClock clock, bool throwsAfterCancellation, Exception? primary)
    {
        Task? stopping = server is null ? null : BeginStop(server);
        Task? disposing = server is null ? null : BeginDispose(server);
        var terminal = new CanceledTerminal();
        WeakReference<Task>[] observers = server is null ? [] : [ObserveCanceledTerminal(stopping!, terminal, false), ObserveCanceledTerminal(disposing!, terminal, true)];
        var holds = new List<Exception>();
        bool completedPhase = false;
        try
        {
            // Both client scopes and every original actual connect/write/read
            // have already left the NoInlining body before this helper runs.
            if (server is not null && primary is null)
            {
                clock.JoinBothActualDisposals();
                RequireCanceledPhase(clock.Phase, throwsAfterCancellation);
                completedPhase = true;
            }
        }
        catch (Exception error) { holds.Add(error); }
        finally
        {
            try { JoinCanceled(service.DisposeAsync().AsTask()); }
            catch (Exception error) { holds.Add(error); }
        }
        bool candidate = server is not null && primary is null && completedPhase && holds.Count == 0
            && !stopping!.IsFaulted && !stopping.IsCanceled && !disposing!.IsFaulted && !disposing.IsCanceled;
        if (candidate)
        {
            // For the reviewed one-slot bookkeeping/final-progress family,
            // unchanged BeginStop closes every published stream. Actual
            // RunSlot/HandleClient tails retain the server through real work.
            // The source and all readonly timer callback/state graphs must
            // die too; retaining clock/current as a scalar proxy is unsafe.
            WeakReference[] graph = [new(server!), new(service), new(source), .. clock.WeakGraph()];
            return new(null, graph, clock.Phase, throwsAfterCancellation, terminal, observers);
        }
        if (server is not null)
        {
            Task all = Task.WhenAll(stopping!, disposing!);
            try { JoinCanceled(all); }
            catch (Exception error) { holds.Add(all.Exception ?? error); }
            foreach (WeakReference<Task> reference in observers)
            {
                try { if (reference.TryGetTarget(out Task? actualObserver)) { JoinCanceled(actualObserver); } }
                catch (Exception error) { holds.Add(error); }
            }
            holds.AddRange(terminal.Failures());
        }
        holds.AddRange(clock.Phase.Failures());
        if (holds.Count > 0)
        {
            if (primary is not null) { holds.Insert(0, primary); }
            primary = new AggregateException("The original response-isolation assertion or actual owned cleanup did not settle.", holds);
        }
        return new(primary is null ? null : ExceptionDispatchInfo.Capture(primary), [], clock.Phase, throwsAfterCancellation, terminal, []);
    }

    private static void ObserveCanceledSettlement(CanceledObservation observed)
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
            { throw new InvalidOperationException("Actual source/server/timer callback or queued/IO ownership remains reachable; elapsed time supplies no settlement."); }
            RequireCanceledPhase(observed.Phase, observed.ThrowsAfterCancellation);
            foreach (WeakReference<Task> reference in observed.Observers)
            {
                if (reference.TryGetTarget(out Task? actualObserver)) { JoinCanceled(actualObserver); }
            }
            Exception[] errors = observed.Terminal.Failures();
            if (errors.Length > 0) { throw new AggregateException("An actual terminal operation failed.", errors); }
            Assert.IsTrue(observed.Terminal.StopCompleted, "Actual Stop must publish successful completion after both original request/source/timer and real server owners settle.");
            Assert.IsTrue(observed.Terminal.DisposeCompleted, "Actual Dispose must publish successful completion after the actual response-isolation owners settle.");
        }
        observed.Primary?.Throw();
    }

    private static void RequireCanceledPhase(CanceledPhase phase, bool throwsAfterCancellation)
    {
        Exception[] faults = phase.Failures();
        if (faults.Length > 0) { throw new AggregateException("An unexpected actual source or deadline callback fault remains a runtime cleanup hold.", faults); }
        if (Volatile.Read(ref phase.Captures) != 2 || Volatile.Read(ref phase.SourceExits) != 2
            || Volatile.Read(ref phase.ExpectedSourceCancellations) != (throwsAfterCancellation ? 1 : 0)
            || Volatile.Read(ref phase.CreatedTimers) != 2 || Volatile.Read(ref phase.DisposedTimers) != 2
            || Volatile.Read(ref phase.CallbacksFired) != 1 || Volatile.Read(ref phase.CallbackEntries) != 1 || Volatile.Read(ref phase.CallbackReturns) != 1
            || Volatile.Read(ref phase.DisposalRequests) < 2 || Volatile.Read(ref phase.DisposalRequests) != Volatile.Read(ref phase.DisposalReturns))
        { throw new InvalidOperationException("Exactly two actual source/request/timer exits and the one original returned deadline callback are required; unknown or raced phases remain holds."); }
    }

    private static void JoinCanceled(Task actual) => actual.WaitAsync(Deadline).GetAwaiter().GetResult();
    private static T JoinCanceled<T>(Task<T> actual) => actual.WaitAsync(Deadline).GetAwaiter().GetResult();
    private static WeakReference<Task> ObserveCanceledTerminal(Task actual, CanceledTerminal terminal, bool disposing)
    {
        Task observer = actual.ContinueWith(static (completed, state) =>
        {
            var observed = ((CanceledTerminal Terminal, bool Disposing))state!;
            if (completed.Exception is { } error) { observed.Terminal.RecordFailure(error); }
            else if (completed.IsCanceled) { observed.Terminal.RecordFailure(new TaskCanceledException("Actual response-isolation terminal wait canceled.")); }
            else { observed.Terminal.RecordSuccess(observed.Disposing); }
        }, (terminal, disposing), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return new(observer);
    }
    private sealed record CanceledObservation(ExceptionDispatchInfo? Primary, WeakReference[] Graph,
        CanceledPhase Phase, bool ThrowsAfterCancellation, CanceledTerminal Terminal, WeakReference<Task>[] Observers);

    private sealed class CanceledPhase
    {
        internal int Captures, SourceExits, ExpectedSourceCancellations, CreatedTimers, DisposedTimers;
        internal int CallbacksFired, CallbackEntries, CallbackReturns, DisposalRequests, DisposalReturns;
        private readonly object gate = new();
        private readonly List<Exception> faults = [];
        internal void RecordFailure(Exception error) { lock (gate) { faults.Add(error); } }
        internal Exception[] Failures() { lock (gate) { return [.. faults]; } }
    }
    private sealed class CanceledTerminal
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
