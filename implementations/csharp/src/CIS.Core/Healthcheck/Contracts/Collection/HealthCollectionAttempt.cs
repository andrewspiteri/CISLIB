namespace CIS.Healthcheck;

/// <summary>The state coordinator's disposition of one collection attempt.</summary>
public enum HealthCollectionDisposition
{
    /// <summary>The applicable result or bounded error was recorded.</summary>
    Committed = 0,
    /// <summary>A newer observation won, so the stale result was discarded.</summary>
    Obsolete = 1,
    /// <summary>The relevant registration or monitor was withdrawn.</summary>
    Retired = 2,
    /// <summary>The owner refused a lease and the monitor was withdrawn.</summary>
    OwnerUnavailable = 3
}

/// <summary>A detached collection completion; its completion does not assert healthy state.</summary>
public sealed class HealthCollectionAttempt
{
    internal HealthCollectionAttempt(
        HealthCollectionDisposition disposition,
        bool evaluationFailed,
        HealthDiagnostic? diagnostic)
    {
        Disposition = disposition;
        EvaluationFailed = evaluationFailed;
        Diagnostic = diagnostic;
    }

    /// <summary>Gets whether the attempt was committed, superseded, or retired.</summary>
    public HealthCollectionDisposition Disposition { get; }

    /// <summary>Gets whether an admitted evaluation failed, independently of the monitor's health.</summary>
    public bool EvaluationFailed { get; }

    /// <summary>Gets the optional detached bounded diagnostic.</summary>
    public HealthDiagnostic? Diagnostic { get; }
}
