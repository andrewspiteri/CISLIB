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

internal sealed record ConfigurationPreparedValues(string Checksum, IReadOnlyList<ConfigurationAssignment> Assignments);
