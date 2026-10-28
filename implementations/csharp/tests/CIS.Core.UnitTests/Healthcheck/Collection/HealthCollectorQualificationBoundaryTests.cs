using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using CIS.Healthcheck;
using CIS.Healthcheck.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorQualificationBoundaryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(3);

    [TestMethod]
    public Task AppendingAnOwnerAfterWithdrawingTheTailPreservesTheNextWaitingOwnersTurn()
        => RunAsync(async scope =>
        {
            Attachment a = scope.Register("A"), b = scope.Register("B"), c = scope.Register("C");
            await scope.StartAsync();
            Task<HealthCollectionAttempt> firstA = await scope.CollectAsync(a);
            Invocation enteredA = await scope.NextAsync();
            Assert.AreEqual("A", enteredA.Name);
            Task<HealthCollectionAttempt> callerB = await scope.CollectAsync(b);
            Task<HealthCollectionAttempt> callerC = await scope.CollectAsync(c);
            await CompleteAsync(enteredA, firstA);
            Invocation enteredB = await scope.NextAsync();
            Assert.AreEqual("B", enteredB.Name);

            // B holds the only slot. The next cursor denotes C, the tail. A is
            // already waiting again when C leaves and the new D is appended.
            Task<HealthCollectionAttempt> secondA = await scope.CollectAsync(a);
            Task withdrawal = Task.Run(c.Registration.Withdraw);
            scope.Callers.Add(withdrawal);
            await withdrawal.WaitAsync(Deadline);
            HealthCollectionAttempt retired = await callerC.WaitAsync(Deadline);
            Assert.AreEqual(HealthCollectionDisposition.Retired, retired.Disposition);
            Assert.IsFalse(retired.EvaluationFailed);
            Assert.IsNull(retired.Diagnostic);
            Task cDrain = c.Registration.WaitForDisposalAsync();
            scope.Callers.Add(cDrain);
            await cDrain.WaitAsync(Deadline);
            Attachment d = scope.Register("D");
            Task<HealthCollectionAttempt> callerD = await scope.CollectAsync(d);
            Assert.AreEqual(1, a.Owner.Acquisitions);
            Assert.AreEqual(1, b.Owner.Acquisitions);
            Assert.AreEqual(0, c.Owner.Acquisitions);
            Assert.AreEqual(0, d.Owner.Acquisitions);

            await CompleteAsync(enteredB, callerB);
            Invocation next = await scope.NextAsync();
            Assert.AreEqual("A", next.Name,
                "A newly appended owner must not jump the next owner after the prior tail is withdrawn.");
            await CompleteAsync(next, secondA);
            Invocation appended = await scope.NextAsync();
            Assert.AreEqual("D", appended.Name);
            await CompleteAsync(appended, callerD);
            Assert.AreEqual(2, a.Owner.Cleanups);
            Assert.AreEqual(1, b.Owner.Cleanups);
            Assert.AreEqual(0, c.Owner.Cleanups);
            Assert.AreEqual(1, d.Owner.Cleanups);
            HealthSnapshot snapshot = scope.Service.CaptureSnapshot();
            Assert.AreEqual(4, snapshot.MonitorCounts.Active);
            Assert.AreEqual(4L, snapshot.Monitors.Sum(monitor => monitor.Outcomes!.Successful));
            Assert.IsTrue(snapshot.Monitors.All(monitor => monitor.Outcomes!.Failed == 0));
        });

    [TestMethod]
    public async Task AReservedWrapperSuppressedByStoppingCannotReportAnEvaluationThatNeverEntered()
    {
        using var releaseAcquisition = new ManualResetEventSlim();
        using var releaseStopping = new ManualResetEventSlim();
        await RunAsync(async scope =>
        {
            var acquisitionEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var stoppingEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var stoppingReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cleanupFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var settlementAwaitEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cleanupContext = new AsyncLocal<bool>(change =>
            {
                if (change.ThreadContextChanged && change.PreviousValue && !change.CurrentValue)
                { settlementAwaitEntered.TrySetResult(); }
            });
            Attachment attachment = scope.Register("reserved before application stop");
            attachment.Owner.BeforeAcquisitionReturns = () =>
            {
                acquisitionEntered.TrySetResult();
                if (!releaseAcquisition.Wait(Deadline)) { throw new TimeoutException("The actual owner acquisition was not released by its fixture."); }
            };
            attachment.Owner.AfterCleanup = () =>
            {
                cleanupContext.Value = true;
                cleanupFinished.TrySetResult();
            };
            IHealthCollectionState state = ((IHealthStateAccess)scope.Service).CollectionState;
            // Register after the runner's real observer so this callback receives
            // stopping before the runner closes its still-current attachments.
            CancellationTokenRegistration observer = state.Stopping.Register(() =>
            {
                stoppingEntered.TrySetResult();
                if (releaseStopping.Wait(Deadline)) { stoppingReturned.TrySetResult(); }
                else { stoppingReturned.TrySetException(new TimeoutException("The real stopping observer was not released by its fixture.")); }
            });
            var stopping = new OwnedAction(scope.Service.BeginStopping);
            Exception? primary = null;
            try
            {
                await scope.StartAsync();
                Task<HealthCollectionAttempt> caller = await scope.CollectAsync(attachment);
                await acquisitionEntered.Task.WaitAsync(Deadline);
                stopping.Start();
                scope.Callers.Add(stopping.Returned.Task);
                await stoppingEntered.Task.WaitAsync(Deadline);
                Assert.IsTrue(scope.Service.CaptureSnapshot().Application.IsStopping);
                Assert.IsFalse(stopping.Returned.Task.IsCompleted);

                releaseAcquisition.Set();
                await cleanupFinished.Task.WaitAsync(Deadline);
                // The synchronous cleanup marker leaves Core's current context
                // only once Core yields while the actual stopping observer is
                // still held. Completion has classified this suppressed wrapper.
                await settlementAwaitEntered.Task.WaitAsync(Deadline);
                Assert.AreEqual(0, attachment.Owner.Evaluations);
                Assert.AreEqual(1, attachment.Owner.Cleanups);
                Assert.IsFalse(caller.IsCompleted);
                Assert.IsFalse(stoppingReturned.Task.IsCompleted);
                releaseStopping.Set();

                HealthCollectionAttempt attempt = await caller.WaitAsync(Deadline);
                Assert.AreEqual(HealthCollectionDisposition.Retired, attempt.Disposition,
                    "Stopping suppressed the actual invocation, even while its monitor attachment was still current.");
                Assert.IsFalse(attempt.EvaluationFailed);
                Assert.IsNull(attempt.Diagnostic);
                await stopping.Returned.Task.WaitAsync(Deadline);
                await stoppingReturned.Task.WaitAsync(Deadline);
                Assert.AreEqual(0, attachment.Owner.Evaluations);
                HealthMonitorSnapshot snapshot = scope.Service.CaptureSnapshot().Monitors.Single();
                Assert.AreEqual(0L, snapshot.Outcomes!.Successful);
                Assert.AreEqual(0L, snapshot.Outcomes.Failed);
                Assert.IsFalse(snapshot.Collection!.InProgress);
            }
            catch (Exception error) { primary = error; throw; }
            finally
            {
                releaseAcquisition.Set();
                releaseStopping.Set();
                Task[] observed = [.. stopping.Started ? new[] { stopping.Returned.Task } : Array.Empty<Task>(),
                    .. stoppingEntered.Task.IsCompleted ? new[] { stoppingReturned.Task } : Array.Empty<Task>()];
                try { await HealthCollectorTestCleanup.DrainAsync(Deadline, primary, observed, stopping.JoinAsync); }
                finally { observer.Dispose(); }
            }
        });
    }

    [TestMethod]
    public Task ManualRequestsProgressAcrossTheActualIdleTimerChangeAndLaterDispatcherHandoff()
        => RunAsync(async scope =>
        {
            Attachment attachment = scope.Register("manual owner");
            await scope.StartAsync();
            ChangeRequest actualChange = await scope.Clock.FirstChange.Task.WaitAsync(Deadline);
            Assert.IsTrue(actualChange.Changed);
            Assert.AreEqual(Timeout.InfiniteTimeSpan, actualChange.DueTime);
            Assert.AreEqual(Timeout.InfiniteTimeSpan, actualChange.Period);
            Assert.IsFalse(scope.Clock.FirstChangeReturned.Task.IsCompleted,
                "The real initial Change has returned from its resource but remains held at the integration boundary.");

            Task<HealthCollectionAttempt> first = await scope.CollectAsync(attachment);
            Assert.IsFalse(first.IsCompleted);
            Assert.AreEqual(0, attachment.Owner.Acquisitions);
            scope.Clock.ReleaseChange();
            Invocation initial = await scope.NextAsync();
            Assert.AreEqual("manual owner", initial.Name);
            await CompleteAsync(initial, first);
            await scope.Clock.FirstChangeReturned.Task.WaitAsync(Deadline);

            // A further request may meet the current dispatcher or queue a new
            // one after it releases ownership. Both paths must make progress.
            Task<HealthCollectionAttempt> second = await scope.CollectAsync(attachment);
            Invocation later = await scope.NextAsync();
            Assert.AreEqual("manual owner", later.Name);
            await CompleteAsync(later, second);
            Assert.AreEqual(2, attachment.Owner.Acquisitions);
            Assert.AreEqual(2, attachment.Owner.Cleanups);
            Assert.AreEqual(2L, scope.Service.CaptureSnapshot().Monitors.Single().Outcomes!.Successful);
        }, pauseFirstChange: true);

    private static async Task CompleteAsync(Invocation invocation, Task<HealthCollectionAttempt> actualCaller)
    {
        invocation.Release();
        HealthCollectionAttempt attempt = await actualCaller.WaitAsync(Deadline);
        Assert.AreEqual(HealthCollectionDisposition.Committed, attempt.Disposition);
        Assert.IsFalse(attempt.EvaluationFailed);
        Assert.IsNull(attempt.Diagnostic);
    }

    private static async Task RunAsync(Func<Scenario, Task> body, bool pauseFirstChange = false)
    {
        var scope = new Scenario(pauseFirstChange);
        Exception? primary = null;
        try { await body(scope); }
        catch (Exception error) { primary = error; throw; }
        finally { await scope.DrainAsync(primary); }
    }

    private sealed record Attachment(IConsecutiveFailureMonitor Monitor, Owner Owner, IHealthCollectionRegistration Registration);

    private sealed class Scenario
    {
        private readonly List<Attachment> attachments = [];
        private readonly Channel<Invocation> entered = Channel.CreateUnbounded<Invocation>();
        internal Scenario(bool pauseFirstChange)
        {
            Clock = new(pauseFirstChange);
            Service = new(new() { MaxConcurrentCollections = 1 }, timeProvider: Clock);
            Runner = new(Service);
        }
        internal ObservedClock Clock { get; }
        internal HealthcheckService Service { get; }
        internal HealthCollectorRunner Runner { get; }
        internal List<Task> Callers { get; } = [];
        internal Attachment Register(string name)
        {
            IConsecutiveFailureMonitor monitor = Service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1, Name = name });
            var owner = new Owner(name, entered.Writer);
            IHealthCollectionRegistration registration = Runner.Register(monitor, owner, new());
            var attachment = new Attachment(monitor, owner, registration);
            attachments.Add(attachment);
            return attachment;
        }
        internal async Task StartAsync()
        {
            Task actual = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => Runner.StartAsync());
            Callers.Add(actual);
            await actual.WaitAsync(Deadline);
        }
        internal async Task<Task<HealthCollectionAttempt>> CollectAsync(Attachment attachment)
        {
            Task<HealthCollectionAttempt> actual = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => attachment.Registration.CollectAsync());
            Callers.Add(actual);
            return actual;
        }
        internal async Task<Invocation> NextAsync()
        {
            using var canceled = new CancellationTokenSource();
            try { return await entered.Reader.ReadAsync(canceled.Token).AsTask().WaitAsync(Deadline); }
            finally { canceled.Cancel(); }
        }
        internal async Task DrainAsync(Exception? primary)
        {
            Clock.ReleaseChange();
            foreach (Attachment attachment in attachments) { attachment.Owner.ReleaseAll(); }
            var drains = attachments.Select<Attachment, Func<Task>>(attachment => () => attachment.Registration.DisposeAsync().AsTask()).ToList();
            drains.Add(() => Runner.DisposeAsync().AsTask());
            drains.Add(() => Service.DisposeAsync().AsTask());
            var observed = new List<Task>(Callers);
            if (Clock.FirstChange.Task.IsCompleted) { observed.Add(Clock.FirstChangeReturned.Task); }
            Exception? cleanupFailure = null;
            try { await HealthCollectorTestCleanup.DrainAsync(Deadline, primary, observed, drains.ToArray()); }
            catch (Exception error) { cleanupFailure = error; }
            // Keep a real-resource fallback separate from the SUT's actual drain
            // observations, so it cannot supply a missing Stop completion.
            try { await Clock.DisposeFixtureTimerAsync().WaitAsync(Deadline); }
            catch (Exception error)
            {
                cleanupFailure = cleanupFailure is not null ? new AggregateException(cleanupFailure, error)
                    : primary is not null ? new AggregateException(primary, error) : error;
            }
            finally { Clock.Dispose(); GC.KeepAlive(attachments); }
            if (cleanupFailure is not null) { ExceptionDispatchInfo.Capture(cleanupFailure).Throw(); }
        }
    }

    private sealed class Invocation(string name)
    {
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal string Name { get; } = name;
        internal Task Released => released.Task;
        internal void Release() => released.TrySetResult();
    }

    private sealed class Owner(string name, ChannelWriter<Invocation> entered) : IHealthCollector
    {
        private readonly object gate = new();
        private readonly List<Invocation> invocations = [];
        private bool releasing;
        private int acquisitions, evaluations, cleanups;
        internal int Acquisitions => Volatile.Read(ref acquisitions);
        internal int Evaluations => Volatile.Read(ref evaluations);
        internal int Cleanups => Volatile.Read(ref cleanups);
        internal Action? BeforeAcquisitionReturns { get; set; }
        internal Action? AfterCleanup { get; set; }
        public bool TryAcquire(out IHealthCollectionLease? lease)
        {
            Interlocked.Increment(ref acquisitions);
            BeforeAcquisitionReturns?.Invoke();
            lease = new Lease(this);
            return true;
        }
        internal void ReleaseAll()
        {
            lock (gate)
            {
                releasing = true;
                foreach (Invocation invocation in invocations) { invocation.Release(); }
            }
        }
        private async Task<HealthCollectionResult> EvaluateAsync(CancellationToken lifetimeCancellation)
        {
            Interlocked.Increment(ref evaluations);
            var invocation = new Invocation(name);
            lock (gate)
            {
                invocations.Add(invocation);
                if (releasing) { invocation.Release(); }
            }
            if (!entered.TryWrite(invocation)) { throw new InvalidOperationException("The actual invocation observation was rejected."); }
            await invocation.Released;
            lifetimeCancellation.ThrowIfCancellationRequested();
            return HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded);
        }
        private sealed class Lease(Owner owner) : IHealthCollectionLease
        {
            private readonly MemoryStream resource = new([1]);
            public Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation) => owner.EvaluateAsync(lifetimeCancellation);
            public ValueTask DisposeAsync()
            {
                resource.Dispose();
                Interlocked.Increment(ref owner.cleanups);
                owner.AfterCleanup?.Invoke();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class OwnedAction
    {
        private readonly Thread thread;
        internal OwnedAction(Action action)
        {
            thread = new(() =>
            {
                try { action(); Returned.TrySetResult(); }
                catch (Exception error) { Returned.TrySetException(error); }
            })
            { IsBackground = true };
        }
        internal bool Started { get; private set; }
        internal TaskCompletionSource Returned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Start() { Started = true; thread.Start(); }
        internal Task JoinAsync() => Task.Run(() =>
        {
            if (Started && !thread.Join(Deadline)) { throw new TimeoutException("The fixture-owned application-stop thread did not exit."); }
        });
    }

    private sealed record ChangeRequest(TimeSpan DueTime, TimeSpan Period, bool Changed);

    private sealed class ObservedClock(bool pauseFirstChange) : TimeProvider, IDisposable
    {
        private readonly object gate = new();
        private readonly List<ITimer> actualTimers = [];
        private readonly ManualResetEventSlim released = new(!pauseFirstChange);
        private int changes;
        internal TaskCompletionSource<ChangeRequest> FirstChange { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource FirstChangeReturned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void ReleaseChange() => released.Set();
        public override long TimestampFrequency => TimeProvider.System.TimestampFrequency;
        public override long GetTimestamp() => TimeProvider.System.GetTimestamp();
        public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ITimer actual = TimeProvider.System.CreateTimer(callback, state, dueTime, period);
            lock (gate) { actualTimers.Add(actual); }
            return new Timer(this, actual);
        }
        internal Task DisposeFixtureTimerAsync()
        {
            lock (gate) { return Task.WhenAll(actualTimers.Select(timer => timer.DisposeAsync().AsTask())); }
        }
        public void Dispose()
        {
            if (!FirstChange.Task.IsCompleted || FirstChangeReturned.Task.IsCompleted) { released.Dispose(); }
        }
        private sealed class Timer(ObservedClock owner, ITimer actual) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                bool changed = actual.Change(dueTime, period);
                if (Interlocked.Increment(ref owner.changes) == 1)
                {
                    owner.FirstChange.TrySetResult(new(dueTime, period, changed));
                    if (owner.released.Wait(Deadline)) { owner.FirstChangeReturned.TrySetResult(); }
                    else { owner.FirstChangeReturned.TrySetException(new TimeoutException("The actual timer Change was not released by the fixture owner.")); }
                }
                return changed;
            }
            public void Dispose() => actual.Dispose();
            public ValueTask DisposeAsync() => actual.DisposeAsync();
        }
    }
}
