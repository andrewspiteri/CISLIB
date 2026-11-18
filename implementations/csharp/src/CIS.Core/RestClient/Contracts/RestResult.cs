using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace CIS.RestClient;

/// <summary>Marks a result that does not require body decoding.</summary>
public readonly record struct RestNoValue;

/// <summary>The finite category of an expected REST failure.</summary>
public enum RestFailureKind
{
    /// <summary>No failure.</summary>
    None,
    /// <summary>A non-success HTTP status.</summary>
    Http,
    /// <summary>An explicitly mapped business rejection.</summary>
    Business,
    /// <summary>An expected transport failure.</summary>
    Transport,
    /// <summary>An identified operation or attempt timeout.</summary>
    Timeout,
    /// <summary>An invalid required response.</summary>
    InvalidResponse,
    /// <summary>A processing limit was exceeded.</summary>
    LimitExceeded,
    /// <summary>A declared authentication rejection.</summary>
    Authentication,
    /// <summary>A declared preparation rejection.</summary>
    Preparation,
    /// <summary>The client owner is stopping.</summary>
    Stopping
}

/// <summary>Why no further retry was performed.</summary>
public enum RestRetryStopReason
{
    /// <summary>No retry policy was selected.</summary>
    Disabled,
    /// <summary>The method or operation is not eligible.</summary>
    NotEligible,
    /// <summary>The classified outcome is not retryable.</summary>
    NonRetryable,
    /// <summary>The policy iteration limit was reached.</summary>
    AttemptLimit,
    /// <summary>The chosen delay cannot fit the remaining total budget.</summary>
    InsufficientTimeRemaining,
    /// <summary>The request body cannot be recreated.</summary>
    BodyNotReplayable
}

/// <summary>A detached copied diagnostic prefix, never a live native response.</summary>
public sealed class RestErrorBody
{
    private readonly byte[] bytes;
    /// <summary>Copies a bounded prefix selected by the owning response reader.</summary>
    public RestErrorBody(ReadOnlyMemory<byte> bytes, string? mediaType = null)
    { this.bytes = bytes.ToArray(); MediaType = mediaType; }
    /// <summary>Gets a copied read-only byte view.</summary>
    public IReadOnlyList<byte> Bytes => Array.AsReadOnly(bytes);
    /// <summary>Gets the response media type, when known.</summary>
    public string? MediaType { get; }
}

/// <summary>Detached terminal facts; contains no attempt history or transport owner.</summary>
public sealed class RestResultContext
{
    internal RestResultContext(int? httpStatusCode = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? headers = null, bool hasValue = false,
        RestFailureKind failureKind = RestFailureKind.None, string? remoteErrorCode = null,
        RestErrorBody? errorBody = null, bool errorBodyTruncated = false, bool errorMessageTruncated = false,
        string clientName = "request", string operationName = "request", int attemptCount = 0,
        TimeSpan duration = default, RestRetryStopReason? retryStopReason = null, string? correlationId = null)
    {
        if (httpStatusCode is < 100 or > 599) { throw new ArgumentOutOfRangeException(nameof(httpStatusCode)); }
        if (!Enum.IsDefined(failureKind) || retryStopReason is { } reason && !Enum.IsDefined(reason))
        { throw new ArgumentOutOfRangeException(nameof(failureKind)); }
        if (attemptCount < 0 || duration < TimeSpan.Zero) { throw new ArgumentOutOfRangeException(nameof(attemptCount)); }
        if (hasValue && failureKind != RestFailureKind.None) { throw new ArgumentException("A failed result has no successful value."); }
        ArgumentException.ThrowIfNullOrWhiteSpace(clientName);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        HttpStatusCode = httpStatusCode;
        var copied = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in headers ?? new Dictionary<string, IReadOnlyList<string>>())
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key);
            ArgumentNullException.ThrowIfNull(pair.Value);
            copied.Add(pair.Key, Array.AsReadOnly(pair.Value.ToArray()));
        }
        Headers = new ReadOnlyDictionary<string, IReadOnlyList<string>>(copied);
        HasValue = hasValue; FailureKind = failureKind; RemoteErrorCode = remoteErrorCode;
        ErrorBody = errorBody; ErrorBodyTruncated = errorBodyTruncated; ErrorMessageTruncated = errorMessageTruncated;
        ClientName = clientName; OperationName = operationName; AttemptCount = attemptCount;
        Duration = duration; RetryStopReason = retryStopReason; CorrelationId = correlationId;
    }

    /// <summary>Gets the terminal response status, or null when the terminal exchange had none.</summary>
    public int? HttpStatusCode { get; }
    /// <summary>Gets copied case-insensitive multi-value response and content headers.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; }
    /// <summary>Gets whether a successful value was supplied; present null differs from absence.</summary>
    public bool HasValue { get; }
    /// <summary>Gets the expected failure category.</summary>
    public RestFailureKind FailureKind { get; }
    /// <summary>Gets an explicitly extracted provider code.</summary>
    public string? RemoteErrorCode { get; }
    /// <summary>Gets the optional bounded error prefix.</summary>
    public RestErrorBody? ErrorBody { get; }
    /// <summary>Gets whether diagnostic body capture was truncated.</summary>
    public bool ErrorBodyTruncated { get; }
    /// <summary>Gets whether the explanatory message was shortened.</summary>
    public bool ErrorMessageTruncated { get; }
    /// <summary>Gets the configured client alias.</summary>
    public string ClientName { get; }
    /// <summary>Gets the registered operation alias or request fallback.</summary>
    public string OperationName { get; }
    /// <summary>Gets actual owned primary dispatches, not policy iterations or packets.</summary>
    public int AttemptCount { get; }
    /// <summary>Gets elapsed time from public entry through actual owned cleanup.</summary>
    public TimeSpan Duration { get; }
    /// <summary>Gets why retry stopped, when applicable.</summary>
    public RestRetryStopReason? RetryStopReason { get; }
    /// <summary>Gets an existing valid W3C trace ID captured at entry, or null.</summary>
    public string? CorrelationId { get; }
}

/// <summary>An immutable typed terminal result. Check Success and Context.HasValue before using Value.</summary>
public sealed class RestResult<T>
{
    private RestResult(bool success, string? code, string? message, T value, RestResultContext context)
    { Success = success; ErrorCode = code; ErrorMessage = message; Value = value; Context = context; }
    /// <summary>Gets whether all promised processing succeeded.</summary>
    public bool Success { get; }
    /// <summary>Gets a stable portable code, or null on success.</summary>
    public string? ErrorCode { get; }
    /// <summary>Gets a bounded explanation, or null on success.</summary>
    public string? ErrorMessage { get; }
    /// <summary>Gets the actual value; default(T) can be null when HasValue is false.</summary>
    [MaybeNull] public T Value { get; }
    /// <summary>Gets detached terminal context.</summary>
    public RestResultContext Context { get; }

    internal static RestResult<T> FromValue(T value, RestResultContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.HasValue || context.FailureKind != RestFailureKind.None || context.HttpStatusCode is < 200 or > 299)
        { throw new ArgumentException("A supplied success value requires successful present context and no non-success HTTP status."); }
        return new(true, null, null, value, context);
    }
    internal static RestResult<T> Empty(RestResultContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.HasValue || context.FailureKind != RestFailureKind.None || context.HttpStatusCode is < 200 or > 299)
        { throw new ArgumentException("An empty success has no value, failure or non-success HTTP status."); }
        return new(true, null, null, default!, context);
    }
    internal static RestResult<T> Failed(string code, string message, RestResultContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code); ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(context);
        if (context.HasValue || context.FailureKind == RestFailureKind.None) { throw new ArgumentException("A failure requires failed absent context."); }
        if (context.FailureKind == RestFailureKind.Http && (context.HttpStatusCode is null or >= 200 and <= 299))
        { throw new ArgumentException("An HTTP failure requires a known non-success HTTP status."); }
        return new(false, code, message, default!, context);
    }
}
