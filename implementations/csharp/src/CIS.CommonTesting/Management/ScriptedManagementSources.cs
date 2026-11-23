using CIS.Management;

namespace CIS.CommonTesting;

/// <summary>One safe logical snapshot capture record, without catalog, observations, tokens or caller context.</summary>
public sealed record ScriptedManagementCapture(string Kind);

/// <summary>A finite content-only observation script; it runs no monitor, transport or scheduler.</summary>
public sealed class ScriptedManagementObservationSource : IManagementObservationSource
{
    private readonly ManagementSourceScript<ManagementObservationContent> script;

    /// <summary>Bounds queued steps and safe records, initially sixteen, through 65,536.</summary>
    public ScriptedManagementObservationSource(int capacity = 16) => script = new("observation", capacity);
    /// <summary>Gets bounded safe capture metadata only.</summary>
    public IReadOnlyList<ScriptedManagementCapture> Captures => script.Captures;
    /// <summary>Gets the saturating admitted capture count.</summary>
    public long TotalCaptures => script.Total;
    /// <summary>Gets the saturating evicted record count.</summary>
    public long DroppedCaptures => script.Dropped;
    /// <summary>Queues exact selected content, borrowed until capture; no invalid content is silently repaired.</summary>
    public void EnqueueValue(ManagementObservationContent value) => script.Enqueue(value);
    /// <summary>Queues the original fault.</summary>
    public void EnqueueException(Exception error) => script.Enqueue(error);
    /// <summary>Clears queued content and safe records.</summary>
    public void Clear() => script.Clear();
    /// <inheritdoc />
    public ManagementObservationContent CaptureObservation(CancellationToken cancellationToken = default) => script.Capture(cancellationToken);
}

/// <summary>A finite whole-inventory script; it proves no native catalog, handler, authority or persistence behavior.</summary>
public sealed class ScriptedManagementInventorySource : IManagementInventorySource
{
    private readonly ManagementSourceScript<ManagementInventoryContent> script;

    /// <summary>Bounds queued steps and safe records, initially sixteen, through 65,536.</summary>
    public ScriptedManagementInventorySource(int capacity = 16) => script = new("inventory", capacity);
    /// <summary>Gets bounded safe capture metadata only.</summary>
    public IReadOnlyList<ScriptedManagementCapture> Captures => script.Captures;
    /// <summary>Gets the saturating admitted capture count.</summary>
    public long TotalCaptures => script.Total;
    /// <summary>Gets the saturating evicted record count.</summary>
    public long DroppedCaptures => script.Dropped;
    /// <summary>Queues exact selected content, borrowed until capture; no partial inventory is manufactured.</summary>
    public void EnqueueValue(ManagementInventoryContent value) => script.Enqueue(value);
    /// <summary>Queues the original fault or explicit local complete-inventory refusal.</summary>
    public void EnqueueException(Exception error) => script.Enqueue(error);
    /// <summary>Clears queued content and safe records.</summary>
    public void Clear() => script.Clear();
    /// <inheritdoc />
    public ManagementInventoryContent CaptureInventory(CancellationToken cancellationToken = default) => script.Capture(cancellationToken);
}

internal sealed class ManagementSourceScript<T> where T : class
{
    private readonly object gate = new();
    private readonly Queue<object> steps = new();
    private readonly BoundedRecords<ScriptedManagementCapture> captures;
    private readonly string kind;
    private readonly int capacity;

    internal ManagementSourceScript(string kind, int capacity)
    {
        captures = new(capacity);
        this.kind = kind;
        this.capacity = capacity;
    }

    internal IReadOnlyList<ScriptedManagementCapture> Captures => captures.Snapshot();
    internal long Total => captures.Total;
    internal long Dropped => captures.Dropped;

    internal void Enqueue(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (gate)
        {
            if (steps.Count == capacity)
            {
                throw new InvalidOperationException("The finite management content script is full.");
            }
            steps.Enqueue(value);
        }
    }

    internal T Capture(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        object value;
        lock (gate)
        {
            if (!steps.TryDequeue(out value!))
            {
                throw new InvalidOperationException("No management content capture step is queued.");
            }
            captures.Add(new(kind));
        }
        if (value is Exception error)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }
        return (T)value;
    }

    internal void Clear()
    {
        lock (gate)
        {
            steps.Clear();
            captures.Clear();
        }
    }
}
