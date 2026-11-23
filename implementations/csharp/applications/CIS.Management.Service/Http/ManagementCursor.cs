using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using CIS.Management.Wire;
using CIS.Management.Service.Backends;

namespace CIS.Management.Service.Http;

internal sealed class ManagementCursor(ManagementServiceOptions options, Func<ManagementAuthoritySnapshot> currentAuthority) : IDisposable
{
    private readonly byte[] key = options.CopyCursorSigningKey();

    internal string Encode(string endpoint, string actor, int size, string last)
    {
        ManagementAuthoritySnapshot authority = currentAuthority();
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new CursorPayload(2, NamespaceStamp(authority.NamespaceName), authority.AuthorityId,
            authority.RecoveryEpoch, endpoint, actor, size, "uuid-ordinal", last));
        return EncodeBase64(payload) + "." + EncodeBase64(HMACSHA256.HashData(key, payload));
    }

    internal bool TryDecode(string? value, string endpoint, string actor, int size, out string? after)
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
                || cursor.Size != size || cursor.Order != "uuid-ordinal" || !ManagementWire.IsUuid(cursor.Last))
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
