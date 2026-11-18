using System.Collections.ObjectModel;

namespace CIS.RestClient;

/// <summary>The explicitly selected response representation.</summary>
public enum RestResponseFormat
{
    /// <summary>JSON, including a JSON string when T is string.</summary>
    Json,
    /// <summary>Raw text; the default codec requires string.</summary>
    Text,
    /// <summary>Raw bytes; the default codec requires byte[].</summary>
    Bytes,
    /// <summary>No success-body decoding.</summary>
    NoValue
}

/// <summary>Explicit processing bounds, not a whole-process or object-graph memory guarantee.</summary>
public sealed class RestLimits
{
    /// <summary>Gets or sets consumed/decompressed successful response bytes; default sixteen MiB.</summary>
    public long MaxResponseBytes { get; set; } = 16L * 1024 * 1024;
    /// <summary>Gets or sets retained diagnostic error-prefix bytes; default sixteen KiB.</summary>
    public int MaxErrorBodyBytes { get; set; } = 16 * 1024;
    /// <summary>Gets or sets explanatory Unicode scalar count; default 1,024.</summary>
    public int MaxErrorMessageScalars { get; set; } = 1024;
}

/// <summary>Registration inputs copied into one immutable client snapshot; construction performs no network work.</summary>
public sealed class RestClientOptions
{
    /// <summary>Creates defaults for an explicitly named client.</summary>
    public RestClientOptions(string clientName) { ArgumentException.ThrowIfNullOrWhiteSpace(clientName); ClientName = clientName; }
    /// <summary>Gets the explicit registered alias.</summary>
    public string ClientName { get; }
    /// <summary>Gets or sets the optional absolute HTTP base URL.</summary>
    public string? BaseUrl { get; set; }
    /// <summary>Gets default headers copied during registration.</summary>
    public RestHeaders Headers { get; } = new();
    /// <summary>Gets or sets a positive finite total timeout; default thirty seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets or sets processing bounds copied during registration.</summary>
    public RestLimits Limits { get; set; } = new();
    /// <summary>Gets or sets a complete registered retry choice; default None.</summary>
    public RestRetrySelection RetryPolicy { get; set; } = RestRetrySelection.None;
    /// <summary>Gets or sets the default decoding mode.</summary>
    public RestResponseFormat ResponseFormat { get; set; } = RestResponseFormat.Json;
    /// <summary>Gets or sets whether absence is an invalid response.</summary>
    public bool RequireValue { get; set; }
    /// <summary>Gets or sets automatic entry trace forwarding; null enables it only for a final .local hostname.</summary>
    /// <remarks>Forwards an existing W3C traceparent only. Explicitly supplied headers remain manual forwarding.</remarks>
    public bool? ForwardTraceId { get; set; }
    /// <summary>Gets or sets default asynchronous customization.</summary>
    public Func<RestRequestContext, CancellationToken, Task>? ConfigureRequest { get; set; }
    /// <summary>Gets or sets default asynchronous final authentication.</summary>
    public Func<RestAuthenticationContext, CancellationToken, Task>? Authenticate { get; set; }
    /// <summary>Gets or sets the configured codec, or null for the native default.</summary>
    public IRestContentCodec? Codec { get; set; }
    /// <summary>Gets or sets an integration-specific response mapper.</summary>
    public IRestResponseMapper? ResponseMapper { get; set; }
    /// <summary>Gets registered operation aliases; request is always included by registration.</summary>
    public IList<string> Operations { get; } = new List<string>();
    /// <summary>Gets allowed credential origins; an omitted list derives the base origin when present.</summary>
    public IList<string> AllowedOrigins { get; } = new List<string>();
    /// <summary>Gets declared credential header names, removed for TRACE and writable during authentication.</summary>
    public IList<string> CredentialHeaders { get; } = new List<string> { "Authorization", "Proxy-Authorization", "Cookie" };
    /// <summary>Gets declared credential query names, compared ordinally.</summary>
    public IList<string> CredentialQueryParameters { get; } = new List<string>();
    /// <summary>Gets reusable exchange handlers in outward registration order.</summary>
    public IList<IRestExchangeHandler> Handlers { get; } = new List<IRestExchangeHandler>();
}

/// <summary>Per-call overrides captured at admission; body objects/buffers must remain unchanged until settlement.</summary>
public sealed class RestRequestOptions
{
    /// <summary>Gets or sets a request body, independent of response T.</summary>
    public RestBody? Body { get; set; }
    /// <summary>Gets per-call headers; Set replaces and Append preserves repeated values.</summary>
    public RestHeaders Headers { get; } = new();
    /// <summary>Gets explicit raw query additions; the adapter encodes each value once.</summary>
    public RestQuery Query { get; } = new();
    /// <summary>Gets or sets the whole-operation timeout, or null to inherit.</summary>
    public TimeSpan? Timeout { get; set; }
    /// <summary>Gets or sets null to inherit, None to disable, or a complete registered replacement.</summary>
    public RestRetrySelection? RetryPolicy { get; set; }
    /// <summary>Gets or sets explicit remote replay assurance; it does not make consumed content replayable.</summary>
    public RestReplaySafety ReplaySafety { get; set; }
    /// <summary>Gets or sets a registered alias; unknown values use request with bounded diagnostics.</summary>
    public string? OperationName { get; set; }
    /// <summary>Gets or sets replacement customization, or null to inherit.</summary>
    public Func<RestRequestContext, CancellationToken, Task>? ConfigureRequest { get; set; }
    /// <summary>Gets or sets explicit suppression of inherited customization.</summary>
    public bool DisableConfigureRequest { get; set; }
    /// <summary>Gets or sets replacement authentication, or null to inherit.</summary>
    public Func<RestAuthenticationContext, CancellationToken, Task>? Authenticate { get; set; }
    /// <summary>Gets or sets explicit suppression of inherited authentication.</summary>
    public bool DisableAuthentication { get; set; }
    /// <summary>Gets or sets the response mode, or null to inherit.</summary>
    public RestResponseFormat? ResponseFormat { get; set; }
    /// <summary>Gets or sets a value-presence override, or null to inherit.</summary>
    public bool? RequireValue { get; set; }
    /// <summary>Gets or sets automatic trace forwarding for this call, or null to inherit the client policy.</summary>
    public bool? ForwardTraceId { get; set; }
    /// <summary>Gets or sets a replacement response mapper.</summary>
    public IRestResponseMapper? ResponseMapper { get; set; }
    /// <summary>Gets or sets explicit validated processing overrides, or null to inherit.</summary>
    public RestLimits? Limits { get; set; }
}

/// <summary>Case-insensitive multi-value request headers. Reusable instances are snapshotted, never mutated by a call.</summary>
public sealed class RestHeaders
{
    private readonly Dictionary<string, List<string>> values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> appendToInherited = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> removed = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> changed = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string>? writable;
    private bool frozen;
    private Func<string, string[], string[]>? normalize;
    /// <summary>Replaces values for a header.</summary>
    public void Set(string name, params string[] supplied)
    {
        Check(name); ArgumentNullException.ThrowIfNull(supplied); Validate(supplied);
        values[name] = normalize is null ? [.. supplied] : [.. normalize(name, supplied)];
        appendToInherited.Remove(name); removed.Remove(name); changed.Add(name);
    }
    /// <summary>Appends one value.</summary>
    public void Append(string name, string value)
    {
        Check(name); ArgumentNullException.ThrowIfNull(value);
        bool exists = values.TryGetValue(name, out var items);
        string[]? normalized = normalize?.Invoke(name, items is null ? [value] : [.. items, value]);
        if (!exists && !removed.Contains(name)) { appendToInherited.Add(name); }
        removed.Remove(name);
        if (normalized is null)
        {
            if (items is null) { values[name] = items = []; }
            items.Add(value);
        }
        else { values[name] = [.. normalized]; }
        changed.Add(name);
    }
    /// <summary>Removes a header.</summary>
    public bool Remove(string name)
    { Check(name); normalize?.Invoke(name, []); appendToInherited.Remove(name); removed.Add(name); changed.Add(name); return values.Remove(name); }
    /// <summary>Copies an immutable case-insensitive multi-value view.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Snapshot()
        => new ReadOnlyDictionary<string, IReadOnlyList<string>>(values.ToDictionary(pair => pair.Key,
            pair => (IReadOnlyList<string>)Array.AsReadOnly(pair.Value.ToArray()), StringComparer.OrdinalIgnoreCase));
    internal void ApplyTo(RestHeaders target, CancellationToken cancellationToken)
    {
        foreach (string name in removed) { cancellationToken.ThrowIfCancellationRequested(); target.Remove(name); }
        foreach (var pair in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (appendToInherited.Contains(pair.Key)) { foreach (string value in pair.Value) { target.Append(pair.Key, value); } }
            else { target.Set(pair.Key, pair.Value.ToArray()); }
        }
    }
    internal void RestrictWrites(IEnumerable<string> names) => writable = new(names, StringComparer.OrdinalIgnoreCase);
    internal void MarkBaseline() => changed.Clear();
    internal bool ChangedSinceBaseline(string name) => changed.Contains(name);
    internal void NormalizeWith(Func<string, string[], string[]> normalizer) => normalize = normalizer;
    internal void Freeze() { frozen = true; normalize = null; }
    private void Check(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (frozen || writable is not null && !writable.Contains(name)) { throw new InvalidOperationException("This request field is finalized."); }
    }
    private static void Validate(string[] supplied)
    { if (supplied.Any(value => value is null)) { throw new ArgumentException("Header values cannot be null.", nameof(supplied)); } }
}

/// <summary>Ordinal query additions; values remain raw until the native URI adapter encodes them once.</summary>
public sealed class RestQuery
{
    private readonly List<KeyValuePair<string, string?>> values = [];
    private readonly List<RestQueryChange> changes = [];
    private HashSet<string>? writable;
    private bool frozen;
    /// <summary>Replaces additions for the key.</summary>
    public void Set(string name, string? value)
    { Check(name); values.RemoveAll(pair => pair.Key == name); values.Add(new(name, value)); changes.RemoveAll(change => change.Name == name); changes.Add(new(RestQueryChangeKind.Set, name, value)); }
    /// <summary>Appends a repeated key/value.</summary>
    public void Append(string name, string? value) { Check(name); values.Add(new(name, value)); changes.Add(new(RestQueryChangeKind.Append, name, value)); }
    /// <summary>Removes additions for the key.</summary>
    public bool Remove(string name)
    { Check(name); changes.RemoveAll(change => change.Name == name); changes.Add(new(RestQueryChangeKind.Remove, name, null)); return values.RemoveAll(pair => pair.Key == name) != 0; }
    /// <summary>Copies additions in explicit order.</summary>
    public IReadOnlyList<KeyValuePair<string, string?>> Snapshot() => Array.AsReadOnly(values.ToArray());
    internal IReadOnlyList<RestQueryChange> CaptureChanges(CancellationToken token)
    {
        var copied = new RestQueryChange[changes.Count];
        for (int index = 0; index < copied.Length; index++) { token.ThrowIfCancellationRequested(); copied[index] = changes[index]; }
        return Array.AsReadOnly(copied);
    }
    internal void ClearApplied() { values.Clear(); changes.Clear(); }
    internal void RestrictWrites(IEnumerable<string> names) => writable = new(names, StringComparer.Ordinal);
    internal void Freeze() => frozen = true;
    private void Check(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (frozen || writable is not null && !writable.Contains(name)) { throw new InvalidOperationException("This request field is finalized."); }
    }
}

internal enum RestQueryChangeKind { Set, Append, Remove }
internal readonly record struct RestQueryChange(RestQueryChangeKind Kind, string Name, string? Value);
