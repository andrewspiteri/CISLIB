using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;

namespace CIS.Management.Service.Backends;

/// <summary>Optional exact read of a binding's immutable declaration and current source enablement.</summary>
public interface IManagementConfigurationEditorBackend
{
    Task<ManagementStoreResult<ManagementConfigurationEditorBindingResource>> ReadEditorBindingAsync(
        string applicationId, string configurationId, string schemaVersion, CancellationToken token = default);
}

/// <summary>Trusted native snapshot before independent HTTP resource authorization.</summary>
public sealed record ManagementConfigurationEditorBindingResource(ManagementConfigurationBindingResource Binding,
    ManagementConfigurationEditorSourceResource? CommonSource, ManagementConfigurationEditorSourceResource? ApplicationSource,
    string? Description = null);

/// <summary>The actual immutable source declaration and its current enabled state in that same snapshot.</summary>
public sealed record ManagementConfigurationEditorSourceResource(ManagementConfigurationDefinition Definition, bool Enabled);

