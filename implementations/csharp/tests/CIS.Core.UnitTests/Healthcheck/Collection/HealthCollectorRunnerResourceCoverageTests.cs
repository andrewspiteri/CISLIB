using System.Collections;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Threading.Tasks.Sources;
using CIS.Healthcheck;
using CIS.Healthcheck.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

// Monitor.Enter can throw ThreadInterruptedException while waiting for a lock:
// https://learn.microsoft.com/en-us/dotnet/api/system.threading.monitor.enter
// Only fixture-owned dedicated threads are interrupted, never ThreadPool workers.
[TestClass]
public sealed class HealthCollectorRunnerResourceCoverageTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(3);

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task RepeatingACompletedWithdrawalCannotStartClockWorkOrFaultTerminalStop()
    {
        var assembly = typeof(HealthCollectorRunner).Assembly;
        TestContext.WriteLine($"CIS_CORE_BODY {Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly.Location)))} {assembly.ManifestModule.ModuleVersionId:D}");
        using var clock = new ResourceClock();
        var service = new HealthcheckService(new(), timeProvider: clock);
        var runner = new HealthCollectorRunner(service);
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
        var owner = new UnavailableOwner();
        IHealthCollectionRegistration? registration = null;
        InvalidOperationException? actualStopFailure = null;
        Exception? primary = null;
        int unexpectedSamples = 0;
        try
        {
            registration = runner.Register(monitor, owner, new());
            await runner.StartAsync().WaitAsync(Deadline);
            await clock.TimerChanged.Task.WaitAsync(Deadline);
            registration.Withdraw();
            await registration.WaitForDisposalAsync().WaitAsync(Deadline);
            object gate = ReadField(runner, "gate");
            Assert.IsTrue(SpinWait.SpinUntil(() =>
            {
                if (!Monitor.TryEnter(gate)) { return false; }
                try { return ReadField(runner, "pumpScheduled") is false; }
                finally { Monitor.Exit(gate); }
            }, Deadline), "The actual withdrawal dispatcher must relinquish ownership before testing an inert repeat.");

            // No periodic registration or operation remains. Only an erroneous
            // new dispatcher can read this clock after the completed withdrawal.
            clock.TimestampReading = () =>
            {
                Interlocked.Increment(ref unexpectedSamples);
                throw new InvalidOperationException("An already withdrawn registration requested new clock work.");
            };
            registration.Withdraw();
            Task stop = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => runner.StopAsync());
            try { await stop.WaitAsync(Deadline); }
            catch (InvalidOperationException error) { actualStopFailure = error; }
            await clock.ActualTimerDrain.WaitAsync(Deadline);

            Assert.IsNull(actualStopFailure,
                "Repeating a completed withdrawal must leave terminal Stop successful.");
            Assert.AreEqual(0, Volatile.Read(ref unexpectedSamples));
            Assert.AreEqual(0, owner.Acquisitions);
            Assert.AreEqual(1, clock.SutTimerDisposals);
            Assert.AreEqual(1, clock.SutTimerDisposalExits);
            monitor.ReportSuccess();
            Assert.AreEqual(MonitorState.Healthy, service.CaptureSnapshot().Monitors.Single().State);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            clock.TimestampReading = null;
            clock.ReleaseFactory();
            clock.ReleaseDisposal();
            try
            {
                await HealthCollectorTestCleanup.DrainAsync(Deadline, primary, [],
                    DrainRunnerAsync, () => service.DisposeAsync().AsTask(), () => clock.DisposeFixtureTimerAsync());
            }
            finally { GC.KeepAlive(monitor); }
        }

        async Task DrainRunnerAsync()
        {
            try { await runner.DisposeAsync(); }
            catch (InvalidOperationException error) when (ReferenceEquals(error, actualStopFailure))
            { /* The body already asserts this exact terminal error; actual timer disposal is separately joined. */ }
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InterruptedTimerPublicationRetainsTheActualResourceUntilDisposalSettles(bool stopDuringFactory)
    {
        using var clock = new ResourceClock(holdFactory: true, holdDisposal: true);
        var service = new HealthcheckService(new(), timeProvider: clock);
        var runner = new HealthCollectorRunner(service);
        var starting = new OwnedCall<Task>(() => runner.StartAsync());
        using var interruption = new ObservedInterruption(starting.Thread);
        Task? start = null, stop = null;
        InvalidOperationException? expectedStartFailure = null;
        Exception? primary = null;
        try
        {
            starting.Start();
            await clock.FactoryEntered.Task.WaitAsync(Deadline);
            InGate(ReadField(runner, "gate"), () =>
            {
                // This is reentrant entry on the gate this fixture owns. There
                // are no registered collections or published timer yet.
                if (stopDuringFactory) { stop = runner.StopAsync(); }
                clock.ReleaseFactory();
                clock.FactoryReturning.Task.WaitAsync(Deadline).GetAwaiter().GetResult();
                starting.WaitUntilBlocked();
                starting.Thread.Interrupt();
                interruption.Thrown.Task.WaitAsync(Deadline).GetAwaiter().GetResult();
            });

            start = await starting.Returned.Task.WaitAsync(Deadline);
            expectedStartFailure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => start).WaitAsync(Deadline);
            await clock.TimerDisposalEntered.Task.WaitAsync(Deadline);
            stop ??= await HealthCollectorTestCleanup.EnterAsync(Deadline, () => runner.StopAsync());

            Assert.AreEqual(1, clock.SutTimerDisposals);
            Assert.IsFalse(clock.ActualTimerDrain.IsCompleted, "The real timer's disposal remains held by its owner.");
            Assert.IsFalse(stop.IsCompleted,
                "An unpublished timer still belongs to failed startup; Stop cannot acknowledge before that actual resource drains.");

            clock.ReleaseDisposal();
            await stop.WaitAsync(Deadline);
            await clock.ActualTimerDrain.WaitAsync(Deadline);
            Assert.AreEqual(1, clock.SutTimerDisposals);
            Assert.AreEqual(1, clock.SutTimerDisposalExits);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            clock.ReleaseFactory();
            clock.ReleaseDisposal();
            await HealthCollectorTestCleanup.DrainAsync(Deadline, primary, [],
                () => starting.JoinAsync(),
                ObserveStartAsync,
                () => runner.DisposeAsync().AsTask(),
                () => service.DisposeAsync().AsTask(),
                () => clock.DisposeFixtureTimerAsync());
        }

        async Task ObserveStartAsync()
        {
            Task actual = start ?? await starting.Returned.Task;
            try { await actual; }
            catch (InvalidOperationException error) when (ReferenceEquals(error, expectedStartFailure)) { }
        }
    }

    [TestMethod]
    public async Task AnInterruptedRealWithdrawalCannotReplaceAnEarlierTimerResourceFailure()
    {
        var timerFailure = new InvalidOperationException("The first timer disposal failed.");
        using var clock = new ResourceClock(disposalFailure: timerFailure);
        var service = new HealthcheckService(new(), timeProvider: clock);
        var runner = new HealthCollectorRunner(service);
        IHealthCollectionState state = ((IHealthStateAccess)service).CollectionState;
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
        IHealthCollectionRegistration? registration = null;
        OwnedCall<bool>? withdrawing = null;
        ObservedInterruption? interruption = null;
        Task? stop = null;
        InvalidOperationException? actualStopFailure = null;
        Exception? primary = null;
        try
        {
            IHealthCollectionRegistration actualRegistration = await HealthCollectorTestCleanup.EnterAsync(Deadline,
                () => runner.Register(monitor, new UnavailableOwner(), new()));
            registration = actualRegistration;
            await Task.Run(() => runner.StartAsync()).WaitAsync(Deadline);
            await clock.TimerChanged.Task.WaitAsync(Deadline);
            withdrawing = new OwnedCall<bool>(() => { actualRegistration.Withdraw(); return true; });
            interruption = new ObservedInterruption(withdrawing.Thread);
            InGate(ReadField(state, "gate"), () =>
            {
                withdrawing.Start();
                withdrawing.WaitUntilBlocked();
                stop = HealthCollectorTestCleanup.EnterAsync(Deadline, () => runner.StopAsync()).GetAwaiter().GetResult();
                WaitForResourceError(runner, timerFailure.Message);
                Assert.IsFalse(stop.IsCompleted, "The actual withdrawal call is still blocked at the state owner.");

                withdrawing.Thread.Interrupt();
                interruption.Thrown.Task.WaitAsync(Deadline).GetAwaiter().GetResult();
                Assert.IsTrue(SpinWait.SpinUntil(() => withdrawing.Returned.Task.IsCompleted ||
                    (withdrawing.Thread.ThreadState & ThreadState.WaitSleepJoin) != 0, Deadline));
                Assert.IsFalse(stop.IsCompleted, "The earlier timer failure cannot release an unsettled state withdrawal.");
            });

            await withdrawing.JoinAsync().WaitAsync(Deadline);
            Assert.IsTrue(await withdrawing.Returned.Task.WaitAsync(Deadline));
            Assert.IsNotNull(stop);
            actualStopFailure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => stop).WaitAsync(Deadline);
            Assert.AreEqual(timerFailure.Message, actualStopFailure.Message,
                "A later real withdrawal interruption must preserve the first actual runner resource failure.");
            Assert.AreEqual(1, clock.SutTimerDisposals);
            Assert.AreEqual(1, clock.SutTimerDisposalExits);
            await clock.ActualTimerDrain.WaitAsync(Deadline);
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            clock.ReleaseFactory();
            clock.ReleaseDisposal();
            try
            {
                await HealthCollectorTestCleanup.DrainAsync(Deadline, primary, [],
                    () => withdrawing?.JoinAsync() ?? Task.CompletedTask,
                    () => withdrawing?.Returned.Task ?? Task.CompletedTask,
                    () => registration?.DisposeAsync().AsTask() ?? Task.CompletedTask,
                    DrainRunnerAsync,
                    () => service.DisposeAsync().AsTask(),
                    () => clock.DisposeFixtureTimerAsync());
            }
            finally
            {
                interruption?.Dispose();
                GC.KeepAlive(monitor);
            }
        }

        async Task DrainRunnerAsync()
        {
            try { await runner.DisposeAsync(); }
            catch (InvalidOperationException error) when (ReferenceEquals(error, actualStopFailure)) { }
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task InterruptedWithdrawalCannotPublishDisposalBeforeTheRealAttachmentRetires(bool requestDispose)
    {
        var service = new HealthcheckService(new(), new() { Name = "interrupted collection retirement", InstanceId = Guid.NewGuid() });
        var monitor = service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
        var runner = new HealthCollectorRunner(service);
        var registration = runner.Register(monitor, new UnavailableOwner(), new());
        HealthCollectionTarget target = ReadTarget(runner, monitor.MonitorId);
        IHealthCollectionState state = ((IHealthStateAccess)service).CollectionState;
        var withdrawing = new OwnedCall<Task>(() =>
        {
            if (requestDispose) { return registration.DisposeAsync().AsTask(); }
            registration.Withdraw();
            return Task.CompletedTask;
        });
        using var observation = new ObservedInterruption(withdrawing.Thread);
        ThreadInterruptedException? interrupted = null;
        Task? disposal = null, stop = null, requestedDisposal = null;
        InvalidOperationException? expectedStopFailure = null;
        IHealthCollectionRegistration? replacement = null;
        Exception? primary = null;
        try
        {
            InGate(ReadField(state, "gate"), () =>
            {
                withdrawing.Start();
                withdrawing.WaitUntilBlocked();
                disposal = registration.WaitForDisposalAsync();
                Assert.IsFalse(disposal.IsCompleted);

                withdrawing.Thread.Interrupt();
                interrupted = observation.Thrown.Task.WaitAsync(Deadline).GetAwaiter().GetResult();
                Assert.IsTrue(SpinWait.SpinUntil(() => withdrawing.Returned.Task.IsCompleted ||
                    (withdrawing.Thread.ThreadState & ThreadState.WaitSleepJoin) != 0, Deadline));
                Assert.IsFalse(disposal.IsCompleted, "A failed admission cannot acknowledge registration disposal.");
            });

            requestedDisposal = await withdrawing.Returned.Task.WaitAsync(Deadline);
            await withdrawing.JoinAsync().WaitAsync(Deadline);
            await requestedDisposal.WaitAsync(Deadline);
            // FirstChance publishes before the exception trace finishes unwinding.
            // The actual caller has joined before this thread inspects that trace.
            Assert.IsNotNull(interrupted);
            Assert.IsNotNull(interrupted.StackTrace);
            StringAssert.Contains(interrupted.StackTrace, "HealthStateCoordinator.WithdrawCollection",
                "Only interruption at the real coordinator's withdrawal admission is the intended boundary.");
            await disposal!.WaitAsync(Deadline);
            Assert.IsTrue(target.Retirement.IsCancellationRequested,
                "The actual state-issued retirement token must close before its registration acknowledges disposal.");

            // A falsely acknowledged retirement leaves the still-active monitor
            // attached and rejects this new public registration on the same runner.
            replacement = runner.Register(monitor, new UnavailableOwner(), new());
            monitor.ReportSuccess();
            Assert.AreEqual(1, service.CaptureSnapshot().MonitorCounts.Active);
            Assert.IsFalse(service.CaptureSnapshot().Application.IsStopping);

            stop = HealthCollectorTestCleanup.EnterAsync(Deadline, () => runner.StopAsync()).GetAwaiter().GetResult();
            expectedStopFailure = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => stop).WaitAsync(Deadline);
            Assert.AreEqual(observation.Thrown.Task.Result.Message, expectedStopFailure.Message,
                "Successful retry must preserve the original resource diagnostic.");
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            await HealthCollectorTestCleanup.DrainAsync(Deadline, primary, [],
                () => withdrawing.JoinAsync(),
                async () => await (requestedDisposal ?? await withdrawing.Returned.Task),
                () => registration.DisposeAsync().AsTask(),
                ObserveStopAsync,
                ObserveRunnerAsync,
                () => replacement?.DisposeAsync().AsTask() ?? Task.CompletedTask,
                () => monitor.DisposeAsync().AsTask(),
                () => service.DisposeAsync().AsTask());
            GC.KeepAlive(registration);
            GC.KeepAlive(monitor);
        }

        async Task ObserveStopAsync()
        {
            if (stop is null) { return; }
            try { await stop; }
            catch (InvalidOperationException error) when (ReferenceEquals(error, expectedStopFailure)) { }
        }
        async Task ObserveRunnerAsync()
        {
            try { await runner.DisposeAsync(); }
            catch (InvalidOperationException error) when (ReferenceEquals(error, expectedStopFailure)) { }
        }
    }

    [TestMethod]
    public async Task InterruptedCollectionCompletionCannotAbandonTheActualStateReservation()
    {
        using var clock = new ResourceClock();
        var service = new HealthcheckService(new(), timeProvider: clock);
        var runner = new HealthCollectorRunner(service);
        IHealthCollectionState state = ((IHealthStateAccess)service).CollectionState;
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
        var owner = new HeldCleanupOwner();
        var utcObserved = new TaskCompletionSource<Thread>(TaskCreationOptions.RunContinuationsAsynchronously);
        clock.UtcReading = () =>
        {
            if (owner.Disposal.ResourceDisposed) { utcObserved.TrySetResult(Thread.CurrentThread); }
        };
        IHealthCollectionRegistration? registration = null;
        Task<HealthCollectionAttempt>? caller = null;
        OwnedCall<bool>? completing = null;
        ObservedInterruption? interruption = null;
        InvalidOperationException? expectedStopFailure = null;
        Exception? primary = null;
        try
        {
            IHealthCollectionRegistration actualRegistration = await HealthCollectorTestCleanup.EnterAsync(Deadline,
                () => runner.Register(monitor, owner, new()));
            registration = actualRegistration;
            await Task.Run(() => runner.StartAsync()).WaitAsync(Deadline);
            caller = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => actualRegistration.CollectAsync());
            await owner.Disposal.ContinuationRegistered.Task.WaitAsync(Deadline);
            Assert.IsFalse(caller.IsCompleted, "The actual collection owns the lease disposal that is still held.");
            HealthCollectionTarget target = ReadTarget(runner, monitor.MonitorId);
            HealthCollectionReservationResult existing = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => state.TryReserve(target));
            Assert.AreEqual(HealthCollectionReservationDisposition.Existing, existing.Disposition);
            HealthCollectionReservation? reservation = existing.Reservation;
            Assert.IsNotNull(reservation);
            Task actualSettlement = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => state.WaitForSettlementAsync(reservation));
            Assert.IsFalse(actualSettlement.IsCompleted);
            completing = new OwnedCall<bool>(() => { owner.Disposal.Complete(); return true; });
            interruption = new ObservedInterruption(completing.Thread);
            InGate(ReadField(state, "gate"), () =>
            {
                completing.Start();
                Thread continuationThread = utcObserved.Task.WaitAsync(Deadline).GetAwaiter().GetResult();
                Assert.AreSame(completing.Thread, continuationThread,
                    "Only the fixture-owned lease-disposal continuation may be interrupted.");
                completing.WaitUntilBlocked();
                completing.Thread.Interrupt();
                interruption.Thrown.Task.WaitAsync(Deadline).GetAwaiter().GetResult();
            });
            await completing.JoinAsync().WaitAsync(Deadline);
            Assert.IsTrue(await completing.Returned.Task.WaitAsync(Deadline));

            HealthCollectionAttempt attempt = await caller.WaitAsync(Deadline);
            Assert.IsFalse(attempt.EvaluationFailed, "A state-settlement interruption cannot invent an evaluation failure after a successful callback.");
            if (attempt.Diagnostic is { } diagnostic)
            {
                Assert.AreEqual("Healthcheck.CollectionSettlement", diagnostic.Code);
                Assert.AreEqual(typeof(ThreadInterruptedException).FullName, diagnostic.ExceptionType);
            }
            Assert.IsTrue(owner.Disposal.ResourceDisposed);
            // The fixture never calls Complete for this issued reservation.
            // Only the actual runner may finish its state-owned operation.
            await actualSettlement.WaitAsync(Deadline);
            HealthMonitorSnapshot snapshot = (await Task.Run(service.CaptureSnapshot).WaitAsync(Deadline)).Monitors.Single();
            Assert.IsFalse(snapshot.Collection!.InProgress,
                "Publishing the collection result cannot abandon the coordinator's actual operation or its disposal barrier.");
            Assert.AreEqual(1L, snapshot.Outcomes!.Successful);
            Assert.AreEqual(0L, snapshot.Outcomes.Failed);
            Task stop = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => runner.StopAsync());
            try { await stop.WaitAsync(Deadline); }
            catch (InvalidOperationException error)
            {
                Assert.AreEqual((await interruption.Thrown.Task).Message, error.Message);
                expectedStopFailure = error;
            }
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            clock.ReleaseFactory();
            clock.ReleaseDisposal();
            completing ??= new OwnedCall<bool>(() => { owner.Disposal.Complete(); return true; });
            if (!completing.Started) { completing.Start(); }
            try
            {
                await HealthCollectorTestCleanup.DrainAsync(Deadline, primary, caller is null ? [] : [caller],
                    () => completing.JoinAsync(),
                    () => completing.Returned.Task,
                    () => registration?.DisposeAsync().AsTask() ?? Task.CompletedTask,
                    DrainRunnerAsync,
                    () => service.DisposeAsync().AsTask(),
                    () => clock.DisposeFixtureTimerAsync(),
                    () => owner.DisposeFixtureResourceAsync());
            }
            finally
            {
                interruption?.Dispose();
                GC.KeepAlive(monitor);
            }
        }

        async Task DrainRunnerAsync()
        {
            try { await runner.DisposeAsync(); }
            catch (InvalidOperationException error) when (ReferenceEquals(error, expectedStopFailure)) { }
        }
    }

    private static HealthCollectionTarget ReadTarget(HealthCollectorRunner runner, Guid monitorId)
    {
        HealthCollectionTarget? target = null;
        InGate(ReadField(runner, "gate"), () =>
        {
            var entries = (IEnumerable)ReadField(runner, "entries");
            foreach (object entry in entries)
            {
                var issued = (HealthCollectionTarget)ReadField(entry, "Target");
                if (issued.MonitorId == monitorId) { target = issued; break; }
            }
        });
        Assert.IsNotNull(target, "The real registration must retain its actual state-issued target.");
        return target;
    }

    private static void WaitForResourceError(HealthCollectorRunner runner, string firstMessage)
    {
        object gate = ReadField(runner, "gate");
        bool recorded = SpinWait.SpinUntil(() =>
        {
            if (!Monitor.TryEnter(gate)) { return false; }
            try { return ReadNullableField(runner, "resourceError") is HealthDiagnostic diagnostic && diagnostic.Message == firstMessage; }
            finally { Monitor.Exit(gate); }
        }, Deadline);
        Assert.IsTrue(recorded, "The real timer-disposal error must be recorded before interrupting the separately owned withdrawal.");
    }

    private static void InGate(object gate, Action action)
    {
        bool entered = Monitor.TryEnter(gate, Deadline);
        Assert.IsTrue(entered, "The resource fixture must enter the actual synchronization boundary.");
        try { action(); }
        finally { Monitor.Exit(gate); }
    }

    private static object ReadField(object owner, string name)
    {
        object? value = ReadNullableField(owner, name);
        Assert.IsNotNull(value, "The resource fixture requires the existing field " + name + ".");
        return value;
    }

    private static object? ReadNullableField(object owner, string name)
    {
        FieldInfo? field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        Assert.IsNotNull(field, "The resource fixture requires the existing field " + name + ".");
        return field.GetValue(owner);
    }

    private sealed class OwnedCall<T>
    {
        private bool started;
        internal OwnedCall(Func<T> call)
        {
            Thread = new Thread(() =>
            {
                try { Returned.TrySetResult(call()); }
                catch (Exception error) { Returned.TrySetException(error); }
            })
            { IsBackground = true };
        }
        internal Thread Thread { get; }
        internal bool Started => started;
        internal TaskCompletionSource<T> Returned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Start() { started = true; Thread.Start(); }
        internal void WaitUntilBlocked()
        {
            bool blocked = SpinWait.SpinUntil(() => Returned.Task.IsCompleted ||
                (Thread.ThreadState & ThreadState.WaitSleepJoin) != 0, Deadline);
            Assert.IsTrue(blocked && !Returned.Task.IsCompleted,
                "The dedicated caller must block at the held real monitor before interruption.");
        }
        internal Task JoinAsync() => Task.Run(() =>
        {
            if (started) { Assert.IsTrue(Thread.Join(TimeSpan.FromSeconds(2)), "Every fixture-owned caller thread must actually exit."); }
        });
    }

    private sealed class ObservedInterruption : IDisposable
    {
        private readonly EventHandler<FirstChanceExceptionEventArgs> observer;
        internal ObservedInterruption(Thread ownedThread)
        {
            observer = (_, eventArgs) =>
            {
                if (ReferenceEquals(Thread.CurrentThread, ownedThread) && eventArgs.Exception is ThreadInterruptedException interrupted)
                { Thrown.TrySetResult(interrupted); }
            };
            AppDomain.CurrentDomain.FirstChanceException += observer;
        }
        internal TaskCompletionSource<ThreadInterruptedException> Thrown { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Dispose() => AppDomain.CurrentDomain.FirstChanceException -= observer;
    }

    private sealed class ResourceClock(bool holdFactory = false, bool holdDisposal = false, Exception? disposalFailure = null) : TimeProvider, IDisposable
    {
        private readonly ManualResetEventSlim factoryRelease = new(!holdFactory);
        private readonly TaskCompletionSource disposalRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource timerDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly bool holdTimerDisposal = holdDisposal;
        private readonly Exception? timerDisposalFailure = disposalFailure;
        private ITimer? actualTimer;
        private int sutTimerDisposals, sutTimerDisposalExits;
        internal TaskCompletionSource FactoryEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource FactoryReturning { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource TimerDisposalEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource TimerChanged { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task ActualTimerDrain => timerDrained.Task;
        internal int SutTimerDisposals => Volatile.Read(ref sutTimerDisposals);
        internal int SutTimerDisposalExits => Volatile.Read(ref sutTimerDisposalExits);
        internal Action? UtcReading { get; set; }
        internal Action? TimestampReading { get; set; }
        public override long TimestampFrequency => TimeProvider.System.TimestampFrequency;
        public override long GetTimestamp()
        {
            TimestampReading?.Invoke();
            return TimeProvider.System.GetTimestamp();
        }
        public override DateTimeOffset GetUtcNow()
        {
            UtcReading?.Invoke();
            return TimeProvider.System.GetUtcNow();
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            actualTimer = TimeProvider.System.CreateTimer(callback, state, dueTime, period);
            FactoryEntered.TrySetResult();
            factoryRelease.Wait();
            FactoryReturning.TrySetResult();
            return new Timer(this, actualTimer);
        }
        internal void ReleaseFactory() => factoryRelease.Set();
        internal void ReleaseDisposal() => disposalRelease.TrySetResult();
        internal Task DisposeFixtureTimerAsync() => actualTimer?.DisposeAsync().AsTask() ?? Task.CompletedTask;
        public void Dispose() => factoryRelease.Dispose();

        private sealed class Timer(ResourceClock owner, ITimer actual) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                bool result = actual.Change(dueTime, period);
                owner.TimerChanged.TrySetResult();
                return result;
            }
            public async ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref owner.sutTimerDisposals);
                owner.TimerDisposalEntered.TrySetResult();
                if (owner.holdTimerDisposal) { await owner.disposalRelease.Task.ConfigureAwait(false); }
                await actual.DisposeAsync().ConfigureAwait(false);
                owner.timerDrained.TrySetResult();
                Interlocked.Increment(ref owner.sutTimerDisposalExits);
                if (owner.timerDisposalFailure is not null) { throw owner.timerDisposalFailure; }
            }
            public void Dispose() => actual.Dispose();
        }
    }

    private sealed class UnavailableOwner : IHealthCollector
    {
        private int acquisitions;
        internal int Acquisitions => Volatile.Read(ref acquisitions);
        public bool TryAcquire(out IHealthCollectionLease? lease)
        { Interlocked.Increment(ref acquisitions); lease = null; return false; }
    }

    private sealed class HeldCleanupOwner : IHealthCollector
    {
        internal HeldCleanup Disposal { get; } = new();
        public bool TryAcquire(out IHealthCollectionLease? lease) { lease = new Lease(Disposal); return true; }
        internal Task DisposeFixtureResourceAsync() { Disposal.DisposeFixtureResource(); return Task.CompletedTask; }
        private sealed class Lease(HeldCleanup disposal) : IHealthCollectionLease
        {
            public Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
                => Task.FromResult(HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded));
            public ValueTask DisposeAsync() => disposal.WaitForDisposal();
        }
    }

    private sealed class HeldCleanup : IValueTaskSource
    {
        private ManualResetValueTaskSourceCore<bool> completion;
        private readonly MemoryStream resource = new([1]);
        private int completionRequested, resourceDisposed;
        internal HeldCleanup() => completion.RunContinuationsAsynchronously = false;
        internal TaskCompletionSource ContinuationRegistered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool ResourceDisposed => Volatile.Read(ref resourceDisposed) != 0;
        internal ValueTask WaitForDisposal() => new(this, completion.Version);
        internal void Complete()
        {
            if (Interlocked.Exchange(ref completionRequested, 1) == 0) { completion.SetResult(true); }
        }
        internal void DisposeFixtureResource() => resource.Dispose();
        public void GetResult(short token)
        {
            completion.GetResult(token);
            resource.Dispose();
            Volatile.Write(ref resourceDisposed, 1);
        }
        public ValueTaskSourceStatus GetStatus(short token) => completion.GetStatus(token);
        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        {
            completion.OnCompleted(continuation, state, token, flags);
            ContinuationRegistered.TrySetResult();
        }
    }
}
