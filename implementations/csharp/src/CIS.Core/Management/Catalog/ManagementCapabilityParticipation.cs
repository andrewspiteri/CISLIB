using System.Text.Json.Serialization;
using CIS.Management.Wire;

namespace CIS.Management;

/// <summary>One reported version/build participation fact, separate from trusted definitions and execution authority.</summary>
public sealed class ManagementCapabilityParticipation
{
    /// <summary>Copies and validates passive description data.</summary>
    public ManagementCapabilityParticipation(ManagementCapabilityKey key, string registrationId, string inventorySequence,
        string observedAtUtc, string receivedAtUtc, string availability, bool draining = false,
        ManagementWorkerBuildReference? build = null)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!ManagementWire.IsUuid(registrationId) || !ManagementWire.IsTimestamp(observedAtUtc)
            || !ManagementWire.IsTimestamp(receivedAtUtc))
        {
            throw ManagementCatalogValidation.Invalid(nameof(registrationId));
        }
        Key = key;
        RegistrationId = registrationId;
        InventorySequence = ManagementCatalogValidation.Version(inventorySequence, nameof(inventorySequence));
        ObservedAtUtc = observedAtUtc;
        ReceivedAtUtc = receivedAtUtc;
        Availability = ManagementCatalogValidation.Token(availability, nameof(availability));
        Draining = draining;
        Build = build;
    }

    /// <summary>Gets the immutable key.</summary>
    public ManagementCapabilityKey Key { get; }
    /// <summary>Gets the immutable registration id.</summary>
    public string RegistrationId { get; }
    /// <summary>Gets the immutable inventory sequence.</summary>
    public string InventorySequence { get; }
    /// <summary>Gets the immutable observed at utc.</summary>
    public string ObservedAtUtc { get; }
    /// <summary>Gets the immutable received at utc.</summary>
    public string ReceivedAtUtc { get; }
    /// <summary>Gets the immutable availability.</summary>
    public string Availability { get; }
    /// <summary>Gets the immutable draining.</summary>
    public bool Draining { get; }
    /// <summary>Gets the immutable build.</summary>
    public ManagementWorkerBuildReference? Build { get; }
    /// <summary>Preserves future reported values without interpreting them as successful availability.</summary>
    [JsonIgnore]
    public ManagementKnownValue KnownAvailability => new(Availability, Availability is "available" or "unavailable" or "unknown");
}
