namespace CIS.Management.Service.Configuration;

/// <summary>Actual stored binding data resolved before native resource authorization; never an HTTP representation.</summary>
public sealed record ManagementConfigurationBindingResource(string BindingId, string ApplicationId, string Application,
    string Environment, string ConfigurationId, string SchemaVersion, string HostApplicationName,
    ManagementConfigurationShape Shape, string DefaultsJson, bool Enabled, string? CommonSourceId, string? ApplicationSourceId);
