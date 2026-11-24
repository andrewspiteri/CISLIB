using System.Collections;
using System.Text;
using System.Text.Json;

namespace CIS.Configuration;

// A process-runtime logical byte allowance. It covers retained default/source
// buffers, admitted reads and detached mapping work, not arbitrary host callbacks
// or the CLR allocator's object/capacity overhead.
internal sealed class ConfigurationByteBudget
{
    internal const long MaximumBytes = 16 * 1024 * 1024;
    private readonly object gate = new();
    private long owned;

    internal long OwnedBytes
    {
        get
        {
            lock (gate)
            {
                return owned;
            }
        }
    }

    internal Reservation Reserve(long bytes)
    {
        var reservation = new Reservation(this);
        reservation.Resize(bytes);
        return reservation;
    }

    internal sealed class Reservation(ConfigurationByteBudget owner) : IDisposable
    {
        private long bytes;
        private bool disposed;

        internal void Resize(long next)
        {
            lock (owner.gate)
            {
                if (disposed || next < 0 || next > MaximumBytes - owner.owned + bytes)
                {
                    throw ConfigurationCentralLimits.Error("Configuration.Capacity", "The shared logical configuration byte allowance is exhausted.");
                }
                owner.owned = owner.owned - bytes + next;
                bytes = next;
            }
        }

        internal void EnsureAtLeast(long minimum)
        {
            lock (owner.gate)
            {
                if (minimum > bytes)
                {
                    Resize(minimum);
                }
            }
        }

        public void Dispose()
        {
            lock (owner.gate)
            {
                if (disposed)
                {
                    return;
                }
                disposed = true;
                owner.owned -= bytes;
                bytes = 0;
            }
        }
    }
}

internal static class ConfigurationCentralLimits
{
    internal const int MaximumSourceBytes = 256 * 1024;
    internal const int MaximumReadBytes = 1024 * 1024;
    private const int MaximumDepth = 16;
    private const int MaximumNodes = 65_536;
    private const int MaximumCollection = 4096;
    private const int MaximumMembers = 256;
    private const int MaximumScalarBytes = 64 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static void Shape(Type type, int depth, int members)
    {
        if (depth > MaximumDepth || members > MaximumMembers)
        {
            throw ConfigurationDiagnostics.Error(type, "Configuration.Materialization", "A central declared shape exceeds its depth or object-member ceiling.");
        }
    }

    internal static long MeasureDefault(ConfigurationShape shape, object? value)
    {
        var count = new Counter();
        WalkDefault(shape, value, 0, count);
        return count.Bytes;
    }

    internal static long MeasureJson(JsonElement value, bool source = false)
    {
        var count = new Counter();
        WalkJson(value, 0, count);
        if (source && StrictUtf8.GetByteCount(value.GetRawText()) > MaximumSourceBytes)
        {
            throw Error("Configuration.SourceSize", "A central source exceeds its decoded JSON byte ceiling.");
        }
        return count.Bytes;
    }

    internal static long SourceBytes(JsonElement? value)
    {
        if (value is null)
        {
            return 0;
        }
        MeasureJson(value.Value, source: true);
        return StrictUtf8.GetByteCount(value.Value.GetRawText());
    }

    internal static long RetainedSourceBytes(ConfigurationReadResult source)
    {
        long bytes = SourceBytes(source.Payload);
        if (source.ExactSourceText is { } exact)
        {
            int textBytes = StrictUtf8.GetByteCount(exact);
            if (textBytes > MaximumSourceBytes)
            {
                throw Error("Configuration.SourceSize", "An exact central source exceeds its decoded UTF-8 ceiling.");
            }
            bytes += textBytes;
        }
        return bytes;
    }

    // Simulate completion before allocating missing defaults or replacement
    // objects. Empty supplied children can otherwise multiply a valid default.
    internal static long MeasureCompleted(ConfigurationMappedValue value)
    {
        var count = new Counter();
        WalkMapped(value, 0, count);
        return count.Bytes;
    }

    private static void WalkMapped(ConfigurationMappedValue value, int depth, Counter count)
    {
        count.Node(depth);
        if (value.IsNull)
        {
            return;
        }
        if (value.Shape.Kind == ConfigurationValueKind.Object)
        {
            foreach (ConfigurationProperty property in value.Shape.Properties)
            {
                count.Text(property.Name);
                if (value.Members!.TryGetValue(property.Name, out ConfigurationMappedValue? member))
                {
                    WalkMapped(member, depth + 1, count);
                }
                else
                {
                    WalkJson(value.Shape.DeclaredDefaults.GetProperty(property.Name), depth + 1, count);
                }
            }
        }
        else if (value.Shape.Kind == ConfigurationValueKind.Sequence)
        {
            count.Collection(value.Items!.Count);
            foreach (ConfigurationMappedValue item in value.Items)
            {
                WalkMapped(item, depth + 1, count);
            }
        }
        else if (value.Shape.Kind == ConfigurationValueKind.Dictionary)
        {
            count.Collection(value.Members!.Count);
            foreach ((string name, ConfigurationMappedValue member) in value.Members)
            {
                count.Text(name);
                WalkMapped(member, depth + 1, count);
            }
        }
        else if (value.Scalar is string text)
        {
            count.Text(text);
        }
    }

    private static void WalkJson(JsonElement value, int depth, Counter count)
    {
        count.Node(depth);
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonProperty property in value.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                    {
                        throw Error("Configuration.Mapping", "A central record contains duplicate decoded object names.");
                    }
                    count.Collection(names.Count);
                    count.Text(property.Name);
                    WalkJson(property.Value, depth + 1, count);
                }
                break;
            case JsonValueKind.Array:
                count.Collection(value.GetArrayLength());
                foreach (JsonElement item in value.EnumerateArray())
                {
                    WalkJson(item, depth + 1, count);
                }
                break;
            case JsonValueKind.String:
                count.Text(value.GetString()!);
                break;
        }
    }

    private static void WalkDefault(ConfigurationShape shape, object? value, int depth, Counter count)
    {
        count.Node(depth);
        if (value is null)
        {
            return;
        }
        switch (shape.Kind)
        {
            case ConfigurationValueKind.Scalar:
                if (value is string text)
                {
                    count.Text(text);
                }
                break;
            case ConfigurationValueKind.Object:
                Shape(shape.Type, depth, shape.Properties.Count);
                foreach (ConfigurationProperty property in shape.Properties)
                {
                    count.Text(property.Name);
                    WalkDefault(property.Shape, property.Property.GetValue(value), depth + 1, count);
                }
                break;
            case ConfigurationValueKind.Sequence:
                int length = 0;
                foreach (object? item in (IEnumerable)value)
                {
                    count.Collection(++length);
                    WalkDefault(shape.ElementShape!, item, depth + 1, count);
                }
                break;
            case ConfigurationValueKind.Dictionary:
                int entries = 0;
                foreach (DictionaryEntry pair in (IDictionary)value)
                {
                    count.Collection(++entries);
                    count.Text((string)pair.Key);
                    WalkDefault(shape.ElementShape!, pair.Value, depth + 1, count);
                }
                break;
        }
    }

    private sealed class Counter
    {
        private int nodes;
        internal long Bytes
        {
            get; private set;
        }

        internal void Node(int depth)
        {
            if (depth > MaximumDepth || ++nodes > MaximumNodes)
            {
                throw Error("Configuration.Materialization", "A central candidate exceeds its logical depth or value-node ceiling.");
            }
            Bytes += 64;
        }

        internal void Collection(int count)
        {
            if (count > MaximumCollection)
            {
                throw Error("Configuration.Materialization", "A central collection exceeds its logical element ceiling.");
            }
        }

        internal void Text(string text)
        {
            int utf8;
            try
            {
                utf8 = StrictUtf8.GetByteCount(text);
            }
            catch (EncoderFallbackException)
            {
                throw Error("Configuration.Materialization", "Central scalar text must contain valid Unicode.");
            }
            if (utf8 > MaximumScalarBytes)
            {
                throw Error("Configuration.Materialization", "A central scalar exceeds its UTF-8 byte ceiling.");
            }
            // Include worst supported JSON escaping before buffers expand.
            Bytes += checked((long)text.Length * 12);
        }
    }

    internal static CoreConfigurationException Error(string code, string explanation)
        => ConfigurationDiagnostics.Error(typeof(ConfigurationOptions), code, explanation);
}
