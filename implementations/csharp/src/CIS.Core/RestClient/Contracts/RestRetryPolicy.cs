namespace CIS.RestClient;

/// <summary>Explicit retry selection; null on request options inherits the client choice.</summary>
public sealed class RestRetrySelection
{
    private RestRetrySelection(string? name) { PolicyName = name; }
    /// <summary>Explicitly disables retries.</summary>
    public static RestRetrySelection None { get; } = new(null);
    /// <summary>Selects one complete registered policy.</summary>
    public static RestRetrySelection Named(string name)
    { ArgumentException.ThrowIfNullOrWhiteSpace(name); return new(name); }
    /// <summary>Gets the registered name, or null for None.</summary>
    public string? PolicyName { get; }
}

/// <summary>Upstream backoff selection without exposing a resilience SDK.</summary>
public enum RestRetryBackoff
{
    /// <summary>A constant nominal delay.</summary>
    Constant,
    /// <summary>A linearly increasing nominal delay.</summary>
    Linear,
    /// <summary>An exponentially increasing nominal delay.</summary>
    Exponential
}

/// <summary>Explicit assurance about remote replay; content replayability is checked separately.</summary>
public enum RestReplaySafety
{
    /// <summary>Only the conservative method contract applies.</summary>
    Default,
    /// <summary>The caller confirms actual endpoint idempotency and stable payload/key semantics.</summary>
    Assured
}

/// <summary>Portable expected transport detail for an explicitly narrow custom policy.</summary>
public enum RestTransportFailureKind
{
    /// <summary>Resolution of the destination name failed.</summary>
    NameResolution,
    /// <summary>A connection could not be established.</summary>
    Connection,
    /// <summary>Secure connection establishment failed.</summary>
    SecureConnection,
    /// <summary>The peer violated the selected HTTP protocol.</summary>
    Protocol,
    /// <summary>The response ended unexpectedly.</summary>
    ResponseEnded,
    /// <summary>Another expected transport category.</summary>
    Other
}

/// <summary>Bounded detached metadata supplied to an optional policy decision.</summary>
public sealed record RestRetryDecisionContext(int Iteration, int? HttpStatusCode, RestFailureKind FailureKind,
    string? ErrorCode, bool BodyProcessingStarted, bool BodyReplayable, TimeSpan Remaining,
    RestTransportFailureKind? TransportFailure = null);

/// <summary>One immutable named policy. Optional executors are registered separately.</summary>
public sealed class RestRetryPolicy
{
    /// <summary>The finite configured retry ceiling, separate from the default two retries.</summary>
    public const int MaximumRetries = 100;
    /// <summary>Creates a finite policy; method/status collections are copied.</summary>
    public RestRetryPolicy(string name, int maxRetries, TimeSpan initialDelay, TimeSpan maxLocalDelay,
        IReadOnlyList<int>? statusCodes = null, IReadOnlyList<string>? methods = null,
        RestRetryBackoff backoff = RestRetryBackoff.Exponential, bool useJitter = true,
        bool honorRetryAfter = true, TimeSpan? attemptTimeout = null,
        Func<RestRetryDecisionContext, CancellationToken, ValueTask<bool>>? shouldRetry = null,
        IReadOnlyList<RestFailureKind>? failureKinds = null, IReadOnlyList<string>? errorCodes = null,
        bool retryBodyFailures = false, IReadOnlyList<RestTransportFailureKind>? transportFailures = null)
    {
        if (!RestContractRules.Alias(name)) { throw new ArgumentException("Use a bounded policy alias.", nameof(name)); }
        if (maxRetries is < 0 or > MaximumRetries) { throw new ArgumentOutOfRangeException(nameof(maxRetries)); }
        if (initialDelay < TimeSpan.Zero || maxLocalDelay < initialDelay || maxLocalDelay > TimeSpan.FromDays(1)
            || attemptTimeout is { } timeout && timeout <= TimeSpan.Zero)
        { throw new ArgumentOutOfRangeException(nameof(initialDelay)); }
        if (!Enum.IsDefined(backoff)) { throw new ArgumentOutOfRangeException(nameof(backoff)); }
        if (attemptTimeout is { } attempt) { RestContractRules.Timeout(attempt, nameof(attemptTimeout)); }
        int[] statuses = RestContractRules.Copy(statusCodes, 500, nameof(statusCodes));
        string[] selected = RestContractRules.Copy(methods, 32, nameof(methods), ["GET", "HEAD", "OPTIONS"]);
        RestFailureKind[] failures = RestContractRules.Copy(failureKinds, 9, nameof(failureKinds));
        string[] codes = RestContractRules.Copy(errorCodes, 32, nameof(errorCodes));
        RestTransportFailureKind[] transports = RestContractRules.Copy(transportFailures, 6, nameof(transportFailures));
        if (statuses.Any(status => status is < 100 or > 599) || statuses.Distinct().Count() != statuses.Length
            || selected.Any(method => !RestContractRules.Method(method)) || selected.Distinct(StringComparer.Ordinal).Count() != selected.Length
            || failures.Any(kind => !Enum.IsDefined(kind) || kind == RestFailureKind.None) || failures.Distinct().Count() != failures.Length
            || codes.Any(code => !RestContractRules.Alias(code)) || codes.Distinct(StringComparer.Ordinal).Count() != codes.Length
            || transports.Any(kind => !Enum.IsDefined(kind)) || transports.Distinct().Count() != transports.Length)
        { throw new ArgumentException("Use distinct valid retry statuses and method tokens."); }
        Name = name; MaxRetries = maxRetries; InitialDelay = initialDelay; MaxLocalDelay = maxLocalDelay;
        StatusCodes = Array.AsReadOnly(statuses); Methods = Array.AsReadOnly(selected); Backoff = backoff;
        UseJitter = useJitter; HonorRetryAfter = honorRetryAfter; AttemptTimeout = attemptTimeout; ShouldRetry = shouldRetry;
        FailureKinds = Array.AsReadOnly(failures); ErrorCodes = Array.AsReadOnly(codes);
        RetryBodyFailures = retryBodyFailures; TransportFailures = Array.AsReadOnly(transports);
    }
    /// <summary>Gets the supplied conservative preset: two retries, selected statuses, no transport/body/business replay.</summary>
    public static RestRetryPolicy TransientHttp { get; } = new("transient-http", 2, TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(5), [408, 429, 502, 503, 504]);
    /// <summary>Gets the registered alias.</summary>
    public string Name { get; }
    /// <summary>Gets additional policy iterations, independent of actual dispatch count.</summary>
    public int MaxRetries { get; }
    /// <summary>Gets initial nominal delay.</summary>
    public TimeSpan InitialDelay { get; }
    /// <summary>Gets the local cap, which never shortens Retry-After.</summary>
    public TimeSpan MaxLocalDelay { get; }
    /// <summary>Gets explicitly eligible statuses.</summary>
    public IReadOnlyList<int> StatusCodes { get; }
    /// <summary>Gets conservative method eligibility.</summary>
    public IReadOnlyList<string> Methods { get; }
    /// <summary>Gets nominal upstream backoff.</summary>
    public RestRetryBackoff Backoff { get; }
    /// <summary>Gets whether upstream jitter is enabled.</summary>
    public bool UseJitter { get; }
    /// <summary>Gets whether eligible Retry-After is honored.</summary>
    public bool HonorRetryAfter { get; }
    /// <summary>Gets the optional timeout owned by each actual attempt.</summary>
    public TimeSpan? AttemptTimeout { get; }
    /// <summary>Gets an optional decision callback; it never dispatches or schedules work itself.</summary>
    public Func<RestRetryDecisionContext, CancellationToken, ValueTask<bool>>? ShouldRetry { get; }
    /// <summary>Gets explicitly eligible expected failure kinds; empty in the preset.</summary>
    public IReadOnlyList<RestFailureKind> FailureKinds { get; }
    /// <summary>Gets explicitly eligible portable/domain error codes.</summary>
    public IReadOnlyList<string> ErrorCodes { get; }
    /// <summary>Gets whether a replay-assured custom policy may cover post-headers body failure.</summary>
    public bool RetryBodyFailures { get; }
    /// <summary>Gets narrowly eligible transport categories; empty in the preset.</summary>
    public IReadOnlyList<RestTransportFailureKind> TransportFailures { get; }
}
