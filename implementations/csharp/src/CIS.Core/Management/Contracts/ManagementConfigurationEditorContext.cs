namespace CIS.Management;

/// <summary>One exact binding and selected-source declared defaults; never an effective runtime value or write grant.</summary>
public sealed record ManagementConfigurationEditorContext(string BindingId, string ApplicationId, string Application,
    string Environment, string ConfigurationId, string SchemaVersion, string HostApplicationName,
    ManagementConfigurationShape Shape, string SelectedSource, ManagementConfigurationEditorSource CommonSource,
    ManagementConfigurationEditorSource ApplicationSource, ManagementConfigurationEditorDefaults Defaults, string? Description = null);

/// <summary>Available means enabled and metadata-readable; unavailable may retain a metadata-readable disabled identity.</summary>
public sealed record ManagementConfigurationEditorSource(string State, string? SourceId = null);

/// <summary>Only available carries exact declared root defaults authorized through the selected actual source.</summary>
public sealed record ManagementConfigurationEditorDefaults(string State, string? DefaultsJson = null);

