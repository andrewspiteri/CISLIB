using CIS.Messaging;
using CIS.Operations;
using CIS.Security;
using CIS.Transactions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Messaging;

[TestClass]
public sealed class MessageContractTests
{
    [TestMethod]
    public void PortableNamesAndLogicalIdentitiesAreExactAndIndependentOfDeployment()
    {
        var contract = new MessageContract("/Country.Data_v1-update", 1);
        Assert.AreEqual(contract, new MessageContract("/Country.Data_v1-update", 1));
        Assert.AreNotEqual(contract, new MessageContract("/country.Data_v1-update", 1));
        Assert.AreEqual(128, new MessageContract(new string('a', 128), 1).Name.Length);
        Assert.ThrowsExactly<ArgumentException>(() => new MessageContract("clr:Thing", 1));
        Assert.ThrowsExactly<ArgumentException>(() => new MessageContract("message name", 1));
        Assert.ThrowsExactly<ArgumentException>(() => new MessageContract(new string('a', 129), 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MessageContract("message", 0));
        Guid id = Guid.NewGuid();
        var key = new MessageProcessingKey("app", "prod", "Country", id);
        Assert.AreEqual(key, new MessageProcessingKey("app", "prod", "Country", id));
        Assert.AreNotEqual(key, new MessageProcessingKey("app", "prod", "country", id));
        Assert.AreNotEqual(key, new MessageProcessingKey("app", "test", "Country", id));
        Assert.ThrowsExactly<ArgumentException>(() => new MessageProcessingKey("app", "prod", "Country", Guid.Empty));
        Assert.ThrowsExactly<ArgumentException>(() => new MessageReplyRoute("app", "prod", Guid.Empty));
        Assert.ThrowsExactly<ArgumentException>(() => new MessageOperationReference("Debit", "account", "key "));
    }

    [TestMethod]
    public void RetryBindingRetainsTheExactResolvedScopeAndResourceWithoutBecomingAuthority()
    {
        var identity = new OperationIdentity("server", "prod", "account:1", "Debit", "original-key");
        var target = new CapabilityTarget("server", "prod", "account", "1");
        var binding = new MessageOperationBinding(identity, target);
        Assert.AreEqual("account:1", binding.Identity.Scope);
        Assert.AreSame(target, binding.Resource);
        Assert.AreEqual(binding, new MessageOperationBinding(identity, new("server", "prod", "account", "1")));
        Assert.AreNotEqual(binding, new MessageOperationBinding(identity, new("server", "prod", "account", "2")));
        Assert.ThrowsExactly<ArgumentException>(() => new MessageOperationBinding(identity, new("other", "prod", "account", "1")));
        Assert.ThrowsExactly<ArgumentException>(() => new MessageOperationBinding(identity, new("server", "prod")));
    }

    [TestMethod]
    public void UnknownPublicationCannotBeClassifiedAsPermanentBlockingEvidence()
    {
        var confirmed = new MessagePublicationResult(MessagePublicationDisposition.Confirmed);
        var uncertain = new MessagePublicationResult(MessagePublicationDisposition.Unknown, MessagePublicationFailure.Transient,
            "messaging.confirmation_unknown");
        Assert.AreEqual(MessagePublicationFailure.None, confirmed.Failure);
        Assert.AreEqual(MessagePublicationDisposition.Unknown, uncertain.Disposition);
        Assert.ThrowsExactly<ArgumentException>(() => new MessagePublicationResult(MessagePublicationDisposition.Unknown));
        Assert.ThrowsExactly<ArgumentException>(() => new MessagePublicationResult(MessagePublicationDisposition.Unknown, MessagePublicationFailure.Unroutable));
        Assert.ThrowsExactly<ArgumentException>(() => new MessagePublicationResult(MessagePublicationDisposition.Confirmed, MessagePublicationFailure.Configuration));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MessagePublicationResult((MessagePublicationDisposition)99));
        EncodedMessage message = MessageWire.Encode(Envelope());
        Assert.ThrowsExactly<ArgumentException>(() => new MessagePublication(message, new("route"), MessageRecordPurpose.Retry, 2, Guid.NewGuid()));
        Assert.ThrowsExactly<ArgumentException>(() => new MessagePublication(message, new("route"), MessageRecordPurpose.Reply, 1, Guid.NewGuid()));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new MessagePublication(message, new("route"), MessageRecordPurpose.Application, 0, Guid.NewGuid()));
        Assert.ThrowsExactly<ArgumentException>(() => new MessagePublication(message, new("route"), MessageRecordPurpose.Application, 1, Guid.Empty));
    }

    [TestMethod]
    public void SubmissionExchangeProtocolAndBusinessDecisionsStaySeparate()
    {
        EncodedMessage message = MessageWire.Encode(Envelope());
        var receipt = new MessageStageReceipt(Guid.NewGuid(), message.Envelope.MessageId, "Ledger", "client", "test", message.Digest);
        var unknown = new TransactionCompletion(TransactionOutcome.Unknown, TransactionEndReason.CommitRequested,
            true, true, false);
        var submission = new MessageSubmission(MessageSubmissionOutcome.Unknown, receipt, unknown);
        var replyRoute = new MessageReplyRoute("client", "test", Guid.NewGuid());
        var reference = new MessageRequestReference("Lookup", Guid.NewGuid(), receipt, replyRoute, new("country.reply", 1),
            new("Lookup", "country:MT", "original-key"));
        var timedOut = new MessageRequestResult<string>(MessageExchangeOutcome.TimedOut, submission, reference);
        Assert.AreEqual(MessageSubmissionOutcome.Unknown, timedOut.Submission.Outcome);
        Assert.AreEqual("original-key", timedOut.Reference!.Operation!.Key);
        Assert.IsNull(timedOut.Response);
        var rejection = OperationDecision<string>.Reject("country.missing", "No matching country.");
        var business = new MessageResponse<string>(MessageProtocolDisposition.Completed, rejection);
        var conflict = new MessageResponse<string>(MessageProtocolDisposition.KeyConflict);
        Assert.AreEqual(OperationBusinessDecision.Rejected, business.Decision!.Decision);
        Assert.IsNull(conflict.Decision);
        Assert.ThrowsExactly<ArgumentException>(() => new MessageResponse<string>(MessageProtocolDisposition.Completed));
        Assert.ThrowsExactly<ArgumentException>(() => new MessageResponse<string>(MessageProtocolDisposition.KeyConflict, rejection));
        Assert.ThrowsExactly<ArgumentException>(() => new MessageSubmission(MessageSubmissionOutcome.OutboxCommitted, receipt, unknown));
        Assert.ThrowsExactly<ArgumentException>(() => new MessageSubmission(MessageSubmissionOutcome.Unknown, receipt));
        Assert.ThrowsExactly<ArgumentException>(() => new MessageRequestResult<string>(MessageExchangeOutcome.ResponseReceived,
            new(MessageSubmissionOutcome.NotSubmitted, receipt), reference, business));
        Assert.ThrowsExactly<ArgumentException>(() => new MessageRequestResult<string>(MessageExchangeOutcome.NotSubmitted,
            submission, reference));
        Assert.ThrowsExactly<ArgumentException>(() => new MessageStageResult(MessageStageDisposition.Staged));
        Assert.ThrowsExactly<ArgumentException>(() => new MessageStageResult(MessageStageDisposition.Overloaded, receipt));
        Assert.ThrowsExactly<ArgumentException>(() => new MessageRequestResult<string>(MessageExchangeOutcome.InvalidResponse,
            new(MessageSubmissionOutcome.OutboxCommitted, receipt), reference, business));
    }

    [TestMethod]
    public async Task PublicTicketSubstitutionsNeedNoInternalAccessOrTransactionOwner()
    {
        EncodedMessage message = MessageWire.Encode(Envelope());
        var receipt = new MessageStageReceipt(Guid.NewGuid(), message.Envelope.MessageId, "Ledger", "client", "test", message.Digest);
        var reference = new MessageRequestReference("Lookup", Guid.NewGuid(), receipt,
            new("client", "test", Guid.NewGuid()), new("reply", 1));
        var ticket = new ConsumerTicket(reference);
        var staged = new MessageRequestStage<string>(MessageStageDisposition.Staged, ticket);
        Assert.AreSame(reference, staged.Ticket!.Reference);
        Assert.ThrowsExactly<ArgumentException>(() => new MessageRequestStage<string>(MessageStageDisposition.Unavailable, ticket));
        await ticket.DisposeAsync();
        await ticket.DisposeAsync();
        Assert.AreEqual(1, ticket.Releases);
        Assert.AreEqual(receipt.MessageId, ticket.Reference.MessageId);
    }

    private static MessageEnvelope Envelope() => new(Guid.NewGuid(), MessageKind.Command, new("country.lookup", 1),
        "client", "test", DateTimeOffset.UnixEpoch, "null"u8.ToArray());

    private sealed class ConsumerTicket(MessageRequestReference reference) : MessageRequestTicket<string>(reference)
    {
        public int Releases
        {
            get; private set;
        }
        public override ValueTask DisposeAsync()
        {
            Releases = 1;
            return ValueTask.CompletedTask;
        }
    }
}
