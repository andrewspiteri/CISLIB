using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using CIS.Management.Wire;
using CIS.Management.Service.Backends;
using CIS.Management.Service.Persistence;

namespace CIS.Management.Service.Http;

internal sealed class ManagementCursor(ManagementServiceOptions options, Func<ManagementAuthoritySnapshot> currentAuthority) : IDisposable
{
    private readonly byte[] key = options.CopyCursorSigningKey();

    internal string Encode(string endpoint, string actor, int size, string last)
        => EncodeCore(endpoint, actor, size, last, "uuid-ordinal");

    internal string EncodeEnvironment(string endpoint, string actor, int size, string last)
    {
        if (!ManagementWire.IsScalarText(last, 255))
        {
            throw new ArgumentException("Supply an exact disclosed environment position.", nameof(last));
        }
        return EncodeCore(endpoint, actor, size, last, "environment-utf8-ordinal");
    }

    internal string EncodeConfigurationAdoption(string endpoint, string actor, int size, string last)
    {
        if (!IsAdoptionPosition(last))
        {
            throw new ArgumentException("Use the exact configuration adoption page position.", nameof(last));
        }
        return EncodeCore(endpoint, actor, size, last, "configuration-adoption-ordinal");
    }

    internal string EncodeConfigurationCohort(string endpoint, string actor, int size, string last)
    {
        if (!ManagementWire.IsOperationKey(last))
        {
            throw new ArgumentException("Use the exact disclosed Configuration cohort position.", nameof(last));
        }
        return EncodeCore(endpoint, actor, size, last, "configuration-cohort-ordinal");
    }

    internal string EncodeConfigurationRelease(string endpoint, string actor, int size, string last)
    {
        if (!ManagementConfigurationReleasePosition.TryRead(last, out _, out _))
        {
            throw new ArgumentException("Use the exact retained-release position.", nameof(last));
        }
        return EncodeCore(endpoint, actor, size, last, "configuration-release-created-desc");
    }

    private string EncodeCore(string endpoint, string actor, int size, string last, string order)
    {
        ManagementAuthoritySnapshot authority = currentAuthority();
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new CursorPayload(2, NamespaceStamp(authority.NamespaceName), authority.AuthorityId,
            authority.RecoveryEpoch, endpoint, actor, size, order, last));
        return EncodeBase64(payload) + "." + EncodeBase64(HMACSHA256.HashData(key, payload));
    }

    internal bool TryDecode(string? value, string endpoint, string actor, int size, out string? after)
        => TryDecodeCore(value, endpoint, actor, size, "uuid-ordinal", out after);

    internal bool TryDecodeEnvironment(string? value, string endpoint, string actor, int size, out string? after)
        => TryDecodeCore(value, endpoint, actor, size, "environment-utf8-ordinal", out after);

    internal bool TryDecodeConfigurationAdoption(string? value, string endpoint, string actor, int size, out string? after)
        => TryDecodeCore(value, endpoint, actor, size, "configuration-adoption-ordinal", out after);

    internal bool TryDecodeConfigurationCohort(string? value, string endpoint, string actor, int size, out string? after)
        => TryDecodeCore(value, endpoint, actor, size, "configuration-cohort-ordinal", out after);

    internal bool TryDecodeConfigurationRelease(string? value, string endpoint, string actor, int size, out string? after)
        => TryDecodeCore(value, endpoint, actor, size, "configuration-release-created-desc", out after);

    private bool TryDecodeCore(string? value, string endpoint, string actor, int size, string order, out string? after)
    {
        after = null;
        if (value is null)
        {
            return true;
        }
        if (value.Length is < 3 or > 2048)
        {
            return false;
        }
        string[] parts = value.Split('.');
        if (parts.Length != 2)
        {
            return false;
        }
        try
        {
            byte[] payload = DecodeBase64(parts[0]);
            byte[] supplied = DecodeBase64(parts[1]);
            if (supplied.Length != 32 || !CryptographicOperations.FixedTimeEquals(supplied, HMACSHA256.HashData(key, payload)))
            {
                return false;
            }
            CursorPayload? cursor = JsonSerializer.Deserialize<CursorPayload>(payload);
            ManagementAuthoritySnapshot authority = currentAuthority();
            if (cursor is null || cursor.Version != 2 || cursor.NamespaceName != NamespaceStamp(authority.NamespaceName)
                || cursor.AuthorityId != authority.AuthorityId || cursor.RecoveryEpoch != authority.RecoveryEpoch
                || cursor.Endpoint != endpoint || cursor.Actor != actor
                || cursor.Size != size || cursor.Order != order || !ValidPosition(order, cursor.Last))
            {
                return false;
            }
            after = cursor.Last;
            return true;
        }
        catch (Exception error) when (error is FormatException or JsonException)
        {
            return false;
        }
    }

    public void Dispose() => CryptographicOperations.ZeroMemory(key);

    private static bool ValidPosition(string order, string last) => order switch
    {
        "configuration-adoption-ordinal" => IsAdoptionPosition(last),
        "configuration-cohort-ordinal" => ManagementWire.IsOperationKey(last),
        "configuration-release-created-desc" => ManagementConfigurationReleasePosition.TryRead(last, out _, out _),
        "environment-utf8-ordinal" => ManagementWire.IsScalarText(last, 255),
        _ => ManagementWire.IsUuid(last)
    };

    private static bool IsAdoptionPosition(string value)
    {
        if (value is null || value.Length > 1024)
        {
            return false;
        }
        string[] parts = value.Split('/');
        return parts.Length == 4 && PostgreSqlManagementStore.SnapshotPortableId(parts[0])
            && ManagementWire.IsOrderedInteger(parts[1])
            && (parts[2].Length == 0 || PostgreSqlManagementStore.SnapshotPortableId(parts[2]))
            && (parts[3].Length == 0 || ManagementWire.IsUuid(parts[3]));
    }

    private static string NamespaceStamp(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string EncodeBase64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DecodeBase64(string text)
    {
        if (text.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_')))
        {
            throw new FormatException("Invalid opaque cursor.");
        }
        string padded = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight((padded.Length + 3) / 4 * 4, '='));
    }

    private sealed record CursorPayload(int Version, string NamespaceName, string AuthorityId, long RecoveryEpoch,
        string Endpoint, string Actor, int Size, string Order, string Last);
}
