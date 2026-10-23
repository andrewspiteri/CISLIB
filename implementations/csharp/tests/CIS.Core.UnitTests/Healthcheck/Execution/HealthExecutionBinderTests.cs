using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using CIS.Healthcheck;
using CIS.Healthcheck.Internal;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthExecutionBinderTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task FactoryBindingCreatesExactlyOnePendingMonitorBeforeAnyRegistrationFact()
    {
        await using HealthcheckService service = CreateService();
        Guid registrationId = Guid.NewGuid();
        IHealthExecutionBinding binding = HealthExecutionBinder.Bind(service,
            Failures(), Options(registrationId));
        try
        {
            HealthMonitorSnapshot monitor = Only(service);
            Assert.AreEqual(registrationId, binding.RegistrationId);
            Assert.AreEqual(monitor.MonitorId, binding.MonitorId);
            Assert.AreEqual(HealthPolicyKind.ConsecutiveFailures, monitor.Policy);
            Assert.AreEqual(MonitorState.Pending, monitor.State);
            Assert.AreEqual(0L, monitor.Outcomes!.Failed);
            Assert.IsNull(monitor.Outcomes.Execution);
            Assert.IsNull(monitor.LastObservationAtUtc);

            Assert.AreEqual(HealthFactDisposition.Recorded,
                binding.Report(Outcome(registrationId, 1, HealthExecutionOutcome.Failed)));
            Assert.AreEqual(MonitorState.Unhealthy, Only(service).State);
        }
        finally
        {
            await DisposeNoCollectionBindingAsync(binding);
        }
    }

    [TestMethod]
    public async Task IdenticalNormalizedRegisteredFactIsDuplicateWithoutResettingAnOutcome()
    {
        await using HealthcheckService service = CreateService();
        Guid id = Guid.NewGuid();
        IHealthExecutionBinding binding = HealthExecutionBinder.Bind(service, Failures(), Options(id));
        try
        {
            Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(Registration(id, Epoch)));
            Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(Outcome(id, 4, HealthExecutionOutcome.Failed)));
            HealthSnapshot before = service.CaptureSnapshot();

            Assert.AreEqual(HealthFactDisposition.Duplicate, binding.Report(Registration(id, Epoch.ToOffset(TimeSpan.FromHours(2)))));

            HealthSnapshot after = service.CaptureSnapshot();
            Assert.AreEqual(before.Revision, after.Revision);
            Assert.AreEqual(1L, Only(service).Outcomes!.Failed);
            Assert.AreEqual(4L, Only(service).Outcomes!.Execution!.ExecutionId);
            Assert.ThrowsExactly<InvalidOperationException>(() => binding.Report(Registration(id, Epoch.AddTicks(1))));
            Assert.AreEqual(after.Revision, service.CaptureSnapshot().Revision);
        }
        finally
        {
            await DisposeNoCollectionBindingAsync(binding);
        }
    }

    [TestMethod]
    public async Task DuplicateActiveRegistrationRejectsFactoryCreationAtomically()
    {
        await using HealthcheckService service = CreateService(capacity: 4);
        Guid id = Guid.NewGuid();
        IHealthExecutionBinding first = HealthExecutionBinder.Bind(service, Failures(), Options(id));
        try
        {
            HealthSnapshot before = service.CaptureSnapshot();

            Assert.ThrowsExactly<InvalidOperationException>(() => HealthExecutionBinder.Bind(service,
                new AvailabilityMonitorOptions { IsCritical = true }, Options(id, usableData: true)));

            Assert.AreEqual(1, service.CaptureSnapshot().MonitorCounts.Active);
            Assert.AreEqual(before.Revision, service.CaptureSnapshot().Revision);
            Assert.AreEqual(HealthFactDisposition.Recorded, first.Report(Outcome(id, 1, HealthExecutionOutcome.Succeeded)));
            Assert.AreEqual(1L, Only(service).Outcomes!.Successful);
        }
        finally
        {
            await DisposeNoCollectionBindingAsync(first);
        }
    }

    [TestMethod]
    public async Task ConcurrentFactoryBindingsForOneRegistrationCreateOneMonitorAndOneBinding()
    {
        await using HealthcheckService service = CreateService(capacity: 32);
        Guid id = Guid.NewGuid();
        using var start = new ManualResetEventSlim();
        Task<IHealthExecutionBinding?>[] attempts = Enumerable.Range(0, 12).Select(_ => Task.Run(() =>
        {
            start.Wait();
            try { return HealthExecutionBinder.Bind(service, Failures(), Options(id)); }
            catch (InvalidOperationException) { return null; }
        })).ToArray();
        start.Set();
        IHealthExecutionBinding?[] results = await Task.WhenAll(attempts).WaitAsync(TimeSpan.FromSeconds(10));
        IHealthExecutionBinding[] successful = results.OfType<IHealthExecutionBinding>().ToArray();
        Assert.AreEqual(1, successful.Length);
        IHealthExecutionBinding binding = successful.Single();
        try
        {
            Assert.AreEqual(1, service.CaptureSnapshot().MonitorCounts.Active);
            Assert.AreEqual(binding.MonitorId, Only(service).MonitorId);
        }
        finally
        {
            await DisposeNoCollectionBindingAsync(binding);
        }
    }

    [TestMethod]
    public async Task ExistingPolicyMustBeSuitableAndAvailabilityRequiresExplicitUsableData()
    {
        await using HealthcheckService service = CreateService();
        IConditionMonitor condition = service.CreateCondition(new ConditionMonitorOptions { IsCritical = false, InitiallyFlagged = false });
        INumericThresholdMonitor numeric = service.CreateNumericThreshold(new NumericThresholdMonitorOptions
        {
            IsCritical = false,
            Comparison = NumericComparison.GreaterThan,
            Threshold = 3,
            Unit = "items"
        });
        IAvailabilityMonitor availability = service.CreateAvailability(new AvailabilityMonitorOptions { IsCritical = true });
        HealthSnapshot before = service.CaptureSnapshot();

        Assert.Throws<ArgumentException>(() => HealthExecutionBinder.Bind(condition, Options(Guid.NewGuid())));
        Assert.Throws<ArgumentException>(() => HealthExecutionBinder.Bind(numeric, Options(Guid.NewGuid())));
        Assert.Throws<ArgumentException>(() => HealthExecutionBinder.Bind(availability, Options(Guid.NewGuid())));
        Assert.AreEqual(before.Revision, service.CaptureSnapshot().Revision);

        IHealthExecutionBinding binding = HealthExecutionBinder.Bind(availability, Options(Guid.NewGuid(), usableData: true));
        try
        {
            Assert.AreEqual(availability.MonitorId, binding.MonitorId);
            GC.KeepAlive(condition);
            GC.KeepAlive(numeric);
        }
        finally
        {
            await DisposeNoCollectionBindingAsync(binding);
        }
    }

    [TestMethod]
    public async Task ForeignMonitorAndFactoryAreRejectedBeforeInvokingTheirPublicMembers()
    {
        var foreignMonitor = new ForeignMonitor();
        var foreignFactory = new ForeignFactory();
        Assert.Throws<ArgumentException>(() => HealthExecutionBinder.Bind(foreignMonitor, Options(Guid.NewGuid())));
        Assert.Throws<ArgumentException>(() => HealthExecutionBinder.Bind(foreignFactory, Failures(), Options(Guid.NewGuid())));
        Assert.Throws<ArgumentException>(() => HealthExecutionBinder.Bind(foreignFactory,
            new AvailabilityMonitorOptions { IsCritical = false }, Options(Guid.NewGuid(), usableData: true)));
        Assert.AreEqual(0, foreignMonitor.Calls);
        Assert.AreEqual(0, foreignFactory.Calls);
        await foreignMonitor.DisposeAsync();
    }

    [TestMethod]
    public async Task WithdrawnMonitorAndStoppingServiceRejectNewBindingsWithoutMutation()
    {
        await using HealthcheckService service = CreateService();
        IConsecutiveFailureMonitor withdrawn = service.CreateConsecutiveFailures(Failures());
        withdrawn.Withdraw();
        Assert.Throws<ArgumentException>(() => HealthExecutionBinder.Bind(withdrawn, Options(Guid.NewGuid())));
        service.BeginStopping();
        HealthSnapshot before = service.CaptureSnapshot();
        Assert.ThrowsExactly<InvalidOperationException>(() => HealthExecutionBinder.Bind(service, Failures(), Options(Guid.NewGuid())));
        Assert.AreEqual(before.Revision, service.CaptureSnapshot().Revision);
        Assert.AreEqual(0, service.CaptureSnapshot().MonitorCounts.Active);
    }

    [TestMethod]
    public async Task ExistingBindingAcceptsFinalOutcomeAndSettlementAfterStoppingClosesNewBindingAdmission()
    {
        await using HealthcheckService service = CreateService();
        Guid id = Guid.NewGuid();
        IHealthExecutionBinding binding = HealthExecutionBinder.Bind(service, Failures(), Options(id));
        try
        {
            service.BeginStopping();

            Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(Outcome(id, 1, HealthExecutionOutcome.Failed)));
            Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(Settled(id, 1)));
            Assert.AreEqual(1L, Only(service).Outcomes!.Failed);
            Assert.AreEqual(HealthExecutionPhase.Settled, Only(service).Outcomes!.Execution!.Phase);
            Assert.ThrowsExactly<InvalidOperationException>(() => HealthExecutionBinder.Bind(service, Failures(), Options(Guid.NewGuid())));
        }
        finally
        {
            await DisposeNoCollectionBindingAsync(binding);
        }
    }

    [TestMethod]
    public async Task NullArgumentsAreRejectedBeforeCreatingAMonitor()
    {
        await using HealthcheckService service = CreateService();
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(Failures());
        Assert.ThrowsExactly<ArgumentNullException>(() => HealthExecutionBinder.Bind((IHealthMonitor)null!, Options(Guid.NewGuid())));
        Assert.ThrowsExactly<ArgumentNullException>(() => HealthExecutionBinder.Bind(monitor, null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => HealthExecutionBinder.Bind((IHealthMonitorFactory)null!, Failures(), Options(Guid.NewGuid())));
        Assert.ThrowsExactly<ArgumentNullException>(() => HealthExecutionBinder.Bind(service, (ConsecutiveFailureMonitorOptions)null!, Options(Guid.NewGuid())));
        Assert.ThrowsExactly<ArgumentNullException>(() => HealthExecutionBinder.Bind(service, (AvailabilityMonitorOptions)null!, Options(Guid.NewGuid())));
        Assert.ThrowsExactly<ArgumentNullException>(() => HealthExecutionBinder.Bind(service, Failures(), null!));
        Assert.AreEqual(1, service.CaptureSnapshot().MonitorCounts.Active);
        GC.KeepAlive(monitor);
    }

    [TestMethod]
    [DataRow("empty-id")]
    [DataRow("zero-interval")]
    [DataRow("negative-interval")]
    [DataRow("negative-delay")]
    [DataRow("long-name")]
    [DataRow("invalid-unicode")]
    public async Task InvalidBindingMetadataDoesNotConsumeMonitorCapacity(string scenario)
    {
        await using HealthcheckService service = CreateService(capacity: 1);
        Guid id = Guid.NewGuid();
        HealthExecutionBindingOptions options = scenario switch
        {
            "empty-id" => Options(Guid.Empty),
            "zero-interval" => new() { RegistrationId = id, Interval = TimeSpan.Zero },
            "negative-interval" => new() { RegistrationId = id, Interval = TimeSpan.FromTicks(-1) },
            "negative-delay" => new() { RegistrationId = id, InitialDelay = TimeSpan.FromTicks(-1) },
            "long-name" => new() { RegistrationId = id, DisplayName = new string('x', 129) },
            "invalid-unicode" => new() { RegistrationId = id, DisplayName = "\ud800" },
            _ => throw new AssertFailedException("Unknown metadata case.")
        };
        HealthSnapshot before = service.CaptureSnapshot();
        Assert.Throws<ArgumentException>(() => HealthExecutionBinder.Bind(service, Failures(), options));
        Assert.AreEqual(before.Revision, service.CaptureSnapshot().Revision);
        Assert.AreEqual(0, service.CaptureSnapshot().MonitorCounts.Active);
        IHealthExecutionBinding binding = HealthExecutionBinder.Bind(service, Failures(), Options(id));
        try
        {
            Assert.AreEqual(1, service.CaptureSnapshot().MonitorCounts.Active);
        }
        finally
        {
            await DisposeNoCollectionBindingAsync(binding);
        }
    }

    [TestMethod]
    public async Task InvalidFactoryPolicyAndMissingAvailabilityDeclarationLeaveNoPartialBinding()
    {
        await using HealthcheckService service = CreateService(capacity: 1);
        Guid id = Guid.NewGuid();
        HealthSnapshot before = service.CaptureSnapshot();
        Assert.Throws<ArgumentException>(() => HealthExecutionBinder.Bind(service,
            new ConsecutiveFailureMonitorOptions { IsCritical = true, FailureThreshold = 0 }, Options(id)));
        Assert.Throws<ArgumentException>(() => HealthExecutionBinder.Bind(service,
            new AvailabilityMonitorOptions { IsCritical = true }, Options(id)));
        Assert.AreEqual(before.Revision, service.CaptureSnapshot().Revision);
        IHealthExecutionBinding binding = HealthExecutionBinder.Bind(service,
            new AvailabilityMonitorOptions { IsCritical = true }, Options(id, usableData: true));
        try
        {
            Assert.AreEqual(HealthPolicyKind.FirstSuccessAvailability, Only(service).Policy);
        }
        finally
        {
            await DisposeNoCollectionBindingAsync(binding);
        }
    }

    [TestMethod]
    public async Task BindingCapacityFailureDoesNotReserveTheRegistrationIdentity()
    {
        await using HealthcheckService service = CreateService(capacity: 1);
        IConsecutiveFailureMonitor full = service.CreateConsecutiveFailures(Failures());
        Guid id = Guid.NewGuid();
        Assert.ThrowsExactly<HealthcheckCapacityException>(() => HealthExecutionBinder.Bind(service, Failures(), Options(id)));
        full.Withdraw();
        IHealthExecutionBinding binding = HealthExecutionBinder.Bind(service, Failures(), Options(id));
        try
        {
            Assert.AreEqual(1, service.CaptureSnapshot().MonitorCounts.Active);
        }
        finally
        {
            await DisposeNoCollectionBindingAsync(binding);
        }
    }

    [TestMethod]
    public async Task ConsecutiveFailureBindingAppliesDistinctOutcomesOnceAndKeepsNeutralStreaks()
    {
        await using HealthcheckService service = CreateService();
        Guid id = Guid.NewGuid();
        IHealthExecutionBinding binding = HealthExecutionBinder.Bind(service, Failures(threshold: 2), Options(id));
        try
        {
            Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(Outcome(id, 2, HealthExecutionOutcome.Failed)));
            Assert.AreEqual(MonitorState.Healthy, Only(service).State);
            Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(Outcome(id, 5, HealthExecutionOutcome.Skipped, HealthExecutionPhase.Condition)));
            Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(Outcome(id, 9, HealthExecutionOutcome.ExpectedCancellation)));
            Assert.AreEqual(1L, Only(service).Outcomes!.ConsecutiveFailures);
            Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(Outcome(id, 10, HealthExecutionOutcome.Failed)));
            Assert.AreEqual(MonitorState.Unhealthy, Only(service).State);
            Assert.AreEqual(HealthFactDisposition.Duplicate, binding.Report(Outcome(id, 10, HealthExecutionOutcome.Failed)));
            Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(Outcome(id, 12, HealthExecutionOutcome.Succeeded)));

            HealthOutcomeSnapshot outcomes = Only(service).Outcomes!;
            Assert.AreEqual(2L, outcomes.Failed);
            Assert.AreEqual(1L, outcomes.Skipped);
            Assert.AreEqual(1L, outcomes.ExpectedCanceled);
            Assert.AreEqual(1L, outcomes.Successful);
            Assert.AreEqual(0L, outcomes.ConsecutiveFailures);
            Assert.AreEqual(MonitorState.Healthy, Only(service).State);
        }
        finally
        {
            await DisposeNoCollectionBindingAsync(binding);
        }
    }

    [TestMethod]
    public async Task AvailabilitySuccessPublishesDataAndLaterRefreshFailureDoesNotInvalidateIt()
    {
        await using HealthcheckService service = CreateService();
        Guid id = Guid.NewGuid();
        IHealthExecutionBinding binding = HealthExecutionBinder.Bind(service,
            new AvailabilityMonitorOptions { IsCritical = true }, Options(id, usableData: true));
        try
        {
            Assert.AreEqual(MonitorState.Pending, Only(service).State);
            binding.Report(Outcome(id, 1, HealthExecutionOutcome.Failed));
            Assert.AreEqual(MonitorState.Unhealthy, Only(service).State);
            binding.Report(Outcome(id, 2, HealthExecutionOutcome.Succeeded));
            binding.Report(Outcome(id, 3, HealthExecutionOutcome.Failed, diagnostic: new HealthDiagnostic("Refresh")));

            HealthMonitorSnapshot monitor = Only(service);
            Assert.AreEqual(MonitorState.Healthy, monitor.State);
            Assert.AreEqual(1L, monitor.Outcomes!.Successful);
            Assert.AreEqual(2L, monitor.Outcomes.Failed);
            Assert.AreEqual("Refresh", monitor.Outcomes.LastFailureDiagnostic!.Code);
        }
        finally
        {
            await DisposeNoCollectionBindingAsync(binding);
        }
    }

    [TestMethod]
    public async Task RetainedOutcomeContradictionIsBoundedAndDoesNotMutateHealthOrBlockLaterFacts()
    {
        await using HealthcheckService service = CreateService();
        Guid id = Guid.NewGuid();
        IHealthExecutionBinding binding = HealthExecutionBinder.Bind(service, Failures(), Options(id));
        try
        {
            binding.Report(Outcome(id, 7, HealthExecutionOutcome.Failed, diagnostic: new HealthDiagnostic("Original", new string('x', 1024))));
            HealthSnapshot before = service.CaptureSnapshot();

            InvalidOperationException error = Assert.ThrowsExactly<InvalidOperationException>(() =>
                binding.Report(Outcome(id, 7, HealthExecutionOutcome.Succeeded)));

            Assert.IsTrue(error.Message.Length <= 1024);
            Assert.IsFalse(error.Message.Contains(new string('x', 100), StringComparison.Ordinal));
            Assert.AreEqual(before.Revision, service.CaptureSnapshot().Revision);
            Assert.AreEqual(1L, Only(service).Outcomes!.Failed);
            Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(Outcome(id, 8, HealthExecutionOutcome.Succeeded)));
            Assert.AreEqual(MonitorState.Healthy, Only(service).State);
        }
        finally
        {
            await DisposeNoCollectionBindingAsync(binding);
        }
    }

    [TestMethod]
    public async Task SemanticallyIdenticalDiagnosticAndUtcInstantProduceADuplicateOutcome()
    {
        await using HealthcheckService service = CreateService();
        Guid id = Guid.NewGuid();
        IHealthExecutionBinding binding = HealthExecutionBinder.Bind(service, Failures(), Options(id));
        try
        {
            binding.Report(Outcome(id, 1, HealthExecutionOutcome.Failed, diagnostic: new HealthDiagnostic("Failed", "reason", "Type")));
            HealthSnapshot before = service.CaptureSnapshot();
            Assert.AreEqual(HealthFactDisposition.Duplicate, binding.Report(Outcome(id, 1, HealthExecutionOutcome.Failed,
                diagnostic: new HealthDiagnostic("Failed", "reason", "Type"), time: Epoch.ToOffset(TimeSpan.FromHours(-4)))));
            Assert.AreEqual(before.Revision, service.CaptureSnapshot().Revision);
        }
        finally
        {
            await DisposeNoCollectionBindingAsync(binding);
        }
    }

    [TestMethod]
    public async Task PreviousOutcomeDuplicateIsComparedBeforeOutcomeHighWaterAndOlderHistoryIsObsolete()
    {
        await using HealthcheckService service = CreateService();
        Guid id = Guid.NewGuid();
        IHealthExecutionBinding binding = HealthExecutionBinder.Bind(service, Failures(), Options(id));
        try
        {
            binding.Report(Outcome(id, 10, HealthExecutionOutcome.Failed));
            binding.Report(Outcome(id, 30, HealthExecutionOutcome.Succeeded));
            Assert.AreEqual(HealthFactDisposition.Duplicate, binding.Report(Outcome(id, 10, HealthExecutionOutcome.Failed)));
            Assert.ThrowsExactly<InvalidOperationException>(() => binding.Report(Outcome(id, 10, HealthExecutionOutcome.Succeeded)));
            binding.Report(Outcome(id, 50, HealthExecutionOutcome.Succeeded));
            HealthSnapshot before = service.CaptureSnapshot();

            Assert.AreEqual(HealthFactDisposition.Obsolete, binding.Report(Outcome(id, 10, HealthExecutionOutcome.Succeeded)));
            Assert.AreEqual(HealthFactDisposition.Obsolete, binding.Report(Outcome(id, 29, HealthExecutionOutcome.Failed)));
            Assert.AreEqual(before.Revision, service.CaptureSnapshot().Revision);
            Assert.AreEqual(1L, Only(service).Outcomes!.Failed);
            Assert.AreEqual(2L, Only(service).Outcomes!.Successful);
            Assert.AreEqual(50L, Only(service).Outcomes!.Execution!.ExecutionId);
        }
        finally
        {
            await DisposeNoCollectionBindingAsync(binding);
        }
    }

    [TestMethod]
    public async Task FirstLateOutcomeBelowAcceptedHighWaterCannotRewindTheHealthObservation()
    {
        await using HealthcheckService service = CreateService();
        Guid id = Guid.NewGuid();
        IHealthExecutionBinding binding = HealthExecutionBinder.Bind(service, Failures(), Options(id));
        try
        {
            binding.Report(CallbackStarted(id, 10));
            binding.Report(Outcome(id, 20, HealthExecutionOutcome.Succeeded));
            HealthSnapshot before = service.CaptureSnapshot();

            Assert.AreEqual(HealthFactDisposition.Obsolete, binding.Report(Outcome(id, 10, HealthExecutionOutcome.Failed)));

            Assert.AreEqual(before.Revision, service.CaptureSnapshot().Revision);
            Assert.AreEqual(0L, Only(service).Outcomes!.Failed);
            Assert.AreEqual(1L, Only(service).Outcomes!.Successful);
            Assert.AreEqual(20L, Only(service).Outcomes!.Execution!.ExecutionId);
        }
        finally
        {
            await DisposeNoCollectionBindingAsync(binding);
        }
    }

    [TestMethod]
    public async Task LatePreviousSettlementIsAcceptedWithoutMovingTheCurrentExecutionBackward()
    {
        await using HealthcheckService service = CreateService();
        Guid id = Guid.NewGuid();
        IHealthExecutionBinding binding = HealthExecutionBinder.Bind(service, Failures(), Options(id));
        try
        {
            binding.Report(Outcome(id, 100, HealthExecutionOutcome.Failed));
            binding.Report(CallbackStarted(id, 200));

            Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(Settled(id, 100)));
            Assert.AreEqual(200L, Only(service).Outcomes!.Execution!.ExecutionId);
            Assert.AreEqual(HealthExecutionPhase.Callback, Only(service).Outcomes!.Execution!.Phase);
            Assert.AreEqual(1L, Only(service).Outcomes!.Failed);
            Assert.AreEqual(HealthFactDisposition.Duplicate, binding.Report(Outcome(id, 100, HealthExecutionOutcome.Failed)));
            binding.Report(CallbackStarted(id, 300));
            Assert.AreEqual(HealthFactDisposition.Obsolete, binding.Report(Settled(id, 100)));
        }
        finally
        {
            await DisposeNoCollectionBindingAsync(binding);
        }
    }

    [TestMethod]
    public async Task PositiveExecutionIdsMayBeSparseAndReachInt64MaximumWithoutWrapping()
    {
        await using HealthcheckService service = CreateService();
        Guid id = Guid.NewGuid();
        IHealthExecutionBinding binding = HealthExecutionBinder.Bind(service, Failures(), Options(id));
        try
        {
            Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(Outcome(id, 1, HealthExecutionOutcome.Succeeded)));
            Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(Outcome(id, long.MaxValue, HealthExecutionOutcome.Succeeded)));
            Assert.AreEqual(long.MaxValue, Only(service).Outcomes!.Execution!.ExecutionId);
            Assert.AreEqual(HealthFactDisposition.Duplicate, binding.Report(Outcome(id, long.MaxValue, HealthExecutionOutcome.Succeeded)));
            Assert.AreEqual(2L, Only(service).Outcomes!.Successful);
        }
        finally
        {
            await DisposeNoCollectionBindingAsync(binding);
        }
    }

    [TestMethod]
    public async Task TwoBindingsAndDirectReportsRemainIndependentOnOneExternalMonitor()
    {
        await using HealthcheckService service = CreateService();
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(Failures(threshold: 3));
        Guid firstId = Guid.NewGuid();
        Guid secondId = Guid.NewGuid();
        IHealthExecutionBinding first = HealthExecutionBinder.Bind(monitor, Options(firstId));
        IHealthExecutionBinding second = HealthExecutionBinder.Bind(monitor, Options(secondId));
        try
        {
            first.Report(Outcome(firstId, 1, HealthExecutionOutcome.Failed));
            second.Report(Outcome(secondId, 1, HealthExecutionOutcome.Failed));
            monitor.ReportFailure();
            Assert.AreEqual(3L, Only(service).Outcomes!.Failed);
            Assert.AreEqual(MonitorState.Unhealthy, Only(service).State);
            Assert.AreEqual(HealthFactDisposition.Duplicate, first.Report(Outcome(firstId, 1, HealthExecutionOutcome.Failed)));
            await DisposeNoCollectionBindingAsync(first);
            Assert.AreEqual(HealthFactDisposition.Recorded, second.Report(Outcome(secondId, 2, HealthExecutionOutcome.Succeeded)));
            Assert.AreEqual(MonitorState.Healthy, Only(service).State);
            Assert.AreEqual(1L, Only(service).Outcomes!.Successful);
            GC.KeepAlive(monitor);
        }
        finally
        {
            await Task.WhenAll(DisposeNoCollectionBindingAsync(first), DisposeNoCollectionBindingAsync(second));
        }
    }

    [TestMethod]
    public async Task OwnedRetirementWithdrawsSynchronouslyAndAllSubsequentFactsReturnRetired()
    {
        await using HealthcheckService service = CreateService();
        Guid id = Guid.NewGuid();
        IHealthExecutionBinding binding = HealthExecutionBinder.Bind(service, Failures(), Options(id));
        try
        {
            HealthExecutionFact retirement = new()
            {
                RegistrationId = id,
                Kind = HealthExecutionFactKind.Retired,
                OccurredAtUtc = Epoch,
                IntegrationError = new HealthDiagnostic("OwnerUnavailable")
            };
            Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(retirement));
            Assert.AreEqual(0, service.CaptureSnapshot().MonitorCounts.Active);
            HealthSnapshot before = service.CaptureSnapshot();
            Assert.AreEqual(HealthFactDisposition.Retired, binding.Report(retirement));
            Assert.AreEqual(HealthFactDisposition.Retired, binding.Report(Outcome(id, 1, HealthExecutionOutcome.Failed)));
            Assert.AreEqual(HealthFactDisposition.Retired, binding.Report(null!));
            Assert.AreEqual(before.Revision, service.CaptureSnapshot().Revision);
            Assert.AreEqual(id, binding.RegistrationId);
            Assert.AreNotEqual(Guid.Empty, binding.MonitorId);
        }
        finally
        {
            await DisposeNoCollectionBindingAsync(binding);
        }
    }

    [TestMethod]
    public async Task ExternalRetirementReleasesAssociationWithoutWithdrawingTheCallerOwnedMonitor()
    {
        await using HealthcheckService service = CreateService();
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(Failures());
        Guid id = Guid.NewGuid();
        IHealthExecutionBinding binding = HealthExecutionBinder.Bind(monitor, Options(id));
        Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(new HealthExecutionFact
        {
            RegistrationId = id,
            Kind = HealthExecutionFactKind.Retired,
            OccurredAtUtc = Epoch
        }));
        await DisposeNoCollectionBindingAsync(binding);
        monitor.ReportFailure();
        Assert.AreEqual(1, service.CaptureSnapshot().MonitorCounts.Active);
        Assert.AreEqual(1L, Only(service).Outcomes!.Failed);
        Assert.AreEqual(HealthFactDisposition.Retired, binding.Report(Outcome(id, 1, HealthExecutionOutcome.Succeeded)));
    }

    [TestMethod]
    public async Task ExplicitDisposalWithdrawsOnlyAnOwnedMonitorAndIsIdempotent()
    {
        await using HealthcheckService service = CreateService();
        IConsecutiveFailureMonitor external = service.CreateConsecutiveFailures(Failures());
        IHealthExecutionBinding externalBinding = HealthExecutionBinder.Bind(external, Options(Guid.NewGuid()));
        IHealthExecutionBinding ownedBinding = HealthExecutionBinder.Bind(service, Failures(), Options(Guid.NewGuid()));
        await Task.WhenAll(ownedBinding.DisposeAsync().AsTask(), ownedBinding.DisposeAsync().AsTask()).WaitAsync(TimeSpan.FromSeconds(5));
        await externalBinding.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await externalBinding.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual(1, service.CaptureSnapshot().MonitorCounts.Active);
        Assert.AreEqual(external.MonitorId, Only(service).MonitorId);
        external.ReportSuccess();
        Assert.AreEqual(1L, Only(service).Outcomes!.Successful);
        Assert.AreEqual(HealthFactDisposition.Retired, ownedBinding.Report(Outcome(ownedBinding.RegistrationId, 1, HealthExecutionOutcome.Failed)));
    }

    [TestMethod]
    public async Task RetiredOwnedBindingDisposalSharesTheActualCollectionSettlementBarrier()
    {
        await using HealthcheckService service = CreateService();
        Guid id = Guid.NewGuid();
        IHealthExecutionBinding binding = HealthExecutionBinder.Bind(service, Failures(), Options(id));
        IHealthCollectionState collections = ((IHealthStateAccess)service).CollectionState;
        HealthCollectionTarget collection = collections.RegisterCollection(binding.MonitorId, Guid.NewGuid(), new HealthCollectorOptions());
        HealthCollectionReservation reservation = collections.TryReserve(collection).Reservation!;
        Task? first = null;
        Task? second = null;
        try
        {
            Assert.IsTrue(collections.TryEnter(reservation));
            Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(new HealthExecutionFact
            {
                RegistrationId = id,
                Kind = HealthExecutionFactKind.Retired,
                OccurredAtUtc = Epoch
            }));
            first = binding.DisposeAsync().AsTask();
            second = binding.DisposeAsync().AsTask();
            Assert.AreSame(first, second);
            Assert.IsFalse(first.IsCompleted);
            Assert.AreEqual(0, service.CaptureSnapshot().MonitorCounts.Active);
            Assert.IsTrue(reservation.LifetimeCancellation.IsCancellationRequested);
            Assert.AreEqual(HealthFactDisposition.Retired, binding.Report(Outcome(id, 1, HealthExecutionOutcome.Failed)));
        }
        finally
        {
            collections.Complete(reservation, new HealthCollectionCompletion(HealthCollectionCompletionReason.Settled, Epoch));
            await collections.WaitForSettlementAsync(reservation).WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        await binding.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task RetirementCancellationCallbackCanObserveBindingRetirementFromAnotherThread()
    {
        await using HealthcheckService service = CreateService();
        IHealthExecutionBinding binding = HealthExecutionBinder.Bind(service, Failures(), Options(Guid.NewGuid()));
        IHealthCollectionState collections = ((IHealthStateAccess)service).CollectionState;
        HealthCollectionTarget target = collections.RegisterCollection(binding.MonitorId, Guid.NewGuid(), new HealthCollectorOptions());
        HealthCollectionReservation reservation = collections.TryReserve(target).Reservation!;
        bool completedOutsideBinderGate = false;
        HealthFactDisposition? observed = null;
        CancellationTokenRegistration cancellation = default;
        try
        {
            Assert.IsTrue(collections.TryEnter(reservation));
            cancellation = reservation.LifetimeCancellation.Register(() =>
            {
                Task<HealthFactDisposition> reentrant = Task.Run(() => binding.Report(
                    Outcome(binding.RegistrationId, 1, HealthExecutionOutcome.Failed)));
                completedOutsideBinderGate = reentrant.Wait(TimeSpan.FromSeconds(5));
                if (completedOutsideBinderGate) { observed = reentrant.Result; }
            });
            Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(new HealthExecutionFact
            {
                RegistrationId = binding.RegistrationId,
                Kind = HealthExecutionFactKind.Retired,
                OccurredAtUtc = Epoch
            }));
            Assert.IsTrue(completedOutsideBinderGate, "Cancellation observers must not run under the binding ordering gate.");
            Assert.AreEqual(HealthFactDisposition.Retired, observed);
            Assert.IsFalse(binding.DisposeAsync().IsCompleted);
        }
        finally
        {
            try
            {
                collections.Complete(reservation, new HealthCollectionCompletion(HealthCollectionCompletionReason.Settled, Epoch));
                await collections.WaitForSettlementAsync(reservation).WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally { cancellation.Dispose(); }
        }
        await binding.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task ExternalBindingDisposalDoesNotWaitForOrCancelExternalCollectionWork()
    {
        await using HealthcheckService service = CreateService();
        IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(Failures());
        IHealthExecutionBinding binding = HealthExecutionBinder.Bind(monitor, Options(Guid.NewGuid()));
        IHealthCollectionState collections = ((IHealthStateAccess)service).CollectionState;
        HealthCollectionTarget collection = collections.RegisterCollection(monitor.MonitorId, Guid.NewGuid(), new HealthCollectorOptions());
        HealthCollectionReservation reservation = collections.TryReserve(collection).Reservation!;
        try
        {
            Assert.IsTrue(collections.TryEnter(reservation));
            Task disposal = binding.DisposeAsync().AsTask();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.IsFalse(reservation.LifetimeCancellation.IsCancellationRequested);
            monitor.ReportSuccess();
            Assert.AreEqual(1L, Only(service).Outcomes!.Successful);
        }
        finally
        {
            collections.Complete(reservation, new HealthCollectionCompletion(HealthCollectionCompletionReason.Settled, Epoch));
            await collections.WaitForSettlementAsync(reservation).WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [TestMethod]
    public async Task OwnedBindingSelfDisposalRequestsWithdrawalBeforeRejectingItsOwnWait()
    {
        await using HealthcheckService service = CreateService();
        IHealthExecutionBinding binding = HealthExecutionBinder.Bind(service, Failures(), Options(Guid.NewGuid()));
        IHealthCollectionState collections = ((IHealthStateAccess)service).CollectionState;
        HealthCollectionTarget target = collections.RegisterCollection(binding.MonitorId, Guid.NewGuid(), new HealthCollectorOptions());
        HealthCollectionReservation reservation = collections.TryReserve(target).Reservation!;
        try
        {
            Assert.IsTrue(collections.TryEnter(reservation));
            using IDisposable flow = HealthCollectionExecution.Enter(collections, binding.MonitorId, target.CollectionRegistrationId, Guid.NewGuid());
            Assert.ThrowsExactly<InvalidOperationException>(() => binding.DisposeAsync());
            Assert.AreEqual(0, service.CaptureSnapshot().MonitorCounts.Active);
            Assert.IsTrue(reservation.LifetimeCancellation.IsCancellationRequested);
            Assert.AreEqual(HealthFactDisposition.Retired, binding.Report(Outcome(binding.RegistrationId, 1, HealthExecutionOutcome.Failed)));
        }
        finally
        {
            collections.Complete(reservation, new HealthCollectionCompletion(HealthCollectionCompletionReason.Settled, Epoch));
            await collections.WaitForSettlementAsync(reservation).WaitAsync(TimeSpan.FromSeconds(5));
        }
        await binding.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [TestMethod]
    public async Task ExternalMonitorWithdrawalRetiresAllBindingsWithoutCountingLateOutcomes()
    {
        HealthcheckService service = CreateService();
        IHealthExecutionBinding? first = null, second = null;
        Task? firstDrain = null, secondDrain = null;
        Exception? failure = null;
        try
        {
            IConsecutiveFailureMonitor monitor = service.CreateConsecutiveFailures(Failures());
            first = HealthExecutionBinder.Bind(monitor, Options(Guid.NewGuid()));
            second = HealthExecutionBinder.Bind(monitor, Options(Guid.NewGuid()));
            monitor.Withdraw();
            Assert.AreEqual(HealthFactDisposition.Retired, first.Report(Outcome(first.RegistrationId, 1, HealthExecutionOutcome.Failed)));
            Assert.AreEqual(HealthFactDisposition.Retired, second.Report(Outcome(second.RegistrationId, 1, HealthExecutionOutcome.Failed)));
            Assert.AreEqual(HealthFactDisposition.Retired, first.Report(null!));
            Assert.AreEqual(0, service.CaptureSnapshot().MonitorCounts.Active);

            firstDrain = first.DisposeAsync().AsTask();
            secondDrain = second.DisposeAsync().AsTask();
            await Task.WhenAll(firstDrain, secondDrain).WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception error) { failure = error; }

        try
        {
            try { await service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)); }
            finally
            {
                firstDrain ??= first?.DisposeAsync().AsTask();
                secondDrain ??= second?.DisposeAsync().AsTask();
                // Actual service work has been joined. The body attempted the
                // binding barrier with its bounded wait; do not hide a failed
                // body by indefinitely awaiting that same private signal.
                if (firstDrain is { IsCompleted: true }) { await firstDrain; }
                if (secondDrain is { IsCompleted: true }) { await secondDrain; }
            }
        }
        catch (Exception cleanupFailure)
        {
            if (failure is not null) { throw new AggregateException("The binding settlement assertion and actual service cleanup both failed.", failure, cleanupFailure); }
            throw;
        }
        if (failure is not null) { ExceptionDispatchInfo.Capture(failure).Throw(); }
    }

    [TestMethod]
    public async Task ConcurrentIdenticalOutcomeReportsCommitThePolicyOnlyOnce()
    {
        await using HealthcheckService service = CreateService();
        Guid id = Guid.NewGuid();
        IHealthExecutionBinding binding = HealthExecutionBinder.Bind(service, Failures(), Options(id));
        try
        {
            HealthExecutionFact outcome = Outcome(id, 42, HealthExecutionOutcome.Failed);
            HealthFactDisposition[] results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => binding.Report(outcome))));
            Assert.AreEqual(1, results.Count(result => result == HealthFactDisposition.Recorded));
            Assert.AreEqual(19, results.Count(result => result == HealthFactDisposition.Duplicate));
            Assert.AreEqual(1L, Only(service).Outcomes!.Failed);
        }
        finally
        {
            await DisposeNoCollectionBindingAsync(binding);
        }
    }

    [TestMethod]
    public async Task ServiceRegistryDoesNotKeepAnAbandonedBindingAndOwnedMonitorAlive()
    {
        await using HealthcheckService service = CreateService();
        WeakReference lost = CreateAbandonedBinding(service);
        CollectUntilDead(lost);
        Assert.IsFalse(lost.IsAlive);
        Assert.AreEqual(0, service.CaptureSnapshot().MonitorCounts.Active);
    }

    [TestMethod]
    public async Task RetainedBindingKeepsItsCreatedMonitorAliveThroughCollection()
    {
        await using HealthcheckService service = CreateService();
        IHealthExecutionBinding binding = HealthExecutionBinder.Bind(service, Failures(), Options(Guid.NewGuid()));
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.AreEqual(binding.MonitorId, Only(service).MonitorId);
            Assert.AreEqual(HealthFactDisposition.Recorded, binding.Report(Outcome(binding.RegistrationId, 1, HealthExecutionOutcome.Succeeded)));
            GC.KeepAlive(binding);
        }
        finally
        {
            await DisposeNoCollectionBindingAsync(binding);
        }
    }

    private static async Task DisposeNoCollectionBindingAsync(IHealthExecutionBinding binding)
    {
        // Pure-fixture cleanup requests retirement and observes completed
        // faults. The owning service/monitor cleanup joins real work; this
        // helper makes no claim that a pending binding barrier has settled.
        Task disposal = binding.DisposeAsync().AsTask();
        if (disposal.IsCompleted) { await disposal; }
    }

    private static HealthcheckService CreateService(int capacity = 1024) => new(
        new HealthcheckOptions { MaxMonitors = capacity },
        new HealthApplicationIdentity { Name = "execution-binder-tests", InstanceId = Guid.NewGuid() },
        new ManualTimeProvider(Epoch));

    private static ConsecutiveFailureMonitorOptions Failures(int threshold = 1) =>
        new() { IsCritical = true, FailureThreshold = threshold };

    private static HealthExecutionBindingOptions Options(Guid id, bool usableData = false) =>
        new() { RegistrationId = id, SuccessPublishesUsableData = usableData };

    private static HealthMonitorSnapshot Only(HealthcheckService service) => service.CaptureSnapshot().Monitors.Single();

    private static HealthExecutionFact Registration(Guid id, DateTimeOffset time) => new()
    {
        RegistrationId = id,
        Kind = HealthExecutionFactKind.Registered,
        OccurredAtUtc = time
    };

    private static HealthExecutionFact Outcome(Guid id, long executionId, HealthExecutionOutcome outcome,
        HealthExecutionPhase phase = HealthExecutionPhase.Callback, HealthDiagnostic? diagnostic = null, DateTimeOffset? time = null) => new()
        {
            RegistrationId = id,
            Kind = HealthExecutionFactKind.Outcome,
            OccurredAtUtc = time ?? Epoch,
            ExecutionId = executionId,
            Outcome = outcome,
            SourcePhase = phase,
            WorkCompletedAtUtc = time ?? Epoch,
            OriginalError = diagnostic
        };

    private static HealthExecutionFact CallbackStarted(Guid id, long executionId) => new()
    {
        RegistrationId = id,
        Kind = HealthExecutionFactKind.CallbackStarted,
        OccurredAtUtc = Epoch,
        ExecutionId = executionId,
        CallbackStartedAtUtc = Epoch
    };

    private static HealthExecutionFact Settled(Guid id, long executionId) => new()
    {
        RegistrationId = id,
        Kind = HealthExecutionFactKind.Settled,
        OccurredAtUtc = Epoch,
        ExecutionId = executionId,
        SettledAtUtc = Epoch,
        ErrorHandlingStatus = HealthExecutionStepStatus.NotApplicable,
        CleanupStatus = HealthExecutionStepStatus.Succeeded,
        TotalDurationMilliseconds = 10
    };

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateAbandonedBinding(HealthcheckService service) => new(
        HealthExecutionBinder.Bind(service, Failures(), Options(Guid.NewGuid())));

    private static void CollectUntilDead(WeakReference reference)
    {
        for (int attempt = 0; attempt < 10 && reference.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }

    private sealed class ForeignMonitor : IConsecutiveFailureMonitor
    {
        internal int Calls { get; private set; }
        public Guid MonitorId { get { Calls++; throw new AssertFailedException("Foreign monitor must not be queried."); } }
        public string? Name { get { Calls++; throw new AssertFailedException("Foreign monitor must not be queried."); } }
        public bool IsCritical { get { Calls++; throw new AssertFailedException("Foreign monitor must not be queried."); } }
        public void ReportMeasurement(HealthMeasurement measurement) { Calls++; throw new AssertFailedException("Foreign monitor must not be invoked."); }
        public void Withdraw() { Calls++; throw new AssertFailedException("Foreign monitor must not be invoked."); }
        public Task WaitForDisposalAsync(CancellationToken waitCancellation = default) { Calls++; throw new AssertFailedException("Foreign monitor must not be invoked."); }
        public void ReportSuccess() { Calls++; throw new AssertFailedException("Foreign monitor must not be invoked."); }
        public void ReportFailure(HealthDiagnostic? diagnostic = null) { Calls++; throw new AssertFailedException("Foreign monitor must not be invoked."); }
        public void ReportSkipped() { Calls++; throw new AssertFailedException("Foreign monitor must not be invoked."); }
        public void ReportExpectedCancellation() { Calls++; throw new AssertFailedException("Foreign monitor must not be invoked."); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ForeignFactory : IHealthMonitorFactory
    {
        internal int Calls { get; private set; }
        public IConditionMonitor CreateCondition(ConditionMonitorOptions options) { Calls++; throw new AssertFailedException("Foreign factory must not be invoked."); }
        public IConsecutiveFailureMonitor CreateConsecutiveFailures(ConsecutiveFailureMonitorOptions options) { Calls++; throw new AssertFailedException("Foreign factory must not be invoked."); }
        public INumericThresholdMonitor CreateNumericThreshold(NumericThresholdMonitorOptions options) { Calls++; throw new AssertFailedException("Foreign factory must not be invoked."); }
        public IAvailabilityMonitor CreateAvailability(AvailabilityMonitorOptions options) { Calls++; throw new AssertFailedException("Foreign factory must not be invoked."); }
    }
}
