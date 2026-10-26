using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectionCapacityAndFairnessBoundaryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task FullMetricCapacityDistinguishesUpdatesFromEntirelyNewKeys()
    {
        CapacityStartupOwnershipPrerequisite.AssertSettledStart();
        foreach ((bool required, bool onlyNew) in new[] { (false, false), (false, true), (true, false), (true, true) })
        {
            CapacityObservation observation = RunAndReleaseCapacityScenario(required, onlyNew);
            AssertCapacityOwnersReleased(observation);
            await AssertCapacityObservationAsync(observation, required, onlyNew);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static CapacityObservation RunAndReleaseCapacityScenario(bool required, bool onlyNew)
        => RunCapacityScenarioAsync(required, onlyNew).GetAwaiter().GetResult();

    private static async Task<CapacityObservation> RunCapacityScenarioAsync(bool required, bool onlyNew)
    {
        var clock = new CompletionPhaseClock();
        var service = Service(clock);
        List<WeakReference> owners = [new(clock), new(service)];
        IConditionMonitor? monitor = null;
        HealthCollectorRunner? runner = null;
        IHealthCollectionRegistration? registration = null;
        FixedOwner? owner = null;
        Task<HealthCollectionAttempt>? queued = null;
        Task<HealthCollectionAttempt>? operation = null;
        Task? startup = null;
        Task? cleanup = null;
        Task[] actualDrains = [];
        List<Task> publicDrains = [];
        Exception? primary = null;
        try
        {
            monitor = service.CreateCondition(new() { IsCritical = true, InitiallyFlagged = false });
            owners.Add(new(monitor));
            DateTimeOffset beforeAt = DateTimeOffset.UtcNow;
            for (int index = 0; index < 32; index++)
            { monitor.ReportMeasurement(HealthMeasurement.Integer(Key(index), index, beforeAt)); }
            HealthSnapshot before = service.CaptureSnapshot();
            DateTimeOffset afterAt = beforeAt.AddSeconds(1);
            HealthMeasurement[] measurements = onlyNew ? [HealthMeasurement.Integer("new-only", 999, afterAt)]
                : Enumerable.Range(0, 32).Select(index => HealthMeasurement.Integer(Key(index), 1_000 + index, afterAt)).ToArray();
            HealthCollectionResult result = required ? HealthCollectionResult.Condition(true).WithMeasurements(measurements)
                : HealthCollectionResult.Diagnostics(measurements);
            runner = new HealthCollectorRunner(service);
            owners.Add(new(runner));
            owner = new FixedOwner(result, clock);
            owners.Add(new(owner));
            registration = runner.Register(monitor, owner, new() { IsRequired = required });
            owners.Add(new(registration));
            startup = runner.StartAsync();
            await startup.WaitAsync(Deadline);

            queued = registration.CollectAsync();
            await Task.WhenAny(owner.Entered.Task, queued).WaitAsync(Deadline);
            if (queued.IsCompleted)
            {
                await queued;
                Assert.Fail("The manual collection cannot complete before its actual evaluation is released.");
            }
            await owner.Entered.Task.WaitAsync(Deadline);
            operation = registration.CollectAsync();
            owner.Release();
            // The real lease arms only its own execution flow. In the pinned
            // runner the next timestamp on that flow follows actual state
            // settlement. Observe it before any withdrawal can repair a
            // missing finalization; cleanup cannot satisfy this body wait.
            await clock.AfterActualSettlement.Task.WaitAsync(Deadline);

            HealthMonitorSnapshot snapshot = service.CaptureSnapshot().Monitors.Single();
            await CleanupAsync();
            // Only detached snapshots, original notification Tasks and weak
            // references escape this completed async task/NoInlining capsule.
            return new(queued, operation, snapshot, before, beforeAt, afterAt, owners.ToArray());
        }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            owner?.Release();
            List<Exception> errors = primary is null ? [] : [primary];
            Task ownedCleanup = CleanupAsync();
            try { await ownedCleanup; }
            catch (Exception error) { RecordTaskError(ownedCleanup, error); }
            foreach (Task actual in actualDrains.Concat(publicDrains).Concat(startup is null ? [] : new[] { startup }))
            {
                if (!actual.IsCompleted) { continue; }
                try { await actual; }
                catch (Exception error) { RecordTaskError(actual, error); }
            }
            foreach (Task caller in new Task?[] { queued, operation }.OfType<Task>())
            {
                if (!caller.IsCompleted) { continue; }
                try { await caller; }
                catch (Exception error) { RecordTaskError(caller, error); }
            }
            GC.KeepAlive(monitor);
            if (errors.Count == 1) { ExceptionDispatchInfo.Capture(errors[0]).Throw(); }
            if (errors.Count > 1) { throw new AggregateException("The actual capacity scenario, caller or cleanup failed.", errors); }

            void RecordTaskError(Task actual, Exception error)
            {
                if (error is TimeoutException) { Add(error); }
                if (actual.Exception is { } aggregate) { foreach (Exception inner in aggregate.InnerExceptions) { Add(inner); } }
                else { Add(error); }
            }
            void Add(Exception error) { if (!errors.Any(existing => ReferenceEquals(existing, error))) { errors.Add(error); } }
        }

        Task CleanupAsync() => cleanup ??= DrainOnceAsync();
        async Task DrainOnceAsync()
        {
            // Own each actual public shutdown once. Re-entering finally observes this same
            // bounded attempt; an incomplete shutdown remains a timeout, never settlement.
            Task collector = DrainRunnerAsync();
            Task state = DrainServiceAsync();
            Task association = DrainRegistrationAsync();
            Task source = DrainMonitorAsync();
            // Public caller notifications are checked after these actual
            // resource barriers; a lost notification cannot fence itself.
            actualDrains = [collector, state, association, source];
            await Task.WhenAll(actualDrains).WaitAsync(Deadline);
        }
        async Task DrainRunnerAsync()
        {
            if (runner is null) { return; }
            Task actual = runner.DisposeAsync().AsTask();
            publicDrains.Add(actual);
            await actual;
        }
        async Task DrainServiceAsync()
        {
            Task actual = service.DisposeAsync().AsTask();
            publicDrains.Add(actual);
            await actual;
        }
        async Task DrainRegistrationAsync()
        {
            if (registration is null) { return; }
            Task actual = registration.DisposeAsync().AsTask();
            publicDrains.Add(actual);
            await actual;
        }
        async Task DrainMonitorAsync()
        {
            if (monitor is null) { return; }
            Task actual = monitor.DisposeAsync().AsTask();
            publicDrains.Add(actual);
            await actual;
        }
    }

    private sealed record CapacityObservation(Task<HealthCollectionAttempt> Queued, Task<HealthCollectionAttempt> Follower,
        HealthMonitorSnapshot Snapshot, HealthSnapshot Before, DateTimeOffset BeforeAt, DateTimeOffset AfterAt, WeakReference[] Owners);

    private static void AssertCapacityOwnersReleased(CapacityObservation observation)
    {
        List<Exception> errors = [];
        try
        {
            var elapsed = Stopwatch.StartNew();
            while (observation.Owners.Any(owner => owner.IsAlive) && elapsed.Elapsed < Deadline)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Thread.Sleep(1);
            }
            if (observation.Owners.Any(owner => owner.IsAlive))
            {
                throw new InvalidOperationException("The actual capacity execution owner graph remains live after successful resource cleanup.");
            }
        }
        catch (Exception error) { errors.Add(error); }
        // Preserve original caller errors even if owner death itself failed.
        // Never wait for a pending notification while observing those errors.
        foreach (Task caller in new Task[] { observation.Queued, observation.Follower })
        {
            if (!caller.IsCompleted) { continue; }
            try { caller.GetAwaiter().GetResult(); }
            catch (Exception error)
            {
                if (caller.Exception is { } aggregate) { foreach (Exception inner in aggregate.InnerExceptions) { Add(inner); } }
                else { Add(error); }
            }
        }
        if (errors.Count == 1) { ExceptionDispatchInfo.Capture(errors[0]).Throw(); }
        if (errors.Count > 1) { throw new AggregateException("Actual capacity owner or original caller failures remain unresolved.", errors); }
        void Add(Exception error) { if (!errors.Any(existing => ReferenceEquals(existing, error))) { errors.Add(error); } }
    }

    private static async Task AssertCapacityObservationAsync(CapacityObservation observation, bool required, bool onlyNew)
    {
        await AssertOriginalCallersCompleteAsync(observation.Queued, observation.Follower);
        HealthCollectionAttempt attempt = await observation.Queued;
        HealthMonitorSnapshot snapshot = observation.Snapshot;
        HealthSnapshot before = observation.Before;
        DateTimeOffset beforeAt = observation.BeforeAt, afterAt = observation.AfterAt;
        Assert.AreEqual(HealthCollectionDisposition.Committed, attempt.Disposition);
        Assert.AreEqual(onlyNew && !required, attempt.EvaluationFailed);
        Assert.AreEqual(32, snapshot.Measurements.Count);
        Assert.AreEqual(required ? MonitorState.Unhealthy : MonitorState.Healthy, snapshot.State);
        Assert.IsFalse(snapshot.Measurements.Any(measurement => measurement.Key == "new-only"));
        foreach (int index in Enumerable.Range(0, 32))
        {
            HealthMeasurement observed = snapshot.Measurements.Single(measurement => measurement.Key == Key(index));
            Assert.AreEqual(onlyNew ? (long)index : 1_000L + index, observed.IntegerValue);
            Assert.AreEqual(onlyNew ? beforeAt : afterAt, observed.ObservedAtUtc);
        }
        if (onlyNew)
        {
            Assert.IsNotNull(attempt.Diagnostic);
            Assert.AreEqual("Healthcheck.MeasurementCapacity", attempt.Diagnostic!.Code);
            Assert.AreSame(attempt.Diagnostic, snapshot.Collection!.LastError);
        }
        else
        {
            Assert.IsNull(attempt.Diagnostic);
            Assert.IsNull(snapshot.Collection!.LastError);
        }
        if (!required) { Assert.AreEqual(before.Monitors.Single().LastObservationAtUtc, snapshot.LastObservationAtUtc); }
        Assert.IsTrue(before.Monitors.Single().Measurements.All(measurement => measurement.ObservedAtUtc == beforeAt));
    }

    [TestMethod]
    public async Task RemovingTheNextQueuedPeriodicRegistrationDoesNotServeThePreviousHotOwnerAgainFirst()
    {
        CapacityStartupOwnershipPrerequisite.AssertSettledStart();
        var clock = new AdvancingClock();
        var service = Service(clock);
        HealthCollectorRunner? runner = null;
        var entered = Channel.CreateUnbounded<OwnerProgress>(new UnboundedChannelOptions { SingleReader = true });
        var monitors = new List<IConditionMonitor>();
        var owners = new List<GatedOwner>();
        var registrations = new List<IHealthCollectionRegistration>();
        try
        {
            runner = new HealthCollectorRunner(service);
            foreach (string name in new[] { "A", "B", "C", "D" })
            {
                IConditionMonitor monitor = service.CreateCondition(new() { IsCritical = false, InitiallyFlagged = false, Name = name });
                var owner = new GatedOwner(name, entered.Writer);
                monitors.Add(monitor);
                owners.Add(owner);
                registrations.Add(runner.Register(monitor, owner, new() { Interval = TimeSpan.FromTicks(1) }));
            }
            Task<TimeSpan> startupChange = clock.NextChangeAsync();
            await runner.StartAsync().WaitAsync(Deadline);
            await startupChange;
            foreach (GatedOwner owner in owners)
            {
                Assert.AreEqual(0, owner.Acquisitions, "The actual startup dispatcher must leave every unactivated periodic owner inert.");
            }
            Task<TimeSpan> activatedChange = clock.NextChangeAsync();
            registrations[0].ActivatePeriodic();
            await activatedChange;
            Assert.AreEqual(1, owners[0].Acquisitions,
                "The actual activation dispatcher must acquire A before installing its next idle timer request.");
            Invocation first = await NextInvocationAsync(entered.Reader);
            Assert.AreEqual("A", first.Name);
            for (int index = 1; index < registrations.Count; index++) { registrations[index].ActivatePeriodic(); }
            // A owns the sole actual slot, so B remains the next queued position. Its removal must not
            // rewind the scan to A. The public monotonic clock advances by one tick on each read, so
            // completion+one tick is certainly due at the following pump observation.
            registrations[1].Withdraw();
            first.Release.TrySetResult();

            foreach (string expected in new[] { "C", "D" })
            {
                Invocation next = await NextInvocationAsync(entered.Reader);
                Assert.AreEqual(expected, next.Name, "An untouched queued owner must be served before the previous hot owner repeats.");
                next.Release.TrySetResult();
            }
            Invocation repeated = await NextInvocationAsync(entered.Reader);
            Assert.AreEqual("A", repeated.Name);
            Assert.AreEqual(0, owners[1].Acquisitions);
            Assert.AreEqual(2, owners[0].Acquisitions);
            Assert.AreEqual(1, owners[2].Acquisitions);
            Assert.AreEqual(1, owners[3].Acquisitions);
        }
        finally
        {
            Task drain = DrainAsync(service, runner, registrations, monitors);
            foreach (GatedOwner owner in owners) { owner.ReleaseAll(); }
            await drain;
            GC.KeepAlive(monitors);
        }
    }

    private static async Task DrainAsync(HealthcheckService service, HealthCollectorRunner? runner,
        IEnumerable<IHealthCollectionRegistration> registrations, IEnumerable<IHealthMonitor> monitors)
    {
        // Start every real shutdown even if another resource cannot settle. The
        // outer watchdog is a genuine timeout, never an assertion or settlement.
        Task runnerDrain = DrainRunnerAsync();
        Task serviceDrain = DrainServiceAsync();
        Task[] registrationDrains = registrations.Select(DrainRegistrationAsync).ToArray();
        Task[] monitorDrains = monitors.Select(DrainMonitorAsync).ToArray();
        await Task.WhenAll([runnerDrain, serviceDrain, .. registrationDrains, .. monitorDrains]).WaitAsync(Deadline);

        async Task DrainRunnerAsync() { if (runner is not null) { await runner.DisposeAsync(); } }
        async Task DrainServiceAsync() => await service.DisposeAsync();
        static async Task DrainRegistrationAsync(IHealthCollectionRegistration registration) => await registration.DisposeAsync();
        static async Task DrainMonitorAsync(IHealthMonitor monitor) => await monitor.DisposeAsync();
    }

    private static string Key(int index) => "key-" + index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
    private static HealthcheckService Service(TimeProvider? clock = null) => new(new() { MaxConcurrentCollections = 1 },
        new HealthApplicationIdentity { Name = "capacity and fair removal", InstanceId = Guid.NewGuid() }, clock);

    private sealed class AdvancingClock : TimeProvider
    {
        private long timestamp;
        private TaskCompletionSource<TimeSpan>? nextChange;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Increment(ref timestamp);
        internal Task<TimeSpan> NextChangeAsync()
        {
            var next = new TaskCompletionSource<TimeSpan>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (Interlocked.CompareExchange(ref nextChange, next, null) is not null)
            { throw new InvalidOperationException("The previous actual timer Change observation is still armed."); }
            return next.Task.WaitAsync(Deadline);
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            => new ObservedTimer(base.CreateTimer(callback, state, dueTime, period), this);
        private sealed class ObservedTimer(ITimer inner, AdvancingClock clock) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                bool changed = inner.Change(dueTime, period);
                Interlocked.Exchange(ref clock.nextChange, null)?.TrySetResult(dueTime);
                return changed;
            }
            public void Dispose() => inner.Dispose();
            public ValueTask DisposeAsync() => inner.DisposeAsync();
        }
    }

    private static async Task AssertOriginalCallersCompleteAsync(params Task[] callers)
    {
        List<Exception> errors = [];
        foreach (Task caller in callers)
        {
            if (!caller.IsCompleted) { continue; }
            try { await caller; }
            catch (Exception error) { if (!errors.Any(existing => ReferenceEquals(existing, error))) { errors.Add(error); } }
        }
        if (errors.Count == 1) { ExceptionDispatchInfo.Capture(errors[0]).Throw(); }
        if (errors.Count > 1) { throw new AggregateException("Original capacity callers faulted after actual ownership cleanup.", errors); }
        Assert.IsTrue(callers.All(caller => caller.IsCompleted),
            "Every original capacity caller must receive completion after actual state settlement and resource cleanup.");
    }

    private sealed class CompletionPhaseClock : TimeProvider
    {
        private readonly AsyncLocal<object?> actualLease = new();
        internal TaskCompletionSource AfterActualSettlement { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void ObserveActualLeaseDisposal(object lease)
        {
            if (actualLease.Value is not null) { throw new InvalidOperationException("An actual lease observation is already armed on this flow."); }
            actualLease.Value = lease;
        }
        public override long GetTimestamp()
        {
            long sample = base.GetTimestamp();
            if (actualLease.Value is not null)
            {
                actualLease.Value = null;
                AfterActualSettlement.TrySetResult();
            }
            return sample;
        }
    }

    private sealed class FixedOwner(HealthCollectionResult result, CompletionPhaseClock clock) : IHealthCollector
    {
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Release() => released.TrySetResult();
        private void ObserveActualLeaseDisposal(object lease) => clock.ObserveActualLeaseDisposal(lease);
        public bool TryAcquire(out IHealthCollectionLease? lease) { lease = new Lease(this, result); return true; }
        private sealed class Lease(FixedOwner owner, HealthCollectionResult result) : IHealthCollectionLease
        {
            public async Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
            {
                owner.Entered.TrySetResult();
                await owner.released.Task;
                return result;
            }
            public ValueTask DisposeAsync()
            {
                owner.ObserveActualLeaseDisposal(this);
                return ValueTask.CompletedTask;
            }
        }
    }

    private static async Task<Invocation> NextInvocationAsync(ChannelReader<OwnerProgress> progress)
    {
        OwnerProgress observed = await progress.ReadAsync().AsTask().WaitAsync(Deadline);
        Assert.IsNotNull(observed.Invocation, "The actual owner lease was disposed before its expected collection callback entered.");
        return observed.Invocation;
    }

    private sealed record OwnerProgress(Invocation? Invocation);

    private sealed class Invocation(string name)
    {
        internal string Name { get; } = name;
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class GatedOwner(string name, ChannelWriter<OwnerProgress> entered) : IHealthCollector
    {
        private readonly object gate = new();
        private readonly List<Invocation> invocations = [];
        private int acquisitions;
        private bool releasing;
        internal int Acquisitions => Volatile.Read(ref acquisitions);
        public bool TryAcquire(out IHealthCollectionLease? lease)
        { Interlocked.Increment(ref acquisitions); lease = new Lease(this); return true; }
        internal void ReleaseAll()
        { lock (gate) { releasing = true; foreach (Invocation invocation in invocations) { invocation.Release.TrySetResult(); } } }
        private async Task<HealthCollectionResult> Evaluate()
        {
            var invocation = new Invocation(name);
            lock (gate) { invocations.Add(invocation); if (releasing) { invocation.Release.TrySetResult(); } }
            if (!entered.TryWrite(new(invocation))) { throw new InvalidOperationException("The test observation channel was closed."); }
            await invocation.Release.Task;
            return HealthCollectionResult.Condition(false);
        }
        private sealed class Lease(GatedOwner owner) : IHealthCollectionLease
        {
            private bool entered;
            public Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
            {
                entered = true;
                return owner.Evaluate();
            }
            public ValueTask DisposeAsync()
            {
                if (!entered) { owner.ReportSkippedEvaluation(); }
                return ValueTask.CompletedTask;
            }
        }
        private void ReportSkippedEvaluation() => entered.TryWrite(new(null));
    }
}
