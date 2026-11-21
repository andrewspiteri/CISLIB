using System.Text;
using CIS.Operations;
using CIS.Security;
using CIS.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Operations;

[TestClass]
public sealed class OperationLookupTests
{
    [TestMethod]
    public async Task LookupUsesFreshAuthoritativeReadCommittedOwnerAndActuallyRollsBackAndDisposesIt()
    {
        using var fixture = new OperationRuntimeFixture();
        await using ILocalTransaction original = await fixture.Begin();
        fixture.Journal.Read = (context, identity, token) =>
        {
            Assert.AreNotSame(original.Context, context);
            Assert.AreEqual(TransactionIsolation.ReadCommitted, context.Isolation);
            return Task.FromResult<OperationJournalRecord?>(RuntimeJournal.Row(identity, Fingerprint()));
        };
        OperationLookup<string> found = await fixture.Service.GetResultAsync<string>(Request());
        Assert.AreEqual(OperationLookupDisposition.Found, found.Disposition);
        Assert.AreEqual("original", found.Decision!.Value);
        Assert.IsNotNull(found.ReadScopeCompletion);
        Assert.AreEqual(TransactionOutcome.NotCommitted, found.ReadScopeCompletion.Outcome);
        Assert.IsTrue(found.ReadScopeCompletion.RollbackConfirmed);
        Assert.IsNull(original.Completion);
        Assert.AreEqual(2, fixture.Source.OpenCalls);
        var read = fixture.Source.Connections.Last();
        Assert.AreEqual(1, read.Transactions.Single().RollbackCalls);
        Assert.AreEqual(1, read.Transactions.Single().DisposeCalls);
        Assert.AreEqual(1, read.DisposeCalls);
        Assert.AreEqual(0, fixture.Source.DisposeCalls, "Lookup does not own the application data source.");
        CollectionAssert.AreEqual(new[] { "ledger.read" }, fixture.Authorizer.Capabilities!.ToArray());
    }

    [TestMethod]
    public async Task MissingCommittedReadDoesNotTouchAnActiveClaimOrInferItsRollback()
    {
        using var fixture = new OperationRuntimeFixture();
        await using ILocalTransaction original = await fixture.Begin();
        Assert.AreEqual(OperationExecutionDisposition.Prepared,
            (await fixture.Service.ExecuteAsync(original.Context, OperationRuntimeFixture.Request(), OperationRuntimeFixture.Success)).Disposition);
        OperationLookup<string> lookup = await fixture.Service.GetResultAsync<string>(Request());
        Assert.AreEqual(OperationLookupDisposition.NotFound, lookup.Disposition);
        Assert.AreNotSame(original.Context, fixture.Journal.ReadContext);
        Assert.IsFalse(original.Context.IsRollbackOnly);
        Assert.IsNull(original.Completion);
        Assert.AreEqual(1, fixture.Journal.Claims);
        Assert.AreEqual(1, fixture.Journal.Writes);
    }

    [TestMethod]
    [DataRow("expired", OperationLookupDisposition.ResultExpired)]
    [DataRow("unknown-reader", OperationLookupDisposition.Incompatible)]
    [DataRow("decode-false", OperationLookupDisposition.Incompatible)]
    [DataRow("incomplete", OperationLookupDisposition.Unavailable)]
    [DataRow("corrupt", OperationLookupDisposition.Unavailable)]
    [DataRow("wrong-identity", OperationLookupDisposition.Unavailable)]
    public async Task RetainedProtectionIsNeverReportedAsAbsent(string shape, OperationLookupDisposition expected)
    {
        using var fixture = new OperationRuntimeFixture();
        fixture.Codec.DecodeSupported = shape != "decode-false";
        fixture.Journal.Read = (_, identity, _) =>
        {
            OperationStoredResult? result = shape == "unknown-reader"
                ? new(new("debit-result", 99), OperationBusinessDecision.Succeeded, Encoding.UTF8.GetBytes("original"))
                : null;
            OperationIdentity actual = shape == "wrong-identity"
                ? new(identity.Application, identity.Environment, identity.Scope, identity.OperationName, "different-key")
                : identity;
            return Task.FromResult<OperationJournalRecord?>(RuntimeJournal.Row(actual, Fingerprint(), result,
                expired: shape == "expired", corrupt: shape == "corrupt", finalized: shape != "incomplete"));
        };
        OperationLookup<string> lookup = await fixture.Service.GetResultAsync<string>(Request());
        Assert.AreEqual(expected, lookup.Disposition);
        Assert.IsNull(lookup.Decision);
        Assert.IsNotNull(lookup.ReadScopeCompletion);
    }

    [TestMethod]
    [DataRow("begin", true)]
    [DataRow("read", true)]
    [DataRow("read", false)]
    [DataRow("codec", true)]
    [DataRow("authorizer", true)]
    [DataRow("resolver", true)]
    public async Task OnlyNativeBeginAndReadTransientFaultsBecomeUnavailable(string phase, bool transient)
    {
        var owner = new RuntimeReadOwner();
        var factory = new RuntimeReadFactory(owner);
        using var fixture = new OperationRuntimeFixture(services: services => services.AddSingleton<ILocalTransactionFactory>(factory));
        var original = new RuntimeDbException(transient);
        switch (phase)
        {
            case "begin":
                factory.BeginError = original;
                break;
            case "read":
                fixture.Journal.Read = (_, _, _) => Task.FromException<OperationJournalRecord?>(original);
                break;
            case "codec":
                fixture.Journal.Read = (_, identity, _) => Task.FromResult<OperationJournalRecord?>(RuntimeJournal.Row(identity, Fingerprint()));
                fixture.Codec.Decoder = (_, _) => throw original;
                break;
            case "authorizer":
                fixture.Authorizer.Handler = _ => ValueTask.FromException<AuthorizationDecision>(original);
                break;
            case "resolver":
                fixture.Resolver.Handler = _ => ValueTask.FromException<OperationScope>(original);
                break;
        }
        if (transient && phase is "begin" or "read")
        {
            OperationLookup<string> lookup = await fixture.Service.GetResultAsync<string>(Request());
            Assert.AreEqual(OperationLookupDisposition.Unavailable, lookup.Disposition);
            Assert.AreEqual(phase == "begin", lookup.ReadScopeCompletion is null);
        }
        else
        {
            RuntimeDbException observed = await Assert.ThrowsExactlyAsync<RuntimeDbException>(() => fixture.Service.GetResultAsync<string>(Request()));
            Assert.AreSame(original, observed);
        }
        bool began = phase is "read" or "codec";
        Assert.AreEqual(began ? 1 : 0, owner.Rollbacks);
        Assert.AreEqual(began ? 1 : 0, owner.Disposals);
        if (factory.Calls > 0)
        {
            Assert.AreEqual(TransactionIsolation.ReadCommitted, factory.Options!.Isolation);
        }
    }

    [TestMethod]
    public async Task CancellationDuringReadPreservesTokenAndWaitsForReadOwnerCleanup()
    {
        var owner = new RuntimeReadOwner();
        var factory = new RuntimeReadFactory(owner);
        using var fixture = new OperationRuntimeFixture(services: services => services.AddSingleton<ILocalTransactionFactory>(factory));
        using var cancellation = new CancellationTokenSource();
        fixture.Journal.Read = (_, _, _) =>
        {
            cancellation.Cancel();
            return Task.FromException<OperationJournalRecord?>(new OperationCanceledException(cancellation.Token));
        };
        OperationCanceledException observed = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.Service.GetResultAsync<string>(Request(), cancellation.Token));
        Assert.AreEqual(cancellation.Token, observed.CancellationToken);
        Assert.AreEqual(1, owner.Rollbacks);
        Assert.AreEqual(1, owner.Disposals);
    }

    [TestMethod]
    public async Task FoundSurvivesCleanupFaultsWithActualCompletionAndPrimaryFaultAlwaysWins()
    {
        var owner = new RuntimeReadOwner
        {
            RollbackError = new InvalidOperationException("Controlled rollback fault"),
            DisposeError = new InvalidOperationException("Controlled dispose fault")
        };
        var factory = new RuntimeReadFactory(owner);
        using var fixture = new OperationRuntimeFixture(services: services => services.AddSingleton<ILocalTransactionFactory>(factory));
        fixture.Journal.Read = (_, identity, _) => Task.FromResult<OperationJournalRecord?>(RuntimeJournal.Row(identity, Fingerprint()));
        OperationLookup<string> found = await fixture.Service.GetResultAsync<string>(Request());
        Assert.AreEqual(OperationLookupDisposition.Found, found.Disposition);
        Assert.AreEqual("original", found.Decision!.Value);
        Assert.IsNull(found.ReadScopeCompletion, "Never fabricate facts that the owner did not publish.");
        Assert.AreEqual(1, owner.Rollbacks);
        Assert.AreEqual(1, owner.Disposals);

        var primary = new InvalidOperationException("Original decoder failure");
        fixture.Codec.Decoder = (_, _) => throw primary;
        Assert.AreSame(primary, await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Service.GetResultAsync<string>(Request())));
        Assert.AreEqual(2, owner.Rollbacks);
        Assert.AreEqual(2, owner.Disposals);
        fixture.Codec.Decoder = null;
        fixture.Journal.Read = (_, _, _) => Task.FromResult<OperationJournalRecord?>(null);
        Assert.AreSame(owner.RollbackError, await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => fixture.Service.GetResultAsync<string>(Request())));
    }

    [TestMethod]
    public async Task KnownFoundPreservesRealReadCompletionCleanupDiagnostics()
    {
        using var fixture = new OperationRuntimeFixture();
        fixture.Source.ConnectionCreated = connection => connection.TransactionCreated = transaction =>
            transaction.DisposeHandler = () => ValueTask.FromException(new InvalidOperationException("Controlled native transaction disposal failure"));
        fixture.Journal.Read = (_, identity, _) => Task.FromResult<OperationJournalRecord?>(RuntimeJournal.Row(identity, Fingerprint()));
        OperationLookup<string> found = await fixture.Service.GetResultAsync<string>(Request());
        Assert.AreEqual(OperationLookupDisposition.Found, found.Disposition);
        Assert.IsNotNull(found.ReadScopeCompletion);
        Assert.AreEqual(TransactionOutcome.NotCommitted, found.ReadScopeCompletion.Outcome);
        Assert.AreEqual(1, found.ReadScopeCompletion.CleanupErrors.Count);
        Assert.AreEqual(TransactionErrorPhase.TransactionDispose, found.ReadScopeCompletion.CleanupErrors[0].Phase);
    }

    [TestMethod]
    public async Task DeniedLookupDoesNotOpenSourceAndUnknownTypesCannotDiscloseData()
    {
        using var fixture = new OperationRuntimeFixture();
        fixture.Authorizer.Handler = _ => ValueTask.FromResult(AuthorizationDecision.Deny(new(SecurityFailureKind.CapabilityDenied, "Denied")));
        Assert.AreEqual(OperationLookupDisposition.Denied, (await fixture.Service.GetResultAsync<string>(Request())).Disposition);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => fixture.Service.GetResultAsync<int>(Request()));
        Assert.AreEqual(0, fixture.Journal.Reads);
        Assert.AreEqual(0, fixture.Source.OpenCalls);
    }

    private static OperationLookupRequest Request() => new("Debit", "account-alias", "key");
    private static OperationFingerprint Fingerprint() => new(new("debit-request", 1), new byte[32]);
}
