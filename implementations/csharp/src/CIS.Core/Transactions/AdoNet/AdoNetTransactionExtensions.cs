using System.Data.Common;

namespace CIS.Transactions.AdoNet;

/// <summary>Uses the public native capability without requiring a concrete runtime implementation.</summary>
public static class AdoNetTransactionExtensions
{
    /// <summary>Lends native handles for one awaited callback, or rejects a portable-only context.</summary>
    public static Task UseAdoNetAsync(this ILocalTransactionContext context,
        Func<DbConnection, DbTransaction, CancellationToken, Task> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(work);
        return Capability(context).UseAsync(work, cancellationToken);
    }

    /// <summary>Lends native handles and returns the callback value without inspecting it.</summary>
    public static Task<T> UseAdoNetAsync<T>(this ILocalTransactionContext context,
        Func<DbConnection, DbTransaction, CancellationToken, Task<T>> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(work);
        return Capability(context).UseAsync(work, cancellationToken);
    }

    private static IAdoNetTransactionContext Capability(ILocalTransactionContext context) => context as IAdoNetTransactionContext
        ?? throw new NotSupportedException("This participant context does not provide the public ADO.NET capability.");
}
