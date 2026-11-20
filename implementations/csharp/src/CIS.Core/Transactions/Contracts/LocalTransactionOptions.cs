namespace CIS.Transactions;

/// <summary>Per-owner options; omitted isolation uses the named registration's captured default.</summary>
public sealed record LocalTransactionOptions
{
    /// <summary>Gets the explicit isolation override, or null for the captured default.</summary>
    public TransactionIsolation? Isolation
    {
        get; init;
    }
}
