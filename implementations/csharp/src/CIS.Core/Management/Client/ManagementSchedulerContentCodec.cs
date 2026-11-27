using CIS.Management.Wire;
using CIS.RestClient;
using CIS.RestClient.Http;

namespace CIS.Management;

/// <summary>Fixed optional Scheduler/catalog DTO codec inside the native REST deadline and content owner.</summary>
public sealed class ManagementSchedulerContentCodec : IRestContentCodec
{
    private readonly ManagementSchedulerWireLimits limits;

    /// <summary>Captures finite immutable limits without I/O.</summary>
    public ManagementSchedulerContentCodec(ManagementSchedulerWireLimits? limits = null)
    {
        this.limits = limits ?? new();
    }

    internal ManagementSchedulerWireLimits Limits => limits;

    /// <inheritdoc />
    public bool CanDecode(Type resultType, RestResponseFormat format)
        => format == RestResponseFormat.Json && (resultType == typeof(ManagementSchedulerTrigger)
            || resultType == typeof(ManagementSchedulerTask) || resultType == typeof(ManagementSchedulerTaskPreview)
            || resultType == typeof(ManagementSchedulerOperationResult) || resultType == typeof(ManagementSchedulerEventReceipt)
            || resultType == typeof(ManagementSchedulerEvent) || resultType == typeof(ManagementSchedulerRunDetail)
            || resultType == typeof(ManagementSchedulerRunCapsule) || resultType == typeof(ManagementSchedulerAttemptAdmission)
            || resultType == typeof(ManagementReportReceipt) || resultType == typeof(ManagementCapabilityDefinition)
            || resultType == typeof(ManagementConfigurationActions)
            || resultType == typeof(ManagementPage<ManagementSchedulerTrigger>)
            || resultType == typeof(ManagementPage<ManagementSchedulerTask>)
            || resultType == typeof(ManagementPage<ManagementSchedulerRun>)
            || resultType == typeof(ManagementPage<ManagementSchedulerAttempt>)
            || resultType == typeof(ManagementPage<ManagementCapabilityDefinition>)
            || resultType == typeof(ManagementPage<ManagementCapabilityParticipation>)
            || resultType == typeof(ManagementPage<ManagementConfigurationCohortChoice>));

    /// <inheritdoc />
    public async ValueTask EncodeAsync(RestBody body, Stream destination, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (body.Kind != RestBodyKind.Json)
        {
            throw new ArgumentException("Select one declared Scheduler JSON command.", nameof(body));
        }
        byte[] bytes = body.Value switch
        {
            ManagementSchedulerTriggerCommand value => ManagementSchedulerWire.EncodeRequest(value, limits),
            ManagementSchedulerTaskCommand value => ManagementSchedulerWire.EncodeRequest(value, limits),
            ManagementSchedulerStateCommand value => ManagementSchedulerWire.EncodeRequest(value, limits),
            ManagementSchedulerTaskPreviewCommand value => ManagementSchedulerWire.EncodeRequest(value, limits),
            ManagementSchedulerCalendarPreviewCommand value => ManagementSchedulerWire.EncodeRequest(value, limits),
            ManagementSchedulerEventCommand value => ManagementSchedulerWire.EncodeRequest(value, limits),
            ManagementSchedulerManualRunCommand value => ManagementSchedulerWire.EncodeRequest(value, limits),
            ManagementSchedulerAttemptCommand value => ManagementSchedulerWire.EncodeRequest(value, limits),
            ManagementSchedulerAttemptReport value => ManagementSchedulerWire.EncodeRequest(value, limits),
            _ => throw new ArgumentException("Select one declared Scheduler command.", nameof(body))
        };
        cancellationToken.ThrowIfCancellationRequested();
        await destination.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<RestDecodedValue<T>> DecodeAsync<T>(Stream source, RestResponseFormat format,
        CancellationToken cancellationToken)
    {
        if (!CanDecode(typeof(T), format))
        {
            throw new ArgumentException("Select one declared Scheduler/catalog response and JSON mode.", nameof(format));
        }
        using var bytes = new MemoryStream();
        byte[] buffer = new byte[8192];
        while (true)
        {
            int count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                break;
            }
            if (count > limits.MaximumBodyBytes - bytes.Length)
            {
                throw new RestResponseLimitException();
            }
            bytes.Write(buffer, 0, count);
        }
        cancellationToken.ThrowIfCancellationRequested();
        ReadOnlyMemory<byte> json = bytes.GetBuffer().AsMemory(0, checked((int)bytes.Length));
        object? value = typeof(T) == typeof(ManagementSchedulerTrigger) ? ManagementSchedulerWire.DecodeResponse<ManagementSchedulerTrigger>(json, limits)
            : typeof(T) == typeof(ManagementSchedulerTask) ? ManagementSchedulerWire.DecodeResponse<ManagementSchedulerTask>(json, limits)
            : typeof(T) == typeof(ManagementSchedulerTaskPreview) ? ManagementSchedulerWire.DecodeResponse<ManagementSchedulerTaskPreview>(json, limits)
            : typeof(T) == typeof(ManagementSchedulerOperationResult) ? ManagementSchedulerWire.DecodeResponse<ManagementSchedulerOperationResult>(json, limits)
            : typeof(T) == typeof(ManagementSchedulerEventReceipt) ? ManagementSchedulerWire.DecodeResponse<ManagementSchedulerEventReceipt>(json, limits)
            : typeof(T) == typeof(ManagementSchedulerEvent) ? ManagementSchedulerWire.DecodeResponse<ManagementSchedulerEvent>(json, limits)
            : typeof(T) == typeof(ManagementSchedulerRunDetail) ? ManagementSchedulerWire.DecodeResponse<ManagementSchedulerRunDetail>(json, limits)
            : typeof(T) == typeof(ManagementSchedulerRunCapsule) ? ManagementSchedulerWire.DecodeResponse<ManagementSchedulerRunCapsule>(json, limits)
            : typeof(T) == typeof(ManagementSchedulerAttemptAdmission) ? ManagementSchedulerWire.DecodeResponse<ManagementSchedulerAttemptAdmission>(json, limits)
            : typeof(T) == typeof(ManagementReportReceipt) ? ManagementSchedulerWire.DecodeResponse<ManagementReportReceipt>(json, limits)
            : typeof(T) == typeof(ManagementCapabilityDefinition) ? ManagementSchedulerWire.DecodeResponse<ManagementCapabilityDefinition>(json, limits)
            : typeof(T) == typeof(ManagementConfigurationActions) ? ManagementSchedulerWire.DecodeResponse<ManagementConfigurationActions>(json, limits)
            : typeof(T) == typeof(ManagementPage<ManagementSchedulerTrigger>) ? ManagementSchedulerWire.DecodeResponse<ManagementPage<ManagementSchedulerTrigger>>(json, limits)
            : typeof(T) == typeof(ManagementPage<ManagementSchedulerTask>) ? ManagementSchedulerWire.DecodeResponse<ManagementPage<ManagementSchedulerTask>>(json, limits)
            : typeof(T) == typeof(ManagementPage<ManagementSchedulerRun>) ? ManagementSchedulerWire.DecodeResponse<ManagementPage<ManagementSchedulerRun>>(json, limits)
            : typeof(T) == typeof(ManagementPage<ManagementSchedulerAttempt>) ? ManagementSchedulerWire.DecodeResponse<ManagementPage<ManagementSchedulerAttempt>>(json, limits)
            : typeof(T) == typeof(ManagementPage<ManagementCapabilityDefinition>) ? ManagementSchedulerWire.DecodeResponse<ManagementPage<ManagementCapabilityDefinition>>(json, limits)
            : typeof(T) == typeof(ManagementPage<ManagementCapabilityParticipation>) ? ManagementSchedulerWire.DecodeResponse<ManagementPage<ManagementCapabilityParticipation>>(json, limits)
            : ManagementSchedulerWire.DecodeResponse<ManagementPage<ManagementConfigurationCohortChoice>>(json, limits);
        cancellationToken.ThrowIfCancellationRequested();
        return value is null ? RestDecodedValue<T>.Absent : RestDecodedValue<T>.Present((T)value);
    }
}
