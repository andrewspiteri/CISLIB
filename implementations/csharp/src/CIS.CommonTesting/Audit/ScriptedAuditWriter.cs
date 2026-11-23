using CIS.Audit;
using CIS.Messaging;
using CIS.Transactions;

namespace CIS.CommonTesting;

/// <summary>The selected public writer operation, without a persistence claim.</summary>
public enum ScriptedAuditCallKind
{
    /// <summary>Original-context staging.</summary>
    Stage,
    /// <summary>Independent submission.</summary>
    Submit
}

/// <summary>Safe immutable call metadata; payloads, caller facts and transaction handles are not retained.</summary>
public sealed record ScriptedAuditCall(ScriptedAuditCallKind Kind, Guid AuditId, string? DatabaseName);

/// <summary>A bounded ordered script for the public Audit writer port.</summary>
/// <remarks>Results prove no SQL atomicity, native authentication, broker delivery or receiver persistence.
/// The helper does not encode records, own a transaction or start background work.</remarks>
public sealed class ScriptedAuditWriter : IAuditWriter
{
    private readonly object gate = new();
    private readonly Queue<Step> scripts = new();
    private readonly List<ScriptedAuditCall> calls = [];
    private readonly int scriptCapacity;
    private readonly int recordingCapacity;

    /// <summary>Bounds queued scripts and retained metadata independently.</summary>
    public ScriptedAuditWriter(int scriptCapacity = 16, int recordingCapacity = 16)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(scriptCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scriptCapacity, 65_536);
        ArgumentOutOfRangeException.ThrowIfLessThan(recordingCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(recordingCapacity, 65_536);

        this.scriptCapacity = scriptCapacity;
        this.recordingCapacity = recordingCapacity;
    }

    /// <summary>Gets a detached finite snapshot without retaining the record or its original context.</summary>
    public IReadOnlyList<ScriptedAuditCall> Calls
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

    /// <summary>Queues exact local staging facts; no owner is completed.</summary>
    public void EnqueueStage(MessageStageResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Add(new(ScriptedAuditCallKind.Stage, result, null));
    }

    /// <summary>Queues actual three-state submission facts without executing SQL or a broker.</summary>
    public void EnqueueSubmit(MessageSubmission result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Outcome == MessageSubmissionOutcome.NotConfirmed)
        {
            throw new ArgumentException("A provisional request ticket is not an Audit submission.", nameof(result));
        }
        Add(new(ScriptedAuditCallKind.Submit, result, null));
    }

    /// <summary>Queues an original staging fault; scripted OCE remains a faulted task.</summary>
    public void EnqueueStageFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Add(new(ScriptedAuditCallKind.Stage, null, exception));
    }

    /// <summary>Queues an original submission fault; scripted OCE remains a faulted task.</summary>
    public void EnqueueSubmitFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Add(new(ScriptedAuditCallKind.Submit, null, exception));
    }

    /// <inheritdoc />
    public Task<AuditStageResult> StageAsync(ILocalTransactionContext context, CapturedAuditRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(record);
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<AuditStageResult>(cancellationToken);
        }

        return Take(new(ScriptedAuditCallKind.Stage, record.Record.AuditId, DatabaseAlias(context.DatabaseName)),
            value => new AuditStageResult(record.Record.AuditId, (MessageStageResult)value), cancellationToken);
    }

    /// <inheritdoc />
    public Task<AuditSubmissionResult> SubmitAsync(CapturedAuditRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        return Take(new(ScriptedAuditCallKind.Submit, record.Record.AuditId, null),
            value => new AuditSubmissionResult(record.Record.AuditId, (MessageSubmission)value), cancellationToken);
    }

    private void Add(Step step)
    {
        lock (gate)
        {
            if (scripts.Count == scriptCapacity)
            {
                throw new InvalidOperationException("The configured Audit script capacity has been reached.");
            }
            scripts.Enqueue(step);
        }
    }

    private static string DatabaseAlias(string value)
    {
        if (value is null || value.Length is < 1 or > 64 || !AlphaNumeric(value[0])
            || value.Any(character => !AlphaNumeric(character) && character is not '_' and not '.' and not '-'))
        {
            throw new ArgumentException("Use a bounded portable transaction database alias.", nameof(value));
        }

        return value;
    }

    private static bool AlphaNumeric(char value) => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';

    private Task<T> Take<T>(ScriptedAuditCall call, Func<object, T> result, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }
        lock (gate)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<T>(cancellationToken);
            }
            if (calls.Count == recordingCapacity)
            {
                return Task.FromException<T>(new InvalidOperationException("The configured Audit recording capacity has been reached."));
            }
            if (!scripts.TryPeek(out Step? step) || step.Kind != call.Kind)
            {
                return Task.FromException<T>(new InvalidOperationException("No matching Audit operation is scripted for this call."));
            }
            scripts.Dequeue();
            calls.Add(call);

            return step.Exception is { } fault ? Task.FromException<T>(fault) : Task.FromResult(result(step.Result!));
        }
    }

    private sealed record Step(ScriptedAuditCallKind Kind, object? Result, Exception? Exception);
}
