using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace CIS.Coordination;

/// <summary>An ordinal, case-sensitive duty identity; presentation and execution identities are separate.</summary>
public sealed record CoordinationKey
{
    /// <summary>Maximum UTF-8 bytes in the authoritative environment.</summary>
    public const int MaximumEnvironmentBytes = 1024;
    /// <summary>Maximum UTF-8 bytes in an execution scope.</summary>
    public const int MaximumExecutionScopeBytes = 256;
    /// <summary>Maximum UTF-8 bytes in a duty identifier.</summary>
    public const int MaximumDutyIdBytes = 128;
    /// <summary>Maximum UTF-8 bytes in a resource identifier.</summary>
    public const int MaximumResourceIdBytes = 512;
    /// <summary>The explicit resource for an unpartitioned duty.</summary>
    public const string DefaultResourceId = "default";

    /// <summary>Validates four independent Unicode components without normalization or culture conversion.</summary>
    public CoordinationKey(string environment, string executionScope, string dutyId, string resourceId = DefaultResourceId)
    {
        Environment = CoordinationText.Utf8(environment, MaximumEnvironmentBytes, nameof(environment));
        ExecutionScope = CoordinationText.Utf8(executionScope, MaximumExecutionScopeBytes, nameof(executionScope));
        DutyId = CoordinationText.Utf8(dutyId, MaximumDutyIdBytes, nameof(dutyId));
        ResourceId = CoordinationText.Utf8(resourceId, MaximumResourceIdBytes, nameof(resourceId));
    }

    /// <summary>Gets the trusted environment.</summary>
    public string Environment
    {
        get;
    }
    /// <summary>Gets the responsible application or explicitly authorized shared scope.</summary>
    public string ExecutionScope
    {
        get;
    }
    /// <summary>Gets the stable duty, independent of its category or schedule.</summary>
    public string DutyId
    {
        get;
    }
    /// <summary>Gets the stable partition or the explicit default resource.</summary>
    public string ResourceId
    {
        get;
    }

    /// <summary>Returns version-one canonical bytes, each component prefixed by its unsigned big-endian UTF-8 byte length.</summary>
    public byte[] GetCanonicalBytes()
    {
        string[] values = [Environment, ExecutionScope, DutyId, ResourceId];
        int length = values.Sum(value => Encoding.UTF8.GetByteCount(value)) + 16;
        byte[] bytes = new byte[length];
        int offset = 0;
        foreach (string value in values)
        {
            int count = Encoding.UTF8.GetByteCount(value);
            BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(offset, 4), count);
            offset += 4;
            offset += Encoding.UTF8.GetBytes(value, bytes.AsSpan(offset, count));
        }
        return bytes;
    }

    /// <summary>Returns a bounded exact-resource identifier for an application-owned capability mapping.</summary>
    /// <remarks>This fingerprint identifies a resource; possession confers no permission.</remarks>
    public string GetResourceIdentifier()
    {
        return Convert.ToHexStringLower(SHA256.HashData(GetCanonicalBytes()));
    }
}

internal static class CoordinationText
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static string Utf8(string value, int maximumBytes, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        if (value.Length > maximumBytes || value.Any(char.IsControl))
        {
            throw new ArgumentException("Supply bounded valid Unicode without control characters.", name);
        }
        try
        {
            if (StrictUtf8.GetByteCount(value) > maximumBytes)
            {
                throw new ArgumentException("The value exceeds its published UTF-8 byte bound.", name);
            }
        }
        catch (EncoderFallbackException error)
        {
            throw new ArgumentException("Supply valid Unicode without unpaired surrogates.", name, error);
        }
        return value;
    }

    internal static Guid Id(Guid value, string name)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Supply a nonempty identity.", name);
        }
        return value;
    }

    internal static TimeSpan Duration(TimeSpan value, string name, bool allowZero = false)
    {
        if (value < TimeSpan.Zero || (!allowZero && value == TimeSpan.Zero) || value > TimeSpan.FromDays(1))
        {
            throw new ArgumentOutOfRangeException(name, "Supply a finite duration of at most one day.");
        }
        return value;
    }

    internal static string? Code(string? value)
    {
        if (value is not null && (value.Length is < 1 or > 128 || value.Any(character => character is < '!' or > '~')))
        {
            throw new ArgumentException("A reason code has one to 128 printable ASCII characters.", nameof(value));
        }
        return value;
    }
}
