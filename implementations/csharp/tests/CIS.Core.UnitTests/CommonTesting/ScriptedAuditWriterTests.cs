using System.Text;
using CIS.Audit;
using CIS.CommonTesting;
using CIS.Messaging;
using CIS.Transactions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.CommonTesting;

[TestClass]
public sealed class ScriptedAuditWriterTests
{
    [TestMethod]
    public async Task StagingAndSubmissionRetainExactFactsWithoutOwningTheTransactionOrRetainingPayloads()
    {
        CapturedAuditRecord record = Record();
        var writer = new ScriptedAuditWriter();
        IAuditWriter port = writer;
        var context = new Participant();
        MessageStageReceipt receipt = Receipt();
        var staging = new MessageStageResult(MessageStageDisposition.Staged, receipt);
        var completion = new TransactionCompletion(TransactionOutcome.Unknown, TransactionEndReason.CommitRequested,
            commitAttempted: true, rollbackAttempted: true, rollbackConfirmed: true);
        var submission = new MessageSubmission(MessageSubmissionOutcome.Unknown, receipt, completion);
        writer.EnqueueStage(staging);
        writer.EnqueueSubmit(submission);
        IReadOnlyList<ScriptedAuditCall> before = writer.Calls;

        AuditStageResult staged = await port.StageAsync(context, record);
        AuditSubmissionResult submitted = await port.SubmitAsync(record);

        Assert.AreEqual(record.AuditId, staged.AuditId);
        Assert.AreSame(staging, staged.Staging);
        Assert.AreSame(receipt, staged.Receipt);
        Assert.AreSame(submission, submitted.Submission);
        Assert.AreSame(completion, submitted.Completion);
        Assert.AreEqual(AuditSubmissionOutcome.Unknown, submitted.Outcome);
        Assert.IsEmpty(before);
        Assert.HasCount(2, writer.Calls);
        Assert.AreEqual("Ledger", writer.Calls[0].DatabaseName);
        Assert.IsNull(writer.Calls[1].DatabaseName);
        Assert.IsFalse(context.IsRollbackOnly);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<ScriptedAuditCall>)writer.Calls).Clear());
        Assert.IsFalse(typeof(ScriptedAuditCall).GetProperties().Any(property =>
            property.PropertyType == typeof(CapturedAuditRecord) || property.PropertyType == typeof(ILocalTransactionContext)));
    }

    [TestMethod]
    public async Task KindMismatchAndExhaustionDoNotInventAcceptanceOrConsumeTheNextScript()
    {
        CapturedAuditRecord record = Record();
        var writer = new ScriptedAuditWriter();
        writer.EnqueueStage(new(MessageStageDisposition.Overloaded, error: new("audit.overloaded", "Capacity refused.")));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => writer.SubmitAsync(record));
        Assert.AreEqual(1, writer.PendingScripts);
        Assert.IsEmpty(writer.Calls);
        Assert.ThrowsExactly<ArgumentException>(() => writer.StageAsync(new Participant(new string('x', 65)), record));
        Assert.AreEqual(1, writer.PendingScripts);
        Assert.AreEqual(AuditStageDisposition.Overloaded, (await writer.StageAsync(new Participant(), record)).Disposition);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => writer.StageAsync(new Participant(), record));
        Assert.HasCount(1, writer.Calls);
    }

    [TestMethod]
    public async Task PreCancellationConsumesNothingAndAnOriginalScriptedCancellationRemainsFaulted()
    {
        CapturedAuditRecord record = Record();
        var writer = new ScriptedAuditWriter();
        var original = new OperationCanceledException("Independent script failure.");
        writer.EnqueueSubmitFailure(original);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Task<AuditSubmissionResult> cancelledCall = writer.SubmitAsync(record, cancelled.Token);
        OperationCanceledException observed = await Assert.ThrowsAsync<OperationCanceledException>(() => cancelledCall);
        Assert.AreEqual(cancelled.Token, observed.CancellationToken);
        Assert.IsTrue(cancelledCall.IsCanceled);
        Assert.AreEqual(1, writer.PendingScripts);
        Assert.IsEmpty(writer.Calls);

        Task<AuditSubmissionResult> faultedCall = writer.SubmitAsync(record);
        Assert.AreSame(original, await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => faultedCall));
        Assert.IsTrue(faultedCall.IsFaulted);
    }

    [TestMethod]
    public async Task ScriptAndRecordingCapacitiesAreIndependentAndBounded()
    {
        CapturedAuditRecord record = Record();
        var writer = new ScriptedAuditWriter(scriptCapacity: 1, recordingCapacity: 1);
        writer.EnqueueSubmit(new(MessageSubmissionOutcome.NotSubmitted));
        Assert.ThrowsExactly<InvalidOperationException>(() => writer.EnqueueSubmit(new(MessageSubmissionOutcome.NotSubmitted)));
        await writer.SubmitAsync(record);
        writer.EnqueueSubmit(new(MessageSubmissionOutcome.NotSubmitted));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => writer.SubmitAsync(record));
        Assert.AreEqual(1, writer.PendingScripts);
        Assert.HasCount(1, writer.Calls);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ScriptedAuditWriter(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ScriptedAuditWriter(recordingCapacity: 65_537));
    }

    [TestMethod]
    public void PublicResultsRejectProvisionalRequestFactsAndKeepAllThreeActualSubmissionStates()
    {
        Guid id = Guid.NewGuid();
        MessageStageReceipt receipt = Receipt();
        var provisional = new MessageSubmission(MessageSubmissionOutcome.NotConfirmed, receipt);
        Assert.ThrowsExactly<ArgumentException>(() => new AuditSubmissionResult(id, provisional));
        Assert.ThrowsExactly<ArgumentException>(() => new ScriptedAuditWriter().EnqueueSubmit(provisional));
        Assert.ThrowsExactly<ArgumentException>(() => new AuditSubmissionResult(Guid.Empty, new(MessageSubmissionOutcome.NotSubmitted)));
        Assert.AreEqual(AuditSubmissionOutcome.NotSubmitted,
            new AuditSubmissionResult(id, new(MessageSubmissionOutcome.NotSubmitted)).Outcome);
        Assert.AreEqual(AuditSubmissionOutcome.OutboxCommitted,
            new AuditSubmissionResult(id, new(MessageSubmissionOutcome.OutboxCommitted, receipt)).Outcome);
    }

    private static CapturedAuditRecord Record()
    {
        const string json = """
            {"auditVersion":1,"auditId":"96f27915-bbb4-46f5-8ec9-f2cdcba1db89","eventType":"example.changed","eventVersion":1,"category":"business","occurredAtUtc":"2026-11-23T00:00:00Z","recordedAtUtc":"2026-11-23T00:00:00Z","producer":{"application":"app","environment":"test"},"actor":{"kind":"unknown"},"outcome":"unknown","details":{}}
            """;
        var codec = new AuditRecordCodec();
        Assert.IsTrue(codec.TryDecode(codec.Contract, Encoding.UTF8.GetBytes(json), out AuditRecord? record));

        // Friend-assembly setup only. Public consumers must use the trusted capture port.
        return new CapturedAuditRecord(record!);
    }

    private static MessageStageReceipt Receipt() => new(Guid.NewGuid(), Guid.NewGuid(), "Ledger", "app", "test", new(new byte[32]));

    private sealed class Participant(string alias = "Ledger") : ILocalTransactionContext
    {
        public string DatabaseName => alias;
        public TransactionIsolation Isolation => TransactionIsolation.ReadCommitted;
        public bool IsRollbackOnly
        {
            get; private set;
        }
        public string? RollbackReasonCode
        {
            get; private set;
        }

        public void MarkRollbackOnly(string reasonCode)
        {
            IsRollbackOnly = true;
            RollbackReasonCode ??= reasonCode;
        }
    }
}
