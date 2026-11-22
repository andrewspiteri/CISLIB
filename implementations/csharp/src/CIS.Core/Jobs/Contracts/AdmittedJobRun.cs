using CIS.Messaging;

namespace CIS.Jobs;

/// <summary>Only capabilities actually implemented by a trusted occurrence source.</summary>
[Flags]
public enum JobRunSourceCapabilities
{
    /// <summary>No execution capability.</summary>
    None = 0,
    /// <summary>Validates and delivers already-admitted occurrences; this does not imply calendar evaluation.</summary>
    ExternallyAdmittedOccurrences = 1
}

/// <summary>Immutable scheduled input; constructing it does not establish trusted admission.</summary>
public sealed class AdmittedJobRun
{
    /// <summary>Copies occurrence identity, immutable settings and existing owned Command framing.</summary>
    public AdmittedJobRun(string executionScope, string jobId, string resourceId, string occurrenceId,
        MessageOperationReference operation, long admissionRevision, JobExecutionSettings settings, EncodedMessage input,
        string sourceId, string admissionReference, DateTimeOffset? scheduledAtUtc = null)
    {
        ExecutionScope = JobText.Text(executionScope, 256, nameof(executionScope));
        JobId = JobText.Text(jobId, 128, nameof(jobId));
        ResourceId = JobText.Text(resourceId, 512, nameof(resourceId));
        OccurrenceId = JobText.Text(occurrenceId, 512, nameof(occurrenceId));
        SourceId = JobText.Text(sourceId, 128, nameof(sourceId));
        AdmissionReference = JobText.Text(admissionReference, 512, nameof(admissionReference));
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfLessThan(admissionRevision, 1);
        if (settings.Mode != JobExecutionMode.Scheduled || input.Envelope.Kind != MessageKind.Command
            || input.Envelope.Operation != operation)
        {
            throw new ArgumentException("Scheduled input requires a Command with the same original operation reference and Scheduled settings.");
        }

        Operation = operation;
        AdmissionRevision = admissionRevision;
        Settings = settings;
        Input = input;
        ScheduledAtUtc = scheduledAtUtc?.ToUniversalTime();
    }

    /// <summary>Gets the registered responsible scope.</summary>
    public string ExecutionScope
    {
        get;
    }
    /// <summary>Gets the registered stable job identifier.</summary>
    public string JobId
    {
        get;
    }
    /// <summary>Gets the stable shared job/resource key component.</summary>
    public string ResourceId
    {
        get;
    }
    /// <summary>Gets the stable occurrence identity retained on redelivery.</summary>
    public string OccurrenceId
    {
        get;
    }
    /// <summary>Gets the unchanged original RM-03 request identity, independent of grant and generation.</summary>
    public MessageOperationReference Operation
    {
        get;
    }
    /// <summary>Gets the authoritative source's admitted settings revision, distinct from local convergence.</summary>
    public long AdmissionRevision
    {
        get;
    }
    /// <summary>Gets the immutable execution snapshot retained for this admitted occurrence.</summary>
    public JobExecutionSettings Settings
    {
        get;
    }
    /// <summary>Gets existing immutable bounded UTF-8 JSON Command framing; producer metadata is not authentication.</summary>
    public EncodedMessage Input
    {
        get;
    }
    /// <summary>Gets the exact trusted source registration to validate this admission.</summary>
    public string SourceId
    {
        get;
    }
    /// <summary>Gets a bounded provider-owned reference that the selected source must validate.</summary>
    public string AdmissionReference
    {
        get;
    }
    /// <summary>Gets the supplied scheduled instant in UTC, or null for a noncalendar trigger.</summary>
    public DateTimeOffset? ScheduledAtUtc
    {
        get;
    }
}

/// <summary>A trusted source's explicit validation fact, separate from payload/header claims.</summary>
public sealed class JobAdmissionValidation
{
    /// <summary>Copies success or a bounded safe refusal reason.</summary>
    public JobAdmissionValidation(bool admitted, string? reasonCode = null)
    {
        Admitted = admitted;
        ReasonCode = reasonCode is null ? null : JobText.Text(reasonCode, 128, nameof(reasonCode));
    }

    /// <summary>Gets whether the configured source validated this exact immutable admission.</summary>
    public bool Admitted
    {
        get;
    }
    /// <summary>Gets an optional safe diagnostic code.</summary>
    public string? ReasonCode
    {
        get;
    }
}

/// <summary>Trusted scheduler/trigger admission boundary; it owns durable occurrences and their acknowledgement policy.</summary>
public interface IJobRunSource
{
    /// <summary>Gets the exact bounded registration identity matched by AdmittedJobRun.SourceId.</summary>
    string SourceId
    {
        get;
    }
    /// <summary>Gets declared execution capabilities; external delivery alone does not advertise recurrence.</summary>
    JobRunSourceCapabilities Capabilities
    {
        get;
    }
    /// <summary>Validates the exact revision, snapshot, target and provider-owned admission reference.</summary>
    ValueTask<JobAdmissionValidation> ValidateAsync(AdmittedJobRun run, CancellationToken cancellationToken = default);
}

/// <summary>Separate recurring evaluator capability; RM-05 supplies no implementation or due-time engine.</summary>
public interface IJobScheduleEvaluator
{
    /// <summary>Reports support for this complete form, time zone, DST and missed-run policy before AutomaticRecurrence activation.</summary>
    bool CanEvaluate(JobScheduleDescriptor schedule);
}

/// <summary>Dispatches one trusted scheduled occurrence without taking ownership of its authoritative pending record.</summary>
public interface IScheduledJobDispatcher
{
    /// <summary>Returns Deferred under local disable/pressure and establishes actual completion before reporting it.</summary>
    Task<ScheduledJobResult> DispatchAsync(AdmittedJobRun run, CancellationToken cancellationToken = default);
}
