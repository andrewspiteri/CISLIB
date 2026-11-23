using System.Diagnostics.Metrics;
using CIS.CommonTesting;
using CIS.Coordination;
using CIS.Coordination.Hosting;
using CIS.Healthcheck;
using CIS.Metrics;
using CIS.Tests.UnitTests.Metrics;
using CIS.Transactions;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Coordination;

[TestClass]
public sealed class CoordinationInstrumentationTests
{
    [TestMethod]
    public async Task PhysicalCallVocabularyIsFiniteAndDoesNotExportOwnershipIdentifiers()
    {
        await using var fixture = new Fixture();
        foreach (CoordinationRuntimeOperation operation in Enum.GetValues<CoordinationRuntimeOperation>())
        {
            foreach (CoordinationRuntimeOperationOutcome outcome in Enum.GetValues<CoordinationRuntimeOperationOutcome>())
            {
                fixture.Instrumentation.OperationCompleted(operation, outcome, TimeSpan.FromSeconds(2.5));
            }
        }

        MetricsPoint[] points = fixture.Listener.Points("cis.coordination.operation.duration");
        Assert.HasCount(32, points);
        Assert.IsTrue(points.All(point => (double)point.Value == 2.5));
        CollectionAssert.AreEquivalent(new[] { "acquire", "renew", "release", "resolve" },
            points.Select(point => (string)point.Tags.Single(tag => tag.Key == "operation").Value!).Distinct().ToArray());
        CollectionAssert.AreEquivalent(new[] { "succeeded", "busy", "refused", "unavailable", "indeterminate", "not_observed", "cancelled", "failed" },
            points.Select(point => (string)point.Tags.Single(tag => tag.Key == "outcome").Value!).Distinct().ToArray());
        Assert.IsTrue(points.All(point => point.Tags.Length == 2));
        Assert.AreEqual(4, fixture.Metrics.Definitions.Count);
        Assert.AreEqual(0, fixture.Provider.Calls);
    }

    [TestMethod]
    public async Task AttachingDuringPhysicalProviderCallDoesNotRetroactivelyRecordItsDuration()
    {
        await using var fixture = new Fixture(attachMetrics: false);
        var entered = MetricsTestSupport.Signal();
        var returned = new TaskCompletionSource<CoordinationAcquireResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Provider.AcquireOverride = _ =>
        {
            entered.TrySetResult();
            return returned.Task;
        };
        await fixture.Runtime.StartAsync();
        Task<ManagedCoordinationResult> first = fixture.Runtime.AcquireAuthorizedAsync(Acquisition());
        try
        {
            await entered.Task.WaitAsync(MetricsTestSupport.Deadline);
            fixture.Instrumentation.AttachMetrics(fixture.Metrics);
            returned.TrySetResult(new(CoordinationAcquireOutcome.Unavailable));
            Assert.AreEqual(CoordinationAcquireOutcome.Unavailable, (await first.WaitAsync(MetricsTestSupport.Deadline)).Outcome);
            await UntilAsync(() => fixture.Runtime.GetSnapshot().ManagedReservations == 0);
            Assert.IsEmpty(fixture.Listener.Points("cis.coordination.operation.duration"));

            fixture.Provider.AcquireOverride = null;
            fixture.Provider.AcquireOutcome = CoordinationAcquireOutcome.Unavailable;
            Assert.AreEqual(CoordinationAcquireOutcome.Unavailable, (await fixture.Runtime.AcquireAuthorizedAsync(Acquisition())).Outcome);
            await UntilAsync(() => fixture.Listener.Points("cis.coordination.operation.duration").Length == 1);
            MetricsTestSupport.AssertTags(fixture.Listener.Points("cis.coordination.operation.duration").Single(),
                new("operation", "acquire"), new("outcome", "unavailable"));
        }
        finally
        {
            returned.TrySetResult(new(CoordinationAcquireOutcome.Unavailable));
            await first.WaitAsync(MetricsTestSupport.Deadline);
        }
    }

    [TestMethod]
    [DataRow(true, "cancelled")]
    [DataRow(false, "failed")]
    public async Task ActualProviderCancellationIsUnknownAvailabilityWhileUnsignalledCancellationFaultIsFailed(
        bool cancelledTask, string expectedOutcome)
    {
        await using var fixture = new Fixture();
        var resolution = new TaskCompletionSource<CoordinationObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Provider.AcquireOverride = _ => cancelledTask
            ? Task.FromCanceled<CoordinationAcquireResult>(new CancellationToken(true))
            : Task.FromException<CoordinationAcquireResult>(new OperationCanceledException("Unsignalled original provider fault."));
        fixture.Provider.ResolveOverride = _ => resolution.Task;
        await fixture.Runtime.StartAsync();
        using var caller = new CancellationTokenSource();
        Task<ManagedCoordinationResult> attempt = fixture.Runtime.AcquireAuthorizedAsync(Acquisition(), caller.Token);
        try
        {
            await UntilAsync(() => fixture.Listener.Points("cis.coordination.operation.duration")
                .Any(point => point.Tags.Any(tag => tag.Key == "operation" && tag.Value as string == "acquire")));
            MetricsPoint physical = fixture.Listener.Points("cis.coordination.operation.duration")
                .Single(point => point.Tags.Any(tag => tag.Key == "operation" && tag.Value as string == "acquire"));
            MetricsTestSupport.AssertTags(physical, new("operation", "acquire"), new("outcome", expectedOutcome));
            if (cancelledTask)
            {
                Assert.IsNull(fixture.Runtime.GetSnapshot().AuthorityAvailable);
            }
            else
            {
                Assert.AreEqual(false, fixture.Runtime.GetSnapshot().AuthorityAvailable);
            }

            caller.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => attempt);
        }
        finally
        {
            caller.Cancel();
            resolution.TrySetResult(new(CoordinationObservationOutcome.NotObserved));
            try
            {
                await attempt.WaitAsync(MetricsTestSupport.Deadline);
            }
            catch (OperationCanceledException) when (caller.IsCancellationRequested)
            {
            }
        }
    }

    [TestMethod]
    public async Task LossAndGuardReasonVocabulariesStayCompleteAndSuccessIsNotARejection()
    {
        await using var fixture = new Fixture();
        foreach (CoordinationLossReason reason in Enum.GetValues<CoordinationLossReason>())
        {
            fixture.Instrumentation.Transition(new(Acquisition(), CoordinationLeaseState.Lost, null, reason, 0, 0));
        }

        var transactions = new ScriptedLocalTransactionFactory();
        transactions.Enqueue();
        await using ILocalTransaction transaction = await transactions.BeginAsync("Ledger");
        var guard = new Guard();
        var observed = new ObservedCoordinationGuard(guard, fixture.Instrumentation);
        CoordinationGrant grant = Grant();
        foreach (CoordinationRejectionReason reason in Enum.GetValues<CoordinationRejectionReason>())
        {
            guard.Result = new(reason);
            Assert.AreSame(guard.Result, await observed.ValidateAsync(transaction.Context, grant));
        }

        CollectionAssert.AreEquivalent(new[] { "expired", "superseded", "renewal_deadline_exceeded", "authority_unavailable", "shutdown_deadline_exceeded", "caller_cancelled" },
            Reasons(fixture.Listener.Points("cis.coordination.ownership.losses")));
        CollectionAssert.AreEquivalent(new[] { "missing", "released", "expired", "superseded", "wrong_authority", "wrong_database", "invalid_transaction", "authority_unavailable" },
            Reasons(fixture.Listener.Points("cis.coordination.guard.rejections")));
        Assert.IsNull(transaction.Completion);
    }

    [TestMethod]
    public async Task GuardKeepsOriginalContextGrantCancellationResultAndFaultWithoutCompletingOwner()
    {
        await using var fixture = new Fixture();
        var transactions = new ScriptedLocalTransactionFactory();
        transactions.Enqueue();
        await using ILocalTransaction transaction = await transactions.BeginAsync("Ledger");
        using var cancellation = new CancellationTokenSource();
        var rejected = new CoordinationGuardResult(CoordinationRejectionReason.Expired);
        var guard = new Guard { Result = rejected, MarkRollback = true };
        var observed = new ObservedCoordinationGuard(guard, fixture.Instrumentation);
        CoordinationGrant grant = Grant();

        Assert.AreSame(rejected, await observed.ValidateAsync(transaction.Context, grant, cancellation.Token));
        Assert.AreSame(transaction.Context, guard.Context);
        Assert.AreSame(grant, guard.Grant);
        Assert.AreEqual(cancellation.Token, guard.Cancellation);
        Assert.AreEqual("original.guard_rejection", transaction.Context.RollbackReasonCode);
        Assert.IsNull(transaction.Completion);
        Assert.HasCount(1, fixture.Listener.Points("cis.coordination.guard.rejections"));

        var original = new InvalidOperationException("original provider programming fault");
        guard.Failure = original;
        Assert.AreSame(original, await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => observed.ValidateAsync(transaction.Context, grant, cancellation.Token)));
        Assert.HasCount(1, fixture.Listener.Points("cis.coordination.guard.rejections"));
        Assert.IsNull(transaction.Completion);
    }

    [TestMethod]
    public async Task CollectionCallbackCutoffPreservesGuardResultAndFaultWithoutDuplicateRejection()
    {
        await using var fixture = new Fixture();
        await fixture.Metrics.StopAsync();
        Assert.IsTrue(fixture.Metrics.IsStopping);
        var transactions = new ScriptedLocalTransactionFactory();
        transactions.Enqueue();
        await using ILocalTransaction transaction = await transactions.BeginAsync("Ledger");
        var rejected = new CoordinationGuardResult(CoordinationRejectionReason.InvalidTransaction);
        var guard = new Guard { Result = rejected };
        var observed = new ObservedCoordinationGuard(guard, fixture.Instrumentation);

        Assert.AreSame(rejected, await observed.ValidateAsync(transaction.Context, Grant()));
        var original = new InvalidOperationException("original failure after metric shutdown");
        guard.Failure = original;
        Assert.AreSame(original, await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => observed.ValidateAsync(transaction.Context, Grant())));
        // Callback cutoff does not dispose the host factory's meter or disable its synchronous counters.
        MetricsPoint point = fixture.Listener.Points("cis.coordination.guard.rejections").Single();
        Assert.AreEqual(1L, (long)point.Value);
        MetricsTestSupport.AssertTags(point, new KeyValuePair<string, object?>("reason", "invalid_transaction"));
        Assert.IsNull(transaction.Completion);
    }

    [TestMethod]
    public async Task AuthorityHealthFollowsCompletedProviderFactsAndScrapesPerformNoProviderWork()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        await using var health = new HealthcheckService(new(), new()
        {
            Name = "coordination-test",
            InstanceId = Guid.NewGuid()
        }, clock);
        await using var fixture = new Fixture(clock);
        await fixture.Runtime.StartAsync();
        fixture.Instrumentation.ActivateHealth(health, true);
        await UntilAsync(() => health.CaptureSnapshot().Monitors.Single().State == MonitorState.Healthy);

        fixture.Provider.AcquireOutcome = CoordinationAcquireOutcome.Unavailable;
        Assert.AreEqual(CoordinationAcquireOutcome.Unavailable, (await fixture.Runtime.AcquireAuthorizedAsync(Acquisition())).Outcome);
        await UntilAsync(() => health.CaptureSnapshot().Monitors.Single().State == MonitorState.Unhealthy);
        Assert.AreEqual("coordination.authority_unavailable", health.CaptureSnapshot().Monitors.Single().Diagnostic!.Code);
        await UntilAsync(() => fixture.Runtime.GetSnapshot().AcquisitionRequests == 0);

        fixture.Provider.AcquireOutcome = CoordinationAcquireOutcome.Busy;
        Assert.AreEqual(CoordinationAcquireOutcome.Busy, (await fixture.Runtime.AcquireAuthorizedAsync(Acquisition())).Outcome);
        await UntilAsync(() => health.CaptureSnapshot().Monitors.Single().State == MonitorState.Healthy);
        int calls = fixture.Provider.Calls;
        for (int index = 0; index < 20; index++)
        {
            fixture.Listener.Collect();
            fixture.Instrumentation.RefreshHealth();
            _ = health.CaptureSnapshot();
        }

        Assert.AreEqual(calls, fixture.Provider.Calls);
        Assert.AreEqual(0d, (double)fixture.Listener.Points("cis.coordination.active.leases").Last().Value);
        Assert.IsTrue(fixture.Listener.Points("cis.coordination.active.leases").All(point => point.Tags.Length == 0));
    }

    [TestMethod]
    public async Task ActiveLeaseGaugeCountsEveryOwnedOrDrainingHandleWithoutScrapeTimeProviderCalls()
    {
        await using var fixture = new Fixture();
        fixture.Provider.AcquireOutcome = CoordinationAcquireOutcome.Acquired;
        await fixture.Runtime.StartAsync();
        ICoordinationLease? first = null;
        ICoordinationLease? second = null;
        try
        {
            first = (await fixture.Runtime.AcquireAuthorizedAsync(Acquisition("first"))).Lease!;
            second = (await fixture.Runtime.AcquireAuthorizedAsync(Acquisition("second"))).Lease!;
            first.BeginDraining(TimeSpan.FromSeconds(1));
            Assert.HasCount(1, fixture.Runtime.GetSnapshot(take: 1).Entries);
            int calls = fixture.Provider.Calls;
            fixture.Listener.Collect();
            Assert.AreEqual(2d, (double)fixture.Listener.Points("cis.coordination.active.leases").Last().Value);
            Assert.AreEqual(calls, fixture.Provider.Calls);
            await first.DisposeAsync();
            fixture.Listener.Collect();
            Assert.AreEqual(1d, (double)fixture.Listener.Points("cis.coordination.active.leases").Last().Value);
        }
        finally
        {
            if (second is not null)
            {
                await second.DisposeAsync();
            }
            if (first is not null)
            {
                await first.DisposeAsync();
            }
        }
    }

    [TestMethod]
    public async Task StuckCancellationCannotDelayOnceOnlyLossMetricOrEraseItsActualReservation()
    {
        await using var fixture = new Fixture();
        fixture.Provider.AcquireOutcome = CoordinationAcquireOutcome.Acquired;
        await fixture.Runtime.StartAsync();
        ICoordinationLease lease = (await fixture.Runtime.AcquireAuthorizedAsync(Acquisition())).Lease!;
        var callbackStarted = MetricsTestSupport.Signal();
        var finishCallback = MetricsTestSupport.Signal();
        using CancellationTokenRegistration registration = lease.OwnershipLost.Register(() =>
        {
            callbackStarted.TrySetResult();
            finishCallback.Task.GetAwaiter().GetResult();
        });
        try
        {
            lease.BeginDraining(TimeSpan.FromMilliseconds(100));
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(100));
            await callbackStarted.Task.WaitAsync(MetricsTestSupport.Deadline);
            await UntilAsync(() => fixture.Listener.Points("cis.coordination.ownership.losses").Length == 1);
            for (int index = 0; index < 20; index++)
            {
                Assert.AreEqual(CoordinationLeaseState.Lost, lease.State);
                fixture.Listener.Collect();
            }

            Assert.AreEqual(0d, (double)fixture.Listener.Points("cis.coordination.active.leases").Last().Value);
            Assert.HasCount(1, fixture.Listener.Points("cis.coordination.ownership.losses"));
            Assert.AreEqual(1, fixture.Runtime.GetSnapshot().ManagedReservations);
            Assert.IsTrue(fixture.Runtime.GetSnapshot().UnfinishedNotifications > 0);
            Task stopping = fixture.Runtime.StopAsync();
            Assert.IsFalse(stopping.IsCompleted);
        }
        finally
        {
            finishCallback.TrySetResult();
            await lease.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task RepeatedLossReportingIsBoundedAcrossFlapsWhileEveryMetricObservationSurvives()
    {
        using var logs = new Logs();
        await using var fixture = new Fixture(loggerFactory: logs);
        for (int index = 0; index < 1_000; index++)
        {
            fixture.Instrumentation.Transition(new(Acquisition(), CoordinationLeaseState.Lost, null,
                CoordinationLossReason.Superseded, 0, 0));
        }

        Assert.AreEqual(1L, logs.Logger.TotalCount);
        for (int index = 0; index < 16; index++)
        {
            fixture.Clock.Advance(TimeSpan.FromSeconds(30));
            fixture.Instrumentation.Transition(new(Acquisition(), CoordinationLeaseState.Lost, null,
                CoordinationLossReason.Superseded, 0, 0));
        }

        Assert.AreEqual(8L, logs.Logger.TotalCount);
        Assert.IsTrue(logs.Logger.Entries.All(entry => entry.Level == LogLevel.Warning));
        Assert.HasCount(1_016, fixture.Listener.Points("cis.coordination.ownership.losses"));
    }

    [TestMethod]
    [DataRow("factory")]
    [DataRow("enabled")]
    [DataRow("write")]
    public async Task LoggerFailureCannotEraseLossMetricOrReplaceOriginalGuardResult(string failingPhase)
    {
        using var logger = new ThrowingLogger(failingPhase);
        await using var fixture = new Fixture(loggerFactory: logger);
        fixture.Instrumentation.Transition(new(Acquisition(), CoordinationLeaseState.Lost, null,
            CoordinationLossReason.Expired, 0, 0));
        var original = new CoordinationGuardResult(CoordinationRejectionReason.WrongDatabase);
        var guard = new Guard { Result = original };
        var observed = new ObservedCoordinationGuard(guard, fixture.Instrumentation);
        var transactions = new ScriptedLocalTransactionFactory();
        transactions.Enqueue();
        await using ILocalTransaction transaction = await transactions.BeginAsync("Ledger");

        Assert.AreSame(original, await observed.ValidateAsync(transaction.Context, Grant()));
        Assert.HasCount(1, fixture.Listener.Points("cis.coordination.ownership.losses"));
        Assert.HasCount(1, fixture.Listener.Points("cis.coordination.guard.rejections"));
    }

    private static string[] Reasons(MetricsPoint[] points)
    {
        Assert.IsTrue(points.All(point => point.Tags.Length == 1 && (long)point.Value == 1));
        return points.Select(point => (string)point.Tags.Single().Value!).ToArray();
    }

    private static CoordinationAcquisition Acquisition(string resource = "default")
        => new(new("test", "scope", "duty", resource), "application", "instance", Guid.NewGuid());

    private static CoordinationGrant Grant() => new(Guid.Parse("820df885-d9af-43d7-acb4-76d47cd6a2f0"), Acquisition(), 1, DateTimeOffset.UnixEpoch.AddSeconds(30));

    private static async Task UntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(MetricsTestSupport.Deadline);
        while (!predicate())
        {
            await Task.Delay(1, timeout.Token);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly Meter meter = new("CoordinationObservations");
        internal readonly ManualTimeProvider Clock;
        internal readonly Provider Provider = new();
        internal readonly CoordinationRuntime Runtime;
        internal readonly MetricsRuntime Metrics;
        internal readonly MetricsTestListener Listener;
        internal readonly CoordinationInstrumentation Instrumentation;

        internal Fixture(ManualTimeProvider? clock = null, ILoggerFactory? loggerFactory = null, bool attachMetrics = true)
        {
            Clock = clock ?? new(DateTimeOffset.UnixEpoch);
            Runtime = new(Provider, new CoordinationOptions(), Clock);
            Listener = new(meter);
            Metrics = new(meter);
            Instrumentation = new(new CoordinationComposition(), Runtime, Clock, loggerFactory);
            if (attachMetrics)
            {
                Instrumentation.AttachMetrics(Metrics);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Runtime.DisposeAsync();
            await Instrumentation.DisposeAsync();
            await Metrics.DisposeAsync();
            Listener.Dispose();
            meter.Dispose();
        }
    }

    private sealed class Provider : ICoordinationProvider
    {
        internal int Calls;
        internal CoordinationAcquireOutcome AcquireOutcome = CoordinationAcquireOutcome.Busy;
        internal Func<CancellationToken, Task<CoordinationAcquireResult>>? AcquireOverride;
        internal Func<CancellationToken, Task<CoordinationObservation>>? ResolveOverride;
        public Guid AuthorityId { get; } = Guid.Parse("820df885-d9af-43d7-acb4-76d47cd6a2f0");
        public Task ValidateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<CoordinationAcquireResult> AcquireAsync(CoordinationAcquireRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            if (AcquireOverride is { } acquire)
            {
                return acquire(cancellationToken);
            }

            return Task.FromResult(AcquireOutcome == CoordinationAcquireOutcome.Acquired
                ? new CoordinationAcquireResult(AcquireOutcome, new(AuthorityId, request.Acquisition, 1,
                    DateTimeOffset.UnixEpoch.AddSeconds(30)), TimeSpan.FromSeconds(30))
                : new CoordinationAcquireResult(AcquireOutcome));
        }
        public Task<CoordinationMutationResult> RenewAsync(CoordinationGrant grant, TimeSpan leaseDuration, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new CoordinationMutationResult(CoordinationMutationOutcome.Succeeded, grant, leaseDuration));
        }
        public Task<CoordinationMutationResult> ReleaseAsync(CoordinationGrant grant, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new CoordinationMutationResult(CoordinationMutationOutcome.Succeeded));
        }
        public Task<CoordinationObservation> ResolveAsync(CoordinationAcquisition acquisition, CancellationToken cancellationToken = default)
            => ResolveOverride?.Invoke(cancellationToken)
                ?? throw new InvalidOperationException("A definite acquisition result needs no resolution.");
    }

    private sealed class Guard : ICoordinationGuard
    {
        internal CoordinationGuardResult Result = new();
        internal Exception? Failure;
        internal bool MarkRollback;
        internal ILocalTransactionContext? Context;
        internal CoordinationGrant? Grant;
        internal CancellationToken Cancellation;
        public Task<CoordinationGuardResult> ValidateAsync(ILocalTransactionContext context, CoordinationGrant grant, CancellationToken cancellationToken = default)
        {
            Context = context;
            Grant = grant;
            Cancellation = cancellationToken;
            if (Failure is { } failure)
            {
                return Task.FromException<CoordinationGuardResult>(failure);
            }
            if (MarkRollback)
            {
                context.MarkRollbackOnly("original.guard_rejection");
            }

            return Task.FromResult(Result);
        }
    }

    private sealed class Logs : ILoggerFactory
    {
        internal RecordingLogger<CoordinationInstrumentationTests> Logger { get; } = new();
        public ILogger CreateLogger(string categoryName) => Logger;
        public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();
        public void Dispose()
        {
        }
    }

    private sealed class ThrowingLogger(string phase) : ILoggerFactory, ILogger
    {
        public ILogger CreateLogger(string categoryName) => phase == "factory" ? throw new InvalidOperationException("factory fault") : this;
        public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => phase == "enabled" ? throw new InvalidOperationException("enablement fault") : true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => throw new InvalidOperationException("write fault");
        public void Dispose()
        {
        }
    }
}
