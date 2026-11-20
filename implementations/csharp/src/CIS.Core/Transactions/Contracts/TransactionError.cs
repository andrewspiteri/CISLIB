namespace CIS.Transactions;

/// <summary>Bounded safe error facts, without a native exception, SQL, parameters or credentials.</summary>
public sealed record TransactionError
{
    /// <summary>Creates portable safe diagnostics. Messages must already be safe for disclosure.</summary>
    public TransactionError(TransactionErrorPhase phase, string code, string message)
    {
        if (!Enum.IsDefined(phase))
        {
            throw new ArgumentOutOfRangeException(nameof(phase));
        }
        Phase = phase;
        Code = TransactionText.Code(code, nameof(code));
        if (message is null || message.Length > 512 || string.IsNullOrWhiteSpace(message) || message.Any(char.IsControl))
        {
            throw new ArgumentException("Use a safe nonempty message of at most 512 characters without controls.", nameof(message));
        }
        Message = message;
    }

    /// <summary>Gets the completion or release phase.</summary>
    public TransactionErrorPhase Phase
    {
        get;
    }
    /// <summary>Gets the stable bounded printable ASCII code.</summary>
    public string Code
    {
        get;
    }
    /// <summary>Gets predefined or application-approved safe text.</summary>
    public string Message
    {
        get;
    }
}
