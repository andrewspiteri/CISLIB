using CIS.CommonTesting;
using CIS.Messaging;
using CIS.Operations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.CommonTesting;

[TestClass]
public sealed class ScriptedMessagingClientTests
{
    [TestMethod]
    public async Task TypedRequestReturnsExactIndependentFactsAndDetachedMetadataWithoutRetainingInput()
    {
        var client = new ScriptedMessagingClient();
        IMessagingClient port = client;
        MessageRequestReference reference = Reference();
        var result = new MessageRequestResult<string>(MessageExchangeOutcome.ResponseReceived,
            new(MessageSubmissionOutcome.OutboxCommitted, reference.Receipt), reference,
            new(MessageProtocolDisposition.Completed, OperationDecision<string>.Reject("app.declined", "Original rejection")));
        client.EnqueueRequest<MutableInput, string>(result);
        IReadOnlyList<ScriptedMessagingCall> before = client.Calls;
        using var cancellation = new CancellationTokenSource();
        var input = new MutableInput { Amount = 7 };
        Assert.AreSame(result, await port.RequestAsync<MutableInput, string>(new("Lookup", input), cancellation.Token));
        input.Amount = 99;
        ScriptedMessagingCall call = client.Calls.Single();
        Assert.IsEmpty(before);
        Assert.AreEqual(ScriptedMessagingCallKind.Request, call.Kind);
        Assert.AreEqual("Lookup", call.Alias);
        Assert.AreEqual(typeof(MutableInput), call.RequestType);
        Assert.AreEqual(typeof(string), call.ResponseType);
        Assert.AreEqual(cancellation.Token, call.CancellationToken);
        Assert.IsNull(call.RequestAttemptId);
        Assert.AreEqual(MessageSubmissionOutcome.OutboxCommitted, result.Submission.Outcome);
        Assert.AreEqual(OperationBusinessDecision.Rejected, result.Response!.Decision!.Decision);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<ScriptedMessagingCall>)client.Calls).Clear());
    }

    [TestMethod]
    [DataRow(MessageExchangeOutcome.TimedOut)]
    [DataRow(MessageExchangeOutcome.Cancelled)]
    [DataRow(MessageExchangeOutcome.Unavailable)]
    [DataRow(MessageExchangeOutcome.Overloaded)]
    [DataRow(MessageExchangeOutcome.InvalidResponse)]
    public async Task ScriptedExchangeFactsNeverRewriteCommittedSubmission(MessageExchangeOutcome outcome)
    {
        var client = new ScriptedMessagingClient();
        MessageRequestReference reference = Reference();
        var result = new MessageRequestResult<string>(outcome, new(MessageSubmissionOutcome.OutboxCommitted, reference.Receipt), reference);
        client.EnqueueRequest<int, string>(result);
        Assert.AreSame(result, await client.RequestAsync<int, string>(new("Lookup", 1)));
        Assert.AreEqual(MessageSubmissionOutcome.OutboxCommitted, result.Submission.Outcome);
        Assert.IsNull(result.Response);
    }

    [TestMethod]
    public async Task KindAndGenericMismatchOrUnconfiguredCallsConsumeNeitherScriptNorRecord()
    {
        var client = new ScriptedMessagingClient();
        client.EnqueueRequest<int, string>(Unavailable());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.SendAsync("commands", 1));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.RequestAsync<string, string>(new("Lookup", "wrong")));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.RequestAsync<int, int>(new("Lookup", 1)));
        Assert.AreEqual(1, client.PendingScripts);
        Assert.IsEmpty(client.Calls);
        await client.RequestAsync<int, string>(new("Lookup", 1));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.RequestAsync<int, string>(new("Lookup", 1)));
        Assert.HasCount(1, client.Calls);
        Assert.ThrowsExactly<ArgumentException>(() => client.PublishAsync(new string('x', 65), 1));
    }

    [TestMethod]
    public async Task ActualPreCancellationConsumesNothingAndOriginalCancellationFaultRemainsFaulted()
    {
        var client = new ScriptedMessagingClient();
        var original = new OperationCanceledException("Unrelated scripted fault");
        client.EnqueueRequestFailure<int, string>(original);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Task<MessageRequestResult<string>> cancelled = client.RequestAsync<int, string>(new("Lookup", 1), cancellation.Token);
        OperationCanceledException observed = await Assert.ThrowsAsync<OperationCanceledException>(() => cancelled);
        Assert.AreEqual(cancellation.Token, observed.CancellationToken);
        Assert.IsTrue(cancelled.IsCanceled);
        Assert.AreEqual(1, client.PendingScripts);
        Assert.IsEmpty(client.Calls);
        Task<MessageRequestResult<string>> faulted = client.RequestAsync<int, string>(new("Lookup", 1));
        Assert.AreSame(original, await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => faulted));
        Assert.IsTrue(faulted.IsFaulted);
        var sendFault = new InvalidOperationException("Original send failure");
        client.EnqueueSendFailure<int>(sendFault);
        Assert.AreSame(sendFault, await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.SendAsync("commands", 1)));
    }

    [TestMethod]
    public async Task CallerOwnedAndScriptedTicketsStayExplicitlyOwnedAcrossWaitSuccessAndFault()
    {
        var client = new ScriptedMessagingClient();
        MessageRequestReference reference = Reference();
        var scripted = new ScriptedMessageRequestTicket<string>(reference);
        MessageRequestResult<string> result = Unavailable();
        client.EnqueueWaitForReply(result);
        Assert.AreSame(result, await client.WaitForReplyAsync(scripted));
        Assert.IsFalse(scripted.DisposeRequested);
        Assert.AreEqual(reference.RequestAttemptId, client.Calls.Single().RequestAttemptId);
        Assert.IsNull(client.Calls.Single().RequestType);
        await scripted.DisposeAsync();
        await scripted.DisposeAsync();
        Assert.IsTrue(scripted.DisposeRequested);

        var callerOwned = new CallerTicket(reference);
        var original = new InvalidOperationException("Original wait failure");
        client.EnqueueWaitForReplyFailure<string>(original);
        Assert.AreSame(original, await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.WaitForReplyAsync(callerOwned)));
        Assert.AreEqual(0, callerOwned.Disposals);
        await callerOwned.DisposeAsync();
        Assert.AreEqual(1, callerOwned.Disposals);
    }

    [TestMethod]
    public async Task FiniteQueueAndNonEvictingRecordLimitsPreserveTheNextScript()
    {
        var client = new ScriptedMessagingClient(scriptCapacity: 1, recordingCapacity: 1);
        var publication = new MessageSubmission(MessageSubmissionOutcome.NotSubmitted);
        client.EnqueuePublish<int>(publication);
        Assert.ThrowsExactly<InvalidOperationException>(() => client.EnqueueSend<int>(publication));
        Assert.AreSame(publication, await client.PublishAsync("events", 7));
        client.EnqueueSend<int>(publication);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => client.SendAsync("commands", 7));
        Assert.AreEqual(1, client.PendingScripts);
        Assert.HasCount(1, client.Calls);
        Assert.AreEqual(ScriptedMessagingCallKind.Publish, client.Calls[0].Kind);
        Assert.IsNull(client.Calls[0].ResponseType);
    }

    private static MessageRequestResult<string> Unavailable()
        => new(MessageExchangeOutcome.Unavailable, new(MessageSubmissionOutcome.NotSubmitted));

    private static MessageRequestReference Reference()
    {
        var receipt = new MessageStageReceipt(Guid.NewGuid(), Guid.NewGuid(), "Ledger", "app", "test", new MessageDigest(new byte[32]));
        return new("Lookup", Guid.NewGuid(), receipt, new("app", "test", Guid.NewGuid()), new("lookup.result", 1));
    }

    private sealed class MutableInput
    {
        internal int Amount
        {
            get; set;
        }
    }

    private sealed class CallerTicket(MessageRequestReference reference) : MessageRequestTicket<string>(reference)
    {
        internal int Disposals
        {
            get; private set;
        }
        public override ValueTask DisposeAsync()
        {
            Disposals++;
            return ValueTask.CompletedTask;
        }
    }
}
