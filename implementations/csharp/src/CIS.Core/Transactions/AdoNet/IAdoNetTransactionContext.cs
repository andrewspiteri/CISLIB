using System.Data.Common;

namespace CIS.Transactions.AdoNet;

/// <summary>Lends the owner's exact native handles for one exclusive, awaited callback.</summary>
/// <remarks>Finish and dispose commands, readers and temporary ORM contexts before returning.
/// Do not retain handles, complete the transaction, re-enter this context or abandon child work.</remarks>
public interface IAdoNetTransactionContext : ILocalTransactionContext
{
    /// <summary>Admits one sequential native callback and poisons the unit if it escapes with a fault or cancellation.</summary>
    Task UseAsync(Func<DbConnection, DbTransaction, CancellationToken, Task> work,
        CancellationToken cancellationToken = default);
    /// <summary>Returns a callback value without inferring business success or committing the owner.</summary>
    Task<T> UseAsync<T>(Func<DbConnection, DbTransaction, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default);
}
