using System.Text.Json;
using System.Text.Json.Serialization;
using CIS.Management.Wire;
using CIS.Messaging;

namespace CIS.Management;

/// <summary>The finite passive capability-description profile, beneath enclosing wire limits.</summary>
public static class ManagementCapabilityLimits
{
    /// <summary>The finite maximum definition bytes.</summary>
    public const int MaximumDefinitionBytes = 64 * 1024;
    /// <summary>The finite maximum definitions.</summary>
    public const int MaximumDefinitions = 1024;
    /// <summary>The finite maximum fields.</summary>
    public const int MaximumFields = 128;
    /// <summary>The finite maximum groups.</summary>
    public const int MaximumGroups = 32;
    /// <summary>The finite maximum lookups.</summary>
    public const int MaximumLookups = 16;
    /// <summary>The finite maximum operations.</summary>
    public const int MaximumOperations = 16;
    /// <summary>The finite maximum depth.</summary>
    public const int MaximumDepth = 16;
    /// <summary>The finite maximum shape nodes.</summary>
    public const int MaximumShapeNodes = 1024;
    /// <summary>The finite maximum enum values.</summary>
    public const int MaximumEnumValues = 256;
}

internal static class ManagementCatalogValidation
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        MaxDepth = ManagementCapabilityLimits.MaximumDepth
    };

    internal static string Text(string value, int bytes, string name, bool empty = false)
        => ManagementWire.IsUtf8Text(value, bytes, empty) ? value : throw Invalid(name);

    internal static string Scalar(string value, int scalars, string name)
        => ManagementWire.IsScalarText(value, scalars) ? value : throw Invalid(name);

    internal static string Version(string value, string name)
        => ManagementWire.IsOrderedInteger(value) ? value : throw Invalid(name);

    internal static string Token(string value, string name)
    {
        Text(value, 128, name);
        if (value.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-')))
        {
            throw Invalid(name);
        }
        return value;
    }

    internal static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> source, int maximum, string name) where T : class
    {
        ArgumentNullException.ThrowIfNull(source, name);
        int count = source.Count;
        if (count > maximum)
        {
            throw Invalid(name);
        }
        var values = new T[count];
        for (int index = 0; index < count; index++)
        {
            values[index] = source[index] ?? throw Invalid(name);
        }
        return Array.AsReadOnly(values);
    }

    internal static ManagementConfigurationShape Freeze(ManagementConfigurationShape source)
    {
        int nodes = 0;
        return Freeze(source, 1, ref nodes);
    }

    private static ManagementConfigurationShape Freeze(ManagementConfigurationShape source, int depth, ref int nodes)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (depth > ManagementCapabilityLimits.MaximumDepth || ++nodes > ManagementCapabilityLimits.MaximumShapeNodes)
        {
            throw Invalid(nameof(source));
        }
        if (source.Kind == "object")
        {
            if (source.Members is null || source.Element is not null || source.AllowedValues is not null)
            {
                throw Invalid(nameof(source));
            }
            IReadOnlyList<ManagementConfigurationMember> members = Copy(source.Members, 256, nameof(source));
            var names = new HashSet<string>(StringComparer.Ordinal);
            var copied = new List<ManagementConfigurationMember>(members.Count);
            foreach (ManagementConfigurationMember member in members)
            {
                Scalar(member.Name, 256, nameof(source));
                if (!names.Add(member.Name))
                {
                    throw Invalid(nameof(source));
                }
                copied.Add(new(member.Name, member.Required, Freeze(member.Shape, depth + 1, ref nodes)));
            }
            return new("object", source.Nullable, copied.AsReadOnly());
        }
        if (source.Kind is "array" or "dictionary")
        {
            if (source.Element is null || source.Members is not null || source.AllowedValues is not null)
            {
                throw Invalid(nameof(source));
            }
            return new(source.Kind, source.Nullable, Element: Freeze(source.Element, depth + 1, ref nodes));
        }
        if (source.Kind is not ("string" or "number" or "integer" or "boolean")
            || source.Members is not null || source.Element is not null
            || source.AllowedValues is not null && source.Kind != "string")
        {
            throw Invalid(nameof(source));
        }
        IReadOnlyList<string>? allowed = null;
        if (source.AllowedValues is not null)
        {
            allowed = Copy(source.AllowedValues, ManagementCapabilityLimits.MaximumEnumValues, nameof(source));
            if (allowed.Count == 0 || allowed.Distinct(StringComparer.Ordinal).Count() != allowed.Count)
            {
                throw Invalid(nameof(source));
            }
            foreach (string value in allowed)
            {
                Text(value, 1024, nameof(source), empty: true);
            }
        }
        return new(source.Kind, source.Nullable, AllowedValues: allowed);
    }

    internal static ManagementConfigurationShape Resolve(ManagementConfigurationShape root, string path)
    {
        Text(path, 1024, nameof(path));
        if (!path.StartsWith('/'))
        {
            throw Invalid(nameof(path));
        }
        ManagementConfigurationShape current = root;
        foreach (string part in path[1..].Split('/'))
        {
            for (int index = 0; index < part.Length; index++)
            {
                if (part[index] == '~' && (++index == part.Length || part[index] is not ('0' or '1')))
                {
                    throw Invalid(nameof(path));
                }
            }
            string member = part.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal);
            current = current.Kind == "object"
                ? current.Members!.SingleOrDefault(item => item.Name == member)?.Shape ?? throw Invalid(nameof(path))
                : throw Invalid(nameof(path));
        }
        return current;
    }

    internal static ManagementJobExecutionSupport Support(ManagementJobExecutionSupport source)
    {
        ArgumentNullException.ThrowIfNull(source.CurrentContract);
        IReadOnlyList<MessageContract> starts = Copy(source.SupportedStarts, 16, nameof(source));
        IReadOnlyList<MessageContract> recoveries = Copy(source.SupportedRecoveries, 16, nameof(source));
        if (starts.Distinct().Count() != starts.Count || recoveries.Distinct().Count() != recoveries.Count)
        {
            throw Invalid(nameof(source));
        }
        return new(source.CurrentContract, starts, recoveries, source.CheckpointContract, source.OperationName);
    }

    internal static byte[] Encode(ManagementCapabilityDefinition value)
    {
        using var output = new DescriptionOutput();
        try
        {
            JsonSerializer.Serialize(output, value, Json);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            throw Invalid(nameof(value));
        }
        return output.ToArray();
    }

    internal static ArgumentException Invalid(string name)
        => new("Supply a complete supported finite capability description.", name);

    private sealed class DescriptionOutput : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            Admit(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Admit(buffer.Length);
            base.Write(buffer);
        }

        public override void WriteByte(byte value)
        {
            Admit(1);
            base.WriteByte(value);
        }

        private void Admit(int count)
        {
            if (count > ManagementCapabilityLimits.MaximumDefinitionBytes - Length)
            {
                throw Invalid("description");
            }
        }
    }
}
