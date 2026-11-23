namespace CIS.Audit;

/// <summary>An expected Audit submission failure, independent of the protected business effect.</summary>
public sealed class AuditSecurityBridgeException : InvalidOperationException
{
    /// <summary>Preserves actual submission facts without exposing exception text or inventing remote completion.</summary>
    public AuditSecurityBridgeException(AuditSubmissionResult result)
        : base("The Security observation did not establish committed Audit submission.")
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Outcome == AuditSubmissionOutcome.OutboxCommitted)
        {
            throw new ArgumentException("A committed Audit submission is not a bridge failure.", nameof(result));
        }
        Result = result;
    }

    /// <summary>Gets the allocated occurrence and exact available submission, receipt, error and cleanup facts.</summary>
    public AuditSubmissionResult Result
    {
        get;
    }
}
