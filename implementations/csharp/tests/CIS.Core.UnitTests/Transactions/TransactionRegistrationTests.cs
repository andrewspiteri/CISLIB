using CIS.TestSupport.Transactions;
using CIS.Transactions;
using CIS.Transactions.AdoNet;
using CIS.Transactions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Transactions;

[TestClass]
public sealed class TransactionRegistrationTests
{
    [TestMethod]
    public async Task RegistrationAndFactoryResolutionArePassiveAndOptionsAreDetached()
    {
        using var source = new RecordingTransactionDataSource();
        var levels = new List<TransactionIsolation> { TransactionIsolation.RepeatableRead };
        var options = new AdoNetDatabaseOptions
        {
            DefaultIsolation = TransactionIsolation.RepeatableRead,
            SupportedIsolationLevels = levels
        };
        var runtime = new LocalTransactionRuntimeOptions { LogCalls = true };
        var services = new ServiceCollection();
        int resolutions = 0;
        services.AddCisTransactions(runtime);
        services.AddCisAdoNetDatabase("Ledger", _ => { resolutions++; return source; }, options);
        levels.Clear();
        options.DefaultIsolation = TransactionIsolation.Serializable;
        runtime.RollbackTimeout = TimeSpan.Zero;
        runtime.LogCalls = false;

        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        ILocalTransactionFactory factory = provider.GetRequiredService<ILocalTransactionFactory>();
        TransactionsComposition metadata = provider.GetRequiredService<TransactionsComposition>();
        Assert.AreEqual(0, resolutions);
        Assert.AreEqual(0, source.OpenCalls);
        Assert.AreEqual(TransactionIsolation.RepeatableRead, metadata.Databases.Single().DefaultIsolation);
        CollectionAssert.AreEqual(new[] { TransactionIsolation.RepeatableRead },
            metadata.Databases.Single().SupportedIsolationLevels.ToArray());
        Assert.AreSame(metadata.RegistrationIdentity, provider.GetRequiredService<TransactionInstrumentation>().RegistrationIdentity);

        await using ILocalTransaction transaction = await factory.BeginAsync("Ledger");
        Assert.AreEqual(TransactionIsolation.RepeatableRead, transaction.Context.Isolation);
        Assert.AreEqual(System.Data.IsolationLevel.RepeatableRead, source.Connections.Single().RequestedIsolation);
        Assert.AreEqual(1, resolutions);
        Assert.AreEqual(0, source.DisposeCalls);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("-ledger")]
    [DataRow("ledger db")]
    [DataRow("ledger\n")]
    [DataRow("élèdge")]
    public void InvalidAliasesRejectBeforeRegistrationOrSourceResolution(string alias)
    {
        var services = new ServiceCollection();
        Assert.ThrowsExactly<ArgumentException>(() => services.AddCisAdoNetDatabase(alias,
            _ => throw new AssertFailedException("Invalid aliases cannot resolve a source."), Options()));
        Assert.AreEqual(0, services.Count);
    }

    [TestMethod]
    public void AliasesAreOrdinalBoundedAndDuplicateRejectionDoesNotConsumeCapacity()
    {
        var services = new ServiceCollection();
        services.AddCisAdoNetDatabase("Ledger", Unused, Options());
        Assert.ThrowsExactly<ArgumentException>(() => services.AddCisAdoNetDatabase("Ledger", Unused, Options()));
        services.AddCisAdoNetDatabase("ledger", Unused, Options());
        for (int index = 2; index < 16; index++)
        {
            services.AddCisAdoNetDatabase("Db" + index, Unused, Options());
        }
        Assert.ThrowsExactly<InvalidOperationException>(() => services.AddCisAdoNetDatabase("Overflow", Unused, Options()));
        Assert.ThrowsExactly<ArgumentException>(() => services.AddCisAdoNetDatabase(new string('A', 65), Unused, Options()));
        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.AreEqual(16, provider.GetRequiredService<TransactionsComposition>().Databases.Count);
    }

    [TestMethod]
    [DataRow("empty")]
    [DataRow("default")]
    [DataRow("supported")]
    [DataRow("missing")]
    public void InvalidIsolationDeclarationsAreRejectedBeforeComposition(string scenario)
    {
        AdoNetDatabaseOptions options = Options();
        if (scenario == "empty")
        {
            options.SupportedIsolationLevels = [];
        }
        else if (scenario == "default")
        {
            options.DefaultIsolation = (TransactionIsolation)999;
        }
        else if (scenario == "supported")
        {
            options.SupportedIsolationLevels = [(TransactionIsolation)999];
        }
        else
        {
            options.DefaultIsolation = TransactionIsolation.Serializable;
        }
        var services = new ServiceCollection();
        Assert.ThrowsExactly<ArgumentException>(() => services.AddCisAdoNetDatabase("Ledger", Unused, options));
        Assert.AreEqual(0, services.Count);
    }

    [TestMethod]
    public void EquivalentRuntimeRegistrationIsIdempotentAndConflictsAreRejected()
    {
        var services = new ServiceCollection();
        services.AddCisTransactions(new()
        {
            LogCalls = true,
            RollbackTimeout = TimeSpan.FromSeconds(7)
        });
        services.AddCisTransactions(new()
        {
            LogCalls = true,
            RollbackTimeout = TimeSpan.FromSeconds(7)
        });
        services.AddCisAdoNetDatabase("Ledger", Unused, Options());
        Assert.AreEqual(1, services.Count(item => item.ServiceType == typeof(ILocalTransactionFactory)));
        Assert.ThrowsExactly<InvalidOperationException>(() => services.AddCisTransactions());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ServiceCollection().AddCisTransactions(
            new()
            {
                RollbackTimeout = Timeout.InfiniteTimeSpan
            }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ServiceCollection().AddCisTransactions(
            new()
            {
                RollbackTimeout = TimeSpan.Zero
            }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ServiceCollection().AddCisTransactions(
            new()
            {
                RollbackTimeout = TimeSpan.MaxValue
            }));
    }

    [TestMethod]
    public void UnknownUnsupportedAmbientAndPreCancelledBeginDoNotResolveTheDataSource()
    {
        var services = new ServiceCollection();
        services.AddCisAdoNetDatabase("Ledger", Unused, Options());
        using ServiceProvider provider = services.BuildServiceProvider();
        ILocalTransactionFactory factory = provider.GetRequiredService<ILocalTransactionFactory>();
        Assert.ThrowsExactly<ArgumentException>(() => factory.BeginAsync("ledger"));
        Assert.ThrowsExactly<ArgumentException>(() => factory.BeginAsync("Ledger",
            new LocalTransactionOptions { Isolation = TransactionIsolation.Serializable }));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => factory.BeginAsync("Ledger", cancellation.Token));
        using var ambient = new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled);
        Assert.ThrowsExactly<InvalidOperationException>(() => factory.BeginAsync("Ledger"));
        ambient.Complete();
    }

    private static AdoNetDatabaseOptions Options() => new()
    {
        SupportedIsolationLevels = [TransactionIsolation.ReadCommitted]
    };

    private static System.Data.Common.DbDataSource Unused(IServiceProvider _) =>
        throw new AssertFailedException("This check must finish before resolving its data source.");
}
