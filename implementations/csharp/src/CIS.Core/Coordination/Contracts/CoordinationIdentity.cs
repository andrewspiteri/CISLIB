using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CIS.Coordination;

/// <summary>One client-generated acquisition identity, independent of any business-operation identifier.</summary>
public sealed record CoordinationAcquisition
{
    /// <summary>Copies the trusted owner and new acquisition; the acquisition ID also serves as the grant ID.</summary>
    public CoordinationAcquisition(CoordinationKey key, string ownerApplication, string instanceId, Guid acquisitionId)
    {
        ArgumentNullException.ThrowIfNull(key);
        Key = key;
        OwnerApplication = CoordinationText.Utf8(ownerApplication, 1024, nameof(ownerApplication));
        InstanceId = CoordinationText.Utf8(instanceId, 128, nameof(instanceId));
        AcquisitionId = CoordinationText.Id(acquisitionId, nameof(acquisitionId));
    }

    /// <summary>Gets the complete duty identity.</summary>
    public CoordinationKey Key
    {
        get;
    }
    /// <summary>Gets the authoritative local application identity.</summary>
    public string OwnerApplication
    {
        get;
    }
    /// <summary>Gets the unique process-lifetime instance identity.</summary>
    public string InstanceId
    {
        get;
    }
    /// <summary>Gets the fresh acquisition identity, also used as GrantId.</summary>
    public Guid AcquisitionId
    {
        get;
    }
    /// <summary>Gets the exact grant identity, equal to AcquisitionId in version one.</summary>
    public Guid GrantId => AcquisitionId;
}

/// <summary>A committed authority grant; its generation orders grants but authenticates nobody.</summary>
public sealed record CoordinationGrant
{
    /// <summary>Creates immutable identity and observed expiry; only the authority can establish current ownership.</summary>
    public CoordinationGrant(Guid authorityId, CoordinationAcquisition acquisition, long generation, DateTimeOffset expiresAtUtc)
    {
        ArgumentNullException.ThrowIfNull(acquisition);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(generation);
        AuthorityId = CoordinationText.Id(authorityId, nameof(authorityId));
        Acquisition = acquisition;
        Generation = generation;
        ExpiresAtUtc = expiresAtUtc.ToUniversalTime();
    }

    /// <summary>Gets the stable installed authority identity.</summary>
    public Guid AuthorityId
    {
        get;
    }
    /// <summary>Gets the exact key, owner and acquisition.</summary>
    public CoordinationAcquisition Acquisition
    {
        get;
    }
    /// <summary>Gets the positive generation, encoded as a decimal JSON string to preserve Int64 fidelity.</summary>
    [JsonConverter(typeof(CoordinationGenerationJsonConverter))]
    public long Generation
    {
        get;
    }
    /// <summary>Gets the observed authoritative expiry, not a client-side authorization check.</summary>
    public DateTimeOffset ExpiresAtUtc
    {
        get;
    }
    /// <summary>Gets the exact duty identity.</summary>
    [JsonIgnore]
    public CoordinationKey Key => Acquisition.Key;
    /// <summary>Gets the exact grant identity.</summary>
    [JsonIgnore]
    public Guid GrantId => Acquisition.GrantId;
}

internal sealed class CoordinationGenerationJsonConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || !long.TryParse(reader.GetString(), NumberStyles.None,
                CultureInfo.InvariantCulture, out long value) || value <= 0)
        {
            throw new JsonException("A coordination generation is a positive Int64 decimal string.");
        }
        return value;
    }

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
    }
}
