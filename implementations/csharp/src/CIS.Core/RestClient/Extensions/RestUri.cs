using Microsoft.AspNetCore.WebUtilities;

namespace CIS.RestClient;

internal static class RestUri
{
    internal static Uri Resolve(string? baseUrl, string url, IReadOnlyList<RestQueryChange>? changes = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        Uri? resolved;
        bool valid = baseUrl is null ? Uri.TryCreate(url, UriKind.Absolute, out resolved)
            : Uri.TryCreate(new Uri(baseUrl, UriKind.Absolute), url, out resolved);
        if (!valid || resolved is null || resolved.Scheme is not ("http" or "https") || resolved.UserInfo.Length != 0)
        { throw new ArgumentException("Use an HTTP(S) destination without embedded credentials, resolved against an absolute base.", nameof(url)); }
        if (changes is null || changes.Count == 0) { return new UriBuilder(resolved) { Fragment = string.Empty }.Uri; }
        var query = new List<KeyValuePair<string, string>>();
        foreach (string segment in resolved.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var pair in new QueryStringEnumerable(segment))
            { query.Add(new(pair.DecodeName().ToString(), segment)); }
        }
        foreach (RestQueryChange change in changes ?? [])
        {
            if (change.Kind is RestQueryChangeKind.Set or RestQueryChangeKind.Remove)
            { query.RemoveAll(pair => string.Equals(pair.Key, change.Name, StringComparison.Ordinal)); }
            if (change.Kind != RestQueryChangeKind.Remove)
            { query.Add(new(change.Name, Uri.EscapeDataString(change.Name) + (change.Value is null ? string.Empty : "=" + Uri.EscapeDataString(change.Value)))); }
        }
        var builder = new UriBuilder(resolved) { Query = string.Empty, Fragment = string.Empty };
        // Native key decoding selects replacements. Existing encoded segments keep
        // their order, flags and octets; only newly supplied raw values are encoded.
        string rendered = string.Join("&", query.Select(pair => pair.Value));
        builder.Query = rendered;
        return builder.Uri;
    }

    internal static bool ContainsQueryField(Uri uri, IReadOnlyList<string> names)
    {
        foreach (var pair in new QueryStringEnumerable(uri.Query))
        { if (names.Contains(pair.DecodeName().ToString(), StringComparer.Ordinal)) { return true; } }
        return false;
    }

    internal static bool AllowedOrigin(RestClientSnapshot client, Uri uri)
        => client.AllowedOrigins.Count == 0 || client.AllowedOrigins.Contains(uri.GetLeftPart(UriPartial.Authority), StringComparer.OrdinalIgnoreCase);
}
