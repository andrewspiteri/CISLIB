namespace CIS.Transactions.AdoNet;

/// <summary>Application-declared native isolation support, copied when the database is registered.</summary>
public sealed class AdoNetDatabaseOptions
{
    /// <summary>Gets or sets the concrete default isolation.</summary>
    public TransactionIsolation DefaultIsolation { get; set; } = TransactionIsolation.ReadCommitted;
    /// <summary>Gets or sets the required nonempty supported subset, including the default.</summary>
    public IReadOnlyCollection<TransactionIsolation> SupportedIsolationLevels { get; set; } = [];
}
