using CIS.Messaging;

namespace CIS.CommonTesting;

/// <summary>The explicit public messaging operation selected by one script.</summary>
public enum ScriptedMessagingCallKind
{
    /// <summary>A logical event submission.</summary>
    Publish,
    /// <summary>A logical command submission.</summary>
    Send,
    /// <summary>A complete typed request result.</summary>
    Request,
    /// <summary>A typed wait on a caller-owned ticket.</summary>
    WaitForReply
}

/// <summary>Bounded invocation metadata, without input/result payloads, transaction owners or ticket references.</summary>
public sealed record ScriptedMessagingCall
{
    internal ScriptedMessagingCall(ScriptedMessagingCallKind kind, string alias, Type? requestType,
        Type? responseType, Guid? requestAttemptId, CancellationToken cancellationToken)
    {
        Kind = kind;
        Alias = alias;
        RequestType = requestType;
        ResponseType = responseType;
        RequestAttemptId = requestAttemptId;
        CancellationToken = cancellationToken;
    }

    /// <summary>Gets the selected logical method.</summary>
    public ScriptedMessagingCallKind Kind
    {
        get;
    }
    /// <summary>Gets the bounded route or request alias.</summary>
    public string Alias
    {
        get;
    }
    /// <summary>Gets the actual input type, or null for an independent wait.</summary>
    public Type? RequestType
    {
        get;
    }
    /// <summary>Gets the typed response type, or null for an ordinary submission.</summary>
    public Type? ResponseType
    {
        get;
    }
    /// <summary>Gets the caller ticket's attempt identity for a wait, without retaining that ticket.</summary>
    public Guid? RequestAttemptId
    {
        get;
    }
    /// <summary>Gets the exact supplied cooperative token; no cancellation source is created.</summary>
    public CancellationToken CancellationToken
    {
        get;
    }
}

/// <summary>A public test-owned ticket with explicit idempotent disposal and no native waiter capacity.</summary>
public sealed class ScriptedMessageRequestTicket<TResponse>(MessageRequestReference reference) : MessageRequestTicket<TResponse>(reference)
{
    private int disposed;

    /// <summary>Gets whether the test owner explicitly disposed this ticket.</summary>
    public bool DisposeRequested => Volatile.Read(ref disposed) != 0;

    /// <summary>Marks disposal once; no transaction, durable request or remote execution is changed.</summary>
    public override ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref disposed, 1);
        return ValueTask.CompletedTask;
    }
}

/// <summary>Supplies finite ordered logical messaging results through public Core contracts.</summary>
/// <remarks>No codec, broker, authorization, handler, transaction completion or durable recovery is simulated.
/// Queued typed values remain test-owned references until consumed. Wait calls never dispose caller-owned tickets.</remarks>
public sealed class ScriptedMessagingClient : IMessagingClient
{
    private readonly object gate = new();
    private readonly Queue<Step> scripts = new();
    private readonly List<ScriptedMessagingCall> calls = [];
    private readonly int scriptCapacity;
    private readonly int recordingCapacity;

    /// <summary>Bounds queued scripts and retained metadata separately, initially sixteen each.</summary>
    public ScriptedMessagingClient(int scriptCapacity = 16, int recordingCapacity = 16)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(scriptCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scriptCapacity, 65_536);
        ArgumentOutOfRangeException.ThrowIfLessThan(recordingCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(recordingCapacity, 65_536);
        this.scriptCapacity = scriptCapacity;
        this.recordingCapacity = recordingCapacity;
    }

    /// <summary>Gets a detached non-evicting snapshot of admitted metadata.</summary>
    public IReadOnlyList<ScriptedMessagingCall> Calls
    {
        get
        {
            lock (gate)
            {
                return Array.AsReadOnly(calls.ToArray());
            }
        }
    }

    /// <summary>Gets the remaining ordered script count.</summary>
    public int PendingScripts
    {
        get
        {
            lock (gate)
            {
                return scripts.Count;
            }
        }
    }

    /// <summary>Queues an exact logical event submission without creating a transaction.</summary>
    public void EnqueuePublish<T>(MessageSubmission result) => AddResult(ScriptedMessagingCallKind.Publish, typeof(T), null, result);
    /// <summary>Queues an exact logical command submission without broker dispatch.</summary>
    public void EnqueueSend<T>(MessageSubmission result) => AddResult(ScriptedMessagingCallKind.Send, typeof(T), null, result);
    /// <summary>Queues one exact typed request result, preserving all independent public facts.</summary>
    public void EnqueueRequest<TRequest, TResponse>(MessageRequestResult<TResponse> result)
        => AddResult(ScriptedMessagingCallKind.Request, typeof(TRequest), typeof(TResponse), result);
    /// <summary>Queues one exact typed wait result without confirming a commit or taking ticket ownership.</summary>
    public void EnqueueWaitForReply<TResponse>(MessageRequestResult<TResponse> result)
        => AddResult(ScriptedMessagingCallKind.WaitForReply, null, typeof(TResponse), result);
    /// <summary>Queues an original event-submission fault; scripted cancellation remains faulted.</summary>
    public void EnqueuePublishFailure<T>(Exception exception) => AddFault(ScriptedMessagingCallKind.Publish, typeof(T), null, exception);
    /// <summary>Queues an original command-submission fault.</summary>
    public void EnqueueSendFailure<T>(Exception exception) => AddFault(ScriptedMessagingCallKind.Send, typeof(T), null, exception);
    /// <summary>Queues an original typed request fault without converting it to a logical result.</summary>
    public void EnqueueRequestFailure<TRequest, TResponse>(Exception exception)
        => AddFault(ScriptedMessagingCallKind.Request, typeof(TRequest), typeof(TResponse), exception);
    /// <summary>Queues an original typed wait fault without disposing the supplied ticket.</summary>
    public void EnqueueWaitForReplyFailure<TResponse>(Exception exception)
        => AddFault(ScriptedMessagingCallKind.WaitForReply, null, typeof(TResponse), exception);

    /// <inheritdoc />
    public Task<MessageSubmission> PublishAsync<T>(string routeAlias, T value, MessageSendOptions? options = null,
        CancellationToken cancellationToken = default)
        => Take<MessageSubmission>(new(ScriptedMessagingCallKind.Publish, Alias(routeAlias), typeof(T), null, null, cancellationToken));

    /// <inheritdoc />
    public Task<MessageSubmission> SendAsync<T>(string routeAlias, T value, MessageSendOptions? options = null,
        CancellationToken cancellationToken = default)
        => Take<MessageSubmission>(new(ScriptedMessagingCallKind.Send, Alias(routeAlias), typeof(T), null, null, cancellationToken));

    /// <inheritdoc />
    public Task<MessageRequestResult<TResponse>> RequestAsync<TRequest, TResponse>(MessageRequest<TRequest> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Take<MessageRequestResult<TResponse>>(new(ScriptedMessagingCallKind.Request, request.RequestAlias,
            typeof(TRequest), typeof(TResponse), null, cancellationToken));
    }

    /// <inheritdoc />
    public Task<MessageRequestResult<TResponse>> WaitForReplyAsync<TResponse>(MessageRequestTicket<TResponse> ticket,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return Take<MessageRequestResult<TResponse>>(new(ScriptedMessagingCallKind.WaitForReply, ticket.Reference.RequestAlias,
            null, typeof(TResponse), ticket.Reference.RequestAttemptId, cancellationToken));
    }

    private void AddResult(ScriptedMessagingCallKind kind, Type? requestType, Type? responseType, object result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Add(new(kind, requestType, responseType, result, null));
    }

    private void AddFault(ScriptedMessagingCallKind kind, Type? requestType, Type? responseType, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Add(new(kind, requestType, responseType, null, exception));
    }

    private void Add(Step step)
    {
        lock (gate)
        {
            if (scripts.Count == scriptCapacity)
            {
                throw new InvalidOperationException("The configured messaging script capacity has been reached.");
            }
            scripts.Enqueue(step);
        }
    }

    private Task<T> Take<T>(ScriptedMessagingCall call)
    {
        CancellationToken token = call.CancellationToken;
        if (token.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(token);
        }
        lock (gate)
        {
            if (token.IsCancellationRequested)
            {
                return Task.FromCanceled<T>(token);
            }
            if (calls.Count == recordingCapacity)
            {
                return Task.FromException<T>(new InvalidOperationException("The configured messaging recording capacity has been reached."));
            }
            if (!scripts.TryPeek(out Step? step))
            {
                return Task.FromException<T>(new InvalidOperationException("No messaging result is scripted for this call."));
            }
            if (step.Kind != call.Kind || step.RequestType != call.RequestType || step.ResponseType != call.ResponseType)
            {
                return Task.FromException<T>(new InvalidOperationException("The scripted messaging kind and generic types must match the invocation."));
            }
            scripts.Dequeue();
            calls.Add(call);
            return step.Exception is { } error ? Task.FromException<T>(error) : Task.FromResult((T)step.Result!);
        }
    }

    private static string Alias(string alias)
    {
        ArgumentNullException.ThrowIfNull(alias);
        if (alias.Length is < 1 or > 64 || !char.IsAsciiLetterOrDigit(alias[0])
            || alias.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '_' and not '-'))
        {
            throw new ArgumentException("Supply a bounded messaging alias.", nameof(alias));
        }
        return alias;
    }

    private sealed record Step(ScriptedMessagingCallKind Kind, Type? RequestType, Type? ResponseType, object? Result, Exception? Exception);
}
