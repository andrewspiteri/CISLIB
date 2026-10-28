using CIS.Healthcheck;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthCollectorRunnerValidationReasonTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(3);

    [TestMethod]
    [DataRow(0L)]
    [DataRow(-1L)]
    public Task AnInvalidClockFrequencyExplainsWhyTheServiceCannotSupplyTheRunner(long frequency)
        => RunAsync(scope =>
        {
            ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => _ = scope.Runner);
            AssertArgumentReason(error, "service");
            Assert.IsFalse(scope.Service.CaptureSnapshot().Application.IsStopping);
        }, frequency);

    [TestMethod]
    public Task AForeignMonitorRejectionExplainsTheRejectedInputWithoutAttachingIt()
        => RunAsync(scope =>
        {
            HealthCollectorRunner runner = scope.Runner;
            IConsecutiveFailureMonitor foreign = scope.CreateForeignMonitor();
            HealthSnapshot before = scope.Service.CaptureSnapshot();

            ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() =>
                runner.Register(foreign, new MalformedOwner(0), new()));

            AssertArgumentReason(error, "monitor");
            HealthSnapshot after = scope.Service.CaptureSnapshot();
            Assert.AreEqual(before.Revision, after.Revision);
            Assert.IsNull(after.Monitors.Single().Collection);
            foreign.ReportSuccess();
        });

    [TestMethod]
    [DataRow(0d)]
    [DataRow(-1d)]
    [DataRow(4294967295d)]
    public Task AnInvalidIntervalExplainsTheRejectedOptionsWithoutPartialRegistration(double milliseconds)
        => RunAsync(scope =>
        {
            HealthCollectorRunner runner = scope.Runner;
            IConsecutiveFailureMonitor monitor = scope.Monitor;
            HealthSnapshot before = scope.Service.CaptureSnapshot();

            ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() =>
                runner.Register(monitor, new MalformedOwner(0), new() { Interval = TimeSpan.FromMilliseconds(milliseconds) }));

            AssertArgumentReason(error, "options");
            HealthSnapshot after = scope.Service.CaptureSnapshot();
            Assert.AreEqual(before.Revision, after.Revision);
            Assert.IsNull(after.Monitors.Single().Collection);
        });

    [TestMethod]
    public Task AClockRangeRejectionExplainsTheUnrepresentableIntervalWithoutAttachingIt()
        => RunAsync(scope =>
        {
            ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() =>
                scope.Runner.Register(scope.Monitor, new MalformedOwner(0), new() { Interval = TimeSpan.FromSeconds(2) }));

            AssertArgumentReason(error, "options");
            Assert.IsNull(scope.Service.CaptureSnapshot().Monitors.Single().Collection);
        }, long.MaxValue);

    [TestMethod]
    [DataRow("start")]
    [DataRow("register")]
    public Task StoppedRunnerAdmissionRetainsAModuleReason(string operation)
        => RunAsync(async scope =>
        {
            Task stop = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => scope.Runner.StopAsync());
            scope.Callers.Add(stop);
            await stop.WaitAsync(Deadline);
            InvalidOperationException error = operation == "start"
                ? Assert.ThrowsExactly<InvalidOperationException>(() => _ = scope.Runner.StartAsync())
                : Assert.ThrowsExactly<InvalidOperationException>(() => scope.Runner.Register(scope.Monitor, new MalformedOwner(0), new()));

            AssertModuleReason(error.Message);
            Assert.AreNotEqual(new InvalidOperationException().Message, error.Message,
                "The rejection must explain the module condition rather than expose only the runtime's generic exception fallback.");
            Assert.IsFalse(scope.Service.CaptureSnapshot().Application.IsStopping);
        });

    [TestMethod]
    [DataRow("activate")]
    [DataRow("collect")]
    public Task WorkRequestedBeforeStartExplainsTheMissingRunnerLifecycleStep(string operation)
        => RunAsync(scope =>
        {
            var owner = new MalformedOwner(0);
            IHealthCollectionRegistration registration = scope.Register(owner, TimeSpan.FromSeconds(1));
            InvalidOperationException error = operation == "activate"
                ? Assert.ThrowsExactly<InvalidOperationException>(registration.ActivatePeriodic)
                : Assert.ThrowsExactly<InvalidOperationException>(() => _ = registration.CollectAsync());

            AssertModuleReason(error.Message);
            Assert.AreNotEqual(new InvalidOperationException().Message, error.Message);
            Assert.AreEqual(0, owner.Acquisitions);
            Assert.IsFalse(scope.Service.CaptureSnapshot().Monitors.Single().Collection!.InProgress);
        });

    [TestMethod]
    public Task PeriodicActivationOfAManualRegistrationExplainsTheMissingInterval()
        => RunAsync(async scope =>
        {
            var owner = new MalformedOwner(0);
            IHealthCollectionRegistration registration = scope.Register(owner);
            await scope.StartAsync();

            InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(registration.ActivatePeriodic);

            AssertModuleReason(error.Message);
            Assert.AreNotEqual(new InvalidOperationException().Message, error.Message);
            Assert.AreEqual(0, owner.Acquisitions);
            Assert.IsFalse(scope.Service.CaptureSnapshot().Monitors.Single().Collection!.InProgress);
        });

    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public Task AnInconsistentAcquisitionKeepsItsProtocolCategoryAndModuleReason(int fault)
        => RunAsync(async scope =>
        {
            var owner = new MalformedOwner(fault);
            IHealthCollectionRegistration registration = scope.Register(owner);
            await scope.StartAsync();
            HealthCollectionAttempt attempt = await scope.CollectAsync(registration);

            Assert.AreEqual(HealthCollectionDisposition.Committed, attempt.Disposition);
            Assert.IsFalse(attempt.EvaluationFailed);
            HealthMonitorSnapshot snapshot = scope.Service.CaptureSnapshot().Monitors.Single();
            HealthDiagnostic? diagnostic = snapshot.Collection!.LastError;
            Assert.IsNotNull(diagnostic);
            Assert.AreEqual("Healthcheck.LeaseProtocol", diagnostic.Code);
            AssertModuleReason(diagnostic.Message);
            Assert.IsNull(diagnostic.ExceptionType, "This protocol diagnostic is created by the module, not projected from an unrelated constructor failure.");
            Assert.AreEqual(0L, snapshot.Outcomes!.Successful);
            Assert.AreEqual(0L, snapshot.Outcomes.Failed);
            Assert.AreEqual(1, owner.Acquisitions);
            Assert.AreEqual(0, owner.Evaluations);
            Assert.AreEqual(fault == 1 ? 1 : 0, owner.Cleanups);
        });

    [TestMethod]
    public Task AnAbsentEvaluationTaskKeepsItsEvaluationCategoryAndModuleReason()
        => RunAsync(async scope =>
        {
            var owner = new MalformedOwner(2);
            IHealthCollectionRegistration registration = scope.Register(owner);
            await scope.StartAsync();
            HealthCollectionAttempt attempt = await scope.CollectAsync(registration);

            Assert.AreEqual(HealthCollectionDisposition.Committed, attempt.Disposition);
            Assert.IsTrue(attempt.EvaluationFailed);
            Assert.IsNotNull(attempt.Diagnostic);
            Assert.AreEqual("Healthcheck.CollectionEvaluation", attempt.Diagnostic.Code);
            Assert.AreEqual(typeof(InvalidOperationException).FullName, attempt.Diagnostic.ExceptionType);
            AssertModuleReason(attempt.Diagnostic.Message);
            Assert.AreNotEqual(new InvalidOperationException().Message, attempt.Diagnostic.Message);
            Assert.AreNotEqual(HealthDiagnostic.FromException(new InvalidOperationException(string.Empty),
                "Healthcheck.CollectionEvaluation").Message, attempt.Diagnostic.Message,
                "The module's absent-task reason must remain distinguishable from a detached exception with no supplied reason.");
            HealthMonitorSnapshot snapshot = scope.Service.CaptureSnapshot().Monitors.Single();
            Assert.AreSame(attempt.Diagnostic, snapshot.Collection!.LastError);
            Assert.AreEqual(0L, snapshot.Outcomes!.Successful);
            Assert.AreEqual(1L, snapshot.Outcomes.Failed);
            Assert.AreEqual(1, owner.Acquisitions);
            Assert.AreEqual(1, owner.Evaluations);
            Assert.AreEqual(1, owner.Cleanups);
        });

    private static void AssertArgumentReason(ArgumentException error, string parameter)
    {
        Assert.AreEqual(parameter, error.ParamName);
        AssertModuleReason(error.Message);
        // Generate baselines using this runtime and culture. Neither parameter-only
        // text nor a generic framework fallback explains the rejected module input.
        Assert.AreNotEqual(new ArgumentException(string.Empty, parameter).Message, error.Message);
        Assert.AreNotEqual(new ArgumentException(null, parameter).Message, error.Message);
    }

    private static void AssertModuleReason(string? message) => Assert.IsFalse(string.IsNullOrWhiteSpace(message),
        "A reason generated by the module for this known invalid operation must remain available to its caller.");

    private static Task RunAsync(Action<ReasonScope> body, long frequency = TimeSpan.TicksPerSecond)
        => RunAsync(scope => { body(scope); return Task.CompletedTask; }, frequency);

    private static async Task RunAsync(Func<ReasonScope, Task> body, long frequency = TimeSpan.TicksPerSecond)
    {
        var scope = new ReasonScope(frequency);
        Exception? primary = null;
        try { await body(scope); }
        catch (Exception error) { primary = error; throw; }
        finally { await scope.DrainAsync(primary); }
    }

    private sealed class ReasonScope(long frequency)
    {
        private readonly List<IHealthCollectionRegistration> registrations = [];
        private readonly List<HealthcheckService> foreignServices = [];
        private readonly List<IConsecutiveFailureMonitor> foreignMonitors = [];
        private HealthCollectorRunner? runner;
        internal HealthcheckService Service { get; } = new(new(), timeProvider: new FixedClock(frequency));
        private IConsecutiveFailureMonitor? monitor;
        internal IConsecutiveFailureMonitor Monitor => monitor ??= Service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
        internal HealthCollectorRunner Runner => runner ??= new(Service);
        internal List<Task> Callers { get; } = [];
        internal IConsecutiveFailureMonitor CreateForeignMonitor()
        {
            var service = new HealthcheckService(new(), timeProvider: new FixedClock(TimeSpan.TicksPerSecond));
            foreignServices.Add(service);
            IConsecutiveFailureMonitor result = service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
            foreignMonitors.Add(result);
            _ = Monitor;
            return result;
        }
        internal IHealthCollectionRegistration Register(IHealthCollector owner, TimeSpan? interval = null)
        {
            IHealthCollectionRegistration registration = Runner.Register(Monitor, owner, new() { Interval = interval });
            registrations.Add(registration);
            return registration;
        }
        internal async Task StartAsync()
        {
            Task actual = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => Runner.StartAsync());
            Callers.Add(actual);
            await actual.WaitAsync(Deadline);
        }
        internal async Task<HealthCollectionAttempt> CollectAsync(IHealthCollectionRegistration registration)
        {
            Task<HealthCollectionAttempt> actual = await HealthCollectorTestCleanup.EnterAsync(Deadline, () => registration.CollectAsync());
            Callers.Add(actual);
            return await actual.WaitAsync(Deadline);
        }
        internal async Task DrainAsync(Exception? primary)
        {
            var drains = new List<Func<Task>>();
            foreach (IHealthCollectionRegistration registration in registrations) { drains.Add(() => registration.DisposeAsync().AsTask()); }
            drains.Add(() => runner?.DisposeAsync().AsTask() ?? Task.CompletedTask);
            if (monitor is not null) { drains.Add(() => monitor.DisposeAsync().AsTask()); }
            drains.Add(() => Service.DisposeAsync().AsTask());
            foreach (HealthcheckService service in foreignServices) { drains.Add(() => service.DisposeAsync().AsTask()); }
            await HealthCollectorTestCleanup.DrainAsync(Deadline, primary, Callers, drains.ToArray());
            GC.KeepAlive(foreignMonitors);
        }
    }

    private sealed class FixedClock(long frequency) : TimeProvider
    {
        public override long TimestampFrequency => frequency;
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

    private sealed class MalformedOwner(int fault) : IHealthCollector
    {
        private int acquisitions, evaluations, cleanups;
        internal int Acquisitions => Volatile.Read(ref acquisitions);
        internal int Evaluations => Volatile.Read(ref evaluations);
        internal int Cleanups => Volatile.Read(ref cleanups);
        public bool TryAcquire(out IHealthCollectionLease? lease)
        {
            Interlocked.Increment(ref acquisitions);
            lease = fault == 0 ? null : new Lease(this);
            return fault != 1;
        }
        private sealed class Lease(MalformedOwner owner) : IHealthCollectionLease
        {
            public Task<HealthCollectionResult> CollectAsync(CancellationToken lifetimeCancellation)
            { Interlocked.Increment(ref owner.evaluations); return null!; }
            public ValueTask DisposeAsync()
            { Interlocked.Increment(ref owner.cleanups); return ValueTask.CompletedTask; }
        }
    }
}
