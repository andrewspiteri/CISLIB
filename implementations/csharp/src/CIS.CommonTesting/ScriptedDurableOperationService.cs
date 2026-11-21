using CIS.Operations;
using CIS.Transactions;

namespace CIS.CommonTesting;

/// <summary>One admitted logical invocation, without arbitrary input/result payloads, handlers or native handles.</summary>
/// <remarks>The exact supplied context is retained only for execution calls, within the configured recording bound.</remarks>
public sealed class ScriptedOperationCall
{
    internal ScriptedOperationCall(string operationName, string requestedScope, string key,
        ILocalTransactionContext? context, Type? requestType, Type resultType)
    {
        OperationName = operationName;
        RequestedScope = requestedScope;
        Key = key;
        Context = context;
        RequestType = requestType;
        ResultType = resultType;
    }

    /// <summary>Gets the requested operation alias.</summary>
    public string OperationName
    {
        get;
    }
    /// <summary>Gets the bounded requested scope, without treating it as authority.</summary>
    public string RequestedScope
    {
        get;
    }
    /// <summary>Gets the unchanged bounded caller key.</summary>
    public string Key
    {
        get;
    }
    /// <summary>Gets the original execution participant, or null for independent lookup.</summary>
    public ILocalTransactionContext? Context
    {
        get;
    }
    /// <summary>Gets the actual requested input type, or null for lookup.</summary>
    public Type? RequestType
    {
        get;
    }
    /// <summary>Gets the actual requested result type.</summary>
    public Type ResultType
    {
        get;
    }
    /// <summary>Gets whether this invocation is a committed-result lookup.</summary>
    public bool IsLookup => Context is null;
}

/// <summary>Substitutes explicit logical durable-operation outcomes through the public port, without executing a handler.</summary>
/// <remarks>This helper does no authorization, normalization, hashing, journal access, transaction admission or completion.
/// Scripted Prepared/Replayed/Found facts are test inputs and never establish SQL atomicity or durable recovery.</remarks>
public sealed class ScriptedDurableOperationService : IDurableOperationService
{
    private readonly object gate = new();
    private readonly Queue<Step> scripts = new();
    private readonly List<ScriptedOperationCall> calls = [];
    private readonly int scriptCapacity;
    private readonly int recordingCapacity;

    /// <summary>Bounds the combined ordered script queue and retained invocation list independently, each initially sixteen.</summary>
    public ScriptedDurableOperationService(int scriptCapacity = 16, int recordingCapacity = 16)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(scriptCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scriptCapacity, 65_536);
        ArgumentOutOfRangeException.ThrowIfLessThan(recordingCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(recordingCapacity, 65_536);
        this.scriptCapacity = scriptCapacity;
        this.recordingCapacity = recordingCapacity;
    }

    /// <summary>Gets detached bounded invocation metadata; records retain neither arbitrary payloads nor handlers.</summary>
    public IReadOnlyList<ScriptedOperationCall> Calls
    {
        get
        {
            lock (gate)
            {
                return Array.AsReadOnly(calls.ToArray());
            }
        }
    }

    /// <summary>Gets how many ordered scripts remain.</summary>
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

    /// <summary>Queues one exact typed execution result without invoking the supplied application handler.</summary>
    public void EnqueueExecution<TRequest, TResult>(OperationExecution<TResult> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Add(new(typeof(TRequest), typeof(TResult), result, null));
    }

    /// <summary>Queues one exact typed committed-result lookup.</summary>
    public void EnqueueLookup<TResult>(OperationLookup<TResult> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Add(new(null, typeof(TResult), result, null));
    }

    /// <summary>Queues an original execution fault. A scripted OCE remains a faulted task; pre-cancellation consumes no script.</summary>
    public void EnqueueExecutionFailure<TRequest, TResult>(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Add(new(typeof(TRequest), typeof(TResult), null, exception));
    }

    /// <summary>Queues an original lookup fault without creating or completing a transaction owner.</summary>
    public void EnqueueLookupFailure<TResult>(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Add(new(null, typeof(TResult), null, exception));
    }

    /// <inheritdoc />
    public Task<OperationExecution<TResult>> ExecuteAsync<TRequest, TResult>(ILocalTransactionContext context,
        OperationRequest<TRequest> request,
        Func<ILocalTransactionContext, TRequest, CancellationToken, Task<OperationDecision<TResult>>> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(handler);
        return Take<OperationExecution<TResult>>(new(request.OperationName, request.RequestedScope, request.Key,
            context, typeof(TRequest), typeof(TResult)), cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationLookup<TResult>> GetResultAsync<TResult>(OperationLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Take<OperationLookup<TResult>>(new(request.OperationName, request.RequestedScope, request.Key,
            null, null, typeof(TResult)), cancellationToken);
    }

    private void Add(Step step)
    {
        lock (gate)
        {
            if (scripts.Count == scriptCapacity)
            {
                throw new InvalidOperationException("The configured durable-operation script capacity has been reached.");
            }
            scripts.Enqueue(step);
        }
    }

    private Task<T> Take<T>(ScriptedOperationCall call, CancellationToken token)
    {
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
                return Task.FromException<T>(new InvalidOperationException("The configured durable-operation recording capacity has been reached."));
            }
            if (!scripts.TryPeek(out Step? step))
            {
                return Task.FromException<T>(new InvalidOperationException("No durable-operation result is scripted for this call."));
            }
            if (step.RequestType != call.RequestType || step.ResultType != call.ResultType)
            {
                return Task.FromException<T>(new InvalidOperationException("The scripted call kind and generic types must match the invocation."));
            }

            scripts.Dequeue();
            calls.Add(call);
            return step.Exception is { } error ? Task.FromException<T>(error) : Task.FromResult((T)step.Result!);
        }
    }

    private sealed record Step(Type? RequestType, Type ResultType, object? Result, Exception? Exception);
}
