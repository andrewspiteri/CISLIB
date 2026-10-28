using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorExecutionSettlementBoundaryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(3);

    [TestMethod]
    public async Task AChildCapturedByTheOwnerCanDisposeWhenTheActualCallerIsPublished()
    {
        using var context = new PublicationContext();
        var service = new HealthcheckService(new(), timeProvider: new FixedClock());
        var runner = new HealthCollectorRunner(service);
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
        var owner = new PublishingOwner();
        IHealthCollectionRegistration? registration = null;
        Task<HealthCollectionAttempt>? caller = null;
        Task? childDisposal = null;
        var childReturned = new TaskCompletionSource<ChildReturn>(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? primary = null;
        try
        {
            IHealthCollectionRegistration actualRegistration = runner.Register(monitor, owner, new());
            registration = actualRegistration;
            await Task.Run(() => runner.StartAsync()).WaitAsync(Deadline);
            Task<HealthCollectionAttempt> actualCaller = await HealthCollectorTestCleanup.EnterAsync(Deadline,
                () => actualRegistration.CollectAsync());
            caller = actualCaller;
            ExecutionContext escaped = await owner.Captured.Task.WaitAsync(Deadline);
            Assert.IsFalse(actualCaller.IsCompleted, "The owner has not yet released its actual evaluation.");

            // This is the actual context captured inside CollectAsync. The public
            // caller's continuation is registered before its evaluation is released.
            ExecutionContext.Run(escaped, _ =>
            {
                SynchronizationContext? previous = SynchronizationContext.Current;
                try
                {
                    SynchronizationContext.SetSynchronizationContext(context);
                    actualCaller.GetAwaiter().OnCompleted(() =>
                    {
                        try
                        {
                            HealthCollectionAttempt attempt = actualCaller.GetAwaiter().GetResult();
                            Task disposal = actualRegistration.DisposeAsync().AsTask();
                            childReturned.TrySetResult(new(attempt, disposal));
                        }
                        catch (Exception error) { childReturned.TrySetException(error); }
                    });
                }
                finally { SynchronizationContext.SetSynchronizationContext(previous); }
            }, null);

            owner.Release();
            await context.Posted.Task.WaitAsync(Deadline);
            ChildReturn child = await childReturned.Task.WaitAsync(Deadline);
            childDisposal = child.Disposal;
            await childDisposal.WaitAsync(Deadline);
            await context.CallbackFinished.WaitAsync(Deadline);
            Assert.IsFalse(context.PostReturned.Task.IsCompleted,
                "The actual caller's publisher remains held while its escaped child observes ownership that must already be settled.");
            Assert.AreEqual(HealthCollectionDisposition.Committed, child.Attempt.Disposition);
            Assert.IsFalse(child.Attempt.EvaluationFailed);
            Assert.IsNull(child.Attempt.Diagnostic);
            Assert.AreEqual(1, owner.Cleanups);
            monitor.ReportSuccess();
            Assert.AreEqual(2L, service.CaptureSnapshot().Monitors.Single().Outcomes!.Successful);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            owner.Release();
            context.Release();
            var observed = new List<Task>(context.ObservedTasks);
            if (caller is not null) { observed.Add(caller); }
            if (context.Started) { observed.Add(childReturned.Task); }
            if (childDisposal is not null) { observed.Add(childDisposal); }
            await HealthCollectorTestCleanup.DrainAsync(Deadline, primary, observed,
                context.JoinAsync,
                () => registration?.DisposeAsync().AsTask() ?? Task.CompletedTask,
                () => runner.DisposeAsync().AsTask(), () => service.DisposeAsync().AsTask());
            GC.KeepAlive(monitor);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AChildCapturedByTheOwnerCanJoinAnActuallyPublishedRetirementBarrier(bool stopRunner)
    {
        using var context = new PublicationContext();
        var service = new HealthcheckService(new(), timeProvider: new FixedClock());
        var runner = new HealthCollectorRunner(service);
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
        var owner = new PublishingOwner();
        IHealthCollectionRegistration? registration = null;
        Task<HealthCollectionAttempt>? caller = null;
        Task? retirement = null, childBarrier = null;
        var childReturned = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? primary = null;
        try
        {
            IHealthCollectionRegistration actualRegistration = runner.Register(monitor, owner, new());
            registration = actualRegistration;
            await Task.Run(() => runner.StartAsync()).WaitAsync(Deadline);
            caller = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => actualRegistration.CollectAsync());
            ExecutionContext escaped = await owner.Captured.Task.WaitAsync(Deadline);
            Task actualRetirement = await HealthCollectorTestCleanup.EnterAsync(Deadline,
                () => stopRunner ? runner.StopAsync() : actualRegistration.DisposeAsync().AsTask());
            retirement = actualRetirement;
            Assert.IsFalse(actualRetirement.IsCompleted, "The actual retirement still contains its owner's held evaluation.");

            ExecutionContext.Run(escaped, _ =>
            {
                SynchronizationContext? previous = SynchronizationContext.Current;
                try
                {
                    SynchronizationContext.SetSynchronizationContext(context);
                    actualRetirement.GetAwaiter().OnCompleted(() =>
                    {
                        try
                        {
                            actualRetirement.GetAwaiter().GetResult();
                            childReturned.TrySetResult(stopRunner ? runner.StopAsync() : actualRegistration.WaitForDisposalAsync());
                        }
                        catch (Exception error) { childReturned.TrySetException(error); }
                    });
                }
                finally { SynchronizationContext.SetSynchronizationContext(previous); }
            }, null);

            owner.Release();
            await context.Posted.Task.WaitAsync(Deadline);
            childBarrier = await childReturned.Task.WaitAsync(Deadline);
            await childBarrier.WaitAsync(Deadline);
            await context.CallbackFinished.WaitAsync(Deadline);
            Assert.IsFalse(context.PostReturned.Task.IsCompleted,
                "The retirement publisher is still held while the escaped child joins the same actual lifetime barrier.");
            Assert.AreEqual(1, owner.Cleanups);
            context.Release();
            HealthCollectionAttempt attempt = await caller.WaitAsync(Deadline);
            Assert.AreEqual(HealthCollectionDisposition.Retired, attempt.Disposition);
            Assert.IsFalse(attempt.EvaluationFailed);
            Assert.IsNull(attempt.Diagnostic);
            monitor.ReportSuccess();
            Assert.AreEqual(1L, service.CaptureSnapshot().Monitors.Single().Outcomes!.Successful);
            Assert.IsFalse(service.CaptureSnapshot().Application.IsStopping);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            owner.Release();
            context.Release();
            var observed = new List<Task>(context.ObservedTasks);
            if (caller is not null) { observed.Add(caller); }
            if (retirement is not null) { observed.Add(retirement); }
            if (context.Started) { observed.Add(childReturned.Task); }
            if (childBarrier is not null) { observed.Add(childBarrier); }
            await HealthCollectorTestCleanup.DrainAsync(Deadline, primary, observed,
                context.JoinAsync,
                () => registration?.DisposeAsync().AsTask() ?? Task.CompletedTask,
                () => runner.DisposeAsync().AsTask(), () => service.DisposeAsync().AsTask());
            GC.KeepAlive(monitor);
        }
    }

    [TestMethod]
    public async Task ActualCancellationSettlementDoesNotDependOnTheComponentsCleanupContext()
    {
        var context = new DeferredContext();
        using var owner = new SettlementOwner(context);
        var service = new HealthcheckService(new() { MaxConcurrentCollections = 1 }, timeProvider: new FixedClock());
        var runner = new HealthCollectorRunner(service);
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
        IHealthCollectionRegistration? registration = null;
        Task<HealthCollectionAttempt>? caller = null;
        OwnedCall? withdrawal = null;
        Exception? primary = null;
        try
        {
            IHealthCollectionRegistration actualRegistration = runner.Register(monitor, owner, new());
            registration = actualRegistration;
            await Task.Run(() => runner.StartAsync()).WaitAsync(Deadline);
            caller = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => actualRegistration.CollectAsync());
            await owner.EvaluationEntered.Task.WaitAsync(Deadline);
            withdrawal = new OwnedCall(actualRegistration.Withdraw);
            withdrawal.Start();
            await owner.CancellationEntered.Task.WaitAsync(Deadline);
            Assert.IsFalse(withdrawal.Returned.Task.IsCompleted, "The real lifetime-cancellation observer remains held by its owner.");

            owner.ReleaseEvaluation();
            await owner.CleanupFinished.Task.WaitAsync(Deadline);
            await owner.SettlementAwaitEntered.Task.WaitAsync(Deadline);
            Assert.IsFalse(caller.IsCompleted, "Synchronous lease cleanup must not release the actual cancellation-settlement barrier.");
            Assert.IsFalse(context.Posted.Task.IsCompleted);
            Assert.AreEqual(1, owner.Cleanups);
            Assert.IsFalse(owner.CancellationFinished.Task.IsCompleted);

            owner.ReleaseCancellation();
            await Task.WhenAny(caller, context.Posted.Task).WaitAsync(Deadline);
            Assert.IsFalse(context.Posted.Task.IsCompleted,
                "Core must finish real state settlement without asking the component's cleanup context to dispatch its continuation.");
            HealthCollectionAttempt attempt = await caller.WaitAsync(Deadline);
            Assert.AreEqual(HealthCollectionDisposition.Retired, attempt.Disposition);
            Assert.IsFalse(attempt.EvaluationFailed);
            Assert.IsNull(attempt.Diagnostic);
            await withdrawal.Returned.Task.WaitAsync(Deadline);
            await owner.CancellationFinished.Task.WaitAsync(Deadline);
            await actualRegistration.WaitForDisposalAsync().WaitAsync(Deadline);
            HealthMonitorSnapshot snapshot = service.CaptureSnapshot().Monitors.Single();
            Assert.IsFalse(snapshot.Collection!.InProgress);
            Assert.AreEqual(0L, snapshot.Outcomes!.Successful);
            Assert.AreEqual(0L, snapshot.Outcomes.Failed);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            owner.ReleaseEvaluation();
            owner.ReleaseCancellation();
            context.Release();
            var observed = new List<Task>();
            if (caller is not null) { observed.Add(caller); }
            if (withdrawal is not null) { observed.Add(withdrawal.Returned.Task); }
            if (owner.CancellationStarted) { observed.Add(owner.CancellationFinished.Task); }
            await HealthCollectorTestCleanup.DrainAsync(Deadline, primary, observed,
                () => withdrawal?.JoinAsync() ?? Task.CompletedTask,
                () => registration?.DisposeAsync().AsTask() ?? Task.CompletedTask,
                () => runner.DisposeAsync().AsTask(), () => service.DisposeAsync().AsTask());
            GC.KeepAlive(monitor);
        }
    }

    private sealed record ChildReturn(HealthCollectionAttempt Attempt, Task Disposal);

    private sealed class PublicationContext : SynchronizationContext, IDisposable
    {
        private readonly ManualResetEventSlim released = new();
        private OwnedCall? callback;
        internal TaskCompletionSource Posted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource PostReturned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Started => Volatile.Read(ref callback) is not null;
        internal Task CallbackFinished => callback?.Returned.Task ?? Task.CompletedTask;
        internal IEnumerable<Task> ObservedTasks => Started ? [PostReturned.Task, CallbackFinished] : [];
        public override void Post(SendOrPostCallback action, object? state)
        {
            try
            {
                ExecutionContext? captured = ExecutionContext.Capture();
                var actual = new OwnedCall(() =>
                {
                    if (captured is null) { throw new InvalidOperationException("The actual caller continuation did not supply its captured execution context."); }
                    ExecutionContext.Run(captured, _ => action(state), null);
                });
                if (Interlocked.CompareExchange(ref callback, actual, null) is not null)
                { throw new InvalidOperationException("The publication fixture received a second unexpected caller continuation."); }
                actual.Start();
                Posted.TrySetResult();
                if (released.Wait(Deadline)) { PostReturned.TrySetResult(); }
                else { PostReturned.TrySetException(new TimeoutException("The actual caller publication was not released by its observer.")); }
            }
            catch (Exception error)
            {
                // Throwing from Post would become an unrelated unhandled runtime
                // exception. Keep every fixture failure on its actual observation.
                Posted.TrySetException(error);
                PostReturned.TrySetException(error);
            }
        }
        internal void Release() => released.Set();
        internal Task JoinAsync() => callback?.JoinAsync() ?? Task.CompletedTask;
        public void Dispose()
        {
            released.Set();
            if (!Started || PostReturned.Task.IsCompleted) { released.Dispose(); }
        }
    }

    private sealed class DeferredContext : SynchronizationContext
    {
        private readonly object gate = new();
        private readonly Queue<(SendOrPostCallback Action, object? State, ExecutionContext? Context)> pending = new();
        private bool released;
        internal TaskCompletionSource Posted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override void Post(SendOrPostCallback action, object? state)
        {
            var work = (Action: action, State: state, Context: ExecutionContext.Capture());
            lock (gate)
            {
                Posted.TrySetResult();
                if (!released) { pending.Enqueue(work); return; }
            }
            Queue(work);
        }
        internal void Release()
        {
            lock (gate)
            {
                released = true;
                while (pending.TryDequeue(out var work)) { Queue(work); }
            }
        }
        private static void Queue((SendOrPostCallback Action, object? State, ExecutionContext? Context) work)
            => ThreadPool.UnsafeQueueUserWorkItem(static item =>
            {
                if (item.Context is null) { item.Action(item.State); }
                else { ExecutionContext.Run(item.Context, _ => item.Action(item.State), null); }
            }, work, preferLocal: false);
    }

    private sealed class OwnedCall
    {
        private readonly Thread thread;
        private bool started;
        internal OwnedCall(Action action)
        {
            thread = new Thread(() =>
            {
                try { action(); Returned.TrySetResult(); }
                catch (Exception error) { Returned.TrySetException(error); }
            })
            { IsBackground = true };
        }
        internal TaskCompletionSource Returned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Start() { started = true; thread.Start(); }
        internal Task JoinAsync() => Task.Run(() =>
        {
            if (started && !thread.Join(Deadline)) { throw new TimeoutException("A fixture-owned caller thread did not actually exit."); }
        });
    }

    private sealed class PublishingOwner : IHealthCollector
    {
        private readonly TaskCompletionSource<HealthCollectionResult> evaluation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int cleanups;
        internal TaskCompletionSource<ExecutionContext> Captured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Cleanups => Volatile.Read(ref cleanups);
        internal void Release() => evaluation.TrySetResult(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
        public bool TryAcquire(out IHealthCollectionLease? lease) { lease = new Lease(this); return true; }
        private sealed class Lease(PublishingOwner owner) : IHealthCollectionLease
        {
            public Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
            {
                ExecutionContext? captured = ExecutionContext.Capture();
                if (captured is null) { owner.Captured.TrySetException(new InvalidOperationException("The actual collector context was unavailable.")); }
                else { owner.Captured.TrySetResult(captured); }
                return owner.evaluation.Task;
            }
            public ValueTask DisposeAsync() { Interlocked.Increment(ref owner.cleanups); return ValueTask.CompletedTask; }
        }
    }

    private sealed class SettlementOwner : IHealthCollector, IDisposable
    {
        private readonly DeferredContext context;
        private readonly ManualResetEventSlim cancellationReleased = new();
        private readonly TaskCompletionSource<HealthCollectionResult> evaluation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly AsyncLocal<bool> cleanupContext;
        private CancellationTokenRegistration cancellation;
        private int cancellationStarted, cleanups;
        internal SettlementOwner(DeferredContext context)
        {
            this.context = context;
            cleanupContext = new(change =>
            {
                // The synchronous cleanup set this marker on Core's current
                // continuation. Returning to its previous context demonstrates
                // that Core yielded while actual cancellation is still held.
                if (change.ThreadContextChanged && change.PreviousValue && !change.CurrentValue)
                { SettlementAwaitEntered.TrySetResult(); }
            });
        }
        internal TaskCompletionSource EvaluationEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CleanupFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CancellationEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource CancellationFinished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource SettlementAwaitEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool CancellationStarted => Volatile.Read(ref cancellationStarted) != 0;
        internal int Cleanups => Volatile.Read(ref cleanups);
        internal void ReleaseEvaluation() => evaluation.TrySetResult(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
        internal void ReleaseCancellation() => cancellationReleased.Set();
        public bool TryAcquire(out IHealthCollectionLease? lease) { lease = new Lease(this); return true; }
        public void Dispose() { cancellation.Dispose(); cancellationReleased.Dispose(); }
        private sealed class Lease(SettlementOwner owner) : IHealthCollectionLease
        {
            private readonly MemoryStream resource = new([1]);
            public Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
            {
                owner.cancellation = lifetimeCancellation.Register(() =>
                {
                    Volatile.Write(ref owner.cancellationStarted, 1);
                    owner.CancellationEntered.TrySetResult();
                    if (owner.cancellationReleased.Wait(Deadline)) { owner.CancellationFinished.TrySetResult(); }
                    else { owner.CancellationFinished.TrySetException(new TimeoutException("The real lifetime-cancellation observer was not released by its owner.")); }
                });
                owner.EvaluationEntered.TrySetResult();
                return owner.evaluation.Task;
            }
            public ValueTask DisposeAsync()
            {
                resource.Dispose();
                Interlocked.Increment(ref owner.cleanups);
                SynchronizationContext.SetSynchronizationContext(owner.context);
                owner.cleanupContext.Value = true;
                owner.CleanupFinished.TrySetResult();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => 0;
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new InertTimer();
        private sealed class InertTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
