namespace CIS.Transactions;

/// <summary>Creates explicit local owners from immutable named database registrations.</summary>
public interface ILocalTransactionFactory
{
    /// <summary>Begins using the named database's configured default isolation.</summary>
    Task<ILocalTransaction> BeginAsync(string databaseName, CancellationToken cancellationToken = default);

    /// <summary>Begins one owned connection lease and native transaction with explicit options.</summary>
    Task<ILocalTransaction> BeginAsync(string databaseName, LocalTransactionOptions options,
        CancellationToken cancellationToken = default);
}
