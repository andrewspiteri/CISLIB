using System.Diagnostics.Metrics;
using CIS.Metrics;
using CIS.Workflows;
using CIS.Workflows.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Metrics;

[TestClass]
public sealed class PipelineModuleMetricsTests
{
    [TestMethod]
    [DataRow(PipelineStatus.Succeeded, "success")]
    [DataRow(PipelineStatus.Failed, "error")]
    [DataRow(PipelineStatus.Cancelled, "cancelled")]
    public async Task ThreeReusableHandlesRecordTheExactRunnerSecondsAndBalanceEveryOutcome(PipelineStatus status, string outcome)
    {
        using var meter = new Meter("PipelineModuleOutcome");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        var instrumentation = new PipelineInstrumentation();
        PipelineRegistration registration = Registration("configured");
        instrumentation.ValidateDefinition("lookup", [registration]);
        instrumentation.AttachMetrics(runtime);
        instrumentation.AttachMetrics(runtime);
        IPipelineObservation observation = instrumentation.Begin(new("lookup", Guid.NewGuid(), [registration], false));
        observation.Started();
        observation.Started();
        observation.PhaseStarted(registration, PipelinePhase.Execute);
        observation.PhaseCompleted(registration, PipelinePhase.Execute, status, TimeSpan.FromSeconds(1.25), null);
        observation.Completed(status, TimeSpan.FromSeconds(4.5), null, 0);
        observation.Completed(status, TimeSpan.FromSeconds(999), null, 0);

        Assert.AreEqual(3, runtime.Definitions.Count);
        Assert.AreEqual(3, listener.Instruments.Count);
        MetricsPoint phase = listener.Points("cis.pipeline.step.duration").Single();
        Assert.AreEqual(1.25, (double)phase.Value);
        MetricsTestSupport.AssertTags(phase, new("pipeline", "lookup"), new("step", "configured"),
            new("phase", "execute"), new("outcome", outcome));
        MetricsPoint whole = listener.Points("cis.pipeline.execution.duration").Single();
        Assert.AreEqual(4.5, (double)whole.Value);
        MetricsTestSupport.AssertTags(whole, new("pipeline", "lookup"), new("outcome", outcome));
        CollectionAssert.AreEqual(new long[] { 1, -1 }, listener.Points("cis.pipeline.active.executions")
            .Select(point => (long)point.Value).ToArray());
    }

    [TestMethod]
    public async Task EntryBeforeAttachmentNeverReplaysOrDecrementsANewerBinding()
    {
        using var meter = new Meter("PipelineAttachment");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        var instrumentation = new PipelineInstrumentation();
        var info = new PipelineRunInfo("lookup", Guid.NewGuid(), [Registration("step")], false);
        IPipelineObservation early = instrumentation.Begin(info);
        early.Started();
        instrumentation.AttachMetrics(runtime);
        early.Completed(PipelineStatus.Succeeded, TimeSpan.FromSeconds(99), null, 0);
        Assert.IsEmpty(listener.Points("cis.pipeline.execution.duration"));
        Assert.IsEmpty(listener.Points("cis.pipeline.active.executions"));

        IPipelineObservation attached = instrumentation.Begin(info);
        attached.Started();
        attached.Completed(PipelineStatus.Succeeded, TimeSpan.FromSeconds(2), null, 0);
        Assert.AreEqual(2d, (double)listener.Points("cis.pipeline.execution.duration").Single().Value);
        CollectionAssert.AreEqual(new long[] { 1, -1 }, listener.Points("cis.pipeline.active.executions")
            .Select(point => (long)point.Value).ToArray());
    }

    [TestMethod]
    public async Task HandlerCancellationIsASeparatePhaseAndASecondHostCannotClaimTheOwner()
    {
        using var meter = new Meter("PipelinePhases");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        using var otherMeter = new Meter("OtherPipelineHost");
        await using var other = new MetricsRuntime(otherMeter);
        var instrumentation = new PipelineInstrumentation();
        instrumentation.AttachMetrics(runtime);
        Assert.ThrowsExactly<InvalidOperationException>(() => instrumentation.AttachMetrics(other));
        PipelineRegistration registration = Registration("step");
        IPipelineObservation observation = instrumentation.Begin(new("lookup", Guid.NewGuid(), [registration], false));
        observation.Started();
        observation.PhaseCompleted(registration, PipelinePhase.Execute, PipelineStatus.Failed, TimeSpan.FromSeconds(1), null);
        observation.PhaseCompleted(registration, PipelinePhase.Error, PipelineStatus.Cancelled, TimeSpan.FromSeconds(3), null);
        Assert.AreEqual(1L, listener.Points("cis.pipeline.active.executions").Sum(point => (long)point.Value),
            "Handler completion alone does not settle owned cleanup.");
        observation.Completed(PipelineStatus.Failed, TimeSpan.FromSeconds(7), null, 1);
        MetricsPoint[] phases = listener.Points("cis.pipeline.step.duration");
        Assert.HasCount(2, phases);
        MetricsTestSupport.AssertTags(phases[1], new("pipeline", "lookup"), new("step", "step"),
            new("phase", "error"), new("outcome", "cancelled"));
        Assert.AreEqual(7d, (double)listener.Points("cis.pipeline.execution.duration").Single().Value);
        Assert.AreEqual(0L, listener.Points("cis.pipeline.active.executions").Sum(point => (long)point.Value));
        Assert.AreEqual(0, other.Definitions.Count);
    }

    [TestMethod]
    public void ThrowingMeasurementHandlesCannotInterruptCompletionOrItsFinalDecrementAttempt()
    {
        var metrics = new ThrowingMetrics();
        var bundle = new PipelineMetrics(metrics);
        bundle.Started("lookup");
        bundle.PhaseCompleted("lookup", "step", PipelinePhase.Execute, PipelineStatus.Failed, TimeSpan.FromSeconds(1));
        bundle.Completed("lookup", PipelineStatus.Failed, TimeSpan.FromSeconds(2));
        Assert.AreEqual(3, metrics.Definitions);
        CollectionAssert.AreEqual(new long[] { 1, -1 }, metrics.ActiveAttempts.ToArray());
        Assert.AreEqual(2, metrics.TimerAttempts);
    }

    [TestMethod]
    public async Task ActualFinalClockFaultStillBalancesActiveWithoutInventingAnOverallDuration()
    {
        using var meter = new Meter("PipelineUnknownFinalDuration");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        var clock = new FinalReadFailureClock();
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddCisWorkflows();
        IHost host = builder.Build();
        try
        {
            await host.StartAsync().WaitAsync(MetricsTestSupport.Deadline);
            host.Services.GetRequiredService<PipelineInstrumentation>().AttachMetrics(runtime);
            IPipeline<object> pipeline = host.Services.GetRequiredService<IPipelineFactory>()
                .CreateBuilder("clock-fault", new object()).Add<SuccessfulStep>("one").Build();

            InvalidOperationException actual = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => pipeline.Execute())
                .WaitAsync(MetricsTestSupport.Deadline);
            Assert.AreSame(clock.Failure, actual);
            Assert.AreEqual(PipelineStatus.Failed, pipeline.Status);
            Assert.IsNull(pipeline.Duration);
            Assert.IsEmpty(listener.Points("cis.pipeline.execution.duration"), "An unavailable duration is not a fabricated zero.");
            Assert.AreEqual(1d, (double)listener.Points("cis.pipeline.step.duration").Single().Value);
            CollectionAssert.AreEqual(new long[] { 1, -1 }, listener.Points("cis.pipeline.active.executions")
                .Select(point => (long)point.Value).ToArray());
        }
        finally
        {
            try
            {
                await host.StopAsync(CancellationToken.None).WaitAsync(MetricsTestSupport.Deadline);
            }
            finally
            {
                if (host is IAsyncDisposable asyncHost)
                {
                    await asyncHost.DisposeAsync();
                }
                else
                {
                    host.Dispose();
                }
            }
        }
    }

    private static PipelineRegistration Registration(string alias) => new(0, "NotAnAutomaticMetricType", alias, typeof(object));

    public sealed class SuccessfulStep : IPipelineStep<object>
    {
        public Task<PipelineStepResult> Execute(object context, CancellationToken cancellationToken)
            => Task.FromResult(PipelineStepResult.Succeeded());
        public Task Error(object context, PipelineFailure failure, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FinalReadFailureClock : TimeProvider
    {
        private int reads;
        internal InvalidOperationException Failure { get; } = new("Final duration is unavailable.");
        public override long TimestampFrequency => 1_000;
        public override long GetTimestamp()
        {
            int read = Interlocked.Increment(ref reads);
            return read == 4 ? throw Failure : (read - 1) * 1_000L;
        }
    }

    private sealed class ThrowingMetrics : IMetrics, IUpDownCounter, CIS.Metrics.ITimer
    {
        internal int Definitions { get; private set; }
        internal int TimerAttempts { get; private set; }
        internal List<long> ActiveAttempts { get; } = [];
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
