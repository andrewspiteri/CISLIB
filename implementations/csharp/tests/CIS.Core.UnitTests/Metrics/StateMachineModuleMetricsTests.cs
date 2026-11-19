using System.Diagnostics.Metrics;
using CIS.CommonTesting;
using CIS.Metrics;
using CIS.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Metrics;

[TestClass]
public sealed class StateMachineModuleMetricsTests
{
    [TestMethod]
    [DataRow(StateTransitionOutcome.Succeeded, "success", true)]
    [DataRow(StateTransitionOutcome.Rejected, "rejected", true)]
    [DataRow(StateTransitionOutcome.Busy, "busy", false)]
    [DataRow(StateTransitionOutcome.Failed, "error", true)]
    [DataRow(StateTransitionOutcome.Cancelled, "cancelled", true)]
    [DataRow(StateTransitionOutcome.Cancelled, "cancelled", false)]
    public async Task EveryCallHasOneDurationButOnlyGateOwnershipCountsAsActive(StateTransitionOutcome outcome, string label, bool admitted)
    {
        using var meter = new Meter("StateMachineCallOutcomes");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        var instrumentation = new StateMachineInstrumentation();
        instrumentation.AttachMetrics(runtime);
        instrumentation.AttachMetrics(runtime);
        IStateMachineObservation call = instrumentation.Begin(Info());
        call.Started();
        Assert.IsEmpty(listener.Points("cis.statemachine.active.attempts"));
        if (admitted)
        {
            call.GateAcquired();
            call.GateAcquired();
        }

        StateMachineCompletion completion = Completion(outcome, TimeSpan.FromSeconds(1.25));
        call.Completed(completion);
        call.Completed(completion with
        {
            Duration = TimeSpan.FromSeconds(999)
        });
        Assert.AreEqual(3, runtime.Definitions.Count);
        Assert.AreEqual(3, listener.Instruments.Count);
        MetricsPoint point = listener.Points("cis.statemachine.trigger.duration").Single();
        Assert.AreEqual(1.25, (double)point.Value);
        MetricsTestSupport.AssertTags(point, new("machine", "orders"), new("source", "Draft"), new("trigger", "Submit"), new("outcome", label));
        CollectionAssert.AreEqual(admitted ? new long[] { 1, -1 } : [], listener.Points("cis.statemachine.active.attempts")
            .Select(value => (long)value.Value).ToArray());
        Assert.IsEmpty(listener.Points("cis.statemachine.handler.duration"), "Admission and direct transitions invent no handler work.");
    }

    [TestMethod]
    public async Task EnteredPhasesUseRunnerSecondsAndUnknownFinalDurationStillBalancesActive()
    {
        using var meter = new Meter("StateMachinePhases");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        var instrumentation = new StateMachineInstrumentation();
        instrumentation.AttachMetrics(runtime);
        IStateMachineObservation call = instrumentation.Begin(Info());
        StateMachineHandlerRegistration handler = new("Arbitrary.Clr.Display.Name", "handler", typeof(object));
        call.Started();
        call.GateAcquired();
        call.PhaseCompleted(handler, PipelinePhase.Execute, PipelineStatus.Succeeded, TimeSpan.FromSeconds(2), null);
        call.PhaseCompleted(handler, PipelinePhase.Error, PipelineStatus.Cancelled, TimeSpan.FromSeconds(0.5), null);
        call.Completed(Completion(StateTransitionOutcome.Cancelled, null));
        Assert.IsEmpty(listener.Points("cis.statemachine.trigger.duration"));
        MetricsPoint[] phases = listener.Points("cis.statemachine.handler.duration");
        CollectionAssert.AreEqual(new[] { 2d, 0.5 }, phases.Select(point => (double)point.Value).ToArray());
        MetricsTestSupport.AssertTags(phases[1], new("machine", "orders"), new("handler", "handler"), new("phase", "error"), new("outcome", "cancelled"));
        Assert.IsFalse(phases.SelectMany(point => point.Tags).Any(pair => Equals(pair.Value, handler.Name)));
        Assert.AreEqual(0L, listener.Points("cis.statemachine.active.attempts").Sum(point => (long)point.Value));
    }

    [TestMethod]
    public async Task OneCapturedBindingPreventsReplayAndAForeignHostClaim()
    {
        using var meter = new Meter("StateMachineAttachment");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        using var otherMeter = new Meter("ForeignStateMachineAttachment");
        await using var other = new MetricsRuntime(otherMeter);
        var instrumentation = new StateMachineInstrumentation();
        IStateMachineObservation early = instrumentation.Begin(Info());
        early.Started();
        early.GateAcquired();
        instrumentation.AttachMetrics(runtime);
        early.Completed(Completion(StateTransitionOutcome.Succeeded, TimeSpan.FromSeconds(99)));
        Assert.IsEmpty(listener.Points("cis.statemachine.trigger.duration"));
        Assert.IsEmpty(listener.Points("cis.statemachine.active.attempts"));
        Assert.ThrowsExactly<InvalidOperationException>(() => instrumentation.AttachMetrics(other));
        IStateMachineObservation current = instrumentation.Begin(Info() with
        {
            Trigger = "invalid"
        });
        current.Started();
        current.GateAcquired();
        current.Completed(Completion(StateTransitionOutcome.Rejected, TimeSpan.FromSeconds(3)));
        MetricsPoint point = listener.Points("cis.statemachine.trigger.duration").Single();
        Assert.IsTrue(point.Tags.Contains(new KeyValuePair<string, object?>("trigger", "invalid")));
        Assert.AreEqual(3d, (double)point.Value);
        Assert.AreEqual(0, other.Definitions.Count);
    }

    [TestMethod]
    public void RoutineEventsAreDebugAndExplicitVisibilityAndQuietModeRemainScoped()
    {
        var factory = new CaptureFactory();
        var instrumentation = new StateMachineInstrumentation(factory);
        IStateMachineObservation normal = instrumentation.Begin(Info() with
        {
            LogCalls = true
        });
        normal.Started();
        normal.Completed(Completion(StateTransitionOutcome.Succeeded, TimeSpan.Zero));
        Assert.IsTrue(factory.Logger.Entries.All(entry => entry.Level == LogLevel.Debug));
        factory.Logger.Clear();
        IStateMachineObservation promoted = instrumentation.Begin(Info() with
        {
            LogCalls = true,
            LogRoutineAsInformation = true
        });
        promoted.Started();
        promoted.Completed(Completion(StateTransitionOutcome.Succeeded, TimeSpan.Zero));
        Assert.AreEqual(LogLevel.Information, factory.Logger.Entries.Single(entry => entry.EventId.Name == "statemachine.completed").Level);
        factory.Logger.Clear();
        instrumentation.Begin(Info()).Completed(Completion(StateTransitionOutcome.Failed, TimeSpan.Zero));
        Assert.IsEmpty(factory.Logger.Entries);
    }

    [TestMethod]
    public void OriginalFaultIsLoggedAtItsOriginOnceAndTerminalContainsNoDuplicateException()
    {
        var factory = new CaptureFactory();
        var instrumentation = new StateMachineInstrumentation(factory);
        var original = new InvalidOperationException("original diagnostic cause");
        var failure = new StateTransitionFailure("StateMachine.Exception", "not a template value", StateTransitionPhase.Execute,
            StateTransitionFailureKind.Exception, original);
        IStateMachineObservation call = instrumentation.Begin(Info() with
        {
            LogCalls = true
        });
        call.Started();
        call.PhaseCompleted(new("HandlerDisplay", "handler", typeof(object)), PipelinePhase.Execute,
            PipelineStatus.Failed, TimeSpan.FromSeconds(1), failure);
        call.Completed(Completion(StateTransitionOutcome.Failed, TimeSpan.FromSeconds(2)) with
        {
            Error = failure
        });
        RecordedLog origin = factory.Logger.Entries.Single(entry => entry.Exception is not null);
        Assert.AreSame(original, origin.Exception);
        Assert.AreEqual(LogLevel.Error, origin.Level);
        RecordedLog terminal = factory.Logger.Entries.Single(entry => entry.EventId.Name == "statemachine.completed");
        Assert.IsNull(terminal.Exception);
        Assert.AreEqual("Draft", terminal.Fields["Source"]);
        Assert.AreEqual("Submit", terminal.Fields["Trigger"]);
        Assert.IsNull(origin.Fields["ErrorMessage"]);
    }

    [TestMethod]
    public void ExplicitSafeBusinessDetailsAreBoundedAndThrowingTelemetryCannotEscape()
    {
        var factory = new CaptureFactory();
        IStateMachineObservation call = new StateMachineInstrumentation(factory).Begin(Info() with
        {
            LogCalls = true
        });
        var failure = new StateTransitionFailure("business.expected", "Safe\n" + new string('x', 600), StateTransitionPhase.Execute,
            StateTransitionFailureKind.ReportedFailure);
        var handler = new StateMachineHandlerRegistration("Display", "handler", typeof(object));
        call.PhaseCompleted(handler, PipelinePhase.Execute, PipelineStatus.Failed, TimeSpan.Zero, failure);
        RecordedLog entry = factory.Logger.Entries.Single();
        Assert.AreEqual(LogLevel.Warning, entry.Level);
        string message = (string)entry.Fields["ErrorMessage"]!;
        Assert.AreEqual(512, message.Length);
        Assert.IsTrue(message.StartsWith("Safe ", StringComparison.Ordinal));

        var throwing = new ThrowingMetrics();
        var observed = new StateMachineObservation(Info() with
        {
            LogCalls = true
        }, new ThrowingLogger(), new StateMachineMetrics(throwing));
        observed.Started();
        observed.GateAcquired();
        observed.PhaseCompleted(handler, PipelinePhase.Execute, PipelineStatus.Failed, TimeSpan.Zero, failure);
        observed.LifecycleFailure(failure);
        observed.CleanupBudgetExpired();
        observed.Completed(Completion(StateTransitionOutcome.Failed, TimeSpan.Zero));
        CollectionAssert.AreEqual(new long[] { 1, -1 }, throwing.ActiveAttempts.ToArray());
        Assert.AreEqual(2, throwing.TimerAttempts);
    }

    private static StateMachineCallInfo Info() => new("orders", Guid.NewGuid(), "Draft", "Submit", false, false);
    private static StateMachineCompletion Completion(StateTransitionOutcome outcome, TimeSpan? duration)
        => new(outcome, null, "Draft", false, duration, null, 0, false);

    private sealed class CaptureFactory : ILoggerFactory
    {
        internal RecordingLogger<StateMachineModuleMetricsTests> Logger { get; } = new();
        public ILogger CreateLogger(string categoryName) => Logger;
        public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();
        public void Dispose()
        {
        }
    }

    private sealed class ThrowingLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => throw new InvalidOperationException("logger defect");
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => throw new InvalidOperationException("logger defect");
    }

    private sealed class ThrowingMetrics : IMetrics, IUpDownCounter, CIS.Metrics.ITimer
    {
        internal List<long> ActiveAttempts { get; } = [];
        internal int TimerAttempts
        {
            get; private set;
        }
        public IUpDownCounter CreateUpDownCounter(MetricDefinition definition) => this;
        public CIS.Metrics.ITimer CreateTimer(MetricDefinition definition) => this;
        public void Add(long delta, MetricDimensions dimensions = default)
        {
            ActiveAttempts.Add(delta);
            throw new InvalidOperationException("measurement defect");
        }
        public void Record(double elapsedSeconds, MetricDimensions dimensions = default)
        {
            TimerAttempts++;
            throw new InvalidOperationException("measurement defect");
        }
        public ICounter CreateCounter(MetricDefinition definition) => throw new NotSupportedException();
        public IGauge CreateGauge(MetricDefinition definition) => throw new NotSupportedException();
        public IHistogram CreateHistogram(MetricDefinition definition) => throw new NotSupportedException();
        public void RegisterObservableGauge(MetricDefinition definition, Func<IReadOnlyList<MetricObservation>> observe) => throw new NotSupportedException();
        public ITimingScope Start(MetricDimensions dimensions = default) => throw new NotSupportedException();
    }
}
