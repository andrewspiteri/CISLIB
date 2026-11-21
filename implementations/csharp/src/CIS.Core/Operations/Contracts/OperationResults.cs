using CIS.Transactions;

namespace CIS.Operations;

/// <summary>A completed business decision independent of execution and database persistence.</summary>
public enum OperationBusinessDecision
{
    /// <summary>The application accepted its business operation.</summary>
    Succeeded,
    /// <summary>The application made a final business rejection.</summary>
    Rejected
}

/// <summary>Stable bounded application error data, without an exception or provider message.</summary>
public sealed record OperationError
{
    /// <summary>Copies the application code and message; neither is a telemetry dimension.</summary>
    public OperationError(string code, string message)
    {
        Code = OperationText.Code(code, nameof(code));
        Message = OperationText.Utf8(message, 1024, nameof(message), allowControls: true);
    }

    /// <summary>Gets the application error code.</summary>
    public string Code
    {
        get;
    }
    /// <summary>Gets the original application message, bounded to 1024 UTF-8 bytes.</summary>
    public string Message
    {
        get;
    }
}

/// <summary>One completed typed business decision, without a commit claim.</summary>
public sealed class OperationDecision<TResult>
{
    private OperationDecision(OperationBusinessDecision decision, TResult? value, OperationError? error)
    {
        Decision = decision;
        Value = value;
        Error = error;
    }

    /// <summary>Gets Succeeded or Rejected.</summary>
    public OperationBusinessDecision Decision
    {
        get;
    }
    /// <summary>Gets the typed success value; null is permitted when the registered codec supports it.</summary>
    public TResult? Value
    {
        get;
    }
    /// <summary>Gets the stable original business rejection, or null for success.</summary>
    public OperationError? Error
    {
        get;
    }
    /// <summary>Creates an accepted business value without claiming persistence.</summary>
    public static OperationDecision<TResult> Succeed(TResult? value) => new(OperationBusinessDecision.Succeeded, value, null);
    /// <summary>Creates a final rejection that may be committed with its valid transaction effects.</summary>
    public static OperationDecision<TResult> Reject(string code, string message)
        => new(OperationBusinessDecision.Rejected, default, new(code, message));
}

/// <summary>The durable execution fact, distinct from both business decision and RM-02 completion.</summary>
public enum OperationExecutionDisposition
{
    /// <summary>The result is staged inside the caller's uncommitted transaction.</summary>
    Prepared,
    /// <summary>A matching original committed result was recovered without invoking the handler.</summary>
    Replayed,
    /// <summary>The retained identity is bound to different comparable inputs.</summary>
    KeyConflict,
    /// <summary>The original payload expired while its duplicate protection remains.</summary>
    ResultExpired,
    /// <summary>A retained contract or stored representation cannot be interpreted safely.</summary>
    Incompatible,
    /// <summary>This attempt could not establish ownership; it proves nothing about another attempt's outcome.</summary>
    Contended,
    /// <summary>A fresh transaction is required; no statement or handler was retried.</summary>
    RetryRequired,
    /// <summary>The complete target/capability check denied the request.</summary>
    Denied,
    /// <summary>The context cannot prepare a result because it is rollback-only.</summary>
    RollbackOnly
}

/// <summary>The result of Execute; only Prepared and Replayed carry a business decision.</summary>
public sealed class OperationExecution<TResult>
{
    /// <summary>Creates consistent execution facts without inferring commit from Prepared.</summary>
    public OperationExecution(OperationExecutionDisposition disposition, OperationDecision<TResult>? decision = null)
    {
        if (!Enum.IsDefined(disposition))
        {
            throw new ArgumentOutOfRangeException(nameof(disposition));
        }
        bool carriesDecision = disposition is OperationExecutionDisposition.Prepared or OperationExecutionDisposition.Replayed;
        if (carriesDecision != (decision is not null))
        {
            throw new ArgumentException("Only Prepared and Replayed carry a completed business decision.", nameof(decision));
        }
        Disposition = disposition;
        Decision = decision;
    }

    /// <summary>Gets the execution disposition.</summary>
    public OperationExecutionDisposition Disposition
    {
        get;
    }
    /// <summary>Gets the independent business decision when present.</summary>
    public OperationDecision<TResult>? Decision
    {
        get;
    }
}

/// <summary>The result of authoritative committed-result lookup.</summary>
public enum OperationLookupDisposition
{
    /// <summary>An original committed result was observed.</summary>
    Found,
    /// <summary>No committed result was observed; this does not prove rollback after Unknown.</summary>
    NotFound,
    /// <summary>Duplicate protection remains but the original payload expired.</summary>
    ResultExpired,
    /// <summary>The retained representation cannot be safely interpreted.</summary>
    Incompatible,
    /// <summary>The authoritative read was unavailable.</summary>
    Unavailable,
    /// <summary>Disclosure was denied before journal access.</summary>
    Denied
}

/// <summary>A committed journal observation plus separate completion facts for its fresh read scope.</summary>
public sealed class OperationLookup<TResult>
{
    /// <summary>Creates lookup facts; read-scope cleanup cannot downgrade a known original Found result.</summary>
    public OperationLookup(OperationLookupDisposition disposition, OperationDecision<TResult>? decision = null,
        TransactionCompletion? readScopeCompletion = null)
    {
        if (!Enum.IsDefined(disposition))
        {
            throw new ArgumentOutOfRangeException(nameof(disposition));
        }
        if ((disposition == OperationLookupDisposition.Found) != (decision is not null))
        {
            throw new ArgumentException("Only Found carries a completed business decision.", nameof(decision));
        }
        Disposition = disposition;
        Decision = decision;
        ReadScopeCompletion = readScopeCompletion;
    }

    /// <summary>Gets the original journal observation.</summary>
    public OperationLookupDisposition Disposition
    {
        get;
    }
    /// <summary>Gets the original business decision when Found.</summary>
    public OperationDecision<TResult>? Decision
    {
        get;
    }
    /// <summary>Gets completion of the new read owner, not persistence evidence for the original operation.</summary>
    public TransactionCompletion? ReadScopeCompletion
    {
        get;
    }
}
