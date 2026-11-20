using System.Data;
using System.Data.Common;
using System.Runtime.ExceptionServices;
using CIS.Transactions.AdoNet;
using CIS.Transactions.Hosting;

namespace CIS.Transactions;

internal sealed class LocalTransactionFactory : ILocalTransactionFactory
{
    private readonly IServiceProvider services;
    private readonly IReadOnlyDictionary<string, TransactionDatabaseRegistration> databases;
    private readonly TransactionRuntimeSettings settings;
    private readonly TimeProvider clock;
    private readonly TransactionInstrumentation instrumentation;

    internal LocalTransactionFactory(IServiceProvider services, IReadOnlyList<TransactionDatabaseRegistration> databases,
        TransactionRuntimeSettings settings, TimeProvider clock, TransactionInstrumentation instrumentation)
    {
        if (databases.Count > TransactionsComposition.MaximumDatabases
            || databases.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != databases.Count)
        {
            throw new InvalidOperationException("Register at most sixteen distinct named databases.");
        }
        this.services = services;
        this.databases = databases.ToDictionary(item => item.Name, StringComparer.Ordinal);
        this.settings = settings;
        this.clock = clock;
        this.instrumentation = instrumentation;
    }

    public Task<ILocalTransaction> BeginAsync(string databaseName, CancellationToken cancellationToken = default)
        => BeginAsync(databaseName, new LocalTransactionOptions(), cancellationToken);

    public Task<ILocalTransaction> BeginAsync(string databaseName, LocalTransactionOptions options,
        CancellationToken cancellationToken = default)
    {
        TransactionText.Alias(databaseName);
        ArgumentNullException.ThrowIfNull(options);
        if (!databases.TryGetValue(databaseName, out TransactionDatabaseRegistration? registration))
        {
            throw new ArgumentException("The named database has not been registered.", nameof(databaseName));
        }
        TransactionIsolation isolation = options.Isolation ?? registration.DefaultIsolation;
        IsolationLevel nativeIsolation = registration.NativeIsolation(isolation);
        if (System.Transactions.Transaction.Current is not null)
        {
            throw new InvalidOperationException("An explicit local transaction cannot participate in an ambient transaction.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        ITransactionObservation observation = TransactionObservation.Capture(instrumentation, databaseName, isolation, settings.LogCalls);
        return BeginCoreAsync(registration, isolation, nativeIsolation, observation, cancellationToken);
    }

    private async Task<ILocalTransaction> BeginCoreAsync(TransactionDatabaseRegistration registration,
        TransactionIsolation isolation, IsolationLevel nativeIsolation, ITransactionObservation observation, CancellationToken token)
    {
        DbConnection? connection = null;
        DbTransaction? transaction = null;
        try
        {
            DbDataSource source = registration.Source(services)
                ?? throw new InvalidOperationException("The registered database source factory returned null.");
            token.ThrowIfCancellationRequested();
            try
            {
                connection = await source.OpenConnectionAsync(token).ConfigureAwait(false);
            }
            catch (Exception error) when (TransactionText.IsOrdinary(error))
            {
                TransactionObservation.Attempt(() => observation.NativeFailure(TransactionNativePhase.Begin,
                    error is OperationCanceledException && token.IsCancellationRequested));
                throw;
            }
            if (connection is null)
            {
                throw new InvalidOperationException("The database source returned no connection lease.");
            }
            token.ThrowIfCancellationRequested();
            try
            {
                transaction = await connection.BeginTransactionAsync(nativeIsolation, token).ConfigureAwait(false);
            }
            catch (Exception error) when (TransactionText.IsOrdinary(error))
            {
                TransactionObservation.Attempt(() => observation.NativeFailure(TransactionNativePhase.Begin,
                    error is OperationCanceledException && token.IsCancellationRequested));
                throw;
            }
            if (transaction is null)
            {
                throw new InvalidOperationException("Native begin returned no transaction handle.");
            }
            var owner = new AdoNetLocalTransaction(registration.Name, isolation, connection, transaction,
                settings.RollbackTimeout, clock, observation);
            token.ThrowIfCancellationRequested();
            owner.Publish();
            return owner;
        }
        catch (Exception original)
        {
            await TransactionCleanup.RunAsync(transaction, connection, rollback: true,
                settings.RollbackTimeout, observation, failedBegin: true).ConfigureAwait(false);
            ExceptionDispatchInfo.Capture(original).Throw();
            throw;
        }
    }
}
