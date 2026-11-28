using System.Buffers;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using CIS.Messaging;
using CIS.Text;

namespace CIS.RuleEngine;

/// <summary>The exact kind of a supplied input or an ordinary preset.</summary>
public enum RuleValueKind
{
    /// <summary>An explicit null.</summary>
    Null,
    /// <summary>A Unicode string.</summary>
    String,
    /// <summary>An exact decimal.</summary>
    Decimal,
    /// <summary>A native Boolean.</summary>
    Boolean,
    /// <summary>An ordered list of literal strings.</summary>
    StringList,
    /// <summary>An opaque JSON value.</summary>
    Json
}

/// <summary>A detached, typed rule input or preset.</summary>
public sealed class RuleValue
{
    private static readonly UInt128 MaximumDecimalCoefficient = ((UInt128)1 << 96) - 1;

    private readonly string? text;
    private readonly decimal number;
    private readonly bool boolean;
    private readonly IReadOnlyList<string>? strings;
    private readonly JsonElement json;

    private RuleValue(RuleValueKind kind, string? text = null, decimal number = 0m, bool boolean = false,
        IReadOnlyList<string>? strings = null, JsonElement json = default)
    {
        Kind = kind;
        this.text = text;
        this.number = number;
        this.boolean = boolean;
        this.strings = strings;
        this.json = json;
    }

    /// <summary>Gets the explicit null value. Omitted inputs have no entry in the input snapshot.</summary>
    public static RuleValue Null { get; } = new(RuleValueKind.Null);

    /// <summary>Captures an exact Unicode string, including an empty string.</summary>
    public static RuleValue FromString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        RuleBounds.RequireString(value, nameof(value));
        return new(RuleValueKind.String, text: value);
    }

    /// <summary>Parses portable decimal text exactly and retains its original accepted spelling.</summary>
    public static RuleValue FromDecimal(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length is 0 or > RuleBounds.MaximumDefinitionBytes)
        {
            throw new ArgumentException("Supply bounded portable decimal text.", nameof(text));
        }

        ReadOnlySpan<char> digits = text.AsSpan();
        bool negative = digits[0] == '-';
        if (negative)
        {
            digits = digits[1..];
        }

        int separator = digits.IndexOf('.');
        ReadOnlySpan<char> integer = separator < 0 ? digits : digits[..separator];
        ReadOnlySpan<char> fraction = separator < 0 ? [] : digits[(separator + 1)..];
        if (integer.IsEmpty || (integer.Length > 1 && integer[0] == '0')
            || integer.IndexOfAnyExceptInRange('0', '9') >= 0
            || (separator >= 0 && fraction.IsEmpty) || fraction.IndexOfAnyExceptInRange('0', '9') >= 0)
        {
            throw new ArgumentException("Use invariant decimal text without grouping, exponent, or padding.", nameof(text));
        }

        fraction = fraction.TrimEnd('0');
        if (fraction.Length > 28 || integer.Length + fraction.Length > 29)
        {
            throw new ArgumentException("The decimal is outside the exact supported range.", nameof(text));
        }

        UInt128 coefficient = Accumulate(0, integer);
        coefficient = Accumulate(coefficient, fraction);
        if (coefficient > MaximumDecimalCoefficient)
        {
            throw new ArgumentException("The decimal is outside the exact supported range.", nameof(text));
        }

        var value = new decimal(
            unchecked((int)(uint)coefficient),
            unchecked((int)(uint)(coefficient >> 32)),
            unchecked((int)(uint)(coefficient >> 64)),
            negative, (byte)fraction.Length);
        return new(RuleValueKind.Decimal, text: text, number: value);
    }

    /// <summary>Captures a native Boolean without string conversion.</summary>
    public static RuleValue FromBoolean(bool value) => new(RuleValueKind.Boolean, boolean: value);

    /// <summary>Copies a bounded list of literal Unicode strings without removing duplicates.</summary>
    public static RuleValue FromStringList(IEnumerable<string> values)
    {
        IReadOnlyList<string> captured = RuleBounds.Copy(values, RuleBounds.MaximumListItems, nameof(values));
        foreach (string value in captured)
        {
            RuleBounds.RequireString(value, nameof(values));
        }

        return new(RuleValueKind.StringList, strings: captured);
    }

    /// <summary>Captures a detached opaque JSON value or maps a JSON null to explicit null.</summary>
    public static RuleValue FromJson(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return Null;
        }

        if (value.ValueKind == JsonValueKind.Undefined)
        {
            throw new ArgumentException("Supply a JSON value.", nameof(value));
        }

        ReadOnlySpan<byte> raw = JsonMarshal.GetRawUtf8Value(value);
        if (raw.Length > RuleBounds.MaximumInputBytes || !MessageWire.ValidateJson(raw, RuleBounds.MaximumJsonDepth))
        {
            throw new ArgumentException("Supply bounded strict JSON with unique decoded member names.", nameof(value));
        }

        return new(RuleValueKind.Json, json: value.Clone());
    }

    /// <summary>Gets the exact value kind.</summary>
    public RuleValueKind Kind
    {
        get;
    }

    /// <summary>Gets a string value.</summary>
    public string StringValue => Kind == RuleValueKind.String ? text! : throw WrongKind(RuleValueKind.String);

    /// <summary>Gets an exact decimal value for comparison.</summary>
    public decimal DecimalValue => Kind == RuleValueKind.Decimal ? number : throw WrongKind(RuleValueKind.Decimal);

    /// <summary>Gets the original accepted decimal text for portable transport.</summary>
    public string DecimalText => Kind == RuleValueKind.Decimal ? text! : throw WrongKind(RuleValueKind.Decimal);

    /// <summary>Gets a native Boolean value.</summary>
    public bool BooleanValue => Kind == RuleValueKind.Boolean ? boolean : throw WrongKind(RuleValueKind.Boolean);

    /// <summary>Gets the exact ordered string list.</summary>
    public IReadOnlyList<string> StringListValue =>
        Kind == RuleValueKind.StringList ? strings! : throw WrongKind(RuleValueKind.StringList);

    /// <summary>Gets the detached opaque JSON value.</summary>
    public JsonElement JsonValue => Kind == RuleValueKind.Json ? json : throw WrongKind(RuleValueKind.Json);

    internal void WriteFields(Utf8JsonWriter writer)
    {
        switch (Kind)
        {
            case RuleValueKind.Null:
                writer.WriteString("kind", "null");
                break;
            case RuleValueKind.String:
                writer.WriteString("kind", "string");
                writer.WriteString("value", StringValue);
                break;
            case RuleValueKind.Decimal:
                writer.WriteString("kind", "decimal");
                writer.WriteString("value", DecimalText);
                break;
            case RuleValueKind.Boolean:
                writer.WriteString("kind", "boolean");
                writer.WriteBoolean("value", BooleanValue);
                break;
            case RuleValueKind.StringList:
                writer.WriteString("kind", "stringList");
                writer.WriteStartArray("value");
                foreach (string value in StringListValue)
                {
                    writer.WriteStringValue(value);
                }

                writer.WriteEndArray();
                break;
            case RuleValueKind.Json:
                writer.WriteString("kind", "json");
                writer.WritePropertyName("value");
                writer.WriteRawValue(JsonMarshal.GetRawUtf8Value(JsonValue), skipInputValidation: true);
                break;
        }
    }

    private static UInt128 Accumulate(UInt128 coefficient, ReadOnlySpan<char> digits)
    {
        foreach (char digit in digits)
        {
            coefficient = coefficient * 10 + (uint)(digit - '0');
        }

        return coefficient;
    }

    private InvalidOperationException WrongKind(RuleValueKind expected) =>
        new($"The requested rule value kind is {expected}. The actual kind is {Kind}.");
}

/// <summary>The exact supplied values of one invocation, detached from the caller.</summary>
public sealed class RuleInputSnapshot
{
    /// <summary>Captures supplied values, including unknown columns and explicit nulls.</summary>
    public RuleInputSnapshot(IEnumerable<KeyValuePair<string, RuleValue>> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var captured = new Dictionary<string, RuleValue>(StringComparer.Ordinal);
        var encoded = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(encoded);
        writer.WriteStartArray();
        foreach (KeyValuePair<string, RuleValue> entry in values)
        {
            RuleBounds.RequireId(entry.Key, nameof(values));
            if (entry.Value is null || !captured.TryAdd(entry.Key, entry.Value))
            {
                throw new ArgumentException("Supply one explicit rule value for each supplied key.", nameof(values));
            }

            WriteEntry(writer, entry.Key, entry.Value);
            if (writer.BytesCommitted + writer.BytesPending >= RuleBounds.MaximumInputBytes)
            {
                throw new ArgumentException("The encoded input snapshot exceeds its byte limit.", nameof(values));
            }
        }

        writer.WriteEndArray();
        writer.Flush();
        if (!MessageWire.ValidateJson(encoded.WrittenSpan, RuleBounds.MaximumJsonDepth))
        {
            throw new ArgumentException("The input snapshot exceeds its JSON depth or contains invalid JSON.", nameof(values));
        }

        Values = new ReadOnlyDictionary<string, RuleValue>(captured);
        EncodedSizeBytes = encoded.WrittenCount;
    }

    /// <summary>Gets the supplied entries under ordinal column identities.</summary>
    public IReadOnlyDictionary<string, RuleValue> Values
    {
        get;
    }

    /// <summary>Gets the encoded byte count of the captured input entries.</summary>
    public int EncodedSizeBytes
    {
        get;
    }

    internal void WriteTo(Utf8JsonWriter writer)
    {
        writer.WriteStartArray();
        foreach ((string columnId, RuleValue value) in Values)
        {
            WriteEntry(writer, columnId, value);
        }

        writer.WriteEndArray();
    }

    private static void WriteEntry(Utf8JsonWriter writer, string columnId, RuleValue value)
    {
        writer.WriteStartObject();
        writer.WriteString("columnId", columnId);
        value.WriteFields(writer);
        writer.WriteEndObject();
    }
}

internal static class RuleBounds
{
    internal const int MaximumColumns = 24;
    internal const int MaximumRows = 48;
    internal const int MaximumListItems = 32;
    internal const int MaximumStringBytes = 512;
    internal const int MaximumIdBytes = 128;
    internal const int MaximumMetadataBytes = 4 * 1024;
    internal const int MaximumDefinitionBytes = 128 * 1024;
    internal const int MaximumInputBytes = 24 * 1024;
    internal const int MaximumJsonDepth = 16;

    internal static bool IsId(string? value) =>
        !string.IsNullOrEmpty(value) && UnicodeScalarText.IsValid(value, MaximumIdBytes, allowControls: false)
        && Encoding.UTF8.GetByteCount(value) <= MaximumIdBytes;

    internal static void RequireId(string value, string name)
    {
        if (!IsId(value))
        {
            throw new ArgumentException("Supply a nonempty bounded Unicode identifier without control scalars.", name);
        }
    }

    internal static void RequireString(string value, string name)
    {
        if (!UnicodeScalarText.IsValid(value, MaximumStringBytes, allowControls: true)
            || Encoding.UTF8.GetByteCount(value) > MaximumStringBytes)
        {
            throw new ArgumentException("Supply bounded Unicode text without unpaired surrogates.", name);
        }
    }

    internal static IReadOnlyList<T> Copy<T>(IEnumerable<T> source, int maximum, string name)
    {
        ArgumentNullException.ThrowIfNull(source, name);

        var copy = new List<T>();
        foreach (T value in source)
        {
            if (value is null || copy.Count == maximum)
            {
                throw new ArgumentException("The collection contains a null entry or exceeds its item limit.", name);
            }

            copy.Add(value);
        }

        return Array.AsReadOnly(copy.ToArray());
    }
}
