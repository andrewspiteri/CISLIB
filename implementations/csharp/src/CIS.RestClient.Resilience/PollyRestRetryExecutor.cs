using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace CIS.RestClient.Resilience;

// Polly owns iteration, jitter and local waiting. State below belongs to one awaited execution only.
internal sealed class PollyRestRetryExecutor : IRestRetryExecutor
{
    private static readonly ResiliencePropertyKey<Execution> StateKey = new("CIS.RestClient.Retry.Execution");
    private static readonly TimeSpan MaximumNativeDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1L);
    private readonly Dictionary<string, Registration> registrations = new(StringComparer.Ordinal);
    private readonly TimeProvider clock;
    private readonly ILogger<PollyRestRetryExecutor>? logger;
    private int invalidRetryAfter;

    internal PollyRestRetryExecutor(IReadOnlyList<RestRetryPolicy> policies, TimeProvider timeProvider,
        ILogger<PollyRestRetryExecutor>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        clock = timeProvider; this.logger = logger;
        foreach (RestRetryPolicy policy in CopyPolicies(policies))
        {
            var builder = new ResiliencePipelineBuilder<RestAttemptOutcome> { TimeProvider = clock };
            if (policy.MaxRetries != 0)
            {
                builder.AddRetry(new RetryStrategyOptions<RestAttemptOutcome>
                {
                    MaxRetryAttempts = policy.MaxRetries,
                    Delay = policy.InitialDelay,
                    MaxDelay = policy.MaxLocalDelay,
                    BackoffType = policy.Backoff switch
                    {
                        RestRetryBackoff.Constant => DelayBackoffType.Constant,
                        RestRetryBackoff.Linear => DelayBackoffType.Linear,
                        _ => DelayBackoffType.Exponential
                    },
                    UseJitter = policy.UseJitter,
                    ShouldHandle = args => ShouldRetryAsync(policy, args),
                    OnRetry = args => BeforeWaitAsync(policy, args),
                });
            }
            registrations.Add(policy.Name, new(policy, builder.Build()));
        }
    }

    internal static RestRetryPolicy[] CopyPolicies(IReadOnlyList<RestRetryPolicy>? policies)
    {
        int count = policies?.Count ?? 0;
        if (count is < 1 or > 32)
        { throw new ArgumentException("Supply one through thirty-two explicit retry policies.", nameof(policies)); }
        var copied = new RestRetryPolicy[count];
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (int index = 0; index < copied.Length; index++)
        {
            RestRetryPolicy policy = policies![index];
            if (policy is null || !names.Add(policy.Name)) { throw new ArgumentException("Retry policies must be nonnull with distinct names.", nameof(policies)); }
            copied[index] = policy;
        }
        return copied;
    }

    public async ValueTask<RestPolicyResult> ExecuteAsync(RestRetryPolicy policy, RestRetryExecutionContext context,
        Func<int, CancellationToken, ValueTask<RestAttemptOutcome>> executeAttempt)
    {
        ArgumentNullException.ThrowIfNull(policy); ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(executeAttempt);
        if (!registrations.TryGetValue(policy.Name, out Registration? selected) || !ReferenceEquals(selected.Policy, policy))
        { throw new InvalidOperationException("Select the exact registered immutable retry policy."); }
        if (!ReferenceEquals(context.TimeProvider, clock)) { throw new InvalidOperationException("The retry executor and operation must share one fixed TimeProvider."); }
        var state = new Execution(context, executeAttempt);
        ResilienceContext resilience = ResilienceContextPool.Shared.Get(context.CancellationToken);
        resilience.Properties.Set(StateKey, state);
        try
        {
            RestAttemptOutcome terminal;
            try
            {
                terminal = await selected.Pipeline.ExecuteAsync(static async (current, execution) =>
                {
                    execution.Context.CancellationToken.ThrowIfCancellationRequested();
                    if (execution.Context.IsStopping)
                    { return execution.Last = new(null, RestFailureKind.Stopping, "client.stopping", Method: execution.Context.Method); }
                    if (execution.Context.Remaining <= TimeSpan.Zero)
                    { return execution.Last = new(null, RestFailureKind.Timeout, "client.timeout", Method: execution.Context.Method); }
                    RestAttemptOutcome result = await execution.Attempt(execution.Iteration++, current.CancellationToken).ConfigureAwait(false);
                    execution.Last = result;
                    return result;
                }, resilience, state).ConfigureAwait(false);
            }
            catch (RetryBudgetStop)
            {
                terminal = state.Last ?? throw new InvalidOperationException("Retry budget rejection requires the completed terminal attempt.");
            }
            return new(terminal, terminal.FailureKind == RestFailureKind.None ? null
                : state.StopReason ?? (policy.MaxRetries == 0 ? RestRetryStopReason.AttemptLimit : RestRetryStopReason.NonRetryable));
        }
        finally { ResilienceContextPool.Shared.Return(resilience); }
    }

    private static async ValueTask<bool> ShouldRetryAsync(RestRetryPolicy policy, RetryPredicateArguments<RestAttemptOutcome> args)
    {
        // Programming defects, including unrelated cancellation, remain exceptions even after operation cancellation.
        if (args.Outcome.Exception is not null) { return false; }
        Execution state = args.Context.Properties.GetValue(StateKey, null!);
        RestAttemptOutcome outcome = args.Outcome.Result!;
        if (outcome.FailureKind is RestFailureKind.None or RestFailureKind.Stopping)
        { state.StopReason = RestRetryStopReason.NonRetryable; return false; }
        state.Context.CancellationToken.ThrowIfCancellationRequested();
        if (state.Context.IsStopping) { state.StopReason = RestRetryStopReason.NonRetryable; return false; }
        if (!outcome.RetryEligible || !RestContractRules.Method(outcome.Method)
            || !policy.Methods.Contains(outcome.Method!, StringComparer.Ordinal)
            || outcome.Method is not ("GET" or "HEAD" or "OPTIONS") && state.Context.ReplaySafety != RestReplaySafety.Assured)
        { state.StopReason = RestRetryStopReason.NotEligible; return false; }
        if (!outcome.BodyReplayable) { state.StopReason = RestRetryStopReason.BodyNotReplayable; return false; }
        if (outcome.BodyProcessingStarted && (outcome.FailureKind is RestFailureKind.Transport or RestFailureKind.Timeout
            or RestFailureKind.InvalidResponse or RestFailureKind.LimitExceeded)
            && (!policy.RetryBodyFailures || state.Context.ReplaySafety != RestReplaySafety.Assured))
        { state.StopReason = RestRetryStopReason.NonRetryable; return false; }
        bool eligible = outcome.FailureKind == RestFailureKind.Transport
            ? outcome.TransportFailure is { } transport && policy.TransportFailures.Contains(transport)
            : outcome.FailureKind == RestFailureKind.Http && outcome.HttpStatusCode is { } status && policy.StatusCodes.Contains(status)
                || policy.FailureKinds.Contains(outcome.FailureKind)
                || outcome.ErrorCode is { } code && policy.ErrorCodes.Contains(code, StringComparer.Ordinal);
        if (!eligible) { state.StopReason = RestRetryStopReason.NonRetryable; return false; }
        if (args.AttemptNumber >= policy.MaxRetries) { state.StopReason = RestRetryStopReason.AttemptLimit; return false; }
        if (policy.ShouldRetry is { } decide)
        {
            bool retry;
            try
            {
                retry = await decide(new(args.AttemptNumber, outcome.HttpStatusCode, outcome.FailureKind,
                    outcome.ErrorCode, outcome.BodyProcessingStarted, outcome.BodyReplayable, state.Context.Remaining, outcome.TransportFailure),
                    state.Context.CancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException canceled) when (canceled.CancellationToken != state.Context.CancellationToken
                || !state.Context.CancellationToken.IsCancellationRequested)
            {
                Exception failure = RestCallbackFaultException.Capture(canceled, state.Context.CancellationToken,
                    enabled: true, knownCallerToken: state.Context.CallerCancellationToken);
                if (ReferenceEquals(failure, canceled)) { throw; }
                throw failure;
            }
            if (!retry) { state.StopReason = RestRetryStopReason.NonRetryable; return false; }
        }
        state.Context.CancellationToken.ThrowIfCancellationRequested();
        if (state.Context.IsStopping) { state.StopReason = RestRetryStopReason.NonRetryable; return false; }
        return true;
    }

    private async ValueTask BeforeWaitAsync(RestRetryPolicy policy, OnRetryArguments<RestAttemptOutcome> args)
    {
        Execution state = args.Context.Properties.GetValue(StateKey, null!);
        TimeSpan local = args.RetryDelay, server = TimeSpan.Zero;
        bool representable = !policy.HonorRetryAfter || TryServerDelay(args.Outcome.Result!.RetryAfter, out server);
        if (!representable || !TryChooseDelay(local, server, state.Context.Remaining, out _, out TimeSpan supplemental))
        {
            state.StopReason = RestRetryStopReason.InsufficientTimeRemaining;
            throw new RetryBudgetStop();
        }
        state.Context.CancellationToken.ThrowIfCancellationRequested();
        // Previous attempt has already disposed its native resources before returning its detached summary.
        // Task.Delay truncates to milliseconds on pinned .NET. Compensate that precision without resampling L:
        // supplemental + floor_ms(L) equals the one admitted ceil_ms(max(L, server)).
        if (supplemental > TimeSpan.Zero)
        { await Task.Delay(supplemental, clock, state.Context.CancellationToken).ConfigureAwait(false); }
    }

    internal static bool TryChooseDelay(TimeSpan local, TimeSpan server, TimeSpan remaining,
        out TimeSpan chosen, out TimeSpan supplemental)
    {
        long ticks = Math.Max(local.Ticks, server.Ticks);
        long milliseconds = ticks / TimeSpan.TicksPerMillisecond;
        if (ticks % TimeSpan.TicksPerMillisecond != 0) { milliseconds++; }
        chosen = supplemental = TimeSpan.Zero;
        if (milliseconds > (long)MaximumNativeDelay.TotalMilliseconds) { return false; }
        chosen = TimeSpan.FromTicks(milliseconds * TimeSpan.TicksPerMillisecond);
        supplemental = chosen - TimeSpan.FromTicks(local.Ticks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond);
        return chosen < remaining;
    }

    private bool TryServerDelay(string? raw, out TimeSpan delay)
    {
        delay = TimeSpan.Zero;
        if (raw is null) { return true; }
        if (RetryConditionHeaderValue.TryParse(raw, out RetryConditionHeaderValue? parsed))
        {
            if (parsed.Delta is { } delta) { delay = delta; }
            else if (parsed.Date is { } date) { delay = date - clock.GetUtcNow(); if (delay < TimeSpan.Zero) { delay = TimeSpan.Zero; } }
            return true;
        }
        // A syntactically integral server minimum which the native parser cannot represent must never become an early retry.
        ReadOnlySpan<char> text = raw.AsSpan().Trim();
        bool digits = text.Length != 0;
        foreach (char character in text) { if (!char.IsAsciiDigit(character)) { digits = false; break; } }
        if (digits) { return false; }
        if (Interlocked.Exchange(ref invalidRetryAfter, 1) == 0)
        {
            try { logger?.LogWarning("CIS RestClient ignored an invalid Retry-After header."); }
            catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) { }
        }
        return true;
    }

    private sealed record Registration(RestRetryPolicy Policy, ResiliencePipeline<RestAttemptOutcome> Pipeline);
    private sealed class RetryBudgetStop : Exception { }
    private sealed class Execution(RestRetryExecutionContext context, Func<int, CancellationToken, ValueTask<RestAttemptOutcome>> attempt)
    {
        internal RestRetryExecutionContext Context { get; } = context;
        internal Func<int, CancellationToken, ValueTask<RestAttemptOutcome>> Attempt { get; } = attempt;
        internal int Iteration { get; set; }
        internal RestAttemptOutcome? Last { get; set; }
        internal RestRetryStopReason? StopReason { get; set; }
    }
}
