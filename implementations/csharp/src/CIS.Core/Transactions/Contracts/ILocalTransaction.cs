namespace CIS.Transactions;

/// <summary>The sole completion owner of one local database transaction and connection lease.</summary>
/// <remarks>Keep the application-owned data source alive until this owner actually settles.
/// Disposal of unfinished work rolls it back; it never implicitly commits.</remarks>
public interface ILocalTransaction : IAsyncDisposable
{
    /// <summary>Gets the separately borrowed participant context.</summary>
    ILocalTransactionContext Context
    {
        get;
    }
    /// <summary>Gets the synchronized admission and cleanup state.</summary>
    LocalTransactionStatus Status
    {
        get;
    }
    /// <summary>Gets immutable completion after every owned cleanup attempt settles, or null.</summary>
    TransactionCompletion? Completion
    {
        get;
    }
    /// <summary>Requests commit and returns persistence evidence after actual cleanup.</summary>
    Task<TransactionCompletion> CommitAsync(CancellationToken cancellationToken = default);
    /// <summary>Requests rollback using an independent cooperative cleanup budget.</summary>
    Task<TransactionCompletion> RollbackAsync();
}
