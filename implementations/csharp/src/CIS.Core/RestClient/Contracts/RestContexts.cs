using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Runtime.ExceptionServices;

namespace CIS.RestClient;

/// <summary>Read-only operation identity and zero-based policy iteration; dispatch count is separate.</summary>
public sealed record RestCallInfo(string ClientName, string OperationName, int Iteration);

/// <summary>One attempt's ordinary mutable request state, finalized before authentication.</summary>
public sealed class RestRequestContext
{
    private string method, url;
    private RestBody? body;
    private bool finalized;
    internal bool IsSealed { get; private set; }
    internal bool UrlChanged { get; private set; }
    internal bool InheritedBaseQuery { get; }
    internal RestRequestContext(string method, string url, RestCallInfo call, RestHeaders headers, RestQuery query, RestBody? body, bool inheritedBaseQuery = false)
    { this.method = method; this.url = url; Call = call; Headers = headers; Query = query; this.body = body; InheritedBaseQuery = inheritedBaseQuery; }
    /// <summary>Gets or replaces the effective method before finalization.</summary>
    public string Method { get => method; set { Mutable(); method = value; } }
    /// <summary>Gets or replaces the resolved destination before finalization.</summary>
    public string Url { get => url; set { Mutable(); url = value; UrlChanged = true; } }
    /// <summary>Gets ordinary and credential headers.</summary>
    public RestHeaders Headers { get; }
    /// <summary>Gets explicit query additions.</summary>
    public RestQuery Query { get; }
    /// <summary>Gets or replaces the body descriptor before finalization.</summary>
    public RestBody? Body { get => body; set { Mutable(); body = value; } }
    /// <summary>Gets immutable call information.</summary>
    public RestCallInfo Call { get; }
    /// <summary>Declares an expected preparation failure without throwing a business exception.</summary>
    public void Fail(string explanation)
    { Mutable(); ArgumentException.ThrowIfNullOrWhiteSpace(explanation); Failure ??= new(RestFailureKind.Preparation, "client.preparation_failed", explanation); }
    internal RestDeclaredFailure? Failure { get; private set; }
    internal void FinalizeOrdinary(IEnumerable<string> credentialHeaders, IEnumerable<string> credentialQuery)
    { finalized = true; Headers.RestrictWrites(credentialHeaders); Query.RestrictWrites(credentialQuery); }
    internal void Seal(string? finalizedUrl = null)
    { if (finalizedUrl is not null) { url = finalizedUrl; } finalized = true; IsSealed = true; Headers.Freeze(); Query.Freeze(); }
    private void Mutable() { if (finalized) { throw new InvalidOperationException("Ordinary request fields are finalized."); } }
}

/// <summary>A finalized request with controlled credential writes only.</summary>
public sealed class RestAuthenticationContext
{
    private readonly object gate = new();
    private readonly RestRequestContext request;
    private Func<int, CancellationToken, ValueTask<ReadOnlyMemory<byte>>>? signingBytes;
    private Func<string>? renderUrl;
    private Task<ReadOnlyMemory<byte>>? materialization;
    private int signingLimit;
    private bool open = true;
    internal RestAuthenticationContext(RestRequestContext request,
        Func<int, CancellationToken, ValueTask<ReadOnlyMemory<byte>>> signingBytes, Func<string>? renderUrl = null)
    { this.request = request; this.signingBytes = signingBytes; this.renderUrl = renderUrl; }
    /// <summary>Gets the finalized method.</summary>
    public string Method => request.Method;
    /// <summary>Gets the finalized URI representation.</summary>
    public string Url => renderUrl?.Invoke() ?? request.Url;
    /// <summary>Gets headers whose mutation is restricted to configured credential names.</summary>
    public RestHeaders Headers => request.Headers;
    /// <summary>Gets query additions whose mutation is restricted to configured credential names.</summary>
    public RestQuery Query => request.Query;
    /// <summary>Gets immutable call information.</summary>
    public RestCallInfo Call => request.Call;
    /// <summary>Obtains one explicitly bounded exact representation reused by the sender.</summary>
    public ValueTask<ReadOnlyMemory<byte>> GetBodyBytesAsync(int maximumBytes, CancellationToken cancellationToken = default)
    {
        if (maximumBytes <= 0) { throw new ArgumentOutOfRangeException(nameof(maximumBytes)); }
        TaskCompletionSource<ReadOnlyMemory<byte>>? start = null;
        Func<int, CancellationToken, ValueTask<ReadOnlyMemory<byte>>>? create = null;
        Task<ReadOnlyMemory<byte>> owned;
        lock (gate)
        {
            if (!open || request.IsSealed) { throw new InvalidOperationException("Authentication has completed."); }
            cancellationToken.ThrowIfCancellationRequested();
            if (materialization is null)
            {
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                materialization = start.Task; signingLimit = maximumBytes; create = signingBytes!;
            }
            else if (maximumBytes < signingLimit && (!materialization.IsCompletedSuccessfully || materialization.Result.Length > maximumBytes))
            { throw new ArgumentException("Await the existing signing representation before requesting a tighter sufficient bound.", nameof(maximumBytes)); }
            owned = materialization;
        }
        if (start is not null) { _ = MaterializeAsync(start, create!, maximumBytes, cancellationToken); }
        return new(owned);
    }
    /// <summary>Declares an expected authentication rejection.</summary>
    public void Fail(string explanation, string? remoteErrorCode = null)
    {
        lock (gate)
        {
            if (!open || request.IsSealed) { throw new InvalidOperationException("Authentication has completed."); }
            ArgumentException.ThrowIfNullOrWhiteSpace(explanation);
            Failure ??= new(RestFailureKind.Authentication, "client.authentication_failed", explanation, remoteErrorCode);
        }
    }
    internal RestDeclaredFailure? Failure { get; private set; }
    internal async ValueTask CompleteAsync()
    {
        Task<ReadOnlyMemory<byte>>? pending; Func<string>? render;
        lock (gate)
        { open = false; pending = materialization; signingBytes = null; render = renderUrl; renderUrl = null; }
        ExceptionDispatchInfo? fault = null;
        try { request.Seal(render?.Invoke() ?? request.Url); request.Query.ClearApplied(); }
        catch (Exception error) { request.Seal(); fault = ExceptionDispatchInfo.Capture(error); }
        try { if (pending is not null) { await pending.ConfigureAwait(false); } }
        catch (Exception error) { fault ??= ExceptionDispatchInfo.Capture(error); }
        finally { lock (gate) { materialization = null; } }
        fault?.Throw();
    }
    private static async Task MaterializeAsync(TaskCompletionSource<ReadOnlyMemory<byte>> completion,
        Func<int, CancellationToken, ValueTask<ReadOnlyMemory<byte>>> create, int maximumBytes, CancellationToken token)
    {
        try
        {
            ReadOnlyMemory<byte> bytes = await create(maximumBytes, token).ConfigureAwait(false);
            if (bytes.Length > maximumBytes) { throw new InvalidOperationException("The signing capability exceeded its explicit bound."); }
            completion.TrySetResult(bytes);
        }
        catch (Exception error) { completion.TrySetException(error); }
    }
}

/// <summary>A decoded value with explicit presence, including representable null.</summary>
public readonly struct RestDecodedValue<T>
{
    private RestDecodedValue(bool hasValue, T value) { HasValue = hasValue; Value = value; }
    /// <summary>Gets whether a decoded value was supplied.</summary>
    public bool HasValue { get; }
    /// <summary>Gets the typed value or default when absent.</summary>
    [MaybeNull] public T Value { get; }
    /// <summary>Creates a present value, including representable null.</summary>
    public static RestDecodedValue<T> Present(T value) => new(true, value);
    /// <summary>Gets absence without a fabricated value.</summary>
    public static RestDecodedValue<T> Absent => default;
}

/// <summary>Detached mapping input; the body has one consumption owner and is never silently read twice.</summary>
public sealed class RestResponseContext<T>
{
    internal RestResponseContext(int statusCode, IReadOnlyDictionary<string, IReadOnlyList<string>> headers,
        RestDecodedValue<T> decoded, RestErrorBody? errorBody)
    { HttpStatusCode = statusCode; Headers = new RestResultContext(headers: headers).Headers; Decoded = decoded; ErrorBody = errorBody; }
    /// <summary>Gets actual status.</summary>
    public int HttpStatusCode { get; }
    /// <summary>Gets copied response/content headers.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; }
    /// <summary>Gets the successful decoded value/presence; errors are never decoded as T.</summary>
    public RestDecodedValue<T> Decoded { get; }
    /// <summary>Gets the bounded error prefix when applicable.</summary>
    public RestErrorBody? ErrorBody { get; }
}

/// <summary>An explicit mapper decision; actual non-2xx status remains a failure.</summary>
public sealed class RestResponseDecision
{
    private RestResponseDecision(bool rejected, string? code, string? explanation, string? remote)
    { IsBusinessRejection = rejected; ErrorCode = code; Explanation = explanation; RemoteErrorCode = remote; }
    /// <summary>Gets an unchanged default classification.</summary>
    public static RestResponseDecision Continue { get; } = new(false, null, null, null);
    /// <summary>Rejects a successful HTTP envelope using a domain code.</summary>
    public static RestResponseDecision RejectBusiness(string explanation, string code = "business.rejected", string? remoteErrorCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(explanation); ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (code.StartsWith("client.", StringComparison.Ordinal) || code.StartsWith("http.", StringComparison.Ordinal))
        { throw new ArgumentException("Client and HTTP codes are reserved.", nameof(code)); }
        return new(true, code, explanation, remoteErrorCode);
    }
    /// <summary>Extracts provider detail without turning an HTTP failure into success.</summary>
    public static RestResponseDecision RemoteError(string? remoteErrorCode, string? explanation = null)
        => new(false, null, explanation, remoteErrorCode);
    /// <summary>Gets whether a mapped business rejection was declared.</summary>
    public bool IsBusinessRejection { get; }
    /// <summary>Gets its domain code, if any.</summary>
    public string? ErrorCode { get; }
    /// <summary>Gets an explicit explanation for bounded normalization by the owner.</summary>
    public string? Explanation { get; }
    /// <summary>Gets a provider code kept distinct from portable codes.</summary>
    public string? RemoteErrorCode { get; }
}

internal sealed record RestDeclaredFailure(RestFailureKind Kind, string Code, string Explanation, string? RemoteErrorCode = null)
{
    internal (string Message, bool Truncated) BoundedMessage(int maximumScalars)
    {
        if (maximumScalars <= 0) { throw new ArgumentOutOfRangeException(nameof(maximumScalars)); }
        ArgumentException.ThrowIfNullOrWhiteSpace(Explanation);
        var result = new StringBuilder(); int count = 0;
        foreach (Rune rune in Explanation.EnumerateRunes())
        { if (count++ == maximumScalars) { return (result.ToString(), true); } result.Append(rune.ToString()); }
        return (result.ToString(), false);
    }
}
