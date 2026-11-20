using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using CIS.Metrics;
using CIS.RestClient.Http;
using Microsoft.Extensions.Logging;

namespace CIS.RestClient;

// One owner for admitted logical operations. Native pools, extension instances
// and policy pipelines are reused; attempt state never leaves the operation.
internal sealed class RestClientRuntime : IRestClientRuntime, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly RestClientComposition composition;
    private readonly Dictionary<string, RestClientBinding> bindings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RestRetryPolicy> policies = new(StringComparer.Ordinal);
    private readonly TimeProvider clock;
    private readonly IRestRetryExecutor? executor;
    private readonly ILogger<RestClientRuntime>? logger;
    private readonly MetricsAttachment attachment = new();
    private readonly CancellationTokenSource stoppingSource = new();
    private readonly CancellationToken stoppingToken;
    private readonly HashSet<Task> operations = [];
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private RestClientMetrics? metrics;
    private bool stopping;
    private Task? cancellation, stop, disposal;
    private int warnedOperation;

    internal RestClientRuntime(RestClientComposition composition, IReadOnlyList<RestClientBinding> bindings,
        TimeProvider clock, IReadOnlyList<RestRetryPolicy>? policies = null, IRestRetryExecutor? executor = null,
        ILogger<RestClientRuntime>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(bindings);
        ArgumentNullException.ThrowIfNull(clock);
        this.composition = composition;
        this.clock = clock;
        this.executor = executor;
        this.logger = logger;
        stoppingToken = stoppingSource.Token;
        foreach (RestRetryPolicy policy in RestContractRules.Copy(policies, 32, nameof(policies)))
        {
            if (policy is null || !this.policies.TryAdd(policy.Name, policy))
            {
                throw new ArgumentException("Use distinct nonnull immutable retry policies.", nameof(policies));
            }
        }
        foreach (RestClientBinding binding in RestContractRules.Copy(bindings, 16, nameof(bindings)))
        {
            if (binding is null || binding.Options is null || binding.Transport is null
                || !this.bindings.TryAdd(binding.Options.ClientName, binding))
            {
                throw new ArgumentException("Use distinct valid client bindings.", nameof(bindings));
            }
            SelectPolicy(binding.Options.RetryPolicy);
            binding.Transport.ValidateBinding(binding.Options.SecurityBinding, binding.Options.AllowedOrigins);
        }
        var declared = new HashSet<RestOperationIdentity>(composition.Operations);
        var actual = new HashSet<RestOperationIdentity>(this.bindings.Values.SelectMany(binding =>
            binding.Options.Operations.Select(operation => new RestOperationIdentity(binding.Options.ClientName, operation))));
        if (this.bindings.Count == 0 || !declared.SetEquals(actual))
        {
            throw new ArgumentException("The passive composition must describe these exact client bindings.", nameof(composition));
        }
    }

    public object RegistrationIdentity => composition.RegistrationIdentity;
    internal bool IsStopping
    {
        get
        {
            lock (gate)
            {
                return stopping;
            }
        }
    }

    public void AttachMetrics(MetricsRuntime runtime)
    {
        lock (gate)
        {
            if (stopping)
            {
                return;
            }
            if (!attachment.Attach(runtime))
            {
                return;
            }
        }
        var bundle = new RestClientMetrics(runtime, composition);
        lock (gate)
        {
            if (!stopping)
            {
                Volatile.Write(ref metrics, bundle);
            }
        }
    }

    internal Task<RestResult<T>> ExecuteAsync<T>(string clientName, string method, string url,
        RestRequestOptions? options, CancellationToken caller)
    {
        Activity? entryActivity = Activity.Current;
        string? trace = CaptureTrace(entryActivity);
        string? traceParent = RestTraceForwarding.Capture(entryActivity);
        long entry = clock.GetTimestamp();
        RestClientMetrics? capturedMetrics = Volatile.Read(ref metrics);
        ArgumentNullException.ThrowIfNull(clientName);
        ArgumentNullException.ThrowIfNull(url);
        if (!bindings.TryGetValue(clientName, out RestClientBinding? binding))
        {
            throw new ArgumentException("Select an explicitly registered client alias.", nameof(clientName));
        }
        if (!RestContractRules.Method(method))
        {
            throw new ArgumentException("Use a valid HTTP method token.", nameof(method));
        }
        Uri initialDestination = RestUri.Resolve(binding.Options.BaseUrl, url);
        TimeSpan timeout = options?.Timeout ?? binding.Options.Timeout;
        RestContractRules.Timeout(timeout, nameof(options));
        // Snapshotting is pure. The timer below uses the remaining entry budget,
        // so capturing reusable inputs never grants a fresh full timeout.
        RestCallSnapshot call;
        try
        {
            call = RestCallSnapshot.Capture(binding.Options, options, caller, timeout);
        }
        catch (OperationCanceledException error) when (error.CancellationToken == caller && caller.IsCancellationRequested)
        {
            return Task.FromCanceled<RestResult<T>>(caller);
        }
        RestRetryPolicy? policy = SelectPolicy(call.RetryPolicy);
        ValidateFormat<T>(binding.Options, call, method);
        if (caller.IsCancellationRequested)
        {
            return Task.FromCanceled<RestResult<T>>(caller);
        }
        bool unknownOperation = options?.OperationName is { } requested && requested != call.OperationName;
        var completion = new TaskCompletionSource<RestResult<T>>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool denied;
        lock (gate)
        {
            if (caller.IsCancellationRequested)
            {
                return Task.FromCanceled<RestResult<T>>(caller);
            }
            denied = stopping;
            if (!denied)
            {
                operations.Add(completion.Task);
            }
        }
        if (denied)
        {
            if (caller.IsCancellationRequested)
            {
                return Task.FromCanceled<RestResult<T>>(caller);
            }
            TimeSpan elapsed = Elapsed(entry);
            RestFailureKind kind = elapsed >= timeout ? RestFailureKind.Timeout : RestFailureKind.Stopping;
            return Task.FromResult(BuildResult<T>(SourceFailure<T>(kind), call, clientName, 0, elapsed, trace, null));
        }
        IRestOperationObservation? observation = capturedMetrics?.Begin(clientName, call.OperationName);
        _ = RunAsync(binding, call, policy, method, url, initialDestination, caller, entry, trace, traceParent, observation, unknownOperation, completion);
        return completion.Task;
    }

    private async Task RunAsync<T>(RestClientBinding binding, RestCallSnapshot call, RestRetryPolicy? policy,
        string method, string url, Uri initialDestination, CancellationToken caller, long entry, string? trace, string? traceParent,
        IRestOperationObservation? observation, bool unknownOperation, TaskCompletionSource<RestResult<T>> completion)
    {
        CancellationTokenSource? total = null, linked = null;
        CancellationToken totalToken = default, operationToken = default;
        AttemptResult<T>? terminal = null;
        RestRetryStopReason? retryStop = null;
        ExceptionDispatchInfo? fault = null;
        ReplayIdentity? replay = null;
        RestBody? replayBody = null;
        int dispatches = 0;
        ILogger<RestClientRuntime>? callLogger = binding.Options.LogCalls ? logger : null;
        string loggedMethod = method, loggedUri = string.Empty;
        TimeSpan elapsed = TimeSpan.Zero;
        try
        {
            if (callLogger is not null)
            {
                loggedUri = RestCallLogging.SafeUri(initialDestination);
            }
            TimeSpan remaining = call.Timeout - Elapsed(entry);
            total = new CancellationTokenSource(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, clock);
            totalToken = total.Token;
            linked = CancellationTokenSource.CreateLinkedTokenSource(caller, totalToken, stoppingToken);
            operationToken = linked.Token;
            operationToken.ThrowIfCancellationRequested();
            RestResponseFormat initialFormat = ResponseFormat<T>(call, method);
            if (initialFormat != RestResponseFormat.NoValue && binding.Options.Codec is { } custom
                && !custom.CanDecode(typeof(T), initialFormat))
            {
                throw new ArgumentException("The response type and explicit content format are incompatible.");
            }
            if (callLogger is not null && unknownOperation && Interlocked.Exchange(ref warnedOperation, 1) == 0)
            {
                try
                {
                    callLogger.LogWarning("An unregistered REST operation alias was normalized to request.");
                }
                catch (Exception error) when (MetricsRuntime.IsOrdinary(error)) { }
            }
            if (remaining <= TimeSpan.Zero)
            {
                terminal = SourceFailure<T>(RestFailureKind.Timeout);
            }
            else if (policy is null)
            {
                await AttemptAsync(0, operationToken).ConfigureAwait(false);
                retryStop = terminal!.Outcome.FailureKind == RestFailureKind.None ? null : RestRetryStopReason.Disabled;
            }
            else
            {
                var context = new RestRetryExecutionContext(clock, entry, call.Timeout, operationToken, method,
                    call.ReplaySafety, () => IsStopping, callerCancellationToken: caller);
                RestPolicyResult result = await executor!.ExecuteAsync(policy, context, AttemptAsync).ConfigureAwait(false);
                // An executor can stop before invoking another attempt. Its new
                // detached summary must never inherit a previous response/value.
                if (terminal is null || !ReferenceEquals(terminal.Outcome, result.Terminal))
                {
                    terminal = FromSummary<T>(result.Terminal);
                }
                retryStop = result.StopReason;
            }
        }
        catch (Exception error)
        {
            if (!KnownCancellation(error, caller, operationToken, totalToken, stoppingToken))
            {
                fault = ExceptionDispatchInfo.Capture(UnwrapFault(error));
            }
            else
            {
                terminal = null;
            }
        }
        finally
        {
            Task? pendingCancellation;
            lock (gate)
            {
                pendingCancellation = cancellation;
            }
            if (pendingCancellation is not null)
            {
                try
                {
                    await pendingCancellation.ConfigureAwait(false);
                }
                catch (Exception error) { fault = AddFault(fault, error); }
            }
            try
            {
                linked?.Dispose();
            }
            catch (Exception error) { fault = AddFault(fault, error); }
            try
            {
                total?.Dispose();
            }
            catch (Exception error) { fault = AddFault(fault, error); }
            try
            {
                elapsed = Elapsed(entry);
            }
            catch (Exception error) { fault = AddFault(fault, error); }

            RestResult<T>? result = null;
            RestOperationOutcome metricOutcome;
            bool canceled;
            // This gate is the terminal commit point. Stop before this point
            // participates in source priority; Stop after it joins this already
            // committed operation through metrics and public-task publication.
            lock (gate)
            {
                canceled = fault is null && caller.IsCancellationRequested;
                if (fault is not null)
                {
                    metricOutcome = RestOperationOutcome.UnexpectedError;
                }
                else if (canceled)
                {
                    metricOutcome = RestOperationOutcome.Cancelled;
                }
                else
                {
                    if (totalToken.IsCancellationRequested || elapsed >= call.Timeout)
                    {
                        terminal = OverrideSource(terminal, RestFailureKind.Timeout);
                    }
                    else if (stopping)
                    {
                        terminal = OverrideSource(terminal, RestFailureKind.Stopping);
                    }
                    terminal ??= SourceFailure<T>(RestFailureKind.Stopping);
                    if (terminal.Outcome.FailureKind != RestFailureKind.None && policy is null)
                    {
                        retryStop = RestRetryStopReason.Disabled;
                    }
                    try
                    {
                        result = BuildResult(terminal, call, binding.Options.ClientName, dispatches, elapsed, trace, retryStop);
                    }
                    catch (Exception error) { fault = AddFault(fault, error); }
                    metricOutcome = fault is null ? MetricOutcome(terminal.Outcome.FailureKind) : RestOperationOutcome.UnexpectedError;
                }
            }
            try
            {
                observation?.Complete(metricOutcome, elapsed);
            }
            catch (Exception error) when (MetricsRuntime.IsOrdinary(error)) { }
            RestCallLogging.Write(callLogger, loggedMethod, loggedUri, metricOutcome, result?.Context.HttpStatusCode,
                elapsed, dispatches, policy?.MaxRetries + 1 ?? 1);
            if (fault is not null)
            {
                completion.TrySetException(fault.SourceException);
            }
            else if (canceled)
            {
                completion.TrySetCanceled(caller);
            }
            else
            {
                completion.TrySetResult(result!);
            }
            lock (gate)
            {
                operations.Remove(completion.Task);
                if (stopping && operations.Count == 0)
                {
                    drained.TrySetResult();
                }
            }
        }

        async ValueTask<RestAttemptOutcome> AttemptAsync(int iteration, CancellationToken suppliedToken)
        {
            if (suppliedToken != operationToken)
            {
                throw new InvalidOperationException("The retry executor must preserve the operation token.");
            }
            AttemptResult<T>? previous = terminal;
            terminal = null;
            var current = new AttemptResult<T>(new(null, RestFailureKind.None, Method: method));
            CancellationTokenSource? attemptTimeout = null, attemptLink = null;
            CancellationToken attemptTimeoutToken = default, attemptToken = operationToken;
            RestHttpContentLease? content = null;
            HttpRequestMessage? message = null;
            RestExchangePipeline? pipeline = null;
            RestRequestContext? request = null;
            ExceptionDispatchInfo? attemptFault = null;
            ExceptionDispatchInfo? cleanupFault = null;
            Exception? expected = null;
            bool rejectedReplay = false;
            bool acquisitionStarted = false;
            RestBody? acquiredBody = null;
            long attemptEntry = 0;
            try
            {
                operationToken.ThrowIfCancellationRequested();
                attemptEntry = clock.GetTimestamp();
                if (policy?.AttemptTimeout is { } attemptDuration)
                {
                    attemptTimeout = new CancellationTokenSource(attemptDuration, clock);
                    attemptTimeoutToken = attemptTimeout.Token;
                    attemptLink = CancellationTokenSource.CreateLinkedTokenSource(operationToken, attemptTimeoutToken);
                    attemptToken = attemptLink.Token;
                }
                request = RestRequestFinalizer.Create(binding.Options, call, method, url, iteration);
                if (replay is not null)
                {
                    request.Body = replayBody ?? replay.Body;
                }
                pipeline = new RestExchangePipeline(binding.Options.Handlers, PrimaryAsync, attemptToken,
                    captureUnexpectedCancellation: true, knownCallerToken: caller);
                IRestResponse response = await pipeline.RunAsync(request).ConfigureAwait(false);
                current.Status = response.HttpStatusCode;
                current.Headers = new RestResultContext(headers: response.Headers).Headers;
                current.BodyStarted = true;
                RestReadResult<T> read = await new RestHttpResponseReader(response, call.Limits, observation)
                    .ReadAsync<T>(ResponseFormat<T>(call, request.Method), ResponseFormat<T>(call, request.Method) != RestResponseFormat.NoValue && call.RequireValue,
                        binding.Options.Codec, attemptToken).ConfigureAwait(false);
                current.ErrorBody = read.ErrorBody;
                current.ErrorBodyTruncated = read.ErrorBodyTruncated;
                RestMappedResponse<T> mapped = await RestResponseMapping.MapAsync(response, read.Decoded, read.ErrorBody, call.ResponseMapper, attemptToken).ConfigureAwait(false);
                current.Decoded = mapped.Decoded;
                current.Explanation = mapped.Explanation;
                current.RemoteErrorCode = mapped.RemoteErrorCode;
                current.Outcome = new(current.Status, mapped.FailureKind, mapped.ErrorCode,
                    RetryAfter(current.Headers), current.BodyStarted, CanReplay(), Method: request.Method);
                if (binding.Options.SecurityBinding is not null && current.Status is 401 or 403)
                {
                    current.Outcome = current.Outcome with
                    {
                        SecurityFailure = new CIS.Security.SecurityFailure(CIS.Security.SecurityFailureKind.CauseUnknown,
                            "The remote HTTP status does not establish a precise security cause.",
                            CIS.Security.SecurityFailureSource.Unknown, current.Status)
                    };
                }
            }
            catch (ReplayRejected error)
            {
                rejectedReplay = true;
                current = previous ?? throw new InvalidOperationException("Replay rejection requires a settled preceding failure.");
                current.Outcome = error.BodyChanged ? current.Outcome with
                {
                    BodyReplayable = false
                }
                    : current.Outcome with
                    {
                        RetryEligible = false
                    };
            }
            catch (AttemptRejected error) { expected = error; }
            catch (RestHttpExpectedException error) { expected = error; }
            catch (Exception error)
            {
                if (KnownCancellation(error, caller, operationToken, totalToken, stoppingToken, attemptToken, attemptTimeoutToken))
                {
                    expected = error;
                }
                else
                {
                    attemptFault = ExceptionDispatchInfo.Capture(error is OperationCanceledException ? new RestCallbackFaultException(error) : error);
                }
            }
            finally
            {
                request?.Seal();
                if (pipeline is not null)
                {
                    try
                    {
                        await pipeline.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception error) { cleanupFault = AddFault(cleanupFault, error); }
                }
                try
                {
                    message?.Dispose();
                }
                catch (Exception error) { cleanupFault = AddFault(cleanupFault, error); }
                if (content is not null)
                {
                    if (!rejectedReplay && replay is not null && content.ReplayBody is { } replacement
                        && !ReferenceEquals(replacement, request?.Body))
                    {
                        replayBody = replacement;
                    }
                    try
                    {
                        await content.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception error)
                    {
                        if (content.EncodingFault is not { } encoding || !ReferenceEquals(UnwrapFault(error), encoding.SourceException))
                        {
                            cleanupFault = AddFault(cleanupFault, error);
                        }
                    }
                    // Native HttpClient cancellation can obscure an encoding
                    // fault while the actual serializer is still settling.
                    // The lease joins that work and preserves its provenance.
                    if (content.EncodingFault is { } encodingFault)
                    {
                        if (attemptFault is null)
                        {
                            attemptFault = ExceptionDispatchInfo.Capture(new RestEncodingFaultException(encodingFault.SourceException));
                        }
                        else if (!ReferenceEquals(UnwrapFault(attemptFault.SourceException), encodingFault.SourceException))
                        {
                            attemptFault = AddFault(attemptFault, new RestEncodingFaultException(encodingFault.SourceException));
                        }
                        expected = null;
                    }
                }
                try
                {
                    attemptLink?.Dispose();
                }
                catch (Exception error) { cleanupFault = AddFault(cleanupFault, error); }
                try
                {
                    attemptTimeout?.Dispose();
                }
                catch (Exception error) { cleanupFault = AddFault(cleanupFault, error); }
            }
            if (cleanupFault is not null)
            {
                attemptFault = AddFault(attemptFault, cleanupFault.SourceException, expected);
            }
            if (attemptFault is not null)
            {
                attemptFault.Throw();
            }
            if (!rejectedReplay)
            {
                current.Outcome = current.Outcome with
                {
                    HttpStatusCode = current.Status ?? current.Outcome.HttpStatusCode,
                    Method = request?.Method ?? method,
                    BodyProcessingStarted = current.BodyStarted,
                    BodyReplayable = CanReplay()
                };
            }
            if (expected is AttemptRejected declared)
            {
                current.Outcome = new(current.Status, declared.Failure.Kind, declared.Failure.Code,
                    BodyReplayable: CanReplay(), RetryEligible: declared.RetryEligible && declared.Failure.Code != "client.unsupported_operation", Method: request?.Method ?? method)
                {
                    SecurityFailure = declared.Failure.SecurityFailure
                };
                current.Explanation = declared.Failure.Explanation;
                current.RemoteErrorCode = declared.Failure.RemoteErrorCode;
            }
            else if (expected is RestHttpExpectedException identified)
            {
                MapExpected(current, identified, request?.Method ?? method, CanReplay(), binding.Options.SecurityBinding is not null);
            }
            if (caller.IsCancellationRequested)
            {
                throw new OperationCanceledException(caller);
            }
            if (totalToken.IsCancellationRequested || Elapsed(entry) >= call.Timeout)
            {
                current = OverrideSource(current, RestFailureKind.Timeout);
            }
            else if (IsStopping)
            {
                current = OverrideSource(current, RestFailureKind.Stopping);
            }
            else if (attemptTimeoutToken.IsCancellationRequested || policy?.AttemptTimeout is { } duration
                && clock.GetElapsedTime(attemptEntry) >= duration)
            {
                current = OverrideSource(current, RestFailureKind.Timeout, attempt: true);
            }
            else if (expected is OperationCanceledException unexpected)
            {
                ExceptionDispatchInfo.Capture(unexpected).Throw();
            }
            terminal = current;
            return current.Outcome;

            bool CanReplay() => content?.CanReplay ?? (!acquisitionStarted || acquiredBody is null || acquiredBody.IsReplayable);

            async ValueTask<IRestResponse> PrimaryAsync(RestRequestContext actual, CancellationToken token)
            {
                RestDeclaredFailure? failure = await RestRequestFinalizer.PrepareAsync(actual, binding.Options, call,
                    async prepareToken =>
                    {
                        RestBody? selected = replay is not null && ReferenceEquals(actual.Body, replay.Body) && replayBody is not null ? replayBody : actual.Body;
                        acquiredBody = selected;
                        acquisitionStarted = true;
                        content = await RestHttpContentLease.AcquireAsync(selected, binding.Options.Codec, observation,
                            prepareToken, knownCallerToken: caller).ConfigureAwait(false);
                    },
                    (maximum, signingToken) => content!.GetSigningBytesAsync(maximum, signingToken), token,
                    () =>
                    {
                        ReplayIdentity candidate = CaptureReplay(actual, binding.Options);
                        if (replay is null)
                        {
                            replay = candidate;
                        }
                        else
                        {
                            if (!SameBody(replay.Body, candidate.Body) && !ReferenceEquals(replayBody, candidate.Body))
                            {
                                throw new ReplayRejected(true);
                            }
                            if (!SameRequest(replay, candidate))
                            {
                                throw new ReplayRejected(false);
                            }
                        }
                        message = RestNativeRequest.Create(actual, content!.Content);
                    }, captureUnexpectedCancellation: true, knownCallerToken: caller, capturedTraceParent: traceParent).ConfigureAwait(false);
                if (failure is not null)
                {
                    throw new AttemptRejected(failure);
                }
                RestNativeRequest.Finalize(message!, actual);
                string dispatchedUri = callLogger is null ? string.Empty : RestCallLogging.SafeUri(message!.RequestUri!);
                return await binding.Transport.SendAsync(message!, () =>
                {
                    // Timer delivery can lag a monotonic deadline. Check the
                    // exact entry budget immediately at primary admission,
                    // without invoking an application clock under the gate.
                    if (Elapsed(entry) >= call.Timeout)
                    {
                        throw new AttemptRejected(new(RestFailureKind.Timeout, "client.timeout", "The REST operation exceeded its time budget."), false);
                    }
                    lock (gate)
                    {
                        token.ThrowIfCancellationRequested();
                        if (stopping)
                        {
                            throw new AttemptRejected(new(RestFailureKind.Stopping, "client.stopping", "The REST client is stopping."), false);
                        }
                        dispatches++;
                        loggedMethod = actual.Method;
                        loggedUri = dispatchedUri;
                    }
                }, token).ConfigureAwait(false);
            }
        }
    }

    public void BeginStopping()
    {
        TaskCompletionSource? start = null;
        lock (gate)
        {
            if (stopping)
            {
                return;
            }
            stopping = true;
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            cancellation = start.Task;
            if (operations.Count == 0)
            {
                drained.TrySetResult();
            }
        }
        _ = CancelAsync(start);
    }
    private async Task CancelAsync(TaskCompletionSource completion)
    {
        try
        {
            await stoppingSource.CancelAsync().ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (Exception error) { completion.TrySetException(error); }
    }
    public Task StopAsync()
    {
        BeginStopping();
        lock (gate)
        {
            return stop ??= Task.WhenAll(drained.Task, cancellation!);
        }
    }
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource? start = null;
        Task shared;
        lock (gate)
        {
            if (disposal is null)
            {
                start = new(TaskCreationOptions.RunContinuationsAsynchronously);
                disposal = start.Task;
            }
            shared = disposal;
        }
        if (start is not null)
        {
            _ = DisposeCoreAsync(start);
        }
        return new(shared);
    }
    private async Task DisposeCoreAsync(TaskCompletionSource completion)
    {
        ExceptionDispatchInfo? fault = null;
        try
        {
            try
            {
                await StopAsync().ConfigureAwait(false);
            }
            catch (Exception error) { fault = AddFault(fault, error); }
            var pools = new HashSet<RestHttpTransport>(ReferenceEqualityComparer.Instance);
            foreach (RestClientBinding binding in bindings.Values)
            {
                if (binding.OwnsTransport && pools.Add(binding.Transport))
                {
                    try
                    {
                        await binding.Transport.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception error) { fault = AddFault(fault, error); }
                }
            }
            try
            {
                stoppingSource.Dispose();
            }
            catch (Exception error) { fault = AddFault(fault, error); }
            if (fault is not null)
            {
                fault.Throw();
            }
            completion.TrySetResult();
        }
        catch (Exception error) { completion.TrySetException(error); }
    }

    private RestRetryPolicy? SelectPolicy(RestRetrySelection selection)
    {
        if (selection.PolicyName is not { } name)
        {
            return null;
        }
        if (!policies.TryGetValue(name, out RestRetryPolicy? policy) || executor is null)
        {
            throw new InvalidOperationException("A named retry choice requires its exact registered policy and executor.");
        }
        return policy;
    }
    private static void ValidateFormat<T>(RestClientSnapshot client, RestCallSnapshot call, string method)
    {
        if (call.Body is not null && !MediaTypeHeaderValue.TryParse(call.Body.MediaType, out _))
        {
            throw new ArgumentException("Use a valid request content media type.");
        }
        RestResponseFormat format = ResponseFormat<T>(call, method);
        if (format == RestResponseFormat.NoValue)
        {
            return;
        }
        // Default capability checks are pure. Application codec callbacks run
        // inside the admitted operation and its already-running deadline.
        if (client.Codec is null && !RestDefaultContentCodec.Instance.CanDecode(typeof(T), format))
        {
            throw new ArgumentException("The response type and explicit content format are incompatible.");
        }
    }
    private static RestResponseFormat ResponseFormat<T>(RestCallSnapshot call, string method)
        => typeof(T) == typeof(RestNoValue) || string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)
            ? RestResponseFormat.NoValue : call.ResponseFormat;
    private TimeSpan Elapsed(long entry)
    {
        TimeSpan value = clock.GetElapsedTime(entry);
        return value >= TimeSpan.Zero ? value : TimeSpan.Zero;
    }
    private static string? CaptureTrace(Activity? entryActivity)
        => entryActivity is { IdFormat: ActivityIdFormat.W3C } activity && activity.TraceId != default ? activity.TraceId.ToHexString() : null;
    private static bool KnownCancellation(Exception error, params CancellationToken[] tokens)
        => error is OperationCanceledException canceled && tokens.Any(token => token == canceled.CancellationToken && token.IsCancellationRequested);
    private static Exception UnwrapFault(Exception error) => error switch
    {
        RestEncodingFaultException encoding => encoding.Original.SourceException,
        RestCallbackFaultException callback => callback.Original.SourceException,
        _ => error
    };
    private static ExceptionDispatchInfo AddFault(ExceptionDispatchInfo? fault, Exception cleanup, Exception? expected = null)
    {
        Exception? first = fault?.SourceException ?? expected;
        return ExceptionDispatchInfo.Capture(first is null ? UnwrapFault(cleanup)
            : new AggregateException("REST work and owned cleanup both failed.", UnwrapFault(first), UnwrapFault(cleanup)));
    }
    private static string? RetryAfter(IReadOnlyDictionary<string, IReadOnlyList<string>> headers)
        => headers.TryGetValue("Retry-After", out var values) ? values.FirstOrDefault() : null;

    private static void MapExpected<T>(AttemptResult<T> current, RestHttpExpectedException error, string method, bool replayable, bool securityBound)
    {
        RestFailureKind kind = error switch
        {
            RestDecodingException => RestFailureKind.InvalidResponse,
            RestResponseLimitException => RestFailureKind.LimitExceeded,
            RestContentPreparationException => RestFailureKind.Preparation,
            RestSecurityPolicyException => RestFailureKind.Preparation,
            RestCredentialException => RestFailureKind.Authentication,
            _ => RestFailureKind.Transport
        };
        if (error is RestResponseReadException read && current.Status is null)
        {
            current.Status = read.HttpStatusCode;
        }
        current.Decoded = RestDecodedValue<T>.Absent;
        current.Explanation = error.Message;
        current.Outcome = new(current.Status, kind, error.Code, RetryAfter(current.Headers), current.BodyStarted, replayable,
            error is not RestCredentialException and not RestSecurityPolicyException,
            error is RestTransportException transport ? transport.Kind : kind == RestFailureKind.Transport ? RestTransportFailureKind.Other : null, method)
        {
            SecurityFailure = error is RestCredentialException credential ? credential.Failure
                : securityBound && error is RestTransportException native
                    ? new CIS.Security.SecurityFailure(CIS.Security.SecurityFailureKind.CauseUnknown,
                        "The native transport failure does not establish a precise security cause.",
                        CIS.Security.SecurityFailureSource.Unknown, current.Status, transportCode: native.Kind.ToString())
                    : null
        };
    }
    private static AttemptResult<T> SourceFailure<T>(RestFailureKind kind, bool attempt = false)
    {
        string code = kind == RestFailureKind.Stopping ? "client.stopping" : attempt ? "client.attempt_timeout" : "client.timeout";
        return new(new(null, kind, code))
        {
            Explanation = kind == RestFailureKind.Stopping ? "The REST client is stopping." : "The REST operation exceeded its time budget."
        };
    }
    private static AttemptResult<T> OverrideSource<T>(AttemptResult<T>? current, RestFailureKind kind, bool attempt = false)
    {
        AttemptResult<T> source = SourceFailure<T>(kind, attempt);
        if (current is null)
        {
            return source;
        }
        current.Decoded = RestDecodedValue<T>.Absent;
        current.Explanation = source.Explanation;
        current.RemoteErrorCode = null;
        current.Outcome = current.Outcome with
        {
            HttpStatusCode = current.Status ?? current.Outcome.HttpStatusCode,
            FailureKind = kind,
            ErrorCode = source.Outcome.ErrorCode,
            SecurityFailure = null
        };
        return current;
    }
    private static AttemptResult<T> FromSummary<T>(RestAttemptOutcome summary)
        => new(summary)
        {
            Explanation = summary.FailureKind == RestFailureKind.None ? null : "The REST operation did not complete successfully."
        };
    private static RestResult<T> BuildResult<T>(AttemptResult<T> terminal, RestCallSnapshot call, string client,
        int dispatches, TimeSpan elapsed, string? trace, RestRetryStopReason? retryStop)
    {
        bool successful = terminal.Outcome.FailureKind == RestFailureKind.None;
        var message = successful ? (Message: (string?)null, Truncated: false)
            : BoundMessage(terminal.Explanation, terminal.Outcome.FailureKind, terminal.Outcome.ErrorCode, call.Limits.MaxErrorMessageScalars);
        var context = new RestResultContext(terminal.Outcome.HttpStatusCode, terminal.Headers, successful && terminal.Decoded.HasValue,
            terminal.Outcome.FailureKind, terminal.RemoteErrorCode, terminal.ErrorBody, terminal.ErrorBodyTruncated,
            message.Truncated, client, call.OperationName, dispatches, elapsed, retryStop, trace);
        if (!successful && terminal.Outcome.SecurityFailure is { } securityFailure)
        {
            context = context.WithSecurityFailure(securityFailure);
        }
        if (!successful)
        {
            return RestResult<T>.Failed(terminal.Outcome.ErrorCode ?? "client.preparation_failed", message.Message!, context);
        }
        return terminal.Decoded.HasValue ? RestResult<T>.FromValue(terminal.Decoded.Value!, context) : RestResult<T>.Empty(context);
    }
    private static (string? Message, bool Truncated) BoundMessage(string? message, RestFailureKind kind, string? code, int maximum)
    {
        var failure = new RestDeclaredFailure(kind, code ?? "client.preparation_failed",
            string.IsNullOrWhiteSpace(message) ? "The REST operation did not complete successfully." : message);
        (string bounded, bool truncated) = failure.BoundedMessage(maximum);
        return (bounded, truncated);
    }
    private static RestOperationOutcome MetricOutcome(RestFailureKind kind) => kind switch
    {
        RestFailureKind.None => RestOperationOutcome.Success,
        RestFailureKind.Http => RestOperationOutcome.HttpError,
        RestFailureKind.Business => RestOperationOutcome.BusinessError,
        RestFailureKind.Transport => RestOperationOutcome.TransportError,
        RestFailureKind.Timeout => RestOperationOutcome.Timeout,
        RestFailureKind.Authentication => RestOperationOutcome.AuthenticationError,
        RestFailureKind.Preparation => RestOperationOutcome.PreparationError,
        RestFailureKind.InvalidResponse => RestOperationOutcome.InvalidResponse,
        RestFailureKind.LimitExceeded => RestOperationOutcome.LimitExceeded,
        RestFailureKind.Stopping => RestOperationOutcome.Stopping,
        _ => RestOperationOutcome.UnexpectedError
    };

    private static ReplayIdentity CaptureReplay(RestRequestContext request, RestClientSnapshot client)
    {
        string[] credentials = ["Authorization", "Proxy-Authorization", "Cookie", .. client.CredentialHeaders];
        var ordinary = request.Headers.Snapshot().Where(pair => !credentials.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        RestQueryChange[] removals = client.CredentialQueryParameters.Select(name => new RestQueryChange(RestQueryChangeKind.Remove, name, null)).ToArray();
        string destination = RestUri.Resolve(null, request.Url, removals).AbsoluteUri;
        return new(request.Method, destination, ordinary, request.Body);
    }
    private static bool SameRequest(ReplayIdentity first, ReplayIdentity later)
        => string.Equals(first.Method, later.Method, StringComparison.Ordinal) && string.Equals(first.Url, later.Url, StringComparison.Ordinal)
            && first.Headers.Count == later.Headers.Count && first.Headers.All(pair => later.Headers.TryGetValue(pair.Key, out var values)
                && pair.Value.SequenceEqual(values, StringComparer.Ordinal));
    private static bool SameBody(RestBody? first, RestBody? later)
    {
        if (ReferenceEquals(first, later))
        {
            return true;
        }
        if (first is null || later is null || first.MediaType != later.MediaType)
        {
            return false;
        }
        return (first, later) switch
        {
            (RestHttpBody.StreamBody { Open: not null } left, RestHttpBody.StreamBody { Open: not null } right)
                => Delegate.Equals(left.Open, right.Open),
            (RestHttpBody.NativeBody left, RestHttpBody.NativeBody right)
                => left.IsReplayable && right.IsReplayable && Delegate.Equals(left.Create, right.Create),
            _ => false
        };
    }
    private sealed record ReplayIdentity(string Method, string Url, IReadOnlyDictionary<string, IReadOnlyList<string>> Headers, RestBody? Body);
    private sealed class ReplayRejected(bool bodyChanged) : Exception
    {
        internal bool BodyChanged { get; } = bodyChanged;
    }
    private sealed class AttemptRejected(RestDeclaredFailure failure, bool retryEligible = true) : Exception
    {
        internal RestDeclaredFailure Failure { get; } = failure; internal bool RetryEligible { get; } = retryEligible;
    }
    private sealed class AttemptResult<T>(RestAttemptOutcome outcome)
    {
        internal RestAttemptOutcome Outcome = outcome;
        internal RestDecodedValue<T> Decoded;
        internal int? Status;
        internal IReadOnlyDictionary<string, IReadOnlyList<string>> Headers = new Dictionary<string, IReadOnlyList<string>>();
        internal RestErrorBody? ErrorBody;
        internal bool ErrorBodyTruncated, BodyStarted;
        internal string? Explanation, RemoteErrorCode;
    }
}
