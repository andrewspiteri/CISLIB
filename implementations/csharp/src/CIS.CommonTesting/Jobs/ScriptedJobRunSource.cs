using System.Text;
using CIS.Jobs;
using CIS.Messaging;

namespace CIS.CommonTesting;

/// <summary>Detached bounded admission metadata, excluding encoded frames and payloads.</summary>
public sealed record ScriptedJobRunCall
{
    internal ScriptedJobRunCall(AdmittedJobRun run)
    {
        ExecutionScope = run.ExecutionScope;
        JobId = run.JobId;
        ResourceId = run.ResourceId;
        OccurrenceId = run.OccurrenceId;
        Operation = run.Operation;
        AdmissionRevision = run.AdmissionRevision;
        Settings = run.Settings;
        SourceId = run.SourceId;
        AdmissionReference = run.AdmissionReference;
        ScheduledAtUtc = run.ScheduledAtUtc;
        InputDigest = run.Input.Digest;
        InputLength = run.Input.Length;
    }

    /// <summary>Gets exact requested execution scope.</summary>
    public string ExecutionScope
    {
        get;
    }
    /// <summary>Gets exact stable job identity.</summary>
    public string JobId
    {
        get;
    }
    /// <summary>Gets the stable shared resource.</summary>
    public string ResourceId
    {
        get;
    }
    /// <summary>Gets the original occurrence identity, including on redelivery.</summary>
    public string OccurrenceId
    {
        get;
    }
    /// <summary>Gets the unchanged original operation reference.</summary>
    public MessageOperationReference Operation
    {
        get;
    }
    /// <summary>Gets source admission revision, independent of local configuration revision.</summary>
    public long AdmissionRevision
    {
        get;
    }
    /// <summary>Gets the exact immutable admitted settings reference.</summary>
    public JobExecutionSettings Settings
    {
        get;
    }
    /// <summary>Gets exact selected source identity.</summary>
    public string SourceId
    {
        get;
    }
    /// <summary>Gets the provider-owned test admission reference.</summary>
    public string AdmissionReference
    {
        get;
    }
    /// <summary>Gets optional supplied occurrence time; no calendar calculation occurs.</summary>
    public DateTimeOffset? ScheduledAtUtc
    {
        get;
    }
    /// <summary>Gets the immutable transport digest without retaining its input frame.</summary>
    public MessageDigest InputDigest
    {
        get;
    }
    /// <summary>Gets recorded encoded length without retaining payload bytes.</summary>
    public int InputLength
    {
        get;
    }
}

/// <summary>Supplies bounded ordered exact-admission validation facts for public consumer tests.</summary>
/// <remarks>This helper does no authentication, authorization, SQL, durable admission, acknowledgement or recurrence.
/// Expected calls retain metadata, immutable settings and a digest; they never retain an EncodedMessage or payload frame.</remarks>
public sealed class ScriptedJobRunSource : IJobRunSource
{
    private readonly object gate = new();
    private readonly Queue<Step> scripts = new();
    private readonly List<ScriptedJobRunCall> calls = [];
    private readonly int scriptCapacity;
    private readonly int recordingCapacity;

    /// <summary>Sets the source identity and independently bounds scripts and recorded calls, initially sixteen each.</summary>
    public ScriptedJobRunSource(string sourceId, int scriptCapacity = 16, int recordingCapacity = 16)
    {
        if (string.IsNullOrWhiteSpace(sourceId) || sourceId.Any(char.IsControl)
            || new UTF8Encoding(false, true).GetByteCount(sourceId) > 128)
        {
            throw new ArgumentException("Supply a valid nonempty source identity within 128 UTF-8 bytes.", nameof(sourceId));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(scriptCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(scriptCapacity, 65_536);
        ArgumentOutOfRangeException.ThrowIfLessThan(recordingCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(recordingCapacity, 65_536);
        SourceId = sourceId;
        this.scriptCapacity = scriptCapacity;
        this.recordingCapacity = recordingCapacity;
    }

    /// <inheritdoc />
    public string SourceId
    {
        get;
    }

    /// <inheritdoc />
    public JobRunSourceCapabilities Capabilities => JobRunSourceCapabilities.ExternallyAdmittedOccurrences;

    /// <summary>Gets detached bounded call metadata, without input frames.</summary>
    public IReadOnlyList<ScriptedJobRunCall> Calls
    {
        get
        {
            lock (gate)
            {
                return Array.AsReadOnly(calls.ToArray());
            }
        }
    }

    /// <summary>Gets the number of ordered validation scripts still queued.</summary>
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

    /// <summary>Queues one exact result for matching metadata, input digest and immutable settings reference.</summary>
    public void EnqueueValidation(AdmittedJobRun expected, JobAdmissionValidation result)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(result);
        Add(new(new(expected), result, null));
    }

    /// <summary>Queues one original fault; pre-cancellation consumes neither a script nor a recording.</summary>
    public void EnqueueFailure(AdmittedJobRun expected, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(exception);
        Add(new(new(expected), null, exception));
    }

    /// <inheritdoc />
    public ValueTask<JobAdmissionValidation> ValidateAsync(AdmittedJobRun run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled<JobAdmissionValidation>(cancellationToken);
        }

        var call = new ScriptedJobRunCall(run);
        lock (gate)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return ValueTask.FromCanceled<JobAdmissionValidation>(cancellationToken);
            }

            if (calls.Count == recordingCapacity)
            {
                return ValueTask.FromException<JobAdmissionValidation>(new InvalidOperationException("The job admission recording capacity has been reached."));
            }

            if (!scripts.TryPeek(out Step? step))
            {
                return ValueTask.FromException<JobAdmissionValidation>(new InvalidOperationException("No job admission validation is scripted."));
            }

            if (run.SourceId != SourceId || step.Expected != call)
            {
                return ValueTask.FromException<JobAdmissionValidation>(new InvalidOperationException("The exact scripted admission metadata, settings reference and input digest must match."));
            }

            scripts.Dequeue();
            calls.Add(call);
            return step.Exception is { } error ? ValueTask.FromException<JobAdmissionValidation>(error)
                : ValueTask.FromResult(step.Result!);
        }
    }

    private void Add(Step step)
    {
        if (step.Expected.SourceId != SourceId)
        {
            throw new ArgumentException("Script only this source's exact admissions.");
        }

        lock (gate)
        {
            if (scripts.Count == scriptCapacity)
            {
                throw new InvalidOperationException("The job admission script capacity has been reached.");
            }

            scripts.Enqueue(step);
        }
    }

    private sealed record Step(ScriptedJobRunCall Expected, JobAdmissionValidation? Result, Exception? Exception);
}
