using System.Text.Json;
using CIS.RestClient;

namespace CIS.CommonTesting;

/// <summary>A copied invocation identity; this helper does not run HTTP, retries, codecs or response mappers.</summary>
/// <param name="Method">The supplied method.</param>
/// <param name="Url">The caller's test-owned URL.</param>
/// <param name="ResponseType">The actual requested generic type.</param>
/// <param name="OperationName">The explicit operation name, or request.</param>
public sealed record ScriptedRestCall(string Method, string Url, Type ResponseType, string OperationName);

/// <summary>Controls one explicitly owned pending script operation without a timer.</summary>
public sealed class PendingRestCall
{
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly bool observeCancellation;
    internal PendingRestCall(bool observeCancellation) => this.observeCancellation = observeCancellation;
    /// <summary>Completes after the actual script operation starts.</summary>
    public Task Entered => entered.Task;
    /// <summary>Releases actual work. Canceling another wait does not release it.</summary>
    public void Release() => release.TrySetResult();
    internal async Task WaitAsync(CancellationToken token)
    {
        entered.TrySetResult();
        if (observeCancellation)
        {
            await release.Task.WaitAsync(token).ConfigureAwait(false);
        }
        else
        {
            await release.Task.ConfigureAwait(false);
        }
        token.ThrowIfCancellationRequested();
    }
}

/// <summary>Executes a finite queue of explicit logical REST results. Unconfigured/mismatched calls fault clearly.</summary>
public sealed class ScriptedRestClient : IRestClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object gate = new();
    private readonly Queue<Step> steps = new();
    private readonly BoundedRecords<ScriptedRestCall> calls;
    private readonly int capacity;

    /// <summary>Bounds both queued steps and retained call records.</summary>
    public ScriptedRestClient(int capacity = 256)
    {
        calls = new(capacity);
        this.capacity = capacity;
    }
    /// <summary>Gets a detached bounded call snapshot.</summary>
    public IReadOnlyList<ScriptedRestCall> Calls => calls.Snapshot();
    /// <summary>Gets all admitted calls since Clear, including evicted call records.</summary>
    public long TotalCalls => calls.Total;
    /// <summary>Gets evicted call record count since Clear.</summary>
    public long DroppedCalls => calls.Dropped;
    /// <summary>Clears queued steps and call records; already-started tasks remain owned by their callers.</summary>
    public void Clear()
    {
        lock (gate)
        {
            steps.Clear();
            calls.Clear();
        }
    }
    /// <summary>Queues a guarded typed result for exactly the requested T and method.</summary>
    public void EnqueueResult<T>(RestResult<T> result, string method = "GET")
    {
        ArgumentNullException.ThrowIfNull(result);
        Enqueue(new TypedStep<T>(method, result));
    }
    /// <summary>Queues a successful value by deserializing the actual requested T, including an internal application DTO.</summary>
    /// <remarks>This supplies a logical success; it does not emulate a response mapper or retry policy.</remarks>
    public void EnqueueJsonValue(string json, int statusCode = 200, string method = "GET")
        => Enqueue(new JsonStep(method, json, statusCode));
    /// <summary>Queues an explicitly absent successful value.</summary>
    public void EnqueueEmpty(int statusCode = 204, string method = "HEAD") => Enqueue(new EmptyStep(method, statusCode));
    /// <summary>Queues one expected logical failure, using Core's guarded result factory.</summary>
    public void EnqueueFailure(RestFailureKind kind, string code, string message, int? statusCode = null, string method = "GET")
        => Enqueue(new FailureStep(method, kind, code, message, statusCode));
    /// <summary>Queues the original programming fault, preserving faulted task state even for an unrelated OCE.</summary>
    public void EnqueueException(Exception exception, string method = "GET")
    {
        ArgumentNullException.ThrowIfNull(exception);
        Enqueue(new FaultStep(method, exception));
    }
    /// <summary>Queues held work followed by a JSON value; caller cancellation normally releases its wait.</summary>
    /// <remarks>observeCancellation=false deliberately holds actual work until Release for drain tests.</remarks>
    public PendingRestCall EnqueuePendingJsonValue(string json, int statusCode = 200, string method = "GET", bool observeCancellation = true)
    {
        var control = new PendingRestCall(observeCancellation);
        Enqueue(new PendingStep(method, json, statusCode, control));
        return control;
    }

    private void Enqueue(Step step)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(step.Method);
        lock (gate)
        {
            if (steps.Count == capacity)
            {
                throw new InvalidOperationException("The configured script capacity has been reached.");
            }
            steps.Enqueue(step);
        }
    }

    /// <inheritdoc />
    public Task<RestResult<T>> Get<T>(string url, RestRequestOptions? options = null, CancellationToken cancellationToken = default)
        => Send<T>("GET", url, options, cancellationToken);
    /// <inheritdoc />
    public Task<RestResult<T>> Post<T>(string url, RestRequestOptions? options = null, CancellationToken cancellationToken = default)
        => Send<T>("POST", url, options, cancellationToken);
    /// <inheritdoc />
    public Task<RestResult<T>> Put<T>(string url, RestRequestOptions? options = null, CancellationToken cancellationToken = default)
        => Send<T>("PUT", url, options, cancellationToken);
    /// <inheritdoc />
    public Task<RestResult<T>> Patch<T>(string url, RestRequestOptions? options = null, CancellationToken cancellationToken = default)
        => Send<T>("PATCH", url, options, cancellationToken);
    /// <inheritdoc />
    public Task<RestResult<T>> Delete<T>(string url, RestRequestOptions? options = null, CancellationToken cancellationToken = default)
        => Send<T>("DELETE", url, options, cancellationToken);
    /// <inheritdoc />
    public Task<RestResult<T>> Options<T>(string url, RestRequestOptions? options = null, CancellationToken cancellationToken = default)
        => Send<T>("OPTIONS", url, options, cancellationToken);
    /// <inheritdoc />
    public Task<RestResult<T>> Trace<T>(string url, RestRequestOptions? options = null, CancellationToken cancellationToken = default)
        => Send<T>("TRACE", url, options, cancellationToken);
    /// <inheritdoc />
    public Task<RestResult<RestNoValue>> Head(string url, RestRequestOptions? options = null, CancellationToken cancellationToken = default)
        => Send<RestNoValue>("HEAD", url, options, cancellationToken);
    /// <inheritdoc />
    public Task<RestResult<T>> Send<T>(string method, string url, RestRequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<RestResult<T>>(cancellationToken);
        }
        Step step;
        var call = new ScriptedRestCall(method, url, typeof(T), options?.OperationName ?? "request");
        var completion = new TaskCompletionSource<RestResult<T>>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (gate)
        {
            if (steps.Count == 0)
            {
                return Task.FromException<RestResult<T>>(new InvalidOperationException("No REST result is scripted for this call."));
            }
            step = steps.Dequeue();
            calls.Add(call);
        }
        // The returned operation exists before any script/user prologue runs.
        _ = ExecuteAsync(completion, step, call, cancellationToken);
        return completion.Task;
    }

    private static async Task ExecuteAsync<T>(TaskCompletionSource<RestResult<T>> completion, Step step, ScriptedRestCall call, CancellationToken token)
    {
        try
        {
            if (step.Method != call.Method)
            {
                throw new InvalidOperationException($"Expected method {step.Method}; received {call.Method}.");
            }
            completion.TrySetResult(await step.ExecuteAsync<T>(call, token).ConfigureAwait(false));
        }
        catch (OperationCanceledException error) when (token.IsCancellationRequested && error.CancellationToken == token)
        {
            completion.TrySetCanceled(token);
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
        }
    }

    private static RestResult<T> JsonResult<T>(string json, int status, ScriptedRestCall call)
        => RestResult<T>.FromValue(JsonSerializer.Deserialize<T>(json, Json)!, new RestResultContext(status,
            hasValue: true, clientName: "scripted", operationName: call.OperationName, attemptCount: 0));

    private abstract class Step(string method)
    {
        internal string Method { get; } = method;
        internal abstract Task<RestResult<T>> ExecuteAsync<T>(ScriptedRestCall call, CancellationToken token);
    }
    private sealed class TypedStep<TValue>(string method, RestResult<TValue> result) : Step(method)
    {
        internal override Task<RestResult<T>> ExecuteAsync<T>(ScriptedRestCall call, CancellationToken token)
            => typeof(T) == typeof(TValue) ? Task.FromResult((RestResult<T>)(object)result)
                : Task.FromException<RestResult<T>>(new InvalidOperationException("The scripted value type does not match the requested response type."));
    }
    private sealed class JsonStep(string method, string json, int status) : Step(method)
    {
        internal override Task<RestResult<T>> ExecuteAsync<T>(ScriptedRestCall call, CancellationToken token)
            => Task.FromResult(JsonResult<T>(json, status, call));
    }
    private sealed class EmptyStep(string method, int status) : Step(method)
    {
        internal override Task<RestResult<T>> ExecuteAsync<T>(ScriptedRestCall call, CancellationToken token)
            => Task.FromResult(RestResult<T>.Empty(new RestResultContext(status, clientName: "scripted", operationName: call.OperationName, attemptCount: 0)));
    }
    private sealed class FailureStep(string method, RestFailureKind kind, string code, string message, int? status) : Step(method)
    {
        internal override Task<RestResult<T>> ExecuteAsync<T>(ScriptedRestCall call, CancellationToken token)
            => Task.FromResult(RestResult<T>.Failed(code, message, new RestResultContext(status, failureKind: kind,
                clientName: "scripted", operationName: call.OperationName, attemptCount: 0)));
    }
    private sealed class FaultStep(string method, Exception exception) : Step(method)
    {
        internal override Task<RestResult<T>> ExecuteAsync<T>(ScriptedRestCall call, CancellationToken token)
            => Task.FromException<RestResult<T>>(exception);
    }
    private sealed class PendingStep(string method, string json, int status, PendingRestCall control) : Step(method)
    {
        internal override async Task<RestResult<T>> ExecuteAsync<T>(ScriptedRestCall call, CancellationToken token)
        {
            await control.WaitAsync(token).ConfigureAwait(false);
            return JsonResult<T>(json, status, call);
        }
    }
}
