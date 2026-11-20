namespace CIS.Transactions;

/// <summary>A concrete portable isolation requested from the configured native database.</summary>
public enum TransactionIsolation
{
    /// <summary>Read committed isolation.</summary>
    ReadCommitted,
    /// <summary>Repeatable read isolation.</summary>
    RepeatableRead,
    /// <summary>Serializable isolation.</summary>
    Serializable
}

/// <summary>The local owner's admission and resource-cleanup lifecycle.</summary>
public enum LocalTransactionStatus
{
    /// <summary>The owner can admit one participant callback.</summary>
    Active,
    /// <summary>Admission is closed and completion or cleanup owns the resources.</summary>
    Completing,
    /// <summary>Every owned cleanup attempt has settled and completion is inspectable.</summary>
    Completed
}

/// <summary>The persistence evidence obtained from native completion.</summary>
public enum TransactionOutcome
{
    /// <summary>Native commit returned successfully under the supported participant contract.</summary>
    Committed,
    /// <summary>The owner did not invoke native commit.</summary>
    NotCommitted,
    /// <summary>Native commit was invoked without confirmed success; it may have committed.</summary>
    Unknown
}

/// <summary>The reason the owner selected its terminal path.</summary>
public enum TransactionEndReason
{
    /// <summary>The application requested commit.</summary>
    CommitRequested,
    /// <summary>The application requested rollback.</summary>
    RollbackRequested,
    /// <summary>An unfinished owner was disposed.</summary>
    UncommittedDisposal,
    /// <summary>A participant's rollback-only mark refused commit.</summary>
    RollbackOnly,
    /// <summary>Cancellation was observed before invoking native commit.</summary>
    Cancellation
}

/// <summary>The native completion or cleanup phase associated with a safe error.</summary>
public enum TransactionErrorPhase
{
    /// <summary>Commit selection or invocation.</summary>
    Commit,
    /// <summary>The explicit cleanup rollback.</summary>
    Rollback,
    /// <summary>Native transaction disposal.</summary>
    TransactionDispose,
    /// <summary>Connection-lease disposal.</summary>
    ConnectionDispose
}
