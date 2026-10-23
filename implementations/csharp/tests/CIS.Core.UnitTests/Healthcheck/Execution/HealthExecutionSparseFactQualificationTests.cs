using CIS.Healthcheck;
using CIS.Healthcheck.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthExecutionSparseFactQualificationTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 10, 22, 18, 0, 0, TimeSpan.Zero);

    [TestMethod]
    [DataRow("monitor", "monitor")]
    [DataRow("monitor-options", "options")]
    [DataRow("failures-factory", "factory")]
    [DataRow("failures-policy", "policy")]
    [DataRow("failures-options", "options")]
    [DataRow("availability-factory", "factory")]
    [DataRow("availability-policy", "policy")]
    [DataRow("availability-options", "options")]
    public async Task EveryPublicBindingOverloadRejectsItsMissingRequiredArgumentWithoutRegistering(string missing, string parameter)
    {
        await using var service = Service();
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new() { IsCritical = false, FailureThreshold = 1 });
        var options = new HealthExecutionBindingOptions { RegistrationId = Guid.NewGuid(), SuccessPublishesUsableData = true };
        var failures = new ConsecutiveFailureMonitorOptions { IsCritical = false, FailureThreshold = 1 };
        var availability = new AvailabilityMonitorOptions { IsCritical = false };
        HealthSnapshot before = service.CaptureSnapshot();

        ArgumentNullException error = missing switch
        {
            "monitor" => Assert.ThrowsExactly<ArgumentNullException>(() => HealthExecutionBinder.Bind((IHealthMonitor)null!, options)),
            "monitor-options" => Assert.ThrowsExactly<ArgumentNullException>(() => HealthExecutionBinder.Bind(monitor, null!)),
            "failures-factory" => Assert.ThrowsExactly<ArgumentNullException>(() => HealthExecutionBinder.Bind(null!, failures, options)),
            "failures-policy" => Assert.ThrowsExactly<ArgumentNullException>(() => HealthExecutionBinder.Bind(service, (ConsecutiveFailureMonitorOptions)null!, options)),
            "failures-options" => Assert.ThrowsExactly<ArgumentNullException>(() => HealthExecutionBinder.Bind(service, failures, null!)),
            "availability-factory" => Assert.ThrowsExactly<ArgumentNullException>(() => HealthExecutionBinder.Bind(null!, availability, options)),
            "availability-policy" => Assert.ThrowsExactly<ArgumentNullException>(() => HealthExecutionBinder.Bind(service, (AvailabilityMonitorOptions)null!, options)),
            "availability-options" => Assert.ThrowsExactly<ArgumentNullException>(() => HealthExecutionBinder.Bind(service, availability, null!)),
            _ => throw new ArgumentOutOfRangeException(nameof(missing))
        };

        Assert.AreEqual(parameter, error.ParamName);
        Assert.AreEqual(before.Revision, service.CaptureSnapshot().Revision);
        Assert.AreEqual(1, service.CaptureSnapshot().MonitorCounts.Active);
        GC.KeepAlive(monitor);
    }

    [TestMethod]
    [DataRow("ascii")]
    [DataRow("supplementary")]
    [DataRow("high-surrogate")]
    [DataRow("low-surrogate")]
    public async Task AvailabilityBindingValidatesItsCopiedPolicyNameBeforeCreatingAMonitor(string invalid)
    {
        await using var service = Service();
        var options = new HealthExecutionBindingOptions { RegistrationId = Guid.NewGuid(), SuccessPublishesUsableData = true };
        HealthSnapshot before = service.CaptureSnapshot();
        string name = invalid switch
        {
            "ascii" => new string('n', 129),
            "supplementary" => string.Concat(Enumerable.Repeat("\U0001F680", 129)),
            "high-surrogate" => "\uD800",
            "low-surrogate" => "\uDC00",
            _ => throw new ArgumentOutOfRangeException(nameof(invalid))
        };

        ArgumentException error = Assert.ThrowsExactly<ArgumentException>(() => HealthExecutionBinder.Bind(service,
            new AvailabilityMonitorOptions { IsCritical = false, Name = name }, options));

        Assert.AreEqual("Name", error.ParamName);
        Assert.AreEqual(before.Revision, service.CaptureSnapshot().Revision);
        Assert.AreEqual(0, service.CaptureSnapshot().MonitorCounts.Active);
        string accepted = string.Concat(Enumerable.Repeat("\U0001F680", 128));
        IHealthExecutionBinding binding = HealthExecutionBinder.Bind(service,
            new AvailabilityMonitorOptions { IsCritical = false, Name = accepted }, options);
        try
        {
            Assert.AreEqual(accepted, service.CaptureSnapshot().Monitors.Single().Name);
        }
        finally
        {
            await DisposeNoCollectionBindingAsync(binding);
        }
    }

    [TestMethod]
    [DataRow(0d)]
    [DataRow(17.25d)]
    public async Task SparseLateIntegrationFactsRetainActualSettlementDurationStatusAndAllEarlierErrors(double duration)
    {
        await using var service = Service();
        IHealthExecutionBinding binding = HealthExecutionBinder.Bind(service,
            new ConsecutiveFailureMonitorOptions { IsCritical = true, FailureThreshold = 1 }, new() { RegistrationId = Guid.NewGuid() });
        try
        {
            var original = new HealthDiagnostic("Work");
            var cleanup = new HealthDiagnostic("Cleanup");
            var integration = new HealthDiagnostic("LateObserver");
            Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(Outcome(binding.RegistrationId, 1, original)));
            Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(new HealthExecutionFact
            {
                RegistrationId = binding.RegistrationId,
                ExecutionId = 1,
                Kind = HealthExecutionFactKind.CleanupCompleted,
                OccurredAtUtc = Epoch.AddSeconds(1),
                CleanupCompletedAtUtc = Epoch.AddSeconds(1),
                CleanupStatus = HealthExecutionStepStatus.Failed,
                CleanupError = cleanup
            }));
            Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(new HealthExecutionFact
            {
                RegistrationId = binding.RegistrationId,
                ExecutionId = 1,
                Kind = HealthExecutionFactKind.Settled,
                OccurredAtUtc = Epoch.AddSeconds(2),
                SettledAtUtc = Epoch.AddSeconds(2),
                TotalDurationMilliseconds = duration,
                ErrorHandlingStatus = HealthExecutionStepStatus.NotApplicable,
                CleanupStatus = HealthExecutionStepStatus.Failed
            }));
            HealthSnapshot settled = service.CaptureSnapshot();

            Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(new HealthExecutionFact
            { RegistrationId = binding.RegistrationId, ExecutionId = 1, Kind = HealthExecutionFactKind.IntegrationFault, OccurredAtUtc = Epoch.AddSeconds(3), IntegrationError = integration }));

            HealthMonitorSnapshot monitor = service.CaptureSnapshot().Monitors.Single();
            HealthExecutionSnapshot execution = monitor.Outcomes!.Execution!;
            Assert.AreEqual(duration, execution.TotalDurationMilliseconds);
            Assert.AreEqual(HealthExecutionPhase.Settled, execution.Phase);
            Assert.AreEqual(Epoch.AddSeconds(2), execution.SettledAtUtc);
            Assert.AreEqual(Epoch.AddSeconds(1), execution.CleanupCompletedAtUtc);
            Assert.AreEqual(HealthExecutionStepStatus.Failed, execution.CleanupStatus);
            Assert.AreEqual(HealthExecutionStepStatus.NotApplicable, execution.ErrorHandlingStatus);
            Assert.AreSame(original, execution.OriginalError);
            Assert.AreSame(cleanup, execution.CleanupError);
            Assert.AreSame(integration, execution.IntegrationError);
            Assert.IsNull(settled.Monitors.Single().Outcomes!.Execution!.IntegrationError);
            Assert.AreEqual(1L, monitor.Outcomes.Failed);
            Assert.AreEqual(0L, monitor.Outcomes.Successful);
            Assert.AreEqual(Epoch, monitor.LastObservationAtUtc);
            Assert.AreEqual(MonitorState.Unhealthy, monitor.State);
        }
        finally
        {
            await DisposeNoCollectionBindingAsync(binding);
        }
    }

    [TestMethod]
    public async Task EnrichingAnOlderRetainedExecutionDoesNotReplaceTheLatestMonitorRecordFromAnotherBinding()
    {
        await using var service = Service();
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new() { IsCritical = false, FailureThreshold = 1 });
        IHealthExecutionBinding first = HealthExecutionBinder.Bind(monitor, new() { RegistrationId = Guid.NewGuid() });
        IHealthExecutionBinding second = HealthExecutionBinder.Bind(monitor, new() { RegistrationId = Guid.NewGuid() });
        try
        {
            Assert.AreEqual(HealthFactDisposition.Recorded, first.Report(Outcome(first.RegistrationId, 1)));
            Assert.AreEqual(HealthFactDisposition.Recorded, first.Report(Outcome(first.RegistrationId, 2)));
            Assert.AreEqual(HealthFactDisposition.Recorded, second.Report(Outcome(second.RegistrationId, 1)));
            HealthSnapshot before = service.CaptureSnapshot();

            Assert.AreEqual(HealthFactDisposition.Recorded, first.Report(new HealthExecutionFact
            {
                RegistrationId = first.RegistrationId,
                ExecutionId = 1,
                Kind = HealthExecutionFactKind.CleanupCompleted,
                OccurredAtUtc = Epoch.AddSeconds(1),
                CleanupCompletedAtUtc = Epoch.AddSeconds(1),
                CleanupStatus = HealthExecutionStepStatus.Succeeded
            }));

            HealthMonitorSnapshot after = service.CaptureSnapshot().Monitors.Single();
            Assert.AreEqual(second.RegistrationId, after.Outcomes!.Execution!.RegistrationId);
            Assert.AreEqual(1L, after.Outcomes.Execution.ExecutionId);
            Assert.AreEqual(3L, after.Outcomes.Failed);
            Assert.IsNull(after.Outcomes.Execution.CleanupCompletedAtUtc);
            Assert.AreSame(before.Monitors.Single().Outcomes!.Execution, after.Outcomes.Execution);
            GC.KeepAlive(monitor);
        }
        finally
        {
            await Task.WhenAll(DisposeNoCollectionBindingAsync(first), DisposeNoCollectionBindingAsync(second));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task TheStateReceivingBoundaryRejectsAnExecutionOlderThanBothRetainedRecordsWithoutAnOutcome(bool outcome)
    {
        await using var service = Service();
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
        IHealthExecutionState state = ((IHealthStateAccess)service).ExecutionState;
        BindingTarget target = state.Bind(BindingRequest.ForMonitor(monitor.MonitorId, new() { RegistrationId = Guid.NewGuid() }));
        try
        {
            foreach (long id in new[] { 2L, 3L, 4L })
            { Assert.AreEqual(HealthFactDisposition.Recorded, state.UpdateLifecycle(target, Started(target.RegistrationId, id))); }
            HealthSnapshot before = service.CaptureSnapshot();

            HealthFactDisposition disposition = outcome ? state.ApplyOutcome(target, Outcome(target.RegistrationId, 1))
                : state.UpdateLifecycle(target, Started(target.RegistrationId, 1));

            Assert.AreEqual(HealthFactDisposition.Obsolete, disposition);
            HealthSnapshot after = service.CaptureSnapshot();
            Assert.AreEqual(before.Revision, after.Revision);
            Assert.AreSame(before.Monitors.Single().Outcomes!.Execution, after.Monitors.Single().Outcomes!.Execution);
            Assert.AreEqual(4L, after.Monitors.Single().Outcomes!.Execution!.ExecutionId);
            Assert.AreEqual(0L, after.Monitors.Single().Outcomes!.Failed);
            Assert.AreEqual(MonitorState.Pending, after.Monitors.Single().State);
            Assert.IsNull(after.Monitors.Single().LastObservationAtUtc);
        }
        finally { await state.ReleaseBinding(target); GC.KeepAlive(monitor); }
    }

    private static HealthcheckService Service() => new(new(), new HealthApplicationIdentity { Name = "sparse execution facts", InstanceId = Guid.NewGuid() });
    private static HealthExecutionFact Outcome(Guid registration, long execution, HealthDiagnostic? diagnostic = null) => new()
    {
        RegistrationId = registration,
        ExecutionId = execution,
        Kind = HealthExecutionFactKind.Outcome,
        OccurredAtUtc = Epoch,
        SourcePhase = HealthExecutionPhase.Callback,
        Outcome = HealthExecutionOutcome.Failed,
        WorkCompletedAtUtc = Epoch,
        OriginalError = diagnostic
    };
    private static HealthExecutionFact Started(Guid registration, long execution) => new()
    { RegistrationId = registration, ExecutionId = execution, Kind = HealthExecutionFactKind.CallbackStarted, OccurredAtUtc = Epoch, CallbackStartedAtUtc = Epoch };

    private static async Task DisposeNoCollectionBindingAsync(IHealthExecutionBinding binding)
    {
        Task disposal = binding.DisposeAsync().AsTask();
        if (disposal.IsCompleted) { await disposal; }
    }
}
