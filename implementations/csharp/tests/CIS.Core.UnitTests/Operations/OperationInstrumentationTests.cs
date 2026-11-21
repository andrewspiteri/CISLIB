using System.Diagnostics.Metrics;
using CIS.CommonTesting;
using CIS.Metrics;
using CIS.Operations;
using CIS.Operations.Hosting;
using CIS.Security;
using CIS.Tests.UnitTests.Metrics;
using CIS.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Operations;

[TestClass]
public sealed class OperationInstrumentationTests
{
    [TestMethod]
    [DataRow(OperationExecutionDisposition.Prepared, false, "prepared_succeeded")]
    [DataRow(OperationExecutionDisposition.Prepared, true, "prepared_rejected")]
    [DataRow(OperationExecutionDisposition.Replayed, false, "replayed_succeeded")]
    [DataRow(OperationExecutionDisposition.Replayed, true, "replayed_rejected")]
    [DataRow(OperationExecutionDisposition.KeyConflict, false, "key_conflict")]
    [DataRow(OperationExecutionDisposition.ResultExpired, false, "result_expired")]
    [DataRow(OperationExecutionDisposition.Incompatible, false, "incompatible")]
    [DataRow(OperationExecutionDisposition.Contended, false, "contended")]
    [DataRow(OperationExecutionDisposition.RetryRequired, false, "retry_required")]
    [DataRow(OperationExecutionDisposition.Denied, false, "denied")]
    [DataRow(OperationExecutionDisposition.RollbackOnly, false, "rollback_only")]
    public async Task ExecuteExportsItsFiniteBusinessAndExecutionMeaningOnce(OperationExecutionDisposition disposition,
        bool rejected, string outcome)
    {
        using var fixture = new OperationRuntimeFixture();
        using var meter = new Meter("OperationOutcome");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var instrumentation = new OperationInstrumentation(fixture.Provider.GetRequiredService<OperationsComposition>(), clock);
        instrumentation.AttachMetrics(runtime);
        var inner = new BoundaryStub
        {
            Execute = () =>
            {
                clock.Advance(TimeSpan.FromSeconds(2.5));
                return new(disposition, disposition is OperationExecutionDisposition.Prepared or OperationExecutionDisposition.Replayed
                    ? rejected ? OperationDecision<string>.Reject("original.decline", "private message")
                        : OperationDecision<string>.Succeed("private result") : null);
            }
        };
        var observed = new ObservedDurableOperationService(inner, [fixture.Definition], instrumentation);
        OperationExecution<string> result = await observed.ExecuteAsync(new RuntimeContext(), OperationRuntimeFixture.Request(), OperationRuntimeFixture.Success);
        Assert.AreEqual(disposition, result.Disposition);
        Assert.AreEqual(2, runtime.Definitions.Count);
        MetricsPoint point = listener.Points("cis.operation.execute.duration").Single();
        Assert.AreEqual(2.5, (double)point.Value);
        MetricsTestSupport.AssertTags(point, new("operation", "Debit"), new("outcome", outcome));
        Assert.IsEmpty(listener.Points("cis.operation.lookup.duration"));
    }

    [TestMethod]
    [DataRow(OperationLookupDisposition.Found, false, "found_succeeded")]
    [DataRow(OperationLookupDisposition.Found, true, "found_rejected")]
    [DataRow(OperationLookupDisposition.NotFound, false, "not_found")]
    [DataRow(OperationLookupDisposition.ResultExpired, false, "result_expired")]
    [DataRow(OperationLookupDisposition.Incompatible, false, "incompatible")]
    [DataRow(OperationLookupDisposition.Unavailable, false, "unavailable")]
    [DataRow(OperationLookupDisposition.Denied, false, "denied")]
    public async Task LookupExportsOnlyItsFiniteObservationMeaning(OperationLookupDisposition disposition,
        bool rejected, string outcome)
    {
        using var fixture = new OperationRuntimeFixture();
        using var meter = new Meter("LookupOutcome");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        var instrumentation = new OperationInstrumentation();
        instrumentation.AttachMetrics(runtime);
        var inner = new BoundaryStub
        {
            Lookup = () => new(disposition, disposition == OperationLookupDisposition.Found
                ? rejected ? OperationDecision<string>.Reject("declined", "private message") : OperationDecision<string>.Succeed("original") : null)
        };
        var observed = new ObservedDurableOperationService(inner, [fixture.Definition], instrumentation);
        Assert.AreEqual(disposition, (await observed.GetResultAsync<string>(new("Debit", "private-scope", "private-key"))).Disposition);
        MetricsTestSupport.AssertTags(listener.Points("cis.operation.lookup.duration").Single(),
            new("operation", "Debit"), new("outcome", outcome));
        Assert.IsEmpty(listener.Points("cis.operation.execute.duration"));
    }

    [TestMethod]
    public async Task BindingIsCapturedBeforeAuthorizationAndCannotReplayIntoLateAttachment()
    {
        using var meter = new Meter("OperationLateAttach");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        using var fixture = new OperationRuntimeFixture();
        OperationInstrumentation instrumentation = fixture.Provider.GetRequiredService<OperationInstrumentation>();
        fixture.Authorizer.Handler = _ =>
        {
            instrumentation.AttachMetrics(runtime);
            return ValueTask.FromResult(AuthorizationDecision.Allow);
        };
        await using (ILocalTransaction first = await fixture.Begin())
        {
            Assert.AreEqual(OperationExecutionDisposition.Prepared,
                (await fixture.Service.ExecuteAsync(first.Context, OperationRuntimeFixture.Request(), OperationRuntimeFixture.Success)).Disposition);
        }
        Assert.IsEmpty(listener.Points("cis.operation.execute.duration"));
        await using (ILocalTransaction next = await fixture.Begin())
        {
            await fixture.Service.ExecuteAsync(next.Context, OperationRuntimeFixture.Request(), OperationRuntimeFixture.Success);
        }
        Assert.HasCount(1, listener.Points("cis.operation.execute.duration"));
        using var otherMeter = new Meter("OtherHost");
        await using var other = new MetricsRuntime(otherMeter);
        Assert.ThrowsExactly<InvalidOperationException>(() => instrumentation.AttachMetrics(other));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnavailableStartOrEndClockOmitsDurationWithoutInventingZero(bool failAtEnd)
    {
        var clock = new FaultClock { Fail = !failAtEnd };
        using var meter = new Meter("OperationClock");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        using var fixture = new OperationRuntimeFixture(services: services => services.AddSingleton<TimeProvider>(clock));
        fixture.Provider.GetRequiredService<OperationInstrumentation>().AttachMetrics(runtime);
        await using ILocalTransaction owner = await fixture.Begin();
        OperationExecution<string> result = await fixture.Service.ExecuteAsync(owner.Context, OperationRuntimeFixture.Request(), (_, _, _) =>
        {
            clock.Fail = true;
            return Task.FromResult(OperationDecision<string>.Succeed("original"));
        });
        Assert.AreEqual(OperationExecutionDisposition.Prepared, result.Disposition);
        Assert.IsEmpty(listener.Points("cis.operation.execute.duration"));
    }

    [TestMethod]
    public async Task LookupDurationWaitsForActualReadCleanupAndCountsFoundOnce()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var fixture = new OperationRuntimeFixture(services: services => services.AddSingleton<TimeProvider>(clock));
        fixture.Source.ConnectionCreated = connection => connection.DisposeHandler = async () =>
        {
            clock.Advance(TimeSpan.FromSeconds(3));
            entered.TrySetResult();
            await release.Task;
        };
        fixture.Journal.Read = (_, identity, _) =>
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            return Task.FromResult<OperationJournalRecord?>(RuntimeJournal.Row(identity, new(new("debit-request", 1), new byte[32])));
        };
        using var meter = new Meter("OperationReadCleanup");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        fixture.Provider.GetRequiredService<OperationInstrumentation>().AttachMetrics(runtime);
        Task<OperationLookup<string>> lookup = fixture.Service.GetResultAsync<string>(new("Debit", "scope", "key"));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsFalse(lookup.IsCompleted);
            Assert.IsEmpty(listener.Points("cis.operation.lookup.duration"));
        }
        finally
        {
            release.TrySetResult();
        }
        Assert.AreEqual(OperationLookupDisposition.Found, (await lookup).Disposition);
        MetricsPoint point = listener.Points("cis.operation.lookup.duration").Single();
        Assert.AreEqual(4d, (double)point.Value);
        MetricsTestSupport.AssertTags(point, new("operation", "Debit"), new("outcome", "found_succeeded"));
    }

    [TestMethod]
    [DataRow("factory")]
    [DataRow("enabled")]
    [DataRow("write")]
    public async Task ThrowingDiagnosticsCannotReplaceAuthorizationOrHandlerFaults(string fault)
    {
        using var meter = new Meter("OperationThrowingDiagnostics");
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (ReferenceEquals(instrument.Meter, meter))
            {
                current.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<double>((_, _, _, _) => throw new InvalidOperationException("metric defect"));
        listener.Start();
        await using var runtime = new MetricsRuntime(meter);
        using var fixture = new OperationRuntimeFixture(services: services => services.AddSingleton<ILoggerFactory>(new ThrowingLogger(fault)));
        fixture.Provider.GetRequiredService<OperationInstrumentation>().AttachMetrics(runtime);
        await using ILocalTransaction owner = await fixture.Begin();
        var original = new InvalidOperationException("private SQL/key/error");
        fixture.Authorizer.Handler = _ => ValueTask.FromException<AuthorizationDecision>(original);
        Assert.AreSame(original, await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Service.ExecuteAsync(
            owner.Context, OperationRuntimeFixture.Request(), OperationRuntimeFixture.Success)));
        Assert.IsFalse(owner.Context.IsRollbackOnly);
        fixture.Authorizer.Handler = null;
        Assert.AreSame(original, await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Service.ExecuteAsync<RuntimeInput, string>(
            owner.Context, OperationRuntimeFixture.Request(), (_, _, _) => Task.FromException<OperationDecision<string>>(original))));
        Assert.IsTrue(owner.Context.IsRollbackOnly);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NormalCallsAreQuietUnlessCapturedLogCallsOptsInAndUnknownAliasesNeverBecomeDimensions(bool enabled)
    {
        var logs = new CaptureLogger();
        OperationDefinitionOptions? supplied = null;
        using var fixture = new OperationRuntimeFixture(options =>
        {
            options.LogCalls = enabled;
            supplied = options;
        }, services: services => services.AddSingleton<ILoggerFactory>(logs));
        supplied!.LogCalls = !enabled;
        using var meter = new Meter("OperationQuiet");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        fixture.Provider.GetRequiredService<OperationInstrumentation>().AttachMetrics(runtime);
        await using ILocalTransaction owner = await fixture.Begin();
        await fixture.Service.ExecuteAsync(owner.Context, OperationRuntimeFixture.Request(), OperationRuntimeFixture.Success);
        Assert.AreEqual(enabled ? 1 : 0, logs.Logger.Entries.Count);
        if (enabled)
        {
            RecordedLog entry = logs.Logger.Entries.Single();
            Assert.AreEqual(LogLevel.Debug, entry.Level);
            Assert.IsNull(entry.Exception);
            Assert.AreEqual("prepared_succeeded", entry.Fields["Outcome"]);
            Assert.IsFalse(entry.Message.Contains("trusted-account", StringComparison.Ordinal));
            Assert.IsFalse(entry.Fields.ContainsKey("Key"));
        }
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Service.GetResultAsync<string>(new("Unregistered", "private", "private")));
        Assert.IsEmpty(listener.Points("cis.operation.lookup.duration"));
        Assert.HasCount(1, listener.Points("cis.operation.execute.duration"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancellationLabelsRequireTheSuppliedTokenForBothPublicMethods(bool lookup)
    {
        using var fixture = new OperationRuntimeFixture();
        using var meter = new Meter("OperationCancellation");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        fixture.Provider.GetRequiredService<OperationInstrumentation>().AttachMetrics(runtime);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        if (lookup)
        {
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Service.GetResultAsync<string>(new("Debit", "scope", "key"), cancellation.Token));
        }
        else
        {
            await using ILocalTransaction owner = await fixture.Begin();
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Service.ExecuteAsync(owner.Context,
                OperationRuntimeFixture.Request(), OperationRuntimeFixture.Success, cancellation.Token));
        }
        MetricsTestSupport.AssertTags(listener.Points(lookup ? "cis.operation.lookup.duration" : "cis.operation.execute.duration").Single(),
            new("operation", "Debit"), new("outcome", "cancelled"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnrequestedCancellationIsFailedAndItsSafeWarningContainsNoOriginalData(bool lookup)
    {
        var logs = new CaptureLogger();
        using var fixture = new OperationRuntimeFixture(services: services => services.AddSingleton<ILoggerFactory>(logs));
        using var meter = new Meter("OperationUnrequestedCancellation");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        fixture.Provider.GetRequiredService<OperationInstrumentation>().AttachMetrics(runtime);
        var original = new OperationCanceledException("private-key private-scope SQL and credentials");
        fixture.Authorizer.Handler = _ => ValueTask.FromException<AuthorizationDecision>(original);
        if (lookup)
        {
            Assert.AreSame(original, await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Service.GetResultAsync<string>(new("Debit", "scope", "key"))));
        }
        else
        {
            await using ILocalTransaction owner = await fixture.Begin();
            Assert.AreSame(original, await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Service.ExecuteAsync(owner.Context,
                OperationRuntimeFixture.Request(), OperationRuntimeFixture.Success)));
        }
        MetricsTestSupport.AssertTags(listener.Points(lookup ? "cis.operation.lookup.duration" : "cis.operation.execute.duration").Single(),
            new("operation", "Debit"), new("outcome", "failed"));
        RecordedLog summary = logs.Logger.Entries.Single();
        Assert.AreEqual(LogLevel.Warning, summary.Level);
        Assert.IsNull(summary.Exception);
        Assert.IsFalse(summary.Message.Contains("private", StringComparison.Ordinal));
        CollectionAssert.AreEquivalent(new[] { "Operation", "Method", "Outcome", "DurationSeconds", "{OriginalFormat}" }, summary.Fields.Keys.ToArray());
    }

    [TestMethod]
    public async Task CompletingOneObservationTwiceNeverRecordsAgainOrChangesItsFirstOutcome()
    {
        using var meter = new Meter("OperationCompleteOnce");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        var instrumentation = new OperationInstrumentation();
        instrumentation.AttachMetrics(runtime);
        OperationObservation observation = instrumentation.Capture("Debit", false, false);
        observation.Complete("prepared_succeeded");
        observation.Complete("failed");
        MetricsTestSupport.AssertTags(listener.Points("cis.operation.execute.duration").Single(),
            new("operation", "Debit"), new("outcome", "prepared_succeeded"));
    }

    private sealed class BoundaryStub : IDurableOperationService
    {
        internal Func<OperationExecution<string>> Execute = () => throw new NotSupportedException();
        internal Func<OperationLookup<string>> Lookup = () => throw new NotSupportedException();
        public Task<OperationExecution<TResult>> ExecuteAsync<TRequest, TResult>(ILocalTransactionContext context,
            OperationRequest<TRequest> request, Func<ILocalTransactionContext, TRequest, CancellationToken, Task<OperationDecision<TResult>>> handler,
            CancellationToken cancellationToken = default) => Task.FromResult((OperationExecution<TResult>)(object)Execute());
        public Task<OperationLookup<TResult>> GetResultAsync<TResult>(OperationLookupRequest request,
            CancellationToken cancellationToken = default) => Task.FromResult((OperationLookup<TResult>)(object)Lookup());
    }

    private sealed class FaultClock : TimeProvider
    {
        internal bool Fail;
        public override long GetTimestamp() => Fail ? throw new InvalidOperationException("clock unavailable") : base.GetTimestamp();
    }

    private sealed class CaptureLogger : ILoggerFactory
    {
        internal RecordingLogger<OperationInstrumentationTests> Logger { get; } = new();
        public ILogger CreateLogger(string categoryName) => Logger;
        public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();
        public void Dispose()
        {
        }
    }

    private sealed class ThrowingLogger(string fault) : ILoggerFactory, ILogger
    {
        public ILogger CreateLogger(string categoryName) => fault == "factory" ? throw new InvalidOperationException("logger factory defect") : this;
        public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();
        public void Dispose()
        {
        }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => fault == "enabled" ? throw new InvalidOperationException("logger enablement defect") : true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => throw new InvalidOperationException("logger write defect");
    }
}
