using CIS.Coordination;

namespace CIS.CommonTesting;

/// <summary>One bounded logical request, with no authorization, provider or application callback retained.</summary>
public sealed class ScriptedCoordinationCall
{
    internal ScriptedCoordinationCall(CoordinationRequest request)
    {
        ExecutionScope = request.ExecutionScope;
        DutyId = request.DutyId;
        ResourceId = request.ResourceId;
    }

    /// <summary>Gets the requested scope, without treating it as trusted authority.</summary>
    public string ExecutionScope
    {
        get;
    }

    /// <summary>Gets the requested stable duty.</summary>
    public string DutyId
    {
        get;
    }

    /// <summary>Gets the requested resource.</summary>
    public string ResourceId
    {
        get;
    }
}

/// <summary>Returns explicitly scripted coordination outcomes through the public consumer port.</summary>
/// <remarks>This helper performs no authentication, authorization, provider access, renewal or exclusivity enforcement.
/// Acquired is a supplied test fact, never evidence of a committed database grant.</remarks>
public sealed class ScriptedCoordinationService : ICoordinationService
{
    private readonly object gate = new();
    private readonly Queue<Step> scripts = new();
    private readonly List<ScriptedCoordinationCall> calls = [];
    private readonly int scriptCapacity;
    private readonly int recordingCapacity;

    /// <summary>Bounds the ordered scripts and retained calls independently, each initially sixteen.</summary>
    public ScriptedCoordinationService(int scriptCapacity = 16, int recordingCapacity = 16)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(scriptCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scriptCapacity, 65_536);
        ArgumentOutOfRangeException.ThrowIfLessThan(recordingCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(recordingCapacity, 65_536);
        this.scriptCapacity = scriptCapacity;
        this.recordingCapacity = recordingCapacity;
    }

    /// <summary>Gets a detached, read-only snapshot containing only bounded request text.</summary>
    public IReadOnlyList<ScriptedCoordinationCall> Calls
    {
        get
        {
            lock (gate)
            {
                return Array.AsReadOnly(calls.ToArray());
            }
        }
    }

    /// <summary>Gets the number of ordered scripts still available.</summary>
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

    /// <summary>Queues the exact result, including any explicitly supplied manual lease.</summary>
    public void Enqueue(ManagedCoordinationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Add(new(result, null));
    }

    /// <summary>Queues an original fault; a scripted cancellation exception remains a faulted task.</summary>
    public void EnqueueFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Add(new(null, exception));
    }

    /// <inheritdoc />
    public Task<ManagedCoordinationResult> AcquireAsync(CoordinationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<ManagedCoordinationResult>(cancellationToken);
        }

        lock (gate)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<ManagedCoordinationResult>(cancellationToken);
            }
            if (calls.Count == recordingCapacity)
            {
                return Task.FromException<ManagedCoordinationResult>(new InvalidOperationException(
                    "The configured coordination recording capacity has been reached."));
            }
            if (!scripts.TryDequeue(out Step? step))
            {
                return Task.FromException<ManagedCoordinationResult>(new InvalidOperationException(
                    "No coordination result is scripted for this call."));
            }

            calls.Add(new(request));
            return step.Exception is { } error
                ? Task.FromException<ManagedCoordinationResult>(error)
                : Task.FromResult(step.Result!);
        }
    }

    private void Add(Step step)
    {
        lock (gate)
        {
            if (scripts.Count == scriptCapacity)
            {
                throw new InvalidOperationException("The configured coordination script capacity has been reached.");
            }

            scripts.Enqueue(step);
        }
    }

    private sealed record Step(ManagedCoordinationResult? Result, Exception? Exception);
}
