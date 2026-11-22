using CIS.Jobs;
using CIS.Messaging;
using CIS.Transactions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Jobs.Catalog;

[TestClass]
public sealed class JobScheduleAdmissionTests
{
    [TestMethod]
    public void CalendarMetadataOwnsWeekdaysAndDeclaresExplicitDstAndCatchUpSemantics()
    {
        DayOfWeek[] days = [DayOfWeek.Friday, DayOfWeek.Monday];
        JobScheduleDescriptor schedule = JobScheduleDescriptor.Weekly(days, new TimeOnly(2, 0), "Europe/Malta",
            JobMissedRunPolicy.CatchUpBounded, 3, TimeSpan.FromDays(7));
        days[0] = DayOfWeek.Sunday;

        CollectionAssert.AreEqual(new[] { DayOfWeek.Monday, DayOfWeek.Friday }, schedule.Weekdays.ToArray());
        Assert.AreEqual("Europe/Malta", schedule.TimeZoneId);
        Assert.AreEqual(JobInvalidLocalTimePolicy.Skip, schedule.InvalidLocalTimePolicy);
        Assert.AreEqual(JobRepeatedLocalTimePolicy.OnceAtEarlierInstant, schedule.RepeatedLocalTimePolicy);
        Assert.AreEqual(3, schedule.MaximumCatchUp);
        Assert.ThrowsExactly<ArgumentException>(() => JobScheduleDescriptor.Weekly([], new(2, 0), "Europe/Malta", JobMissedRunPolicy.Skip));
        Assert.ThrowsExactly<ArgumentException>(() => JobScheduleDescriptor.Daily(new(2, 0), "GMT Standard Time", JobMissedRunPolicy.Skip));
        Assert.ThrowsExactly<ArgumentException>(() => JobScheduleDescriptor.FixedDelay(TimeSpan.FromMinutes(5), JobMissedRunPolicy.CatchUpBounded));
        Assert.ThrowsExactly<ArgumentException>(() => JobScheduleDescriptor.FixedDelay(TimeSpan.FromMinutes(5), JobMissedRunPolicy.Skip, 1, TimeSpan.FromDays(1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => JobScheduleDescriptor.FixedDelay(TimeSpan.Zero, JobMissedRunPolicy.Skip));
    }

    [TestMethod]
    public void ScheduledDefinitionsRemainInspectableDisabledWithoutAnySchedulingProvider()
    {
        JobCatalog catalog = Catalog();
        Assert.AreEqual("customer.dormancy", catalog.Inspect()[0].JobId);
        catalog.ValidateActivation(new(0));
        catalog.ValidateActivation(new(1, enabled: true, jobs:
            [new("customer-service", "customer.dormancy", Settings(), enabled: false)]));
        catalog.ValidateActivation(new(2, enabled: true, categories:
            [new("customer-service", "lifecycle", enabled: false)], jobs:
            [new("customer-service", "customer.dormancy", Settings(), enabled: true)]));
    }

    [TestMethod]
    public void EnabledActivationRequiresActualSourceAndCompatibleAutomaticEvaluator()
    {
        JobCatalog catalog = Catalog();
        var external = new JobConfigurationSnapshot(1, enabled: true, jobs:
            [new("customer-service", "customer.dormancy", Settings(), enabled: true)]);
        Assert.ThrowsExactly<InvalidOperationException>(() => catalog.ValidateActivation(external));
        catalog.ValidateActivation(external, new Source());

        var invalidZone = new JobConfigurationSnapshot(2, enabled: true, jobs:
            [new("customer-service", "customer.dormancy", new(JobExecutionMode.Scheduled,
                schedule: JobScheduleDescriptor.Daily(new(2, 0), "NotAnActual/TimeZone", JobMissedRunPolicy.Skip)), enabled: true)]);
        Assert.ThrowsExactly<InvalidOperationException>(() => catalog.ValidateActivation(invalidZone, new Source()));

        var automatic = new JobConfigurationSnapshot(2, enabled: true, jobs:
            [new("customer-service", "customer.dormancy", Settings(JobScheduledActivation.AutomaticRecurrence), enabled: true)]);
        Assert.ThrowsExactly<InvalidOperationException>(() => catalog.ValidateActivation(automatic, new Source()));
        Assert.ThrowsExactly<InvalidOperationException>(() => catalog.ValidateActivation(automatic, new Source(), new Evaluator(false)));
        catalog.ValidateActivation(automatic, new Source(), new Evaluator(true));
    }

    [TestMethod]
    public void AdmittedOccurrenceRetainsOriginalOperationAndImmutableSnapshotWithoutClaimingAuthority()
    {
        var operation = new MessageOperationReference("Dormancy", "customer-partition", "period-2026-11");
        JobExecutionSettings settings = Settings();
        EncodedMessage message = MessageWire.Encode(new(Guid.NewGuid(), MessageKind.Command, new("dormancy-input", 1),
            "scheduler-service", "test", DateTimeOffset.UtcNow, "{}"u8.ToArray(), operation: operation));
        var first = new AdmittedJobRun("customer-service", "customer.dormancy", "partition-1", "occurrence-1", operation,
            7, settings, message, "controlled-trigger", "admission-1");
        var redelivery = new AdmittedJobRun("customer-service", "customer.dormancy", "partition-1", "occurrence-1", operation,
            7, settings, message, "controlled-trigger", "admission-1");

        Assert.AreEqual(first.OccurrenceId, redelivery.OccurrenceId);
        Assert.AreEqual(first.Operation, redelivery.Operation);
        Assert.AreSame(settings, first.Settings);
        Assert.AreSame(message, first.Input);
        Assert.AreEqual(7L, first.AdmissionRevision);
        Assert.ThrowsExactly<ArgumentException>(() => new AdmittedJobRun("customer-service", "customer.dormancy", "partition-1",
            "occurrence-1", new("Dormancy", "customer-partition", "changed-period"), 7, settings, message, "controlled-trigger", "admission-1"));
        Assert.ThrowsExactly<ArgumentException>(() => new AdmittedJobRun("customer-service", "customer.dormancy", "partition-1",
            "occurrence-1", operation, 7, new(JobExecutionMode.Continuous), message, "controlled-trigger", "admission-1"));
    }

    [TestMethod]
    public void StructuredExecutionResultsCannotTurnUnknownOrUncommittedWorkIntoCompletion()
    {
        var unknown = new TransactionCompletion(TransactionOutcome.Unknown, TransactionEndReason.CommitRequested, true, true, true);
        var uncommitted = new TransactionCompletion(TransactionOutcome.NotCommitted, TransactionEndReason.RollbackRequested, false, true, true);
        Assert.ThrowsExactly<ArgumentException>(() => new ScheduledJobResult(ScheduledJobOutcome.Completed, completion: unknown));
        Assert.ThrowsExactly<ArgumentException>(() => new ScheduledJobResult(ScheduledJobOutcome.Completed, completion: uncommitted));
        Assert.ThrowsExactly<ArgumentException>(() => new ContinuousIterationResult(ContinuousIterationOutcome.MoreWork, completion: unknown));
        Assert.AreSame(unknown, new ScheduledJobResult(ScheduledJobOutcome.Indeterminate, completion: unknown).Completion);
        Assert.AreSame(unknown, new ContinuousIterationResult(ContinuousIterationOutcome.Indeterminate, completion: unknown).Completion);
    }

    [TestMethod]
    public void DefaultsAreExplicitBoundedAndRejectUnsupportedOverlapWithoutSelectingRecurrence()
    {
        var execution = new JobExecutionSettings(JobExecutionMode.Continuous);
        Assert.AreEqual(TimeSpan.FromSeconds(5), execution.IdleDelay);
        Assert.AreEqual(TimeSpan.FromSeconds(30), execution.MaximumDrain);
        Assert.AreEqual(TimeSpan.FromSeconds(5), execution.AcquisitionRetryDelay);
        Assert.IsNull(execution.Timeout);
        Assert.IsFalse(new JobConfigurationSnapshot(0).Enabled);
        Assert.IsFalse(new JobSettings("wallet-service", "wallet.compaction", execution).Enabled);
        Assert.AreEqual(1024, new JobRuntimeLimits().MaximumDefinitions);
        Assert.AreEqual(64, new JobRuntimeLimits().MaximumCategories);
        Assert.AreEqual(32, new JobRuntimeLimits().MaximumContinuousDuties);
        Assert.AreEqual(8, new JobRuntimeLimits().MaximumInvocations);
        Assert.AreEqual(128, new JobRuntimeLimits().MaximumPendingDispatches);
        Assert.AreEqual(100, new JobRuntimeLimits().MaximumSnapshotPageSize);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new JobExecutionSettings(JobExecutionMode.Continuous, idleDelay: TimeSpan.Zero));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new JobExecutionSettings(JobExecutionMode.Scheduled,
            overlapPolicy: (JobOverlapPolicy)1));
    }

    private static JobExecutionSettings Settings(JobScheduledActivation activation = JobScheduledActivation.ExternalOccurrences)
        => new(JobExecutionMode.Scheduled, schedule: JobScheduleDescriptor.Daily(new(2, 0), "Europe/Malta", JobMissedRunPolicy.RunLatestOnce),
            scheduledActivation: activation);

    private static JobCatalog Catalog() => new([new("customer-service", "lifecycle", "Lifecycle")],
        [JobHandlerBinding.Scheduled<int, Handler>(new("customer-service", "customer.dormancy", "Dormancy", "lifecycle",
            [JobExecutionMode.Scheduled], ["jobs.execute"]), new Codec())]);

    private sealed class Handler : IScheduledJobHandler<int>
    {
        public Task<ScheduledJobResult> ExecuteAsync(JobExecutionContext context, int input, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("Metadata validation must not invoke business handlers.");
    }

    private sealed class Codec : IMessageCodec<int>
    {
        public MessageContract Contract => new("dormancy-input", 1);
        public byte[] Encode(int value) => throw new AssertFailedException("Activation metadata does not encode payloads.");
        public bool TryDecode(MessageContract contract, ReadOnlyMemory<byte> json, out int value)
            => throw new AssertFailedException("Activation metadata does not decode payloads.");
    }

    private sealed class Source : IJobRunSource
    {
        public string SourceId => "controlled-trigger";
        public JobRunSourceCapabilities Capabilities => JobRunSourceCapabilities.ExternallyAdmittedOccurrences;
        public ValueTask<JobAdmissionValidation> ValidateAsync(AdmittedJobRun run, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("Activation inspects capabilities; actual admission validation belongs to dispatch.");
    }

    private sealed class Evaluator(bool supported) : IJobScheduleEvaluator
    {
        public bool CanEvaluate(JobScheduleDescriptor schedule) => supported;
    }
}
