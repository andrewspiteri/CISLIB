using System.Collections.ObjectModel;
using System.Globalization;
using CIS.Logging.Values;

namespace CIS.Logging;

/// <summary>An immutable, explicitly typed scalar or owned collection of attribute values.</summary>
/// <remarks>Collection inputs are copied; already immutable child values may be shared.
/// Every collection root is node 1 at depth 1; retained shapes have at most 1024 nodes and depth 8.
/// Oversized collections keep a bounded projection and carry truncation provenance.
/// UUID/time factories accept typed scalars; arbitrary objects have no conversion path.
/// Event capture/rendering applies the separately configured aggregate entry limit.</remarks>
public sealed class LogValue
{
    private const long MaximumSafeInteger = 9_007_199_254_740_991;
    private readonly LogValueScalar scalar;
    private readonly ReadOnlyCollection<LogValue>? array;
    private readonly ReadOnlyDictionary<string, LogValue>? map;

    private LogValue(LogValueKind kind, LogValueScalar scalar = default,
        ReadOnlyCollection<LogValue>? array = null, ReadOnlyDictionary<string, LogValue>? map = null, bool isTruncated = false)
    {
        Kind = kind;
        this.scalar = scalar;
        this.array = array;
        this.map = map;
        var shape = (Nodes: 1, Depth: 1, Truncated: false);
        if (array is not null)
        {
            shape = Measure(array);
        }
        else if (map is not null)
        {
            shape = Measure(map.Values);
        }
        NodeCount = shape.Nodes;
        Depth = shape.Depth;
        IsTruncated = isTruncated || shape.Truncated;
    }

    /// <summary>Gets the constructor-selected representation; magnitude never changes it.</summary>
    public LogValueKind Kind { get; }

    /// <summary>Gets whether this value or a retained descendant has omitted content.</summary>
    /// <remarks>Collection projection propagates prior and newly observed truncation.
    /// Aggregate entry-byte projection remains an event capture/rendering responsibility.</remarks>
    public bool IsTruncated { get; }

    internal int NodeCount { get; }
    internal int Depth { get; }
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

    /// <summary>Creates a lowercase UUID string in hyphenated 8-4-4-4-12 form.</summary>
    /// <remarks>All Guid values, including Guid.Empty, are valid attribute scalars.</remarks>
    public static LogValue Uuid(Guid value) => new(LogValueKind.Uuid, new(value.ToString("D")));

    /// <summary>Creates a UTC timestamp with exactly seven fractional digits.</summary>
    /// <remarks>The supplied offset is converted to the same UTC instant; formatting is invariant.</remarks>
    public static LogValue Timestamp(DateTimeOffset value) => new(LogValueKind.Timestamp,
        new(value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture)));

    /// <summary>Creates a bounded owned array, preserving input order.</summary>
    /// <remarks>The root counts as node/depth 1. At most 1024 nodes and depth 8 are retained.
    /// Copying stops after the first excess child; observed invalid children remain errors.</remarks>
    /// <exception cref="ArgumentNullException">items is null.</exception>
    /// <exception cref="ArgumentException">items contains a null reference instead of LogValue.Null().</exception>
    public static LogValue Array(IEnumerable<LogValue> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return new LogValueProjection().Array(items, depth: 1);
    }

    /// <summary>Creates a bounded owned ordinal map, preserving key text.</summary>
    /// <remarks>The root counts as node/depth 1. At most 1024 nodes and depth 8 are retained.
    /// Structural projection keeps the enumeration prefix and stops at the first excess child.
    /// Retained keys compare ordinally; observed invalid keys/children remain errors.</remarks>
    /// <exception cref="ArgumentNullException">properties is null.</exception>
    /// <exception cref="ArgumentException">A key is empty, malformed, contains controls, exceeds 255 scalars,
    /// occurs twice, or a value is a null reference instead of LogValue.Null().</exception>
    public static LogValue Map(IReadOnlyDictionary<string, LogValue> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        return new LogValueProjection().Map(properties, depth: 1);
    }

    internal static LogValue OwnedArray(List<LogValue> items, bool truncated) =>
        new(LogValueKind.Array, array: items.AsReadOnly(), isTruncated: truncated);

    internal static LogValue OwnedMap(Dictionary<string, LogValue> properties, bool truncated) =>
        new(LogValueKind.Map, map: new(properties), isTruncated: truncated);

    private static (int Nodes, int Depth, bool Truncated) Measure(IEnumerable<LogValue> children)
    {
        int nodes = 1;
        int depth = 1;
        bool truncated = false;
        foreach (LogValue child in children)
        {
            nodes += child.NodeCount;
            depth = Math.Max(depth, child.Depth + 1);
            truncated |= child.IsTruncated;
        }
        return (nodes, depth, truncated);
    }
}
