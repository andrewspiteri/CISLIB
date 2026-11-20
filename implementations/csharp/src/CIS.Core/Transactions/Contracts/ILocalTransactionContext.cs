namespace CIS.Transactions;

/// <summary>Explicit participant metadata and rollback-only control, without completion ownership.</summary>
public interface ILocalTransactionContext
{
    /// <summary>Gets the immutable ordinal database alias.</summary>
    string DatabaseName
    {
        get;
    }
    /// <summary>Gets the concrete selected isolation.</summary>
    TransactionIsolation Isolation
    {
        get;
    }
    /// <summary>Gets whether commit has been prohibited.</summary>
    bool IsRollbackOnly
    {
        get;
    }
    /// <summary>Gets the first accepted rollback-only reason, or null.</summary>
    string? RollbackReasonCode
    {
        get;
    }
    /// <summary>Prohibits commit and preserves the first printable ASCII reason of at most 128 characters.</summary>
    void MarkRollbackOnly(string reasonCode);
}
