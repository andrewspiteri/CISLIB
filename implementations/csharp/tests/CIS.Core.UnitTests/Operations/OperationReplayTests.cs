using System.Text;
using CIS.Operations;
using CIS.Transactions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Operations;

[TestClass]
public sealed class OperationReplayTests
{
    [TestMethod]
    [DataRow("matching", OperationExecutionDisposition.Replayed)]
    [DataRow("mismatch-expired", OperationExecutionDisposition.KeyConflict)]
    [DataRow("unknown-fingerprint-expired", OperationExecutionDisposition.Incompatible)]
    [DataRow("matching-expired", OperationExecutionDisposition.ResultExpired)]
    [DataRow("unknown-result", OperationExecutionDisposition.Incompatible)]
    [DataRow("corrupt", OperationExecutionDisposition.Incompatible)]
    [DataRow("incomplete", OperationExecutionDisposition.Incompatible)]
    [DataRow("wrong-identity", OperationExecutionDisposition.Incompatible)]
    [DataRow("decode-false", OperationExecutionDisposition.Incompatible)]
    public async Task RetainedRowsFollowComparisonExpiryAndReaderPrecedenceWithoutHandler(string shape,
        OperationExecutionDisposition expected)
    {
        using var fixture = new OperationRuntimeFixture();
        await using ILocalTransaction owner = await fixture.Begin();
        fixture.Codec.DecodeSupported = shape != "decode-false";
        fixture.Journal.Claim = (_, identity, fingerprint, _) =>
        {
            bool expired = shape.EndsWith("expired", StringComparison.Ordinal);
            OperationFingerprint storedFingerprint = shape switch
            {
                "mismatch-expired" => new(fingerprint.Contract, new byte[32]),
                "unknown-fingerprint-expired" => new(new("debit-request", 99), fingerprint.Digest),
                _ => fingerprint
            };
            OperationStoredResult? result = shape == "unknown-result"
                ? new(new("debit-result", 99), OperationBusinessDecision.Succeeded, Encoding.UTF8.GetBytes("original"))
                : null;
            OperationIdentity storedIdentity = shape == "wrong-identity"
                ? new(identity.Application, identity.Environment, "another-scope", identity.OperationName, identity.Key)
                : identity;
            return Task.FromResult(new OperationClaim(OperationClaimDisposition.Existing,
                RuntimeJournal.Row(storedIdentity, storedFingerprint, result, expired, shape == "corrupt", shape != "incomplete")));
        };
        OperationExecution<string> execution = await fixture.Service.ExecuteAsync<RuntimeInput, string>(owner.Context, OperationRuntimeFixture.Request(),
            (_, _, _) => throw new AssertFailedException("A retained identity never executes a handler."));
        Assert.AreEqual(expected, execution.Disposition);
        if (expected == OperationExecutionDisposition.Replayed)
        {
            Assert.AreEqual("original", execution.Decision!.Value);
        }
        Assert.AreEqual(1, fixture.Authorizer.Calls);
        Assert.AreEqual(1, fixture.Journal.Claims);
        Assert.AreEqual(0, fixture.Journal.Writes);
        Assert.AreEqual(0, fixture.Codec.Encodes);
        Assert.IsNull(owner.Completion, "Replay cannot complete the caller's owner.");
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Service.ExecuteAsync(owner.Context,
            OperationRuntimeFixture.Request(), OperationRuntimeFixture.Success));
    }

    [TestMethod]
    public async Task HistoricalComparisonAndResultReaderReturnOriginalVersionedBusinessDecision()
    {
        using var fixture = new OperationRuntimeFixture(options => options.FingerprintVersions = [1, 2], version: 2);
        await using ILocalTransaction owner = await fixture.Begin();
        fixture.Journal.Claim = (_, identity, _, _) =>
        {
            NormalizedOperationInput<RuntimeInput> input = fixture.Normalizer.Normalize(new(10), fixture.Resolver.Scope);
            OperationFingerprint historical = OperationCanonicalFrame.Fingerprint(input.Fingerprints.Single(item => item.Contract.Version == 1));
            var rejected = new OperationStoredResult(new("debit-result", 1), OperationBusinessDecision.Rejected,
                error: new("original.decline", "Original message"));
            return Task.FromResult(new OperationClaim(OperationClaimDisposition.Existing, RuntimeJournal.Row(identity, historical, rejected)));
        };
        OperationExecution<string> execution = await fixture.Service.ExecuteAsync<RuntimeInput, string>(owner.Context, OperationRuntimeFixture.Request(),
            (_, _, _) => throw new AssertFailedException("Historical comparison cannot rerun work."));
        Assert.AreEqual(OperationExecutionDisposition.Replayed, execution.Disposition);
        Assert.AreEqual(OperationBusinessDecision.Rejected, execution.Decision!.Decision);
        Assert.AreEqual("Original message", execution.Decision.Error!.Message);
        Assert.AreEqual(0, fixture.Codec.Decodes);
    }

    [TestMethod]
    public async Task NormalizerCannotInjectAnUnregisteredComparisonContractOrOmitCurrent()
    {
        using var fixture = new OperationRuntimeFixture();
        await using ILocalTransaction owner = await fixture.Begin();
        fixture.Normalizer.Versions = [99];
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Service.ExecuteAsync(owner.Context,
            OperationRuntimeFixture.Request(), OperationRuntimeFixture.Success));
        Assert.AreEqual(0, fixture.Journal.Claims);
        Assert.IsFalse(owner.Context.IsRollbackOnly);
        fixture.Normalizer.Versions = [1];
        Assert.AreEqual(OperationExecutionDisposition.Prepared,
            (await fixture.Service.ExecuteAsync(owner.Context, OperationRuntimeFixture.Request(), OperationRuntimeFixture.Success)).Disposition);
    }
}
