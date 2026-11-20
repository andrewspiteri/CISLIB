namespace CIS.Transactions.Hosting;

/// <summary>Owner cleanup and routine logging policy, copied during registration.</summary>
public sealed class LocalTransactionRuntimeOptions
{
    /// <summary>Gets or sets the positive finite cooperative rollback budget; default thirty seconds.</summary>
    /// <remarks>The native timer accepts at most 4,294,967,294 milliseconds. Expiry requests cancellation;
    /// it never substitutes for the actual settlement of rollback or tokenless disposal.</remarks>
    public TimeSpan RollbackTimeout { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets or sets successful terminal Debug summaries; default false. Failures retain diagnostics.</summary>
    public bool LogCalls
    {
        get; set;
    }
}
