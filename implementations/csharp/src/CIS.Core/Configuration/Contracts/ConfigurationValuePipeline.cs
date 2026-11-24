using System.Reflection;
using System.Text.Json;

namespace CIS.Configuration;

// A single substitution boundary lets storage coordination and value preparation
// be implemented/tested independently; it is not a public mapping extension point.
internal interface IConfigurationValuePipeline
{
    ConfigurationPreparedValues Prepare(ConfigurationEntry entry, JsonElement? common, JsonElement? application);

    // Returns true only for an effective runtime change. Initial population accepts
    // its baseline without a notification. The entry owns checksum/revision under ValueGate.
    bool Apply(ConfigurationEntry entry, ConfigurationPreparedValues prepared, CancellationToken cancellationToken,
        Func<bool> stopping, bool initial = false);
}

internal sealed record ConfigurationAssignment(PropertyInfo Property, object? Value);

internal sealed record ConfigurationPreparedValues(string Checksum, IReadOnlyList<ConfigurationAssignment> Assignments)
{
    internal IDisposable? Ownership
    {
        get; init;
    }
}

// Optional only for centrally enrolled entries; existing custom test pipelines
// and independent-record stores acquire no compulsory member.
internal interface IConfigurationSnapshotValuePipeline
{
    ConfigurationSnapshotCommit ApplySnapshot(ConfigurationEntry entry, ConfigurationPreparedValues prepared,
        ConfigurationSnapshotProvenance provenance, long sessionEpoch, CancellationToken cancellationToken, bool initial);
}

internal sealed record ConfigurationSnapshotCommit(bool Changed, ConfigurationAcceptanceStatus Acceptance);

internal sealed class ConfigurationSnapshotAdmission
{
    internal int Closed;
    internal int Faulted;
    internal bool IsClosed => Volatile.Read(ref Closed) != 0;
    internal bool IsFaulted => Volatile.Read(ref Faulted) != 0;
}
