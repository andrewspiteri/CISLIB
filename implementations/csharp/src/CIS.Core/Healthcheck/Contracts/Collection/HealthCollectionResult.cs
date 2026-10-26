namespace CIS.Healthcheck;

/// <summary>Identifies the single policy result represented by a collection value.</summary>
public enum HealthCollectionResultKind
{
    /// <summary>A condition's flagged or clear observation.</summary>
    Condition = 0,
    /// <summary>A consecutive-failure policy's execution outcome.</summary>
    Execution = 1,
    /// <summary>A numeric policy's finite observation.</summary>
    Number = 2,
    /// <summary>Usable data was published for an availability policy.</summary>
    AvailabilityPublished = 3,
    /// <summary>An availability refresh failed without invalidating already published data.</summary>
    AvailabilityRefreshFailed = 4,
    /// <summary>Optional measurements without a policy observation.</summary>
    Diagnostics = 5
}

/// <summary>An immutable, bounded policy result with optional detached diagnostic measurements.</summary>
public sealed class HealthCollectionResult
{
    private const int MaximumMeasurements = 32;

    private HealthCollectionResult(
        HealthCollectionResultKind kind,
        bool? isFlagged = null,
        HealthExecutionOutcome? outcome = null,
        double? value = null,
        HealthDiagnostic? diagnostic = null,
        IReadOnlyList<HealthMeasurement>? measurements = null)
    {
        Kind = kind;
        IsFlagged = isFlagged;
        Outcome = outcome;
        Value = value;
        Diagnostic = diagnostic;
        Measurements = CopyMeasurements(measurements ?? Array.Empty<HealthMeasurement>());
    }

    /// <summary>Gets the result discriminator; only its matching scalar property is populated.</summary>
    public HealthCollectionResultKind Kind { get; }

    /// <summary>Gets the condition observation, or null for another result kind.</summary>
    public bool? IsFlagged { get; }

    /// <summary>Gets the execution observation, or null for another result kind.</summary>
    public HealthExecutionOutcome? Outcome { get; }

    /// <summary>Gets the finite numeric observation, or null for another result kind.</summary>
    public double? Value { get; }

    /// <summary>Gets the optional detached policy diagnostic.</summary>
    public HealthDiagnostic? Diagnostic { get; }

    /// <summary>Gets up to 32 uniquely keyed diagnostic measurements in owned read-only storage.</summary>
    public IReadOnlyList<HealthMeasurement> Measurements { get; }

    /// <summary>Creates a condition observation.</summary>
    public static HealthCollectionResult Condition(bool isFlagged, HealthDiagnostic? diagnostic = null) =>
        new(HealthCollectionResultKind.Condition, isFlagged: isFlagged, diagnostic: diagnostic);

    /// <summary>Creates one execution observation; unsupported outcome values are rejected.</summary>
    public static HealthCollectionResult Execution(HealthExecutionOutcome outcome, HealthDiagnostic? diagnostic = null)
    {
        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }
        return new(HealthCollectionResultKind.Execution, outcome: outcome, diagnostic: diagnostic);
    }

    /// <summary>Creates a finite numeric observation; NaN and infinity are rejected.</summary>
    public static HealthCollectionResult Number(double value, HealthDiagnostic? diagnostic = null)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "A numeric health observation must be finite.");
        }
        return new(HealthCollectionResultKind.Number, value: value, diagnostic: diagnostic);
    }

    /// <summary>Records that usable data was published.</summary>
    public static HealthCollectionResult AvailabilityPublished() =>
        new(HealthCollectionResultKind.AvailabilityPublished);

    /// <summary>Records a refresh failure without explicitly invalidating existing availability.</summary>
    public static HealthCollectionResult AvailabilityRefreshFailed(HealthDiagnostic? diagnostic = null) =>
        new(HealthCollectionResultKind.AvailabilityRefreshFailed, diagnostic: diagnostic);

    /// <summary>Creates an optional-only result, copying and validating all measurements.</summary>
    public static HealthCollectionResult Diagnostics(IReadOnlyList<HealthMeasurement> measurements)
    {
        ArgumentNullException.ThrowIfNull(measurements);
        return new(HealthCollectionResultKind.Diagnostics, measurements: measurements);
    }

    /// <summary>Returns a new policy result with the supplied measurements replacing any prior measurements.</summary>
    /// <remarks>Use the Diagnostics factory for optional-only results. Duplicate keys and nondiagnostic values are invalid.</remarks>
    public HealthCollectionResult WithMeasurements(IReadOnlyList<HealthMeasurement> measurements)
    {
        ArgumentNullException.ThrowIfNull(measurements);
        if (Kind == HealthCollectionResultKind.Diagnostics)
        {
            throw new InvalidOperationException("Attach measurements to a policy result or use the Diagnostics factory.");
        }
        return new(Kind, IsFlagged, Outcome, Value, Diagnostic, measurements);
    }

    private static IReadOnlyList<HealthMeasurement> CopyMeasurements(IReadOnlyList<HealthMeasurement> measurements)
    {
        if (measurements.Count > MaximumMeasurements)
        {
            throw new ArgumentException("A result supports at most 32 measurements.", nameof(measurements));
        }

        var copy = new HealthMeasurement[measurements.Count];
        var keys = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < copy.Length; index++)
        {
            var measurement = measurements[index];
            if (measurement is null || !measurement.IsDiagnostic || !keys.Add(measurement.Key))
            {
                throw new ArgumentException("Measurements must be nonnull diagnostics with unique ordinal keys.", nameof(measurements));
            }
            copy[index] = measurement;
        }
        return Array.AsReadOnly(copy);
    }
}
