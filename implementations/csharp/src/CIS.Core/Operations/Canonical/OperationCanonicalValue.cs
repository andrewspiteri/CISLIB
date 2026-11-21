using System.Globalization;
using System.Numerics;
using System.Text;

namespace CIS.Operations;

/// <summary>An immutable bounded canonical value chosen explicitly by an operation's trusted normalizer.</summary>
/// <remarks>This is typed framing, not reflection or a general JSON normalization engine.</remarks>
public sealed class OperationCanonicalValue
{
    private const int MaximumBytes = 1_048_576;
    private readonly byte tag;
    private readonly byte[] payload;
    private readonly OperationCanonicalValue[] sequence;
    private readonly MapEntry[] map;

    private OperationCanonicalValue(byte tag, byte[] payload)
    {
        if (payload.Length > MaximumBytes)
        {
            throw new ArgumentException("One canonical value cannot exceed one MiB.", nameof(payload));
        }
        this.tag = tag;
        this.payload = payload;
        sequence = [];
        map = [];
        PayloadLength = payload.Length;
        Depth = 1;
        ValueCount = 1;
    }

    private OperationCanonicalValue(OperationCanonicalValue[] values)
    {
        tag = 7;
        payload = [];
        sequence = values;
        map = [];
        PayloadLength = checked(4 + values.Sum(value => value.EncodedLength));
        Depth = 1 + values.Select(value => value.Depth).DefaultIfEmpty().Max();
        ValueCount = 1 + values.Sum(value => value.ValueCount);
        ValidateTree();
    }

    private OperationCanonicalValue(MapEntry[] entries)
    {
        tag = 8;
        payload = [];
        sequence = [];
        map = entries;
        PayloadLength = checked(4 + entries.Sum(entry => entry.Key.EncodedLength + entry.Value.EncodedLength));
        Depth = 1 + entries.Select(entry => Math.Max(entry.Key.Depth, entry.Value.Depth)).DefaultIfEmpty().Max();
        ValueCount = 1 + entries.Sum(entry => entry.Key.ValueCount + entry.Value.ValueCount);
        ValidateTree();
    }

    internal int Depth
    {
        get;
    }
    internal int ValueCount
    {
        get;
    }
    internal int PayloadLength
    {
        get;
    }
    internal int EncodedLength => checked(5 + PayloadLength);

    /// <summary>Represents a missing field; operation-specific defaults are resolved explicitly by its normalizer.</summary>
    public static OperationCanonicalValue Missing { get; } = new(0, []);
    /// <summary>Represents an explicit null, distinct from missing.</summary>
    public static OperationCanonicalValue Null { get; } = new(1, []);
    /// <summary>Represents one boolean as a single zero or one byte.</summary>
    public static OperationCanonicalValue Boolean(bool value) => new(2, [value ? (byte)1 : (byte)0]);
    /// <summary>Represents a signed 64-bit integer as canonical invariant decimal ASCII.</summary>
    public static OperationCanonicalValue Integer(long value)
        => new(3, Encoding.ASCII.GetBytes(value.ToString(CultureInfo.InvariantCulture)));

    /// <summary>Represents a .NET-range decimal as sign byte, scale byte and normalized unsigned coefficient ASCII.</summary>
    public static OperationCanonicalValue Decimal(decimal value)
    {
        int[] bits = decimal.GetBits(value);
        BigInteger coefficient = (uint)bits[0] | ((BigInteger)(uint)bits[1] << 32) | ((BigInteger)(uint)bits[2] << 64);
        int scale = (bits[3] >> 16) & 0xff;
        bool negative = (bits[3] & int.MinValue) != 0;
        if (coefficient.IsZero)
        {
            scale = 0;
            negative = false;
        }
        else
        {
            while (scale > 0 && coefficient % 10 == 0)
            {
                coefficient /= 10;
                scale--;
            }
        }
        byte[] digits = Encoding.ASCII.GetBytes(coefficient.ToString(CultureInfo.InvariantCulture));
        byte[] encoded = new byte[2 + digits.Length];
        encoded[0] = negative ? (byte)1 : (byte)0;
        encoded[1] = (byte)scale;
        digits.CopyTo(encoded, 2);
        return new(4, encoded);
    }

    /// <summary>Copies text normalized to NFC and encoded using strict UTF-8 without a BOM.</summary>
    public static OperationCanonicalValue Text(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > MaximumBytes)
        {
            throw new ArgumentException("One canonical text value cannot exceed one MiB.", nameof(value));
        }
        string normalized;
        try
        {
            normalized = value.Normalize(NormalizationForm.FormC);
            if (OperationText.Utf8Encoding.GetByteCount(normalized) > MaximumBytes)
            {
                throw new ArgumentException("One canonical text value cannot exceed one MiB.", nameof(value));
            }
            return new(5, OperationText.Utf8Encoding.GetBytes(normalized));
        }
        catch (EncoderFallbackException error)
        {
            throw new ArgumentException("Canonical text requires valid Unicode.", nameof(value), error);
        }
    }

    /// <summary>Copies an opaque byte value without text conversion.</summary>
    public static OperationCanonicalValue Bytes(ReadOnlyMemory<byte> value)
    {
        if (value.Length > MaximumBytes)
        {
            throw new ArgumentException("One canonical byte value cannot exceed one MiB.", nameof(value));
        }
        return new(6, value.ToArray());
    }

    /// <summary>Copies at most 64 ordered values, retaining meaningful sequence order.</summary>
    public static OperationCanonicalValue Sequence(IEnumerable<OperationCanonicalValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var copied = new List<OperationCanonicalValue>();
        foreach (OperationCanonicalValue value in values)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (copied.Count == 64)
            {
                throw new ArgumentException("A canonical container contains at most 64 entries.", nameof(values));
            }
            copied.Add(value);
        }
        return new(copied.ToArray());
    }

    /// <summary>Copies at most 64 unique NFC text keys and sorts them by their UTF-8 bytes.</summary>
    public static OperationCanonicalValue Map(IEnumerable<KeyValuePair<string, OperationCanonicalValue>> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var copied = new List<MapEntry>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string key, OperationCanonicalValue value) in entries)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (copied.Count == 64)
            {
                throw new ArgumentException("A canonical container contains at most 64 entries.", nameof(entries));
            }
            OperationCanonicalValue encodedKey = Text(key);
            string normalized = OperationText.Utf8Encoding.GetString(encodedKey.payload);
            if (!keys.Add(normalized))
            {
                throw new ArgumentException("Canonical map keys must remain unique after NFC normalization.", nameof(entries));
            }
            copied.Add(new(encodedKey, value));
        }
        copied.Sort((left, right) => left.Key.payload.AsSpan().SequenceCompareTo(right.Key.payload));
        return new(copied.ToArray());
    }

    internal void Write(CanonicalFrameWriter writer)
    {
        writer.Byte(tag);
        writer.UInt32((uint)PayloadLength);
        if (tag == 7)
        {
            writer.UInt32((uint)sequence.Length);
            foreach (OperationCanonicalValue value in sequence)
            {
                value.Write(writer);
            }
        }
        else if (tag == 8)
        {
            writer.UInt32((uint)map.Length);
            foreach (MapEntry entry in map)
            {
                entry.Key.Write(writer);
                entry.Value.Write(writer);
            }
        }
        else
        {
            writer.Bytes(payload);
        }
    }

    private void ValidateTree()
    {
        if (PayloadLength > MaximumBytes || Depth > 8 || ValueCount > 256)
        {
            throw new ArgumentException("Canonical input admits at most one MiB, depth eight and 256 aggregate values.");
        }
    }

    private sealed record MapEntry(OperationCanonicalValue Key, OperationCanonicalValue Value);
}
