namespace CIS.Healthcheck.Internal;

/// <summary>Detached state-issued identity for one frozen collection attachment.</summary>
internal sealed class HealthCollectionTarget
{
    internal HealthCollectionTarget(
        Guid monitorId,
        Guid collectionRegistrationId,
        long generation,
        TimeSpan? interval,
        bool isRequired,
        CancellationToken retirement = default)
    {
        MonitorId = monitorId;
        CollectionRegistrationId = collectionRegistrationId;
        Generation = generation;
        Interval = interval;
        IsRequired = isRequired;
        Retirement = retirement;
    }

    internal Guid MonitorId { get; }
    internal Guid CollectionRegistrationId { get; }
    internal long Generation { get; }
    internal TimeSpan? Interval { get; }
    internal bool IsRequired { get; }
    internal CancellationToken Retirement { get; }
}
