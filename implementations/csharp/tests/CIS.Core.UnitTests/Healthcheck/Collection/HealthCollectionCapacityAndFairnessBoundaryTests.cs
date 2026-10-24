using System.Threading.Channels;
using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectionCapacityAndFairnessBoundaryTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task FullMetricCapacityDistinguishesUpdatesFromEntirelyNewKeys(bool required, bool onlyNew)
    {
        var service = Service();
        IConditionMonitor? monitor = null;
        HealthCollectorRunner? runner = null;
        IHealthCollectionRegistration? registration = null;
        try
        {
            monitor = service.CreateCondition(new() { IsCritical = true, InitiallyFlagged = false });
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
            registration = runner.Register(monitor, new FixedOwner(result), new() { IsRequired = required });
            await runner.StartAsync().WaitAsync(Deadline);

            HealthCollectionAttempt attempt = await registration.CollectAsync().WaitAsync(Deadline);

            HealthMonitorSnapshot snapshot = service.CaptureSnapshot().Monitors.Single();
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
            GC.KeepAlive(monitor);
        }
        finally
        {
            await DrainAsync(service, runner, registration is null ? [] : [registration], monitor is null ? [] : [monitor]);
        }
    }

    [TestMethod]
    public async Task RemovingTheNextQueuedPeriodicRegistrationDoesNotServeThePreviousHotOwnerAgainFirst()
    {
        var service = Service(new AdvancingClock());
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
            await runner.StartAsync().WaitAsync(Deadline);
            registrations[0].ActivatePeriodic();
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
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Increment(ref timestamp);
    }

    private sealed class FixedOwner(HealthCollectionResult result) : IHealthCollector
    {
        public bool TryAcquire(out IHealthCollectionLease? lease) { lease = new Lease(result); return true; }
        private sealed class Lease(HealthCollectionResult result) : IHealthCollectionLease
        {
            public Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation) => Task.FromResult(result);
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
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
