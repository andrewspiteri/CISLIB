using System.Diagnostics.Metrics;
using System.Text.Json;
using CIS.Audit;
using CIS.Audit.Hosting;
using CIS.CommonTesting;
using CIS.Messaging;
using CIS.Metrics;
using CIS.Security;
using CIS.Tests.UnitTests.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Audit;

[TestClass]
[DoNotParallelize]
public sealed class AuditMetricsTests
{
    [TestMethod]
    public async Task TheSoleCounterAcceptsExactlyTheNineFiniteCategoryOutcomeCombinations()
    {
        using var meter = new Meter("Audit.Counter");
        await using var runtime = new MetricsRuntime(meter);
        using var listener = new MetricsTestListener(meter);
        var owner = Instrumentation();
        owner.AttachMetrics(runtime);
        foreach (AuditCategory category in Enum.GetValues<AuditCategory>())
        {
            foreach (AuditCaptureResult result in Enum.GetValues<AuditCaptureResult>())
            {
                AuditCaptureObservation observation = owner.Capture(category);
                observation.Complete(result);
                observation.Complete(AuditCaptureResult.Faulted);
            }
        }

        MetricsPoint[] points = listener.Points(AuditInstrumentation.MetricName);
        Assert.HasCount(9, points);
        Assert.AreEqual(1, runtime.Definitions.Count);
        Assert.IsTrue(points.All(point => (long)point.Value == 1 && point.Tags.Length == 2));
        CollectionAssert.AreEquivalent(new[] { "business", "administration", "security" }, points
            .Select(point => (string)point.Tags.Single(tag => tag.Key == "category").Value!).Distinct().ToArray());
        CollectionAssert.AreEquivalent(new[] { "captured", "rejected", "faulted" }, points
            .Select(point => (string)point.Tags.Single(tag => tag.Key == "outcome").Value!).Distinct().ToArray());
        Assert.IsTrue(points.All(point => point.Tags.All(tag => tag.Key is "category" or "outcome")));
    }

    [TestMethod]
    [DataRow("captured", "captured")]
    [DataRow("invalid", "rejected")]
    [DataRow("capacity", "rejected")]
    [DataRow("cancelled", "rejected")]
    [DataRow("faulted", "faulted")]
    public async Task ActualCaptureRecordsOneResultAndKeepsOriginalFaultsAndCancellation(string boundary, string expected)
    {
        using var meter = new Meter("Audit.Actual");
        await using var runtime = new MetricsRuntime(meter);
        using var listener = new MetricsTestListener(meter);
        var owner = Instrumentation();
        owner.AttachMetrics(runtime);
        MessagingSettings settings = new MessagingOptions().Capture();
        await using var budget = new MessagingResourceBudget(settings);
        await using var engine = new AuditCaptureEngine(LocalApplicationIdentity.Capture(), new(), TimeProvider.System, budget, owner);
        var fault = new InvalidOperationException("original details fault");
        var codec = new Codec { Fault = boundary == "faulted" ? fault : null, Invalid = boundary == "invalid" };
        var definition = new AuditEventDefinition<int>("app.fact", 1, AuditCategory.Security, codec);
        using var cancellation = new CancellationTokenSource();
        IDisposable? held = null;
        try
        {
            if (boundary == "capacity")
            {
                Assert.IsTrue(budget.TryAcquireBytes(checked((int)settings.MaximumEncodedBufferBytes), out held));
            }
            if (boundary == "cancelled")
            {
                cancellation.Cancel();
                OperationCanceledException actual = Assert.ThrowsExactly<OperationCanceledException>(
                    () => engine.Capture(definition, 42, null, null, cancellationToken: cancellation.Token));
                Assert.AreEqual(cancellation.Token, actual.CancellationToken);
            }
            else if (boundary == "faulted")
            {
                Exception actual = Assert.ThrowsExactly<InvalidOperationException>(() => engine.Capture(definition, 42, null, null));
                Assert.AreSame(fault, actual);
            }
            else if (boundary == "invalid")
            {
                Assert.ThrowsExactly<ArgumentException>(() => engine.Capture(definition, 42, null, null));
            }
            else if (boundary == "capacity")
            {
                Assert.ThrowsExactly<AuditCaptureException>(() => engine.Capture(definition, 42, null, null));
            }
            else
            {
                Assert.AreNotEqual(Guid.Empty, engine.Capture(definition, 42, null, null).AuditId);
            }
        }
        finally
        {
            held?.Dispose();
        }
        MetricsPoint point = listener.Points(AuditInstrumentation.MetricName).Single();
        Assert.AreEqual("security", point.Tags.Single(tag => tag.Key == "category").Value);
        Assert.AreEqual(expected, point.Tags.Single(tag => tag.Key == "outcome").Value);
    }

    [TestMethod]
    public async Task CaptureBindingIsFixedAtEntryAndBelongsToOneHost()
    {
        using var meter = new Meter("Audit.Late");
        using var otherMeter = new Meter("Audit.Other");
        await using var runtime = new MetricsRuntime(meter);
        await using var other = new MetricsRuntime(otherMeter);
        using var listener = new MetricsTestListener(meter);
        var owner = Instrumentation();
        AuditCaptureObservation started = owner.Capture(AuditCategory.Business);

        owner.AttachMetrics(runtime);
        owner.AttachMetrics(runtime);
        started.Complete(AuditCaptureResult.Captured);

        Assert.IsEmpty(listener.Points(AuditInstrumentation.MetricName));
        owner.Capture(AuditCategory.Business).Complete(AuditCaptureResult.Captured);
        Assert.HasCount(1, listener.Points(AuditInstrumentation.MetricName));
        Assert.ThrowsExactly<InvalidOperationException>(() => owner.AttachMetrics(other));
    }

    [TestMethod]
    public void QuietFailureBudgetPersistsAcrossSuccessAndNeverIncludesPayloadOrIdentity()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        using var logs = new Logs();
        var owner = Instrumentation(clock, logs);
        for (int flap = 0; flap < 20; flap++)
        {
            for (int repeated = 0; repeated < 50; repeated++)
            {
                owner.Capture(AuditCategory.Business).Complete(AuditCaptureResult.Rejected);
            }
            owner.Capture(AuditCategory.Business).Complete(AuditCaptureResult.Captured);
            clock.Advance(TimeSpan.FromSeconds(31));
        }

        Assert.AreEqual(8L, logs.Logger.TotalCount);
        Assert.IsTrue(logs.Logger.Entries.All(entry => entry.Level == LogLevel.Warning && entry.Exception is null));
        Assert.IsTrue(logs.Logger.Entries.All(entry => entry.Message == "Audit capture rejected (business)."));
    }

    [TestMethod]
    [DataRow("factory")]
    [DataRow("enabled")]
    [DataRow("write")]
    public async Task OptionalLoggerFaultNeverChangesTheOriginalCaptureFailure(string phase)
    {
        using var logs = new FailingLogs(phase);
        var owner = Instrumentation(logger: logs);
        await using var budget = new MessagingResourceBudget(new MessagingOptions().Capture());
        await using var engine = new AuditCaptureEngine(LocalApplicationIdentity.Capture(), new(), TimeProvider.System, budget, owner);
        var original = new InvalidOperationException("original selected-codec fault");
        var definition = new AuditEventDefinition<int>("app.fact", 1, AuditCategory.Business, new Codec { Fault = original });

        Exception observed = Assert.ThrowsExactly<InvalidOperationException>(() => engine.Capture(definition, 42, null, null));

        Assert.AreSame(original, observed);
        Assert.AreNotEqual(Guid.Empty, engine.Capture(new AuditEventDefinition<int>("app.fact", 1, AuditCategory.Business, new Codec()), 42, null, null).AuditId);
    }

    [TestMethod]
    public async Task CaptureDrainIncludesActualFailureDiagnosticSettlement()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var logs = new HoldingLogs(entered, release);
        var owner = Instrumentation(logger: logs);
        await using var budget = new MessagingResourceBudget(new MessagingOptions().Capture());
        await using var engine = new AuditCaptureEngine(LocalApplicationIdentity.Capture(), new(), TimeProvider.System, budget, owner);
        var original = new InvalidOperationException("original selected-codec fault");
        var definition = new AuditEventDefinition<int>("app.fact", 1, AuditCategory.Business, new Codec { Fault = original });
        Task<CapturedAuditRecord> pending = Task.Run(() => engine.Capture(definition, 42, null, null));
        try
        {
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(10)));
            Task drain = engine.DrainAsync();
            Assert.IsFalse(drain.IsCompleted, "Logging is still executing under the admitted capture boundary.");
            release.Set();
            Exception observed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => pending);
            Assert.AreSame(original, observed);
            await drain.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            release.Set();
            try
            {
                await pending.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (InvalidOperationException error) when (ReferenceEquals(error, original))
            {
            }
        }
    }

    private static AuditInstrumentation Instrumentation(TimeProvider? clock = null, ILoggerFactory? logger = null)
        => new(new AuditHostingRegistration(new("audit")), clock, logger);

    private sealed class Codec : IAuditDetailsCodec<int>
    {
        internal Exception? Fault
        {
            get; init;
        }
        internal bool Invalid
        {
            get; init;
        }
        public void Validate(int details)
        {
            if (Fault is { } fault)
            {
                throw fault;
            }
            if (Invalid)
            {
                throw new ArgumentException("Selected details are invalid.", nameof(details));
            }
        }
        public void Write(Utf8JsonWriter writer, int details)
        {
            writer.WriteStartObject();
            writer.WriteNumber("value", details);
            writer.WriteEndObject();
        }
    }

    private sealed class Logs : ILoggerFactory
    {
        internal RecordingLogger<AuditMetricsTests> Logger { get; } = new();
        public ILogger CreateLogger(string categoryName) => Logger;
        public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();
        public void Dispose()
        {
        }
    }

    private sealed class FailingLogs(string phase) : ILoggerFactory, ILogger
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

    private sealed class HoldingLogs(ManualResetEventSlim entered, ManualResetEventSlim release) : ILoggerFactory, ILogger
    {
        public ILogger CreateLogger(string categoryName) => this;
        public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            entered.Set();
            release.Wait();
        }
        public void Dispose()
        {
        }
    }
}
