using System.Diagnostics.Metrics;
using CIS.CommonTesting;
using CIS.Metrics;
using CIS.TestSupport.Transactions;
using CIS.Tests.UnitTests.Metrics;
using CIS.Transactions;
using CIS.Transactions.Hosting;
using CIS.Transactions.AdoNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Transactions;

[TestClass]
public sealed class TransactionTelemetryTests
{
    [TestMethod]
    [DataRow(TransactionOutcome.Committed, "committed")]
    [DataRow(TransactionOutcome.NotCommitted, "not_committed")]
    [DataRow(TransactionOutcome.Unknown, "unknown")]
    public async Task CompleteBundleRecordsTheReportSecondsOnceAndBalancesEveryOutcome(TransactionOutcome outcome, string label)
    {
        using var meter = new Meter("TransactionOutcome");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        var instrumentation = Instrumentation();
        instrumentation.AttachMetrics(runtime);
        instrumentation.AttachMetrics(runtime);
        ITransactionObservation observation = instrumentation.Capture("Ledger", TransactionIsolation.Serializable, false);
        var completion = new TransactionCompletion(outcome, TransactionEndReason.CommitRequested,
            outcome != TransactionOutcome.NotCommitted, outcome != TransactionOutcome.Committed,
            outcome != TransactionOutcome.Committed, durationSeconds: 4.25);

        observation.OwnerPublished();
        observation.OwnerPublished();
        observation.Completed(completion);
        observation.Completed(completion);

        Assert.AreEqual(3, runtime.Definitions.Count);
        MetricsPoint duration = listener.Points("cis.transaction.duration").Single();
        Assert.AreEqual(completion.DurationSeconds!.Value, (double)duration.Value);
        MetricsTestSupport.AssertTags(duration, new("database", "Ledger"), new("outcome", label));
        CollectionAssert.AreEqual(new long[] { 1, -1 }, listener.Points("cis.transaction.active")
            .Select(point => (long)point.Value).ToArray());
        Assert.IsEmpty(listener.Points("cis.transaction.failures"));
    }

    [TestMethod]
    public async Task BindingCapturedBeforeSourceResolutionCannotReplayAcquisitionOrPublicationAfterLateAttachment()
    {
        using var meter = new Meter("TransactionLateCapture");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        using var source = new RecordingTransactionDataSource();
        var services = new ServiceCollection();
        services.AddCisAdoNetDatabase("Ledger", provider =>
        {
            provider.GetRequiredService<TransactionInstrumentation>().AttachMetrics(runtime);
            return source;
        }, new()
        {
            SupportedIsolationLevels = [TransactionIsolation.ReadCommitted]
        });
        using ServiceProvider provider = services.BuildServiceProvider();
        ILocalTransactionFactory factory = provider.GetRequiredService<ILocalTransactionFactory>();

        await using (ILocalTransaction early = await factory.BeginAsync("Ledger"))
        {
            await early.CommitAsync();
        }
        Assert.IsEmpty(listener.Points("cis.transaction.duration"));
        Assert.IsEmpty(listener.Points("cis.transaction.active"));

        await using (ILocalTransaction next = await factory.BeginAsync("Ledger"))
        {
            await next.CommitAsync();
        }
        Assert.HasCount(1, listener.Points("cis.transaction.duration"));
        CollectionAssert.AreEqual(new long[] { 1, -1 }, listener.Points("cis.transaction.active")
            .Select(point => (long)point.Value).ToArray());
    }

    [TestMethod]
    public async Task NativeBeginFailureHasOneFailureButNoPublishedOwnerOrDuration()
    {
        using var meter = new Meter("TransactionBeginFailure");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        var original = new IOException("fixture-secret SELECT begin");
        using var source = new RecordingTransactionDataSource
        {
            ConnectionCreated = connection => connection.BeginHandler = (_, _) => ValueTask.FromException<System.Data.Common.DbTransaction>(original)
        };
        using ServiceProvider provider = Provider(source);
        provider.GetRequiredService<TransactionInstrumentation>().AttachMetrics(runtime);

        IOException observed = await Assert.ThrowsExactlyAsync<IOException>(() =>
            provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger"));

        Assert.AreSame(original, observed);
        MetricsPoint failure = listener.Points("cis.transaction.failures").Single();
        Assert.AreEqual(1L, failure.Value);
        MetricsTestSupport.AssertTags(failure, new("database", "Ledger"), new("phase", "begin"));
        Assert.IsEmpty(listener.Points("cis.transaction.active"));
        Assert.IsEmpty(listener.Points("cis.transaction.duration"));
        Assert.AreEqual(1, source.Connections.Single().DisposeCalls);
    }

    [TestMethod]
    public async Task CancelledPublicationKeepsOriginalCancellationAndCountsEachFailedCleanupOnlyOnce()
    {
        var logs = new CaptureFactory();
        using var meter = new Meter("TransactionBeginCleanup");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        using var cancellation = new CancellationTokenSource();
        using var source = new RecordingTransactionDataSource
        {
            ConnectionCreated = connection =>
            {
                connection.DisposeHandler = () => ValueTask.FromException(new IOException("fixture-secret SELECT connection"));
                connection.TransactionCreated = transaction =>
                {
                    transaction.RollbackHandler = _ => Task.FromException(new IOException("fixture-secret SELECT rollback"));
                    transaction.DisposeHandler = () => ValueTask.FromException(new IOException("fixture-secret SELECT transaction"));
                    cancellation.Cancel();
                };
            }
        };
        using ServiceProvider provider = Provider(source, logs);
        provider.GetRequiredService<TransactionInstrumentation>().AttachMetrics(runtime);

        OperationCanceledException observed = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger", cancellation.Token));

        Assert.AreEqual(cancellation.Token, observed.CancellationToken);
        CollectionAssert.AreEqual(new[] { "rollback", "dispose", "dispose" }, listener.Points("cis.transaction.failures")
            .Select(point => (string)point.Tags.Single(tag => tag.Key == "phase").Value!).ToArray());
        Assert.IsEmpty(listener.Points("cis.transaction.active"));
        Assert.IsEmpty(listener.Points("cis.transaction.duration"));
        Assert.HasCount(3, logs.Logger.Entries);
        Assert.IsTrue(logs.Logger.Entries.All(entry => entry.EventId.Name == "transaction.begin.cleanup.failed"
            && entry.Level == LogLevel.Warning && entry.Exception is null));
        Assert.IsFalse(logs.Logger.Entries.Any(entry => entry.Message.Contains("fixture-secret", StringComparison.Ordinal)
            || entry.Message.Contains("SELECT", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task FailedNativeInvocationsCountIndividuallyIncludingBothDisposals()
    {
        var logs = new CaptureFactory();
        using var meter = new Meter("TransactionNativeFailures");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        using var source = new RecordingTransactionDataSource
        {
            ConnectionCreated = connection =>
            {
                connection.DisposeHandler = () => ValueTask.FromException(new IOException("private connection"));
                connection.TransactionCreated = transaction =>
                {
                    transaction.CommitHandler = _ => Task.FromException(new IOException("private commit"));
                    transaction.RollbackHandler = _ => Task.FromException(new IOException("private rollback"));
                    transaction.DisposeHandler = () => ValueTask.FromException(new IOException("private transaction"));
                };
            }
        };
        using ServiceProvider provider = Provider(source, logs);
        provider.GetRequiredService<TransactionInstrumentation>().AttachMetrics(runtime);
        await using ILocalTransaction owner = await provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger");
        TransactionCompletion completed = await owner.CommitAsync();

        Assert.AreEqual(TransactionOutcome.Unknown, completed.Outcome);
        Assert.HasCount(3, completed.CleanupErrors);
        MetricsPoint[] failures = listener.Points("cis.transaction.failures");
        Assert.HasCount(4, failures);
        CollectionAssert.AreEqual(new[] { "commit", "rollback", "dispose", "dispose" }, failures
            .Select(point => (string)point.Tags.Single(tag => tag.Key == "phase").Value!).ToArray());
        Assert.AreEqual(0L, listener.Points("cis.transaction.active").Sum(point => (long)point.Value));
        Assert.AreEqual(completed.DurationSeconds!.Value, (double)listener.Points("cis.transaction.duration").Single().Value);
        RecordedLog summary = logs.Logger.Entries.Single();
        Assert.AreEqual(LogLevel.Warning, summary.Level);
        Assert.AreEqual("transaction.rollback_failed", summary.Fields["RollbackErrorCode"]);
        Assert.AreEqual("transaction.transaction_disposal_failed", summary.Fields["TransactionDisposeErrorCode"]);
        Assert.AreEqual("transaction.connection_disposal_failed", summary.Fields["ConnectionDisposeErrorCode"]);
        Assert.IsNull(summary.Exception);
        Assert.IsFalse(summary.Message.Contains("private", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ApplicationFailureMarksRollbackOnlyWithoutInventingANativeFailure()
    {
        using var meter = new Meter("TransactionApplicationFailure");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        using var source = new RecordingTransactionDataSource();
        using ServiceProvider provider = Provider(source);
        provider.GetRequiredService<TransactionInstrumentation>().AttachMetrics(runtime);
        await using ILocalTransaction owner = await provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger");
        var original = new InvalidOperationException("fixture-secret SELECT participant");

        InvalidOperationException observed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            owner.Context.UseAdoNetAsync((_, _, _) => Task.FromException(original)));
        TransactionCompletion completed = await owner.CommitAsync();

        Assert.AreSame(original, observed);
        Assert.AreEqual(TransactionOutcome.NotCommitted, completed.Outcome);
        Assert.IsEmpty(listener.Points("cis.transaction.failures"));
        MetricsTestSupport.AssertTags(listener.Points("cis.transaction.duration").Single(),
            new("database", "Ledger"), new("outcome", "not_committed"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task AwaitUsingAnEscapingCallbackRetainsOneSafeQuietModeWarningWithoutGuessingANativeFailure(bool expectedCancellation)
    {
        var logs = new CaptureFactory();
        using var meter = new Meter("TransactionEscapingCallback");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        using var source = new RecordingTransactionDataSource();
        using ServiceProvider provider = Provider(source, logs);
        provider.GetRequiredService<TransactionInstrumentation>().AttachMetrics(runtime);
        using var cancellation = new CancellationTokenSource();
        Exception original = expectedCancellation
            ? new OperationCanceledException("fixture-secret SELECT cancellation", cancellation.Token)
            : new InvalidOperationException("fixture-secret SELECT callback");
        ILocalTransaction? retained = null;

        async Task Operation()
        {
            await using ILocalTransaction owner = await provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger");
            retained = owner;
            await owner.Context.UseAdoNetAsync((_, _, _) =>
            {
                if (expectedCancellation)
                {
                    cancellation.Cancel();
                }
                return Task.FromException(original);
            }, cancellation.Token);
        }

        Exception observed = await Assert.ThrowsAsync<Exception>(Operation);

        Assert.AreSame(original, observed);
        Assert.IsNotNull(retained);
        Assert.AreEqual(TransactionOutcome.NotCommitted, retained.Completion!.Outcome);
        Assert.AreEqual(TransactionEndReason.UncommittedDisposal, retained.Completion.EndReason);
        Assert.IsNull(retained.Completion.Error);
        Assert.HasCount(0, retained.Completion.CleanupErrors);
        RecordedLog summary = logs.Logger.Entries.Single();
        Assert.AreEqual(LogLevel.Warning, summary.Level);
        Assert.AreEqual("transaction.completed", summary.EventId.Name);
        Assert.AreEqual(true, summary.Fields["RollbackOnly"]);
        Assert.IsNull(summary.Exception);
        Assert.IsFalse(summary.Message.Contains("fixture-secret", StringComparison.Ordinal));
        Assert.IsFalse(summary.Message.Contains("SELECT", StringComparison.Ordinal));
        Assert.IsFalse(summary.Fields.ContainsKey("RollbackReasonCode"));
        Assert.IsEmpty(listener.Points("cis.transaction.failures"));
        CollectionAssert.AreEqual(new long[] { 1, -1 }, listener.Points("cis.transaction.active")
            .Select(point => (long)point.Value).ToArray());
        Assert.HasCount(1, listener.Points("cis.transaction.duration"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task QuietDefaultsAndDebugOptInKeepOneSafeTerminalSummary(bool logCalls)
    {
        var logs = new CaptureFactory();
        using var source = new RecordingTransactionDataSource();
        using ServiceProvider provider = Provider(source, logs, logCalls);
        await using ILocalTransaction owner = await provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger");
        await owner.CommitAsync();
        await owner.DisposeAsync();

        Assert.AreEqual("CIS.Transactions", logs.Category);
        Assert.AreEqual(logCalls ? 1 : 0, logs.Logger.Entries.Count);
        if (logCalls)
        {
            RecordedLog entry = logs.Logger.Entries.Single();
            Assert.AreEqual(LogLevel.Debug, entry.Level);
            Assert.AreEqual("transaction.completed", entry.EventId.Name);
            Assert.IsNull(entry.Exception);
            Assert.AreEqual("Ledger", entry.Fields["Database"]);
            Assert.AreEqual(owner.Completion!.DurationSeconds, entry.Fields["DurationSeconds"]);
        }
    }

    [TestMethod]
    public async Task UnknownIsVisibleInQuietModeWithoutNativeExceptionSqlOrRollbackReason()
    {
        var logs = new CaptureFactory();
        using var source = new RecordingTransactionDataSource
        {
            ConnectionCreated = connection => connection.TransactionCreated = transaction =>
                transaction.CommitHandler = _ => Task.FromException(new IOException("fixture-secret SELECT account DSN=password"))
        };
        using ServiceProvider provider = Provider(source, logs);
        await using ILocalTransaction owner = await provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger");
        TransactionCompletion completion = await owner.CommitAsync();

        RecordedLog entry = logs.Logger.Entries.Single();
        Assert.AreEqual(LogLevel.Warning, entry.Level);
        Assert.AreEqual(TransactionOutcome.Unknown, completion.Outcome);
        Assert.AreEqual("transaction.commit_outcome_unknown", entry.Fields["ErrorCode"]);
        Assert.IsNull(entry.Exception);
        Assert.IsFalse(entry.Message.Contains("fixture-secret", StringComparison.Ordinal));
        Assert.IsFalse(entry.Message.Contains("SELECT", StringComparison.Ordinal));
        Assert.IsFalse(entry.Message.Contains("password", StringComparison.Ordinal));
        Assert.IsFalse(entry.Fields.ContainsKey("RollbackReasonCode"));
    }

    [TestMethod]
    public async Task ExpectedPreCommitCancellationIsQuietAndDoesNotCountAFailedNativeInvocation()
    {
        var logs = new CaptureFactory();
        using var meter = new Meter("TransactionExpectedCancellation");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        using var source = new RecordingTransactionDataSource();
        using ServiceProvider provider = Provider(source, logs);
        provider.GetRequiredService<TransactionInstrumentation>().AttachMetrics(runtime);
        await using ILocalTransaction owner = await provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        TransactionCompletion completion = await owner.CommitAsync(cancellation.Token);

        Assert.AreEqual(TransactionEndReason.Cancellation, completion.EndReason);
        Assert.IsEmpty(logs.Logger.Entries);
        Assert.IsEmpty(listener.Points("cis.transaction.failures"));
        Assert.HasCount(1, listener.Points("cis.transaction.duration"));
    }

    [TestMethod]
    public void ThrowingHandlesStillAttemptAllIndependentObservationsAndTheFinalDecrement()
    {
        var metrics = new ThrowingMetrics();
        var bundle = new TransactionMetrics(metrics);
        var observation = new TransactionObservationScope("Ledger", TransactionIsolation.ReadCommitted, true, new ThrowingLogger(), bundle);
        observation.OwnerPublished();
        observation.NativeFailure(TransactionNativePhase.Commit);
        observation.Completed(new(TransactionOutcome.Unknown, TransactionEndReason.CommitRequested,
            true, true, true, durationSeconds: 3));

        Assert.AreEqual(3, metrics.Definitions);
        CollectionAssert.AreEqual(new long[] { 1, -1 }, metrics.ActiveAttempts.ToArray());
        Assert.AreEqual(1, metrics.TimerAttempts);
        Assert.AreEqual(1, metrics.FailureAttempts);
    }

    [TestMethod]
    [DataRow("factory")]
    [DataRow("enabled")]
    [DataRow("write")]
    public async Task ActualThrowingMetricListenerAndLoggerPreserveOriginalWorkFaultAndOwnedCleanup(string loggingFault)
    {
        using var meter = new Meter("TransactionThrowingTelemetry");
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (ReferenceEquals(instrument.Meter, meter))
            {
                current.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, _, _, _) => throw new InvalidOperationException("metric defect"));
        listener.SetMeasurementEventCallback<double>((_, _, _, _) => throw new InvalidOperationException("metric defect"));
        listener.Start();
        await using var runtime = new MetricsRuntime(meter);
        using var source = new RecordingTransactionDataSource();
        using ServiceProvider provider = Provider(source, new ThrowingLoggerFactory(loggingFault), true);
        provider.GetRequiredService<TransactionInstrumentation>().AttachMetrics(runtime);
        var original = new InvalidOperationException("application original");
        ILocalTransaction? retained = null;

        async Task Operation()
        {
            await using ILocalTransaction owner = await provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger");
            retained = owner;
            await owner.Context.UseAdoNetAsync((_, _, _) => Task.FromException(original));
        }

        InvalidOperationException observed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(Operation);
        Assert.AreSame(original, observed);
        Assert.AreEqual(TransactionOutcome.NotCommitted, retained!.Completion!.Outcome);
        Assert.AreEqual(1, source.Connections.Single().Transactions.Single().RollbackCalls);
        Assert.AreEqual(1, source.Connections.Single().Transactions.Single().DisposeCalls);
        Assert.AreEqual(1, source.Connections.Single().DisposeCalls);
    }

    [TestMethod]
    public async Task UnknownClockOmitsTimerButBalancesActiveAndAnotherHostCannotClaimTheOwner()
    {
        using var meter = new Meter("TransactionUnknownClock");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        using var otherMeter = new Meter("OtherTransactionHost");
        await using var other = new MetricsRuntime(otherMeter);
        using var source = new RecordingTransactionDataSource();
        using ServiceProvider provider = Provider(source, clock: new UnavailableClock());
        TransactionInstrumentation instrumentation = provider.GetRequiredService<TransactionInstrumentation>();
        instrumentation.AttachMetrics(runtime);
        Assert.ThrowsExactly<InvalidOperationException>(() => instrumentation.AttachMetrics(other));
        await using ILocalTransaction owner = await provider.GetRequiredService<ILocalTransactionFactory>().BeginAsync("Ledger");
        TransactionCompletion completed = await owner.CommitAsync();

        Assert.AreEqual(TransactionOutcome.Committed, completed.Outcome);
        Assert.IsNull(completed.DurationSeconds);
        Assert.IsEmpty(listener.Points("cis.transaction.duration"));
        CollectionAssert.AreEqual(new long[] { 1, -1 }, listener.Points("cis.transaction.active")
            .Select(point => (long)point.Value).ToArray());
        Assert.AreEqual(0, other.Definitions.Count);
    }

    private static TransactionInstrumentation Instrumentation() => new(new TransactionsComposition(new object(), []));

    private static ServiceProvider Provider(RecordingTransactionDataSource source, ILoggerFactory? logger = null,
        bool logCalls = false, TimeProvider? clock = null)
    {
        var services = new ServiceCollection();
        if (logger is not null)
        {
            services.AddSingleton(logger);
        }
        if (clock is not null)
        {
            services.AddSingleton(clock);
        }
        services.AddCisTransactions(new()
        {
            LogCalls = logCalls
        });
        services.AddCisAdoNetDatabase("Ledger", _ => source, new()
        {
            SupportedIsolationLevels = [TransactionIsolation.ReadCommitted]
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private sealed class CaptureFactory : ILoggerFactory
    {
        internal string? Category
        {
            get; private set;
        }
        internal RecordingLogger<TransactionTelemetryTests> Logger { get; } = new();
        public ILogger CreateLogger(string categoryName)
        {
            Category = categoryName;
            return Logger;
        }
        public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();
        public void Dispose()
        {
        }
    }

    private sealed class ThrowingLoggerFactory(string fault) : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName) => fault == "factory"
            ? throw new InvalidOperationException("logger factory defect") : new ThrowingLogger(fault == "enabled");
        public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();
        public void Dispose()
        {
        }
    }

    private sealed class ThrowingLogger(bool throwWhenEnabled = true) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => throwWhenEnabled
            ? throw new InvalidOperationException("logger enablement defect") : true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => throw new InvalidOperationException("logger defect");
    }

    private sealed class UnavailableClock : TimeProvider
    {
        public override long GetTimestamp() => throw new InvalidOperationException("clock unavailable");
    }

    private sealed class ThrowingMetrics : IMetrics, ICounter, IUpDownCounter, CIS.Metrics.ITimer
    {
        internal int Definitions
        {
            get; private set;
        }
        internal int TimerAttempts
        {
            get; private set;
        }
        internal int FailureAttempts
        {
            get; private set;
        }
        internal List<long> ActiveAttempts { get; } = [];
        public ICounter CreateCounter(MetricDefinition definition)
        {
            Definitions++;
            return this;
        }
        public IUpDownCounter CreateUpDownCounter(MetricDefinition definition)
        {
            Definitions++;
            return this;
        }
        public CIS.Metrics.ITimer CreateTimer(MetricDefinition definition)
        {
            Definitions++;
            return this;
        }
        void ICounter.Add(long amount, MetricDimensions dimensions)
        {
            FailureAttempts++;
            throw new InvalidOperationException("counter defect");
        }
        void IUpDownCounter.Add(long delta, MetricDimensions dimensions)
        {
            ActiveAttempts.Add(delta);
            throw new InvalidOperationException("active defect");
        }
        public void Record(double elapsedSeconds, MetricDimensions dimensions = default)
        {
            TimerAttempts++;
            throw new InvalidOperationException("timer defect");
        }
        public IGauge CreateGauge(MetricDefinition definition) => throw new NotSupportedException();
        public IHistogram CreateHistogram(MetricDefinition definition) => throw new NotSupportedException();
        public void RegisterObservableGauge(MetricDefinition definition, Func<IReadOnlyList<MetricObservation>> observe) => throw new NotSupportedException();
        public ITimingScope Start(MetricDimensions dimensions = default) => throw new NotSupportedException();
    }
}
