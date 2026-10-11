using System.Collections.ObjectModel;
using CIS.Logging.Values;

namespace CIS.Logging;

/// <summary>An immutable, explicitly typed scalar or owned collection of attribute values.</summary>
/// <remarks>Collection inputs are copied; already immutable child values may be shared.
/// Collection traversal projection and UUID/time factories follow in WP04-S01-T02/T03.
/// Event capture/rendering applies the separately configured aggregate entry limit.</remarks>
public sealed class LogValue
{
    private const long MaximumSafeInteger = 9_007_199_254_740_991;
    private readonly LogValueScalar scalar;
    private readonly ReadOnlyCollection<LogValue>? array;
    private readonly ReadOnlyDictionary<string, LogValue>? map;

    private LogValue(LogValueKind kind, LogValueScalar scalar = default,
        ReadOnlyCollection<LogValue>? array = null, ReadOnlyDictionary<string, LogValue>? map = null)
    {
        Kind = kind;
        this.scalar = scalar;
        this.array = array;
        this.map = map;
    }

    /// <summary>Gets the constructor-selected representation; magnitude never changes it.</summary>
    public LogValueKind Kind { get; }

    /// <summary>Gets whether content was omitted from this value.</summary>
    /// <remarks>The current representation factories retain complete input and return false.
    /// Bounded projection/truncation provenance follows in WP04-S01-T03.</remarks>
    public bool IsTruncated => false;

    internal LogValueScalar Scalar => scalar;
    internal IReadOnlyList<LogValue> ArrayItems => array ?? throw new InvalidOperationException("Value is not an array.");
    internal IReadOnlyDictionary<string, LogValue> MapProperties => map ?? throw new InvalidOperationException("Value is not a map.");

    /// <summary>Creates an explicit null value.</summary>
    public static LogValue Null() => new(LogValueKind.Null);

    /// <summary>Creates a Boolean value.</summary>
    public static LogValue Boolean(bool value) => new(LogValueKind.Boolean, new(value));

    /// <summary>Creates a string, preserving valid Unicode text and control characters.</summary>
    /// <exception cref="ArgumentNullException">value is null.</exception>
    /// <exception cref="ArgumentException">value contains malformed UTF-16.</exception>
    public static LogValue String(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!LogValueText.IsValidUnicode(value))
        {
            throw new ArgumentException("Use valid Unicode scalar text.", nameof(value));
        }
        return new(LogValueKind.String, new(value));
    }

    /// <summary>Creates a finite floating-point number, including integer-valued doubles.</summary>
    /// <exception cref="ArgumentOutOfRangeException">value is NaN or infinite.</exception>
    public static LogValue Number(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Use a finite floating-point number.");
        }
        return new(LogValueKind.Number, new(value));
    }

    /// <summary>Creates an integer within inclusive -9007199254740991 through 9007199254740991.</summary>
    /// <exception cref="ArgumentOutOfRangeException">value is outside the portable safe range.</exception>
    public static LogValue Integer(long value)
    {
        if (value < -MaximumSafeInteger || value > MaximumSafeInteger)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Use an integer within inclusive -9007199254740991 through 9007199254740991, or explicitly select IntegerString.");
        }
        return new(LogValueKind.Integer, new(value));
    }

    /// <summary>Creates a canonical base-10 integer string without limiting its magnitude.</summary>
    /// <remarks>No exponent, leading plus/zeros, whitespace or negative zero is accepted.</remarks>
    /// <exception cref="ArgumentNullException">value is null.</exception>
    /// <exception cref="ArgumentException">value is not a canonical integer representation.</exception>
    public static LogValue IntegerString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!LogValueText.IsInteger(value))
        {
            throw new ArgumentException("Use canonical signed base-10 integer text, without leading zeros, exponent or negative zero.", nameof(value));
        }
        return new(LogValueKind.IntegerString, new(value));
    }

    /// <summary>Creates invariant non-exponent decimal text, preserving fractional precision.</summary>
    /// <remarks>A fractional part is required; trailing zeros and negative decimal zero are preserved.</remarks>
    /// <exception cref="ArgumentNullException">value is null.</exception>
    /// <exception cref="ArgumentException">value is not an invariant decimal representation.</exception>
    public static LogValue DecimalString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!LogValueText.IsDecimal(value))
        {
            throw new ArgumentException("Use invariant decimal text with a fractional part and no exponent or leading zeros.", nameof(value));
        }
        return new(LogValueKind.DecimalString, new(value));
    }

    /// <summary>Creates an owned array, preserving the order of immutable child values.</summary>
    /// <exception cref="ArgumentNullException">items is null.</exception>
    /// <exception cref="ArgumentException">items contains a null reference instead of LogValue.Null().</exception>
    public static LogValue Array(IEnumerable<LogValue> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var owned = new List<LogValue>();
        foreach (LogValue item in items)
        {
            if (item is null)
            {
                throw new ArgumentException("Use LogValue.Null() for explicit null array elements.", nameof(items));
            }
            owned.Add(item);
        }
        return new(LogValueKind.Array, array: owned.AsReadOnly());
    }

    /// <summary>Creates an owned ordinal map, preserving key text and immutable child values.</summary>
    /// <exception cref="ArgumentNullException">properties is null.</exception>
    /// <exception cref="ArgumentException">A key is empty, malformed, contains controls, exceeds 255 scalars,
    /// occurs twice, or a value is a null reference instead of LogValue.Null().</exception>
    public static LogValue Map(IReadOnlyDictionary<string, LogValue> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        var owned = new Dictionary<string, LogValue>(StringComparer.Ordinal);
        foreach ((string key, LogValue value) in properties)
        {
            if (key is null || !LogValueText.IsValidKey(key) || value is null || !owned.TryAdd(key, value))
            {
                throw new ArgumentException("Use unique ordinal keys of 1–255 valid Unicode scalars without controls, and explicit LogValue values.", nameof(properties));
            }
        }
        return new(LogValueKind.Map, map: new(owned));
    }
}
