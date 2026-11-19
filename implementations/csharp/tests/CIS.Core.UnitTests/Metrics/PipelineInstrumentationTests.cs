using CIS.CommonTesting;
using CIS.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Metrics;

[TestClass]
public sealed class PipelineInstrumentationTests
{
    [TestMethod]
    public void AliasAdmissionIsTransactionalAndRepeatedPairsDoNotConsumeCapacity()
    {
        var instrumentation = new PipelineInstrumentation();
        instrumentation.ValidateDefinition("first", Registrations(61));
        Assert.ThrowsExactly<InvalidOperationException>(() => instrumentation.ValidateDefinition("second", Registrations(4)));
        instrumentation.ValidateDefinition("second", Registrations(3));
        instrumentation.ValidateDefinition("second", Registrations(3));
        Assert.ThrowsExactly<InvalidOperationException>(() => instrumentation.ValidateDefinition("second", [Registration("new") ]));
    }

    [TestMethod]
    public void NamesHaveOneFiniteAdmissionDomainEvenWithoutMetrics()
    {
        var instrumentation = new PipelineInstrumentation();
        for (int index = 0; index < 16; index++)
        {
            instrumentation.ValidateDefinition("pipeline" + index, [Registration("step")]);
        }

        Assert.ThrowsExactly<InvalidOperationException>(() => instrumentation.ValidateDefinition("seventeenth", [Registration("step")]));
        instrumentation.ValidateDefinition("pipeline0", [Registration("step")]);
    }

    [TestMethod]
    public void DuplicateRegistrationsAndTypeDisplayNamesAggregateUnderTheConfiguredAlias()
    {
        var instrumentation = new PipelineInstrumentation();
        instrumentation.ValidateDefinition("lookup", Enumerable.Range(0, 200)
            .Select(index => new PipelineRegistration(index, "DisplayType" + index, "step", typeof(object))).ToArray());
        instrumentation.ValidateDefinition("other", Registrations(63));
        Assert.ThrowsExactly<InvalidOperationException>(() => instrumentation.ValidateDefinition("other", [Registration("overflow")]));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("line\nbreak")]
    [DataRow("hidden\u200bformat")]
    [DataRow("line\u2028separator")]
    public void NonprintingNamesAreRejectedBeforeAnyMetadataPublication(string invalid)
    {
        var instrumentation = new PipelineInstrumentation();
        Assert.ThrowsExactly<ArgumentException>(() => instrumentation.ValidateDefinition(invalid, [Registration("step")]));
        Assert.ThrowsExactly<ArgumentException>(() => instrumentation.ValidateDefinition("valid", [Registration(invalid)]));
        instrumentation.ValidateDefinition("valid", [Registration("step")]);
    }

    [TestMethod]
    public void UnicodeLimitsUseUtf8BytesAndAcceptTheExactBoundary()
    {
        var instrumentation = new PipelineInstrumentation();
        string valid = new('\u00e9', 128);
        instrumentation.ValidateDefinition(valid, [Registration(valid)]);
        Assert.ThrowsExactly<ArgumentException>(() => instrumentation.ValidateDefinition(valid + "x", [Registration("step")]));
        Assert.ThrowsExactly<ArgumentException>(() => instrumentation.ValidateDefinition("valid", [Registration(valid + "x")]));
    }

    [TestMethod]
    public void StructuredEventsHaveOneTerminalSummaryAndBoundTheExplicitSafeBusinessMessage()
    {
        var factory = new CaptureFactory();
        var instrumentation = new PipelineInstrumentation(factory);
        PipelineRegistration registration = Registration("find");
        Guid id = Guid.NewGuid();
        IPipelineObservation observation = instrumentation.Begin(new("lookup", id, [registration], true));
        var primary = new PipelineFailure("country.missing", "Safe\n" + new string('x', 600), PipelinePhase.Execute,
            PipelineFailureKind.ReportedFailure, 0, "Find");
        observation.Started();
        observation.PhaseStarted(registration, PipelinePhase.Execute);
        observation.PhaseCompleted(registration, PipelinePhase.Execute, PipelineStatus.Failed, TimeSpan.FromSeconds(1.25), primary);
        observation.PhaseStarted(registration, PipelinePhase.Error);
        observation.PhaseCompleted(registration, PipelinePhase.Error, PipelineStatus.Succeeded, TimeSpan.FromSeconds(0.5), null);
        observation.Completed(PipelineStatus.Failed, TimeSpan.FromSeconds(2), primary, 0);
        observation.Completed(PipelineStatus.Failed, TimeSpan.FromSeconds(100), primary, 0);

        RecordedLog summary = factory.Logger.Entries.Single(entry => entry.EventId.Name == "pipeline.completed");
        Assert.AreEqual(LogLevel.Warning, summary.Level);
        Assert.AreEqual(2d, summary.Fields["DurationSeconds"]);
        Assert.AreEqual(id, summary.Fields["ExecutionId"]);
        Assert.AreEqual(1, summary.Fields["ForwardCount"]);
        Assert.AreEqual(1, summary.Fields["ErrorCount"]);
        Assert.IsTrue(factory.Logger.Entries.Any(entry => entry.Level == LogLevel.Debug));
        Assert.IsTrue(factory.Logger.Entries.All(entry => entry.Exception is null));
        RecordedLog failed = factory.Logger.Entries.Single(entry => entry.EventId.Name == "pipeline.phase.completed" && entry.Level == LogLevel.Warning);
        string message = (string)failed.Fields["ErrorMessage"]!;
        Assert.AreEqual(512, message.Length);
        Assert.IsTrue(message.StartsWith("Safe ", StringComparison.Ordinal));
        Assert.IsFalse(message.Contains('\n'));
        Assert.IsFalse(summary.Fields.ContainsKey("ErrorMessage"), "The terminal summary does not duplicate full diagnostics.");
    }

    [TestMethod]
    public void OriginalFaultDiagnosticsAppearOnceAtTheirOriginAndNeverInTheTerminalSummary()
    {
        var factory = new CaptureFactory();
        var instrumentation = new PipelineInstrumentation(factory);
        var original = new InvalidOperationException("Retained original exception diagnostic");
        var failure = new PipelineFailure("Pipeline.Exception", "not an automatic message field", PipelinePhase.Execute,
            PipelineFailureKind.Exception, 0, "Find", original);
        IPipelineObservation observation = instrumentation.Begin(new("lookup", Guid.NewGuid(), [Registration("find")], true));
        observation.Started();
        observation.PhaseCompleted(Registration("find"), PipelinePhase.Execute, PipelineStatus.Failed, TimeSpan.Zero, failure);
        observation.Completed(PipelineStatus.Failed, TimeSpan.Zero, failure, 0);
        RecordedLog origin = factory.Logger.Entries.Single(entry => entry.Exception is not null);
        Assert.AreSame(original, origin.Exception);
        Assert.AreEqual("pipeline.phase.completed", origin.EventId.Name);
        Assert.AreEqual(LogLevel.Error, origin.Level);
        Assert.IsNull(origin.Fields["ErrorMessage"]);
        Assert.IsNull(factory.Logger.Entries.Single(entry => entry.EventId.Name == "pipeline.completed").Exception);

        factory.Logger.Clear();
        IPipelineObservation lifecycle = instrumentation.Begin(new("construction", Guid.NewGuid(), [], true));
        lifecycle.Started();
        lifecycle.LifecycleFailure(failure with { Phase = PipelinePhase.Construction });
        lifecycle.Completed(PipelineStatus.Failed, TimeSpan.Zero, failure, 0);
        Assert.AreSame(original, factory.Logger.Entries.Single(entry => entry.Exception is not null).Exception);
        Assert.AreEqual("pipeline.lifecycle.failed", factory.Logger.Entries.Single(entry => entry.Exception is not null).EventId.Name);
    }

    [TestMethod]
    public void QuietModeAndThrowingLoggerNeverAffectTheObservationLifecycle()
    {
        var factory = new CaptureFactory();
        var quiet = new PipelineInstrumentation(factory).Begin(new("quiet", Guid.NewGuid(), [Registration("step")], false));
        Observe(quiet);
        Assert.IsEmpty(factory.Logger.Entries);

        var throwing = new PipelineInstrumentation(new ThrowingFactory()).Begin(new("throwing", Guid.NewGuid(), [], true));
        Observe(throwing);
    }

    private static void Observe(IPipelineObservation observation)
    {
        var failure = new PipelineFailure("failure", "not logged", PipelinePhase.Disposal, PipelineFailureKind.Exception);
        observation.Started();
        observation.PhaseStarted(Registration("step"), PipelinePhase.Execute);
        observation.PhaseCompleted(Registration("step"), PipelinePhase.Execute, PipelineStatus.Succeeded, TimeSpan.Zero, null);
        observation.CleanupBudgetExpired();
        observation.LifecycleFailure(failure);
        observation.Completed(PipelineStatus.Failed, TimeSpan.Zero, failure, 1);
    }

    private static PipelineRegistration Registration(string alias) => new(0, "Find", alias, typeof(object));
    private static PipelineRegistration[] Registrations(int count) => Enumerable.Range(0, count)
        .Select(index => new PipelineRegistration(index, "Display" + index, "alias" + index, typeof(object))).ToArray();

    private sealed class CaptureFactory : ILoggerFactory
    {
        internal RecordingLogger<PipelineInstrumentationTests> Logger { get; } = new();
        public ILogger CreateLogger(string categoryName) => Logger;
        public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();
        public void Dispose() { }
    }

    private sealed class ThrowingFactory : ILoggerFactory, ILogger
    {
        public ILogger CreateLogger(string categoryName) => this;
        public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => throw new InvalidOperationException("logger defect");
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => throw new InvalidOperationException("logger defect");
    }
}
