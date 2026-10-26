using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

public sealed partial class HealthPipeInternalMutationQualificationTests
{
    private static HealthPipeServer CreateStartedServer(StartupCountingSource source, HealthcheckOptions options, InternalBudgetClock clock)
    {
        StartupObservation observed = StartBeforeAnyClientAndReleaseFailure(source, options, clock);
        if (observed.Started is { } started) { return started; }
        if (observed.FailedStartupOwner is { } owner)
        {
            CollectFailedStartupOwner(owner);
            if (owner.IsAlive)
            {
                throw new AggregateException("Enabled startup failed and its actual accepting/queued owner remains live.",
                    observed.Primary!.SourceException,
                    new InvalidOperationException("Unsettled actual IO or queued work is a cleanup hold; elapsed time does not certify settlement."));
            }
            try
            {
                foreach (WeakReference<Task> reference in observed.CleanupObservers)
                {
                    if (reference.TryGetTarget(out Task? actualObserver)) { JoinStartupOperation(actualObserver); }
                }
                Exception[] failures = observed.Cleanup!.Failures();
                if (failures.Length > 0) { throw new AggregateException("Actual failed-start cleanup failed.", failures); }
            }
            catch (Exception error)
            {
                throw new AggregateException("The enabled-startup assertion and actual cleanup both failed.",
                    observed.Primary!.SourceException, error);
            }
        }
        observed.Primary!.Throw();
        throw new InvalidOperationException("A failed startup observation must retain its primary failure.");
    }

    // Both output scenarios start their actual probe/client only
    // after this factory returns. No server escapes this synchronous helper on
    // failure; no helper async state machine or returned strong Task owns it.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static StartupObservation StartBeforeAnyClientAndReleaseFailure(StartupCountingSource source,
        HealthcheckOptions options, InternalBudgetClock clock)
    {
        var server = new HealthPipeServer(source, options, clock);
        Exception? primary = null;
        bool exactStartupQueueFailureObserved = false;
        try
        {
            Exception? startupFailure = null;
            string? ownedOriginalFault = null;
            Task? starting = null;
            try { starting = server.StartAsync(); }
            catch (NullReferenceException error)
            {
                // Only the independently established synchronous failed-start
                // shape can reach the assertion: read the actual saved task
                // and observe its exact original queue fault before proceeding.
                try { ownedOriginalFault = ReadAndObserveOriginalQueueFault(server).ToString(); }
                catch (Exception observationFailure)
                {
                    throw new AggregateException("Synchronous startup failed and its original owned fault could not be safely observed.", error, observationFailure);
                }
                exactStartupQueueFailureObserved = true;
                startupFailure = error;
            }
            if (starting is not null)
            {
                try { JoinStartupOperation(starting); }
                catch (InvalidOperationException error) when (IsOriginalQueueFault(error))
                {
                    exactStartupQueueFailureObserved = true;
                    startupFailure = error;
                }
            }
            else if (startupFailure is null)
            {
                throw new InvalidOperationException("Successful startup did not return its actual wait task.");
            }
            Assert.IsNull(startupFailure, "Enabled startup of this fresh local endpoint must succeed. Actual startup failure:\n"
                + startupFailure + (ownedOriginalFault is null ? "" : "\nObserved original owned startup fault:\n" + ownedOriginalFault));
            return new(server, null, null, null, []);
        }
        catch (Exception error) { primary = error; }

        Task stopping = BeginStop(server);
        Task disposing = BeginDispose(server);
        Task? drains = null;
        WeakReference? owner = null;
        CleanupObservation? cleanup = null;
        WeakReference<Task>[] observers = [];
        try
        {
            bool exactNoRequestStartupBoundary = exactStartupQueueFailureObserved && primary is AssertFailedException
                && options.PipeEnabled && options.PipeMaxClients == 8 && source.Captures == 0 && Volatile.Read(ref clock.Phase.Created) == 0
                && !stopping.IsFaulted && !stopping.IsCanceled && !disposing.IsFaulted && !disposing.IsCanceled;
            if (exactNoRequestStartupBoundary)
            {
                // This predicate does not itself prove IO settlement. Under
                // the exact queue-acceptance inversion scoped in this proposal,
                // all eight endpoints are created but only the first callback
                // can be queued before the queue-fault branch. BeginStop and
                // startup's failure-finally close all eight actual streams.
                // The queued callback/RunSlotAsync retains this server through
                // its actual accept and finally. Only after that
                // owner disappears may an unreachable managed signal cycle be
                // left unjoined. No successful Stop/Dispose is claimed here.
                cleanup = new();
                observers = [ObserveActualCleanup(stopping, cleanup), ObserveActualCleanup(disposing, cleanup)];
                owner = new WeakReference(server);
            }
            else
            {
                drains = Task.WhenAll(stopping, disposing);
                JoinStartupOperation(drains);
            }
        }
        catch (Exception error)
        {
            primary = new AggregateException("Startup and actual owned cleanup both failed.", primary!, drains?.Exception ?? error);
        }
        return new(null, ExceptionDispatchInfo.Capture(primary!), owner, cleanup, observers);
    }

    private static InvalidOperationException ReadAndObserveOriginalQueueFault(HealthPipeServer server)
    {
        // Cleanup-only read of the actual owner. No field writes, substituted
        // tasks, worker calls, cancellation injection or completion occurs.
        FieldInfo? field = typeof(HealthPipeServer).GetField("startCompletion", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.FieldType != typeof(TaskCompletionSource) || field.GetValue(server) is not TaskCompletionSource completion
            || !completion.Task.IsFaulted || completion.Task.Exception is not { } original)
        {
            throw new InvalidOperationException("The actual saved startup failure could not be observed; its unresolved runtime work remains a hold.");
        }
        Exception[] failures = original.Flatten().InnerExceptions.ToArray();
        if (failures.Length != 1 || failures[0] is not InvalidOperationException failure || !IsOriginalQueueFault(failure))
        {
            throw new AggregateException("Unexpected actual saved startup fault remains a runtime/cleanup hold.", failures);
        }
        return failure;
    }

    private static bool IsOriginalQueueFault(InvalidOperationException error) => error.GetType() == typeof(InvalidOperationException)
        && error.Message == "The Healthcheck pipe worker could not be queued."
        && error.TargetSite?.Name == "StartCore" && error.TargetSite?.DeclaringType == typeof(HealthPipeServer);

    private static Task BeginStop(HealthPipeServer server)
    {
        try { return server.StopAsync(); }
        catch (Exception error) { return Task.FromException(error); }
    }

    private static Task BeginDispose(HealthPipeServer server)
    {
        try { return server.DisposeAsync().AsTask(); }
        catch (Exception error) { return Task.FromException(error); }
    }

    private static void JoinStartupOperation(Task actual) => actual.WaitAsync(Deadline).GetAwaiter().GetResult();

    private static WeakReference<Task> ObserveActualCleanup(Task actual, CleanupObservation observation)
    {
        Task observer = actual.ContinueWith(static (completed, state) =>
        {
            var errors = (CleanupObservation)state!;
            if (completed.Exception is { } failure) { errors.Add(failure); }
            else if (completed.IsCanceled) { errors.Add(new TaskCanceledException("Actual startup cleanup was canceled.")); }
        }, observation, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return new(observer);
    }

    private static void CollectFailedStartupOwner(WeakReference reference)
    {
        var observation = Stopwatch.StartNew();
        while (reference.IsAlive && observation.Elapsed < Deadline)
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            if (reference.IsAlive && observation.Elapsed < Deadline) { Thread.Sleep(1); }
        }
    }

    private sealed record StartupObservation(HealthPipeServer? Started, ExceptionDispatchInfo? Primary,
        WeakReference? FailedStartupOwner, CleanupObservation? Cleanup, WeakReference<Task>[] CleanupObservers);

    private sealed class CleanupObservation
    {
        private readonly object gate = new();
        private readonly List<Exception> failures = [];
        internal void Add(Exception error) { lock (gate) { failures.Add(error); } }
        internal Exception[] Failures() { lock (gate) { return [.. failures]; } }
    }
}
