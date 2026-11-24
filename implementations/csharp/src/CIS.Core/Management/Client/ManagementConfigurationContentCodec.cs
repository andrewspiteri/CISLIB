using CIS.Management.Wire;
using CIS.RestClient;
using CIS.RestClient.Http;

namespace CIS.Management;

// A per-response owned carrier. Target-aware decode belongs to the per-call
// mapper within the same native deadline, never shared mutable codec state.
internal sealed record ManagementConfigurationSnapshotBody(ReadOnlyMemory<byte> Bytes);

/// <summary>Configuration-specific encoding/decoding inside the native REST deadline and body owner.</summary>
public sealed class ManagementConfigurationContentCodec : IRestContentCodec
{
    private readonly ManagementConfigurationWireLimits limits;

    /// <summary>Captures finite configuration bounds without I/O.</summary>
    public ManagementConfigurationContentCodec(ManagementConfigurationWireLimits? limits = null)
        => this.limits = limits ?? new();

    internal ManagementConfigurationWireLimits Limits => limits;

    /// <inheritdoc />
    public bool CanDecode(Type resultType, RestResponseFormat format)
        => format == RestResponseFormat.Json && (resultType == typeof(ManagementConfigurationSnapshotBody)
            || resultType == typeof(ManagementConfigurationCatalog) || resultType == typeof(ManagementConfigurationSource)
            || resultType == typeof(ManagementConfigurationPublication) || resultType == typeof(ManagementConfigurationRelease)
            || resultType == typeof(ManagementConfigurationAssignmentMap) || resultType == typeof(ManagementConfigurationValidationResult)
            || resultType == typeof(ManagementConfigurationPreview) || resultType == typeof(ManagementConfigurationOperationResult)
            || resultType == typeof(ManagementPage<ManagementConfigurationDefinition>)
            || resultType == typeof(ManagementPage<ManagementConfigurationAdoptionEntry>) || resultType == typeof(ManagementReportReceipt));

    /// <inheritdoc />
    public async ValueTask EncodeAsync(RestBody body, Stream destination, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (body.Kind != RestBodyKind.Json)
        {
            throw new ArgumentException("Select one typed configuration JSON command.", nameof(body));
        }
        byte[] bytes = body.Value switch
        {
            ManagementConfigurationCatalogCommand command => ManagementConfigurationWire.EncodeRequest(command, limits),
            ManagementConfigurationPublicationCommand command => ManagementConfigurationWire.EncodeRequest(command, limits),
            ManagementConfigurationReleaseCommand command => ManagementConfigurationWire.EncodeRequest(command, limits),
            ManagementConfigurationAssignmentCommand command => ManagementConfigurationWire.EncodeRequest(command, limits),
            ManagementConfigurationValidationCommand command => ManagementConfigurationWire.EncodeRequest(command, limits),
            ManagementConfigurationPreviewCommand command => ManagementConfigurationWire.EncodeRequest(command, limits),
            ManagementConfigurationStatusReport command => ManagementConfigurationWire.EncodeRequest(command, limits),
            _ => throw new ArgumentException("Select one declared configuration command.", nameof(body))
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
            throw new ArgumentException("Select a declared configuration response and JSON mode.", nameof(format));
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
        if (typeof(T) == typeof(ManagementConfigurationSnapshotBody))
        {
            return RestDecodedValue<T>.Present((T)(object)new ManagementConfigurationSnapshotBody(json));
        }
        object? value = typeof(T) == typeof(ManagementConfigurationCatalog) ? ManagementConfigurationWire.DecodeResponse<ManagementConfigurationCatalog>(json, limits)
            : typeof(T) == typeof(ManagementConfigurationSource) ? ManagementConfigurationWire.DecodeResponse<ManagementConfigurationSource>(json, limits)
            : typeof(T) == typeof(ManagementConfigurationPublication) ? ManagementConfigurationWire.DecodeResponse<ManagementConfigurationPublication>(json, limits)
            : typeof(T) == typeof(ManagementConfigurationRelease) ? ManagementConfigurationWire.DecodeResponse<ManagementConfigurationRelease>(json, limits)
            : typeof(T) == typeof(ManagementConfigurationAssignmentMap) ? ManagementConfigurationWire.DecodeResponse<ManagementConfigurationAssignmentMap>(json, limits)
            : typeof(T) == typeof(ManagementConfigurationValidationResult) ? ManagementConfigurationWire.DecodeResponse<ManagementConfigurationValidationResult>(json, limits)
            : typeof(T) == typeof(ManagementConfigurationPreview) ? ManagementConfigurationWire.DecodeResponse<ManagementConfigurationPreview>(json, limits)
            : typeof(T) == typeof(ManagementConfigurationOperationResult) ? ManagementConfigurationWire.DecodeResponse<ManagementConfigurationOperationResult>(json, limits)
            : typeof(T) == typeof(ManagementPage<ManagementConfigurationDefinition>) ? ManagementConfigurationWire.DecodeResponse<ManagementPage<ManagementConfigurationDefinition>>(json, limits)
            : typeof(T) == typeof(ManagementPage<ManagementConfigurationAdoptionEntry>) ? ManagementConfigurationWire.DecodeResponse<ManagementPage<ManagementConfigurationAdoptionEntry>>(json, limits)
            : ManagementWire.DecodeResponse<ManagementReportReceipt>(json);
        cancellationToken.ThrowIfCancellationRequested();
        return value is null ? RestDecodedValue<T>.Absent : RestDecodedValue<T>.Present((T)value);
    }
}
