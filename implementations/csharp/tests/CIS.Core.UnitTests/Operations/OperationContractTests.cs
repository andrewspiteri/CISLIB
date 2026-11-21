using CIS.Operations;
using CIS.Transactions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Operations;

[TestClass]
public sealed class OperationContractTests
{
    [TestMethod]
    public void CompleteIdentityIsOrdinalBoundedAndIndependentOfContractDeploymentOrTracing()
    {
        var identity = new OperationIdentity("app", "production", "account:1", "Debit", "Key-1");
        Assert.AreEqual(identity, new OperationIdentity("app", "production", "account:1", "Debit", "Key-1"));
        Assert.AreNotEqual(identity, new OperationIdentity("app", "production", "account:1", "Debit", "key-1"));
        Assert.AreNotEqual(identity, new OperationIdentity("app", "production", "account:2", "Debit", "Key-1"));
        Assert.AreNotEqual(identity, new OperationIdentity("app", "production", "account:1", "debit", "Key-1"));
        Assert.ThrowsExactly<ArgumentException>(() => new OperationIdentity("app", "prod", "scope", "bad/name", "key"));
        Assert.ThrowsExactly<ArgumentException>(() => new OperationIdentity("app", "prod", "scope", "Name", "key "));
        Assert.ThrowsExactly<ArgumentException>(() => new OperationIdentity("app", "prod", "scope", "Name", new string('a', 129)));
        Assert.ThrowsExactly<ArgumentException>(() => new OperationIdentity("app", "prod", new string('é', 129), "Name", "key"));
        Assert.ThrowsExactly<ArgumentException>(() => new OperationIdentity("app", "prod", "scope\ud800", "Name", "key"));
        Assert.ThrowsExactly<ArgumentException>(() => new OperationIdentity("app", "prod", "scope\n", "Name", "key"));
        Assert.AreEqual(256, System.Text.Encoding.UTF8.GetByteCount(new OperationIdentity("app", "prod", new string('é', 128), "Name", "key").Scope));
        Assert.ThrowsExactly<ArgumentException>(() => new OperationContract("bad contract", 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new OperationContract("stable.request", 0));
    }

    [TestMethod]
    public void TypedBusinessDecisionExecutionAndPersistenceCannotBeCollapsed()
    {
        OperationDecision<string> rejection = OperationDecision<string>.Reject("funds.insufficient", "Insufficient funds.");
        var prepared = new OperationExecution<string>(OperationExecutionDisposition.Prepared, rejection);
        var replayed = new OperationExecution<string>(OperationExecutionDisposition.Replayed, rejection);
        Assert.AreEqual(OperationBusinessDecision.Rejected, prepared.Decision!.Decision);
        Assert.AreSame(rejection, replayed.Decision);
        Assert.AreEqual("funds.insufficient", prepared.Decision.Error!.Code);
        Assert.ThrowsExactly<ArgumentException>(() => new OperationExecution<string>(OperationExecutionDisposition.Prepared));
        Assert.ThrowsExactly<ArgumentException>(() => new OperationExecution<string>(OperationExecutionDisposition.KeyConflict, rejection));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new OperationExecution<string>((OperationExecutionDisposition)999));
        Assert.ThrowsExactly<ArgumentException>(() => new OperationError("bad\ncode", "Safe message."));
        Assert.ThrowsExactly<ArgumentException>(() => new OperationError("code", new string('é', 513)));
        Assert.ThrowsExactly<ArgumentException>(() => new OperationError("code", "bad\0message"));
    }

    [TestMethod]
    public void FoundRemainsFoundDespiteFreshReadScopeCleanupFailures()
    {
        var error = new TransactionError(TransactionErrorPhase.ConnectionDispose,
            "transaction.connection_disposal_failed", "Safe cleanup failure.");
        var readCompletion = new TransactionCompletion(TransactionOutcome.NotCommitted,
            TransactionEndReason.RollbackRequested, false, true, true, cleanupErrors: [error]);
        OperationDecision<string> original = OperationDecision<string>.Succeed("original-id");
        var found = new OperationLookup<string>(OperationLookupDisposition.Found, original, readCompletion);
        Assert.AreEqual(OperationLookupDisposition.Found, found.Disposition);
        Assert.AreEqual("original-id", found.Decision!.Value);
        Assert.IsNotNull(found.ReadScopeCompletion);
        Assert.AreSame(readCompletion, found.ReadScopeCompletion);
        Assert.AreEqual(TransactionOutcome.NotCommitted, found.ReadScopeCompletion.Outcome);
        Assert.AreEqual(1, found.ReadScopeCompletion.CleanupErrors.Count);
        Assert.ThrowsExactly<ArgumentException>(() => new OperationLookup<string>(OperationLookupDisposition.NotFound, original));
        Assert.ThrowsExactly<ArgumentException>(() => new OperationLookup<string>(OperationLookupDisposition.Found));
    }

    [TestMethod]
    public void EncodedResultsFingerprintsAndNormalizedListsOwnTheirMutableBytes()
    {
        byte[] bytes = [1, 2, 3];
        var stored = new OperationStoredResult(new("receipt", 1), OperationBusinessDecision.Succeeded, bytes);
        byte[] digest = new byte[32];
        var fingerprint = new OperationFingerprint(new("request", 1), digest);
        bytes[0] = 9;
        digest[0] = 9;
        Assert.AreEqual((byte)1, stored.Value!.Value.Span[0]);
        Assert.AreEqual((byte)0, fingerprint.Digest.Span[0]);
        Assert.AreEqual(3, stored.PayloadBytes);
        Assert.ThrowsExactly<ArgumentException>(() => new OperationFingerprint(new("request", 1), new byte[31]));
        Assert.ThrowsExactly<ArgumentException>(() => new OperationStoredResult(new("receipt", 1), OperationBusinessDecision.Succeeded));
        Assert.ThrowsExactly<ArgumentException>(() => new OperationStoredResult(new("receipt", 1), OperationBusinessDecision.Rejected, bytes));
        Assert.ThrowsExactly<ArgumentException>(() => new OperationStoredResult(new("receipt", 1), OperationBusinessDecision.Succeeded, new byte[1_048_577]));
        var rejection = new OperationStoredResult(new("receipt", 1), OperationBusinessDecision.Rejected,
            error: new("declined", "Original rejection."));
        Assert.IsNull(rejection.Value);
        Assert.AreEqual(27, rejection.PayloadBytes);
        OperationFingerprintInput[] inputs = [new(new("request", 1), OperationCanonicalValue.Null)];
        var normalized = new NormalizedOperationInput<int>(7, inputs);
        inputs[0] = new(new("request", 2), OperationCanonicalValue.Missing);
        Assert.AreEqual(1, normalized.Fingerprints.Single().Contract.Version);
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<OperationFingerprintInput>)normalized.Fingerprints).Clear());
        Assert.ThrowsExactly<ArgumentException>(() => new NormalizedOperationInput<int>(7, Enumerable.Repeat(inputs[0], 2)));
    }

    [TestMethod]
    public void RetentionUsesImmutableObservationFactsAndTombstonesStillCarryTheBinding()
    {
        var forever = new OperationRetentionPolicy();
        Assert.IsNull(forever.ResultRetention);
        Assert.IsNull(forever.DuplicateProtectionRetention);
        Assert.ThrowsExactly<ArgumentException>(() => new OperationRetentionPolicy(null, TimeSpan.FromDays(1)));
        Assert.ThrowsExactly<ArgumentException>(() => new OperationRetentionPolicy(TimeSpan.FromDays(2), TimeSpan.FromDays(1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new OperationRetentionPolicy(TimeSpan.Zero));
        DateTimeOffset now = DateTimeOffset.Parse("2026-11-21T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var times = new OperationJournalTimes(now, now.AddDays(-2), now, now.AddDays(30));
        Assert.IsTrue(times.IsResultExpired);
        var identity = new OperationIdentity("app", "prod", "account:1", "Debit", "key");
        var fingerprint = new OperationFingerprint(new("request", 1), new byte[32]);
        var tombstone = new OperationJournalRecord(identity, fingerprint, null, times, isFinalized: true);
        Assert.IsNull(tombstone.Result);
        Assert.AreSame(fingerprint, tombstone.Fingerprint);
        Assert.AreSame(identity, new OperationClaim(OperationClaimDisposition.Existing, tombstone).Existing!.Identity);
        Assert.ThrowsExactly<ArgumentException>(() => new OperationClaim(OperationClaimDisposition.Existing));
        Assert.ThrowsExactly<ArgumentException>(() => new OperationClaim(OperationClaimDisposition.Acquired, tombstone));
        Assert.ThrowsExactly<ArgumentException>(() => new OperationJournalRecord(identity, null, null, times, true));
        Assert.IsTrue(new OperationJournalRecord(identity, null, null, times, true, isCorrupt: true).IsCorrupt);
    }
}
