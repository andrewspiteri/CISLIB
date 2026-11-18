namespace CIS.RestClient;

/// <summary>The declared representation of request content.</summary>
public enum RestBodyKind
{
    /// <summary>A typed JSON value.</summary>
    Json,
    /// <summary>Text content.</summary>
    Text,
    /// <summary>Byte content.</summary>
    Bytes,
    /// <summary>Repeated form-urlencoded fields.</summary>
    Form,
    /// <summary>An explicitly owned or borrowed stream adapter.</summary>
    Stream,
    /// <summary>An explicit codec extension, such as multipart content.</summary>
    Custom
}

/// <summary>A passive request descriptor. It performs no network work or body acquisition.</summary>
/// <remarks>Objects and buffers are not deep-cloned; callers keep them unchanged until the operation settles.</remarks>
public abstract class RestBody
{
    /// <summary>Initializes an explicit custom descriptor.</summary>
    protected RestBody(RestBodyKind kind, string mediaType, bool isReplayable)
    { ArgumentException.ThrowIfNullOrWhiteSpace(mediaType); Kind = kind; MediaType = mediaType; IsReplayable = isReplayable; }
    /// <summary>Gets the representation.</summary>
    public RestBodyKind Kind { get; }
    /// <summary>Gets its declared media type.</summary>
    public string MediaType { get; }
    /// <summary>Gets whether equivalent content can be recreated; remote replay assurance is separate.</summary>
    public bool IsReplayable { get; }
    /// <summary>Creates a typed JSON descriptor, including a typed null.</summary>
    public static RestBody Json<TBody>(TBody value) => new JsonBody<TBody>(value);
    /// <summary>Creates UTF-8 text content.</summary>
    public static RestBody Text(string value, string mediaType = "text/plain")
    { ArgumentNullException.ThrowIfNull(value); return new ValueBody(RestBodyKind.Text, mediaType, value, typeof(string)); }
    /// <summary>Creates byte content without copying the caller's buffer.</summary>
    public static RestBody Bytes(ReadOnlyMemory<byte> value, string mediaType = "application/octet-stream")
        => new ValueBody(RestBodyKind.Bytes, mediaType, value, typeof(ReadOnlyMemory<byte>));
    /// <summary>Copies form field names/values and their order, preserving repeated keys.</summary>
    public static RestBody Form(IEnumerable<KeyValuePair<string, string>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        KeyValuePair<string, string>[] copied = values.ToArray();
        if (copied.Any(pair => pair.Key is null || pair.Value is null)) { throw new ArgumentException("Form fields cannot be null.", nameof(values)); }
        return new ValueBody(RestBodyKind.Form, "application/x-www-form-urlencoded", Array.AsReadOnly(copied), typeof(IReadOnlyList<KeyValuePair<string, string>>));
    }
    /// <summary>Gets JSON/text/bytes/form data for a codec. Stream acquisition belongs to its explicit adapter.</summary>
    public virtual object? Value => null;
    /// <summary>Gets the declared JSON/data type, including when the value is null.</summary>
    public virtual Type? ValueType => null;

    private sealed class JsonBody<TBody>(TBody value) : RestBody(RestBodyKind.Json, "application/json", true)
    { public override object? Value => value; public override Type ValueType => typeof(TBody); }
    private sealed class ValueBody(RestBodyKind kind, string mediaType, object value, Type type) : RestBody(kind, mediaType, true)
    { public override object Value => value; public override Type ValueType => type; }
}
