using CIS.Operations;
using CIS.Operations.PostgreSql;
using CIS.Transactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Operations;

[TestClass]
public sealed class OperationMaintenanceTests
{
    [TestMethod]
    [DataRow("WrongDatabase", TransactionIsolation.ReadCommitted, false)]
    [DataRow("Ledger", TransactionIsolation.Serializable, false)]
    [DataRow("Ledger", TransactionIsolation.ReadCommitted, true)]
    public async Task FreshFactoryMetadataIsValidatedBeforeSqlAndAnyReturnedOwnerIsDisposed(string database,
        TransactionIsolation isolation, bool nullOwner)
    {
        var returned = new MetadataOwner(database, isolation);
        var factory = new MetadataFactory(nullOwner ? null : returned);
        using var fixture = new OperationRuntimeFixture(options => options.MaintenanceCapabilities = ["ledger.maintain"], services =>
        {
            services.RemoveAll<IOperationJournal>();
            services.AddSingleton<IOperationJournal>(new PostgreSqlOperationJournal());
            services.AddSingleton<ILocalTransactionFactory>(factory);
            services.AddCisPostgreSqlOperationRetentionMaintenance();
        });
        IOperationRetentionMaintenance maintenance = fixture.Scope.ServiceProvider.GetRequiredService<IOperationRetentionMaintenance>();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => maintenance.RunAsync(new("Debit", "account-alias")));
        Assert.AreEqual(1, factory.Calls);
        Assert.AreEqual(!nullOwner, returned.Disposed);
        Assert.AreEqual(0, fixture.Source.OpenCalls);
        Assert.AreEqual("Ledger", factory.Database);
        Assert.AreEqual(TransactionIsolation.ReadCommitted, factory.Isolation);
    }

    private sealed class MetadataFactory(ILocalTransaction? owner) : ILocalTransactionFactory
    {
        internal int Calls;
        internal string? Database;
        internal TransactionIsolation? Isolation;

        public Task<ILocalTransaction> BeginAsync(string databaseName, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("Maintenance must select explicit ReadCommitted options.");

        public Task<ILocalTransaction> BeginAsync(string databaseName, LocalTransactionOptions options,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            Database = databaseName;
            Isolation = options.Isolation;
            return Task.FromResult(owner!);
        }
    }

    private sealed class MetadataOwner(string database, TransactionIsolation isolation) : ILocalTransaction
    {
        internal bool Disposed;
        public ILocalTransactionContext Context { get; } = new MetadataContext(database, isolation);
        public LocalTransactionStatus Status => LocalTransactionStatus.Active;
        public TransactionCompletion? Completion => null;
        public Task<TransactionCompletion> CommitAsync(CancellationToken cancellationToken = default)
            => throw new AssertFailedException("An invalid maintenance owner cannot be committed.");
        public Task<TransactionCompletion> RollbackAsync() => throw new AssertFailedException("Disposal owns failed acquisition cleanup.");
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class MetadataContext(string database, TransactionIsolation isolation) : ILocalTransactionContext
    {
        public string DatabaseName => database;
        public TransactionIsolation Isolation => isolation;
        public bool IsRollbackOnly => false;
        public string? RollbackReasonCode => null;
        public void MarkRollbackOnly(string reasonCode) => throw new AssertFailedException("Preflight precedes native admission.");
    }
}
