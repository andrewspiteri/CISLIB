using CIS.Text;

namespace CIS.Healthcheck;

/// <summary>A bounded immutable scalar measurement with its actual observation time.</summary>
/// <remarks>Text limits count Unicode scalar values. Policy health uses explicit monitor APIs rather than these optional diagnostics.</remarks>
public sealed class HealthMeasurement
{
    private const int MaximumKeyScalars = 128;
    private const int MaximumUnitScalars = 32;
    private const int MaximumStringScalars = 1_024;

    private HealthMeasurement(
        string key,
        HealthMeasurementValueKind valueKind,
        DateTimeOffset observedAtUtc,
        string? unit,
        bool isDiagnostic,
        string? stringValue = null,
        bool? booleanValue = null,
        long? integerValue = null,
        double? numberValue = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ValidateText(key, MaximumKeyScalars, nameof(key));
        if (unit is not null) { ValidateText(unit, MaximumUnitScalars, nameof(unit)); }

        Key = key;
        ValueKind = valueKind;
        Unit = unit;
        ObservedAtUtc = observedAtUtc.ToUniversalTime();
        IsDiagnostic = isDiagnostic;
        StringValue = stringValue;
        BooleanValue = booleanValue;
        IntegerValue = integerValue;
        NumberValue = numberValue;
    }

    /// <summary>The nonblank measurement key, at most 128 Unicode scalar values.</summary>
    public string Key { get; }

    /// <summary>The single scalar representation, or unavailable.</summary>
    public HealthMeasurementValueKind ValueKind { get; }

    /// <summary>The string value for <see cref="HealthMeasurementValueKind.String"/>; otherwise null.</summary>
    public string? StringValue { get; }

    /// <summary>The Boolean value for <see cref="HealthMeasurementValueKind.Boolean"/>; otherwise null.</summary>
    public bool? BooleanValue { get; }

    /// <summary>The Int64 value for <see cref="HealthMeasurementValueKind.Integer"/>; otherwise null.</summary>
    public long? IntegerValue { get; }

    /// <summary>The finite double value for <see cref="HealthMeasurementValueKind.Number"/>; otherwise null.</summary>
    public double? NumberValue { get; }

    /// <summary>An optional unit of at most 32 Unicode scalar values.</summary>
    public string? Unit { get; }

    /// <summary>The supplied observation instant normalized to UTC.</summary>
    public DateTimeOffset ObservedAtUtc { get; }

    /// <summary>Whether this is an optional diagnostic; defaults to true in every factory.</summary>
    /// <remarks>The common monitor report method rejects false without changing state.</remarks>
    public bool IsDiagnostic { get; }

    /// <summary>Creates an unavailable measurement with no scalar representation.</summary>
    /// <remarks>The timestamp is normalized to UTC; unit defaults to null and isDiagnostic defaults to true.</remarks>
    public static HealthMeasurement Unavailable(string key, DateTimeOffset observedAtUtc, string? unit = null, bool isDiagnostic = true) =>
        new(key, HealthMeasurementValueKind.Unavailable, observedAtUtc, unit, isDiagnostic);

    /// <summary>Creates a string measurement, rejecting malformed Unicode or more than 1,024 scalar values.</summary>
    /// <remarks>The timestamp is normalized to UTC; unit defaults to null and isDiagnostic defaults to true.</remarks>
    public static HealthMeasurement String(string key, string value, DateTimeOffset observedAtUtc, string? unit = null, bool isDiagnostic = true)
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidateText(value, MaximumStringScalars, nameof(value));
        return new(key, HealthMeasurementValueKind.String, observedAtUtc, unit, isDiagnostic, stringValue: value);
    }

    /// <summary>Creates a Boolean measurement.</summary>
    /// <remarks>The timestamp is normalized to UTC; unit defaults to null and isDiagnostic defaults to true.</remarks>
    public static HealthMeasurement Boolean(string key, bool value, DateTimeOffset observedAtUtc, string? unit = null, bool isDiagnostic = true) =>
        new(key, HealthMeasurementValueKind.Boolean, observedAtUtc, unit, isDiagnostic, booleanValue: value);

    /// <summary>Creates a signed Int64 measurement.</summary>
    /// <remarks>The timestamp is normalized to UTC; unit defaults to null and isDiagnostic defaults to true.</remarks>
    public static HealthMeasurement Integer(string key, long value, DateTimeOffset observedAtUtc, string? unit = null, bool isDiagnostic = true) =>
        new(key, HealthMeasurementValueKind.Integer, observedAtUtc, unit, isDiagnostic, integerValue: value);

    /// <summary>Creates a finite numeric measurement, rejecting NaN and infinities.</summary>
    /// <remarks>The timestamp is normalized to UTC; unit defaults to null and isDiagnostic defaults to true.</remarks>
    public static HealthMeasurement Number(string key, double value, DateTimeOffset observedAtUtc, string? unit = null, bool isDiagnostic = true)
    {
        if (!double.IsFinite(value)) { throw new ArgumentOutOfRangeException(nameof(value), "Supply a finite number."); }
        return new(key, HealthMeasurementValueKind.Number, observedAtUtc, unit, isDiagnostic, numberValue: value);
    }

    private static void ValidateText(string value, int maximumScalars, string parameterName)
    {
        if (!UnicodeScalarText.IsValid(value, maximumScalars, allowControls: true))
        {
            throw new ArgumentException("Supply bounded valid Unicode scalar text.", parameterName);
        }
    }
}
