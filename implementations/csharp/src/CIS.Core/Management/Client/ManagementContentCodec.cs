using CIS.Management.Wire;
using CIS.RestClient;

namespace CIS.Management;

/// <summary>Runs typed management encoding and response validation inside the existing native REST deadline and cleanup owner.</summary>
public sealed class ManagementContentCodec : IRestContentCodec
{
    private readonly ManagementWireLimits limits;

    /// <summary>Captures finite limits; construction performs no work or I/O.</summary>
    public ManagementContentCodec(ManagementWireLimits? limits = null) => this.limits = limits ?? new();

    internal ManagementWireLimits Limits => limits;

    /// <inheritdoc />
    public bool CanDecode(Type resultType, RestResponseFormat format)
        => format == RestResponseFormat.Json && (resultType == typeof(ManagementServiceInfo)
            || resultType == typeof(ManagementRegistrationReceipt) || resultType == typeof(ManagementReportReceipt)
            || resultType == typeof(ManagementControlConfirmation)
            || resultType == typeof(ManagementCloseReceipt) || resultType == typeof(ManagementApplication)
            || resultType == typeof(ManagementPage<ManagementApplication>) || resultType == typeof(ManagementPage<ManagementInstance>)
            || resultType == typeof(ManagementPage<ManagementInventoryEntry>) || resultType == typeof(ManagementCapabilities)
            || resultType == typeof(ManagementMetadataResult));

    /// <inheritdoc />
    public async ValueTask EncodeAsync(RestBody body, Stream destination, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (body.Kind != RestBodyKind.Json)
        {
            throw new ArgumentException("A management request uses one declared JSON DTO.", nameof(body));
        }
        byte[] bytes = body.Value switch
        {
            ManagementRegistrationRequest registration => ManagementWire.EncodeRequest(registration, limits),
            ManagementObservation observation => ManagementWire.EncodeRequest(observation, limits),
            ManagementInventorySnapshot inventory => ManagementWire.EncodeRequest(inventory, limits),
            ManagementMetadataCommand command => ManagementWire.EncodeRequest(command, limits),
            _ => throw new ArgumentException("Select one declared management request DTO.", nameof(body))
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
            throw new ArgumentException("Select a declared management response DTO and JSON mode.", nameof(format));
        }
        int maximum = typeof(T) == typeof(ManagementPage<ManagementInventoryEntry>)
            ? limits.MaximumInventoryResponseBytes : limits.MaximumRegularResponseBytes;
        using var bytes = new MemoryStream();
        byte[] buffer = new byte[8192];
        while (true)
        {
            int count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                break;
            }
            if (count > maximum - bytes.Length)
            {
                return RestDecodedValue<T>.Absent;
            }
            bytes.Write(buffer, 0, count);
        }
        cancellationToken.ThrowIfCancellationRequested();
        ReadOnlyMemory<byte> json = bytes.GetBuffer().AsMemory(0, checked((int)bytes.Length));
        object? value = typeof(T) == typeof(ManagementServiceInfo) ? ManagementWire.DecodeResponse<ManagementServiceInfo>(json, limits)
            : typeof(T) == typeof(ManagementRegistrationReceipt) ? ManagementWire.DecodeResponse<ManagementRegistrationReceipt>(json, limits)
            : typeof(T) == typeof(ManagementControlConfirmation) ? ManagementWire.DecodeResponse<ManagementControlConfirmation>(json, limits)
            : typeof(T) == typeof(ManagementReportReceipt) ? ManagementWire.DecodeResponse<ManagementReportReceipt>(json, limits)
            : typeof(T) == typeof(ManagementCloseReceipt) ? ManagementWire.DecodeResponse<ManagementCloseReceipt>(json, limits)
            : typeof(T) == typeof(ManagementApplication) ? ManagementWire.DecodeResponse<ManagementApplication>(json, limits)
            : typeof(T) == typeof(ManagementPage<ManagementApplication>) ? ManagementWire.DecodeResponse<ManagementPage<ManagementApplication>>(json, limits)
            : typeof(T) == typeof(ManagementPage<ManagementInstance>) ? ManagementWire.DecodeResponse<ManagementPage<ManagementInstance>>(json, limits)
            : typeof(T) == typeof(ManagementPage<ManagementInventoryEntry>) ? ManagementWire.DecodeResponse<ManagementPage<ManagementInventoryEntry>>(json, limits)
            : typeof(T) == typeof(ManagementCapabilities) ? ManagementWire.DecodeResponse<ManagementCapabilities>(json, limits)
            : ManagementWire.DecodeResponse<ManagementMetadataResult>(json, limits);
        cancellationToken.ThrowIfCancellationRequested();
        return value is null ? RestDecodedValue<T>.Absent : RestDecodedValue<T>.Present((T)value);
    }
}
