using System.Collections.ObjectModel;

namespace CIS.Management;

/// <summary>A passive trusted definition snapshot. Presence reports cannot add or replace its entries.</summary>
public sealed class ManagementCapabilityCatalog
{
    private readonly IReadOnlyDictionary<ManagementCapabilityKey, ManagementCapabilityDefinition> definitions;

    /// <summary>Copies and validates passive description data.</summary>
    public ManagementCapabilityCatalog(IReadOnlyList<ManagementCapabilityDefinition> definitions)
    {
        IReadOnlyList<ManagementCapabilityDefinition> captured = ManagementCatalogValidation.Copy(definitions,
            ManagementCapabilityLimits.MaximumDefinitions, nameof(definitions));
        var unique = new Dictionary<ManagementCapabilityKey, ManagementCapabilityDefinition>();
        foreach (ManagementCapabilityDefinition definition in captured)
        {
            if (!definition.IsSupported)
            {
                throw ManagementCatalogValidation.Invalid(nameof(definitions));
            }
            if (unique.TryGetValue(definition.Key, out ManagementCapabilityDefinition? previous))
            {
                if (previous.SemanticDigest != definition.SemanticDigest)
                {
                    throw new ArgumentException("The same trusted definition version has conflicting descriptions.", nameof(definitions));
                }
                continue;
            }
            unique.Add(definition.Key, definition);
        }
        this.definitions = new ReadOnlyDictionary<ManagementCapabilityKey, ManagementCapabilityDefinition>(unique);
    }

    /// <summary>Gets exact accepted versions; repeated participating instances are not additional definitions.</summary>
    public IReadOnlyDictionary<ManagementCapabilityKey, ManagementCapabilityDefinition> Definitions => definitions;

    /// <summary>Finds only the exact trusted owner/kind/scope/definition/version, without I/O or handler resolution.</summary>
    public ManagementCapabilityDefinition? Find(ManagementCapabilityKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return definitions.GetValueOrDefault(key);
    }
}
