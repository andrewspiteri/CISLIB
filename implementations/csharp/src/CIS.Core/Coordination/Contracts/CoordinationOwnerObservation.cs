using System.Text.Json.Serialization;

namespace CIS.Coordination;

/// <summary>A bounded diagnostic observation of another owner, never a grant or later-effect authorization.</summary>
public sealed class CoordinationOwnerObservation
{
    /// <summary>Copies owner and authority-time observations; the caller already knows the requested key.</summary>
    public CoordinationOwnerObservation(string ownerApplication, string instanceId, long generation,
        DateTimeOffset expiresAtUtc, DateTimeOffset observedAtUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(generation);
        OwnerApplication = CoordinationText.Utf8(ownerApplication, 1024, nameof(ownerApplication));
        InstanceId = CoordinationText.Utf8(instanceId, 128, nameof(instanceId));
        Generation = generation;
        ExpiresAtUtc = expiresAtUtc.ToUniversalTime();
        ObservedAtUtc = observedAtUtc.ToUniversalTime();
    }

    /// <summary>Gets the observed owner application.</summary>
    public string OwnerApplication
    {
        get;
    }
    /// <summary>Gets the observed process instance.</summary>
    public string InstanceId
    {
        get;
    }
    /// <summary>Gets the observed generation as a lossless decimal JSON string.</summary>
    [JsonConverter(typeof(CoordinationGenerationJsonConverter))]
    public long Generation
    {
        get;
    }
    /// <summary>Gets observed expiry; subsequent work still requires the guard.</summary>
    public DateTimeOffset ExpiresAtUtc
    {
        get;
    }
    /// <summary>Gets the authority observation instant.</summary>
    public DateTimeOffset ObservedAtUtc
    {
        get;
    }
}
