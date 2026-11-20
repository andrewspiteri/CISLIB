using System.Data;
using System.Data.Common;
using CIS.Transactions.AdoNet;

namespace CIS.Transactions.Hosting;

internal sealed record TransactionRuntimeSettings(TimeSpan RollbackTimeout, bool LogCalls)
{
    internal static TransactionRuntimeSettings Capture(LocalTransactionRuntimeOptions? options)
    {
        options ??= new();
        if (options.RollbackTimeout <= TimeSpan.Zero || options.RollbackTimeout > TimeSpan.FromMilliseconds(uint.MaxValue - 1L))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Use a positive finite rollback timeout supported by the native timer.");
        }
        return new(options.RollbackTimeout, options.LogCalls);
    }
}

internal sealed class TransactionDatabaseRegistration
{
    private readonly HashSet<TransactionIsolation> supported;

    internal TransactionDatabaseRegistration(string name, Func<IServiceProvider, DbDataSource> source,
        AdoNetDatabaseOptions options)
    {
        Name = TransactionText.Alias(name);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        if (!Enum.IsDefined(options.DefaultIsolation) || options.SupportedIsolationLevels is null
            || options.SupportedIsolationLevels.Count is < 1 or > 3
            || options.SupportedIsolationLevels.Any(level => !Enum.IsDefined(level)))
        {
            throw new ArgumentException("Declare a nonempty supported subset of the three portable isolation levels.", nameof(options));
        }
        supported = new(options.SupportedIsolationLevels);
        if (!supported.Contains(options.DefaultIsolation))
        {
            throw new ArgumentException("The supported subset must contain the default isolation.", nameof(options));
        }
        DefaultIsolation = options.DefaultIsolation;
        SupportedIsolationLevels = Array.AsReadOnly(supported.Order().ToArray());
        Source = source;
    }

    internal string Name
    {
        get;
    }
    internal TransactionIsolation DefaultIsolation
    {
        get;
    }
    internal IReadOnlyList<TransactionIsolation> SupportedIsolationLevels
    {
        get;
    }
    internal Func<IServiceProvider, DbDataSource> Source
    {
        get;
    }

    internal IsolationLevel NativeIsolation(TransactionIsolation requested)
    {
        if (!supported.Contains(requested))
        {
            throw new ArgumentException("The selected database does not support this requested isolation.", nameof(requested));
        }
        return requested switch
        {
            TransactionIsolation.ReadCommitted => IsolationLevel.ReadCommitted,
            TransactionIsolation.RepeatableRead => IsolationLevel.RepeatableRead,
            TransactionIsolation.Serializable => IsolationLevel.Serializable,
            _ => throw new ArgumentOutOfRangeException(nameof(requested))
        };
    }
}
