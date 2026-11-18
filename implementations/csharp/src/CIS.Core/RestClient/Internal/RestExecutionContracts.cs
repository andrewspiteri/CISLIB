using CIS.Metrics;
using System.Runtime.ExceptionServices;

namespace CIS.RestClient;

// Preserve cancellation provenance at the callback boundary, before a joined
// child or cleanup can signal the operation token later.
internal sealed class RestCallbackFaultException(Exception original) : Exception("An extension threw an unrelated cancellation.", original)
{
    internal ExceptionDispatchInfo Original { get; } = ExceptionDispatchInfo.Capture(original);
    internal static Exception Capture(Exception error, CancellationToken token, bool enabled, CancellationToken knownCallerToken = default)
        => enabled && error is OperationCanceledException canceled
            && !(canceled.CancellationToken == token && token.IsCancellationRequested
                || canceled.CancellationToken == knownCallerToken && knownCallerToken.IsCancellationRequested)
                ? new RestCallbackFaultException(error) : error;
}

// The actual RC5 owner implements this fixed module seam. No generic lifecycle registry.
internal interface IRestClientRuntime : IMetricsSource
{
    object RegistrationIdentity { get; }
    void BeginStopping();
    Task StopAsync();
}

internal readonly record struct RestOperationIdentity(string ClientAlias, string OperationAlias);

// Installed only as a known instance by real registration. It contains no provider,
// user factory, transport or runtime reference and is safe to inspect before activation.
internal sealed class RestClientComposition
{
    internal RestClientComposition(object registrationIdentity, IReadOnlyList<RestOperationIdentity> operations)
    {
        ArgumentNullException.ThrowIfNull(registrationIdentity); ArgumentNullException.ThrowIfNull(operations);
        RestOperationIdentity[] copied = RestContractRules.Copy(operations, 32, nameof(operations));
        string[] clients = copied.Select(pair => pair.ClientAlias).Distinct(StringComparer.Ordinal).ToArray();
        if (clients.Length is < 1 or > 16 || copied.Length > 32
            || copied.Distinct().Count() != copied.Length
            || copied.Any(pair => !RestContractRules.Alias(pair.ClientAlias) || !RestContractRules.Alias(pair.OperationAlias))
            || clients.Any(client => !copied.Contains(new(client, "request"))))
        { throw new ArgumentException("Use at most sixteen clients and thirty-two distinct pairs, including each request fallback."); }
        RegistrationIdentity = registrationIdentity; ClientAliases = Array.AsReadOnly(clients);
        Operations = Array.AsReadOnly(copied);
    }
    internal object RegistrationIdentity { get; }
    internal IReadOnlyList<string> ClientAliases { get; }
    internal IReadOnlyList<RestOperationIdentity> Operations { get; }
}

internal enum RestOperationOutcome
{
    Success, HttpError, BusinessError, TransportError, Timeout, Cancelled,
    AuthenticationError, PreparationError, InvalidResponse, LimitExceeded, Stopping, UnexpectedError
}

internal interface IRestOperationObservation
{
    void UploadCompleted(long bytes);
    void DownloadCompleted(long bytes);
    void Complete(RestOperationOutcome outcome, TimeSpan settledElapsed);
}

// A single detached terminal summary, never T, a native response or attempt history.
internal sealed record RestAttemptOutcome(int? HttpStatusCode, RestFailureKind FailureKind,
    string? ErrorCode = null, string? RetryAfter = null, bool BodyProcessingStarted = false,
    bool BodyReplayable = true, bool RetryEligible = true, RestTransportFailureKind? TransportFailure = null,
    string? Method = null);

internal sealed record RestPolicyResult(RestAttemptOutcome Terminal, RestRetryStopReason? StopReason);

internal sealed class RestRetryExecutionContext
{
    internal RestRetryExecutionContext(TimeProvider timeProvider, long entryTimestamp, TimeSpan timeout,
        CancellationToken cancellationToken, string method, RestReplaySafety replaySafety,
        Func<bool> isStopping, CancellationToken callerCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timeProvider); ArgumentNullException.ThrowIfNull(isStopping);
        if (timeout <= TimeSpan.Zero) { throw new ArgumentOutOfRangeException(nameof(timeout)); }
        TimeProvider = timeProvider; EntryTimestamp = entryTimestamp; Timeout = timeout;
        CancellationToken = cancellationToken; Method = method; ReplaySafety = replaySafety; this.isStopping = isStopping;
        CallerCancellationToken = callerCancellationToken;
    }
    private readonly Func<bool> isStopping;
    internal TimeProvider TimeProvider { get; }
    internal long EntryTimestamp { get; }
    internal TimeSpan Timeout { get; }
    internal CancellationToken CancellationToken { get; }
    internal CancellationToken CallerCancellationToken { get; }
    internal string Method { get; }
    internal RestReplaySafety ReplaySafety { get; }
    internal bool IsStopping => isStopping();
    internal TimeSpan Remaining
    {
        get
        {
            TimeSpan remaining = Timeout - TimeProvider.GetElapsedTime(EntryTimestamp);
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }
}

internal interface IRestRetryExecutor
{
    ValueTask<RestPolicyResult> ExecuteAsync(RestRetryPolicy policy, RestRetryExecutionContext context,
        Func<int, CancellationToken, ValueTask<RestAttemptOutcome>> executeAttempt);
}
