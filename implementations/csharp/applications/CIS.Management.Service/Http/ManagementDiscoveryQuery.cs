using System.Globalization;
using System.Buffers.Binary;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CIS.Management.Wire;
using CIS.Security;
using Microsoft.AspNetCore.Http.Features;

namespace CIS.Management.Service.Http;

/// <summary>Strict decoding and current read-policy binding for this product's discovery queries.</summary>
internal static class ManagementDiscoveryQuery
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static bool TryReadApiFeatures(HttpContext context, out int size, out string? cursor)
    {
        size = 32;
        cursor = null;
        if (!TryReadValues(context, ["pageSize", "cursor"], out Dictionary<string, string> values)
            || values.TryGetValue("pageSize", out string? rawSize)
                && (!int.TryParse(rawSize, NumberStyles.None, CultureInfo.InvariantCulture, out size) || size is < 1 or > 64))
        {
            return false;
        }
        values.TryGetValue("cursor", out cursor);
        return ManagementWire.IsPageRequest(new(size, cursor));
    }

    internal static bool TryRead(HttpContext context, bool filters, out ManagementApplicationQuery query,
        out int size, out string? cursor)
    {
        query = new();
        size = 100;
        cursor = null;
        if (!TryReadValues(context, filters ? ["pageSize", "cursor", "query", "environment"] : ["pageSize", "cursor"],
            out Dictionary<string, string> values))
        {
            return false;
        }
        if (values.TryGetValue("pageSize", out string? rawSize)
            && (!int.TryParse(rawSize, NumberStyles.None, CultureInfo.InvariantCulture, out size) || size is < 1 or > 500))
        {
            return false;
        }
        values.TryGetValue("query", out string? search);
        values.TryGetValue("environment", out string? environment);
        if (search is not null && !ManagementWire.IsScalarText(search, 128)
            || environment is not null && !ManagementWire.IsScalarText(environment, 255))
        {
            return false;
        }
        values.TryGetValue("cursor", out cursor);
        if (!ManagementWire.IsPageRequest(new(size, cursor)))
        {
            return false;
        }
        query = new(search, environment);
        return true;
    }

    internal static bool TryReadConfigurationCohorts(HttpContext context, out string schemaVersion, out int size, out string? cursor)
        => TryReadConfigurationPage(context, out schemaVersion, out size, out cursor);

    internal static bool TryReadConfigurationPage(HttpContext context, out string schemaVersion, out int size, out string? cursor)
    {
        schemaVersion = string.Empty;
        size = 100;
        cursor = null;
        if (!TryReadValues(context, ["schemaVersion", "pageSize", "cursor"], out Dictionary<string, string> values)
            || !values.TryGetValue("schemaVersion", out string? schema) || !ManagementWire.IsOrderedInteger(schema))
        {
            return false;
        }
        schemaVersion = schema;
        if (values.TryGetValue("pageSize", out string? rawSize)
            && (!int.TryParse(rawSize, NumberStyles.None, CultureInfo.InvariantCulture, out size) || size is < 1 or > 500))
        {
            return false;
        }
        values.TryGetValue("cursor", out cursor);
        return ManagementWire.IsPageRequest(new(size, cursor));
    }

    internal static bool TryReadConfigurationEditor(HttpContext context, out string schemaVersion, out string source)
    {
        schemaVersion = string.Empty;
        source = string.Empty;
        if (!TryReadValues(context, ["schemaVersion", "source"], out Dictionary<string, string> values)
            || values.Count != 2 || !values.TryGetValue("schemaVersion", out string? schema)
            || !ManagementWire.IsOrderedInteger(schema) || !values.TryGetValue("source", out string? selected)
            || selected is not ("common" or "application"))
        {
            return false;
        }
        schemaVersion = schema;
        source = selected;
        return true;
    }

    private static bool TryReadValues(HttpContext context, string[] allowedNames, out Dictionary<string, string> values)
    {
        values = new(StringComparer.Ordinal);
        string? rawTarget = context.Features.Get<IHttpRequestFeature>()?.RawTarget;
        string raw = string.IsNullOrEmpty(rawTarget) ? context.Request.QueryString.Value ?? string.Empty : rawTarget;
        int question = raw.IndexOf('?');
        raw = question < 0 ? string.Empty : raw[(question + 1)..];
        if (raw.Length > 16384)
        {
            return false;
        }
        try
        {
            foreach (string pair in raw.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = pair.IndexOf('=');
                string name = Decode(equals < 0 ? pair : pair[..equals]);
                string value = Decode(equals < 0 ? string.Empty : pair[(equals + 1)..]);
                if (!allowedNames.Contains(name, StringComparer.Ordinal) || !values.TryAdd(name, value))
                {
                    return false;
                }
            }
        }
        catch (Exception error) when (error is FormatException or DecoderFallbackException or EncoderFallbackException)
        {
            return false;
        }
        return true;
    }

    internal static string Scope(CallerContext caller, string? applicationId = null, ManagementApplicationQuery? query = null,
        ClaimsPrincipal? principal = null)
    {
        byte[] fields = JsonSerializer.SerializeToUtf8Bytes(new[] { "management.query.v1", ManagementActorScope.Create(caller, applicationId),
            caller.Profile, caller.PolicyVersion, query?.Query, query?.Environment, PrincipalDigest(principal) });
        return "management.query.v1." + Convert.ToHexStringLower(SHA256.HashData(fields));
    }

    private static string PrincipalDigest(ClaimsPrincipal? principal)
    {
        Claim[] claims = (principal?.Identities.Where(identity => identity.IsAuthenticated).SelectMany(identity => identity.Claims)
            ?? []).Take(257).ToArray();
        if (claims.Length > 256)
        {
            throw new ManagementRequestException(400, "management.discovery.identity_too_large");
        }
        int total = 0;
        // Establish byte/field bounds before canonical sorting or allocating encoded fields.
        foreach (Claim claim in claims)
        {
            foreach (string field in new[] { claim.Type, claim.Issuer, claim.ValueType, claim.Value })
            {
                if (field.Length > 8192)
                {
                    throw new ManagementRequestException(400, "management.discovery.identity_too_large");
                }
                total += StrictUtf8.GetByteCount(field);
                if (total > 65536)
                {
                    throw new ManagementRequestException(400, "management.discovery.identity_too_large");
                }
            }
        }
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[4];
        foreach (Claim claim in claims.OrderBy(claim => claim.Type, StringComparer.Ordinal)
            .ThenBy(claim => claim.Issuer, StringComparer.Ordinal).ThenBy(claim => claim.ValueType, StringComparer.Ordinal)
            .ThenBy(claim => claim.Value, StringComparer.Ordinal))
        {
            foreach (string field in new[] { claim.Type, claim.Issuer, claim.ValueType, claim.Value })
            {
                byte[] bytes = StrictUtf8.GetBytes(field);
                BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
                digest.AppendData(length);
                digest.AppendData(bytes);
            }
        }
        return Convert.ToHexStringLower(digest.GetHashAndReset());
    }

    private static string Decode(string text)
    {
        var bytes = new List<byte>(text.Length);
        for (int index = 0; index < text.Length; index++)
        {
            char value = text[index];
            if (value == '%')
            {
                if (index + 2 >= text.Length || !byte.TryParse(text.AsSpan(index + 1, 2), NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out byte decoded))
                {
                    throw new FormatException("Malformed query escape.");
                }
                bytes.Add(decoded);
                index += 2;
            }
            else if (value <= 127)
            {
                bytes.Add(value == '+' ? (byte)' ' : (byte)value);
            }
            else
            {
                int count = char.IsHighSurrogate(value) ? 2 : 1;
                bytes.AddRange(StrictUtf8.GetBytes(text.AsSpan(index, Math.Min(count, text.Length - index)).ToString()));
                index += count - 1;
            }
        }
        return StrictUtf8.GetString(bytes.ToArray());
    }
}
