using CIS.Coordination;
using CIS.Coordination.PostgreSql;
using CIS.Transactions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Coordination.PostgreSql;

[TestClass]
public sealed class PostgreSqlCoordinationBoundaryTests
{
    [TestMethod]
    public void ConstructionCopiesAuthorityWithoutOpeningOrTakingOwnershipOfTheApplicationsFactory()
    {
        var factory = new UnusedFactory();
        Guid expected = Guid.NewGuid();
        var options = new PostgreSqlCoordinationOptions { AuthorityId = expected };
        var provider = new PostgreSqlCoordinationProvider(factory, "Ledger", options);
        options.AuthorityId = Guid.NewGuid();
        options.Schema = "mutated";
        Assert.AreEqual(expected, provider.AuthorityId);
        Assert.AreEqual(0, factory.Calls);
    }

    [TestMethod]
    [DataRow("bad.schema")]
    [DataRow("schema\"; DROP SCHEMA public CASCADE;")]
    [DataRow("1schema")]
    public void UnsafeInstallationIdentifierIsRejectedBeforeSqlCanBeObtained(string schema)
    {
        Assert.ThrowsExactly<ArgumentException>(() => PostgreSqlCoordinationSchema.GetInstallationSql(Guid.NewGuid(), schema));
    }

    [TestMethod]
    public void AnInstallationRequiresANonemptyAuthorityAndProviderRequiresFiniteNativeTimeout()
    {
        Assert.ThrowsExactly<ArgumentException>(() => PostgreSqlCoordinationSchema.GetInstallationSql(Guid.Empty));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new PostgreSqlCoordinationProvider(new UnusedFactory(), "Ledger",
            new()
            {
                AuthorityId = Guid.NewGuid(),
                CommandTimeoutSeconds = 0
            }));
    }

    [TestMethod]
    public async Task CancelledInfrastructureFailureBeforeOwnerPublicationPreservesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var factory = new CancelledBeginFactory(cancellation);
        var provider = new PostgreSqlCoordinationProvider(factory, "Ledger", new()
        {
            AuthorityId = Guid.NewGuid()
        });
        var request = new CoordinationAcquireRequest(new(new("test", "wallet", "compaction"),
            "wallet", "instance", Guid.NewGuid()), TimeSpan.FromSeconds(30));

        await Assert.ThrowsAsync<OperationCanceledException>(() => provider.AcquireAsync(request, cancellation.Token));

        Assert.AreEqual(1, factory.Calls);
    }

    [TestMethod]
    [DataRow(CoordinationRejectionReason.WrongAuthority)]
    [DataRow(CoordinationRejectionReason.WrongDatabase)]
    [DataRow(CoordinationRejectionReason.InvalidTransaction)]
    public async Task LocalGuardRefusalsPoisonTheOriginalParticipantWithoutBorrowingNativeHandles(CoordinationRejectionReason expected)
    {
        Guid authority = Guid.NewGuid();
        var provider = new PostgreSqlCoordinationProvider(new UnusedFactory(), "Ledger", new()
        {
            AuthorityId = authority
        });
        var context = new PortableContext(expected == CoordinationRejectionReason.WrongDatabase ? "Other" : "Ledger");
        var grant = new CoordinationGrant(expected == CoordinationRejectionReason.WrongAuthority ? Guid.NewGuid() : authority,
            new(new("test", "wallet", "compaction"), "wallet", "instance", Guid.NewGuid()), 1, DateTimeOffset.UtcNow.AddMinutes(1));
        CoordinationGuardResult result = await provider.ValidateAsync(context, grant);
        Assert.IsFalse(result.Admitted);
        Assert.AreEqual(expected, result.RejectionReason);
        Assert.IsTrue(context.IsRollbackOnly);
        Assert.AreEqual("coordination.guard_rejected", context.RollbackReasonCode);
    }

    private sealed class PortableContext(string database) : ILocalTransactionContext
    {
        public string DatabaseName => database;
        public TransactionIsolation Isolation => TransactionIsolation.ReadCommitted;
        public bool IsRollbackOnly => RollbackReasonCode is not null;
        public string? RollbackReasonCode
        {
            get; private set;
        }

        public void MarkRollbackOnly(string reasonCode)
        {
            RollbackReasonCode ??= reasonCode;
        }
    }

    private sealed class UnusedFactory : ILocalTransactionFactory
    {
        internal int Calls;

        public Task<ILocalTransaction> BeginAsync(string databaseName, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("Passive metadata must not open a transaction.");
        }

        public Task<ILocalTransaction> BeginAsync(string databaseName, LocalTransactionOptions options, CancellationToken cancellationToken = default)
            => BeginAsync(databaseName, cancellationToken);
    }

    private sealed class CancelledBeginFactory(CancellationTokenSource cancellation) : ILocalTransactionFactory
    {
        internal int Calls;

        public Task<ILocalTransaction> BeginAsync(string databaseName, CancellationToken cancellationToken = default)
            => BeginAsync(databaseName, new LocalTransactionOptions(), cancellationToken);

        public Task<ILocalTransaction> BeginAsync(string databaseName, LocalTransactionOptions options,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            cancellation.Cancel();
            throw new IOException("The driver reported cancellation as an infrastructure failure before returning an owner.");
        }
    }
}
