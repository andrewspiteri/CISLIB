using CIS.RestClient.Http;

namespace CIS.RestClient;

// Authentication sees canonical native ordinary headers; credential writes
// update that same message before it is sent. No second public request model.
internal static class RestNativeRequest
{
    internal static HttpRequestMessage Create(RestRequestContext context, HttpContent? content)
    {
        var message = new HttpRequestMessage(new HttpMethod(context.Method), new Uri(context.Url)) { Content = content };
        try
        {
            var headers = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
            if (content is not null) { foreach (var pair in content.Headers) { headers[pair.Key] = pair.Value.ToArray(); } }
            foreach (var pair in context.Headers.Snapshot()) { headers[pair.Key] = pair.Value; }
            content?.Headers.Clear();
            foreach (var pair in headers) { Add(message, pair.Key, pair.Value); }
            IEnumerable<KeyValuePair<string, IEnumerable<string>>> contentHeaders = content is null ? [] : content.Headers;
            foreach (var pair in message.Headers.Concat(contentHeaders)) { context.Headers.Set(pair.Key, pair.Value.ToArray()); }
            context.Headers.NormalizeWith((name, values) =>
            {
                string[] previous = message.Headers.TryGetValues(name, out var current) ? current.ToArray() : [];
                string[] previousContent = message.Content?.Headers.TryGetValues(name, out var currentContent) == true ? currentContent.ToArray() : [];
                Remove(message.Headers, name);
                if (message.Content is not null) { Remove(message.Content.Headers, name); }
                try { if (values.Length != 0) { Add(message, name, values); } }
                catch
                {
                    Remove(message.Headers, name);
                    if (message.Content is not null) { Remove(message.Content.Headers, name); }
                    if (previous.Length != 0) { message.Headers.Add(name, previous); }
                    if (previousContent.Length != 0) { message.Content!.Headers.Add(name, previousContent); }
                    throw;
                }
                if (message.Headers.TryGetValues(name, out var normalized)) { return normalized.ToArray(); }
                return message.Content?.Headers.TryGetValues(name, out var normalizedContent) == true ? normalizedContent.ToArray() : [];
            });
            return message;
        }
        catch (Exception error) when (error is FormatException or InvalidOperationException or ArgumentException)
        { message.Dispose(); throw new RestContentPreparationException(error); }
    }
    internal static void Finalize(HttpRequestMessage message, RestRequestContext context)
    {
        if (!context.IsSealed || !string.Equals(message.Method.Method, context.Method, StringComparison.Ordinal))
        { throw new InvalidOperationException("Send only the same finalized request that authentication observed."); }
        message.RequestUri = new Uri(context.Url, UriKind.Absolute);
        if (string.Equals(context.Method, "TRACE", StringComparison.OrdinalIgnoreCase)
            && (message.Content is not null || new[] { "Authorization", "Proxy-Authorization", "Cookie" }.Any(message.Headers.Contains)))
        { throw new RestContentPreparationException(new ArgumentException("TRACE cannot contain a body or credentials.")); }
    }
    private static void Add(HttpRequestMessage message, string name, IEnumerable<string> values)
    {
        try { message.Headers.Add(name, values); }
        catch (InvalidOperationException) when (message.Content is not null) { message.Content.Headers.Add(name, values); }
    }
    private static void Remove(System.Net.Http.Headers.HttpHeaders headers, string name)
    { if (headers.TryGetValues(name, out _)) { headers.Remove(name); } }
}
