using CIS.CommonTesting;
using CIS.Jobs;
using CIS.Messaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.CommonTesting.Jobs;

[TestClass]
public sealed class ScriptedJobRunSourceTests
{
    [TestMethod]
    public async Task PublicSourceReturnsExactExplicitFactAndRecordsOnlyDetachedAdmissionMetadata()
    {
        var source = new ScriptedJobRunSource("controlled-source");
        IJobRunSource port = source;
        AdmittedJobRun run = Run();
        var original = new JobAdmissionValidation(true, "test.explicit");
        source.EnqueueValidation(run, original);
        IReadOnlyList<ScriptedJobRunCall> before = source.Calls;
        Assert.AreEqual(JobRunSourceCapabilities.ExternallyAdmittedOccurrences, port.Capabilities);
        Assert.AreSame(original, await port.ValidateAsync(run));
        Assert.IsEmpty(before);
        Assert.HasCount(1, source.Calls);
        ScriptedJobRunCall call = source.Calls.Single();
        Assert.AreEqual(run.Operation, call.Operation);
        Assert.AreSame(run.Settings, call.Settings);
        Assert.AreEqual(run.Input.Digest, call.InputDigest);
        Assert.AreEqual(run.Input.Length, call.InputLength);
        Assert.AreEqual(run.OccurrenceId, call.OccurrenceId);
        Assert.AreEqual(run.AdmissionRevision, call.AdmissionRevision);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<ScriptedJobRunCall>)source.Calls).Clear());
        Assert.AreEqual(0, source.PendingScripts);
    }

    [TestMethod]
    public async Task ForgedOriginalOperationOrInputCannotConsumeAnExactScript()
    {
        var source = new ScriptedJobRunSource("controlled-source");
        AdmittedJobRun expected = Run();
        source.EnqueueValidation(expected, new(true));
        AdmittedJobRun changedOperation = Run(expected.Settings, new("Dormancy", "business-scope", "forged-key"));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => source.ValidateAsync(changedOperation).AsTask());
        AdmittedJobRun changedInput = Run(expected.Settings, expected.Operation, "8");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => source.ValidateAsync(changedInput).AsTask());
        Assert.IsEmpty(source.Calls);
        Assert.AreEqual(1, source.PendingScripts);
        Assert.IsTrue((await source.ValidateAsync(expected)).Admitted);
    }

    [TestMethod]
    public async Task PrecancellationConsumesNothingAndScriptedFaultPreservesItsOriginalIdentity()
    {
        var source = new ScriptedJobRunSource("controlled-source");
        AdmittedJobRun run = Run();
        var original = new OperationCanceledException("scripted original fault");
        source.EnqueueFailure(run, original);
        using var caller = new CancellationTokenSource();
        caller.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => source.ValidateAsync(run, caller.Token).AsTask());
        Assert.AreEqual(1, source.PendingScripts);
        Assert.IsEmpty(source.Calls);
        Task<JobAdmissionValidation> fault = source.ValidateAsync(run).AsTask();
        Assert.IsTrue(fault.IsFaulted);
        Assert.AreSame(original, await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fault));
        Assert.AreEqual(0, source.PendingScripts);
    }

    [TestMethod]
    public async Task ScriptAndRecordingBoundsAreIndependentAndNeverDiscardPendingScripts()
    {
        var source = new ScriptedJobRunSource("controlled-source", scriptCapacity: 1, recordingCapacity: 1);
        AdmittedJobRun run = Run();
        source.EnqueueValidation(run, new(true));
        Assert.ThrowsExactly<InvalidOperationException>(() => source.EnqueueValidation(run, new(false)));
        await source.ValidateAsync(run);
        source.EnqueueValidation(run, new(false));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => source.ValidateAsync(run).AsTask());
        Assert.AreEqual(1, source.PendingScripts);
        Assert.HasCount(1, source.Calls);
    }

    [TestMethod]
    public async Task ExplicitRedeliveryScriptsKeepOccurrenceAndBusinessIdentityWithoutInventingRecovery()
    {
        var source = new ScriptedJobRunSource("controlled-source");
        AdmittedJobRun original = Run();
        var redelivery = new AdmittedJobRun(original.ExecutionScope, original.JobId, original.ResourceId, original.OccurrenceId,
            original.Operation, original.AdmissionRevision, original.Settings, original.Input, original.SourceId,
            original.AdmissionReference, original.ScheduledAtUtc);
        source.EnqueueValidation(original, new(true));
        source.EnqueueValidation(redelivery, new(true));
        await source.ValidateAsync(original);
        await source.ValidateAsync(redelivery);
        Assert.AreEqual(source.Calls[0].OccurrenceId, source.Calls[1].OccurrenceId);
        Assert.AreEqual(source.Calls[0].Operation, source.Calls[1].Operation);
        Assert.AreEqual(source.Calls[0].InputDigest, source.Calls[1].InputDigest);
    }

    [TestMethod]
    public void SourceIdentityAndStorageBoundsRejectInvalidValues()
    {
        Assert.Throws<ArgumentException>(() => new ScriptedJobRunSource("\ud800"));
        Assert.Throws<ArgumentException>(() => new ScriptedJobRunSource("control\nsource"));
        Assert.Throws<ArgumentException>(() => new ScriptedJobRunSource(new string('é', 65)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ScriptedJobRunSource("source", 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ScriptedJobRunSource("source", recordingCapacity: 65_537));
    }

    private static AdmittedJobRun Run(JobExecutionSettings? settings = null, MessageOperationReference? operation = null,
        string payload = "7")
    {
        settings ??= new(JobExecutionMode.Scheduled,
            schedule: JobScheduleDescriptor.FixedDelay(TimeSpan.FromMinutes(5), JobMissedRunPolicy.Skip));
        operation ??= new("Dormancy", "business-scope", "original-key");
        EncodedMessage message = MessageWire.Encode(new(Guid.Parse("6cdeac81-2230-4d6a-8b22-1691f8c30dc4"), MessageKind.Command,
            new("dormancy-input", 1), "claimed-producer", "claimed-environment", DateTimeOffset.UnixEpoch,
            System.Text.Encoding.UTF8.GetBytes(payload), operation: operation));
        return new("customer-service", "customer.dormancy", "partition", "stable-occurrence", operation, 7, settings,
            message, "controlled-source", "same-admission", DateTimeOffset.UnixEpoch);
    }
}
