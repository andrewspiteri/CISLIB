using CIS.Management.Service.Backends;
using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;
using CIS.Transactions;

namespace CIS.Management.Service.Http;

internal static partial class ManagementEndpoints
{
    private static readonly ManagementFeature[] ConfigurationEditorFeatures =
    [
        new("getConfigurationEditorContext", 1, "GET", "/applications/{applicationId}/configuration/{configurationId}/editor-context",
            ["management.applications.read", ManagementConfigurationCapabilities.DefinitionsRead])
    ];

    private static void MapConfigurationEditor(RouteGroupBuilder group)
        => group.MapGet("/applications/{applicationId}/configuration/{configurationId}/editor-context", ConfigurationEditorContext);

    private static async Task<IResult> ConfigurationEditorContext(HttpContext context, string applicationId, string configurationId,
        IManagementBackend applications, IManagementConfigurationBackend backend)
    {
        if (!ManagementWire.IsUuid(applicationId) || !ManagementWire.IsOperationKey(configurationId)
            || !ManagementDiscoveryQuery.TryReadConfigurationEditor(context, out string schema, out string selected)
            || ManagementConfigurationHttp.HasRequestBody(context))
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.invalid_request");
        }
        ManagementApplication? application = await VisibleApplication(context, applicationId, applications);
        if (application is null)
        {
            return ManagementConfigurationHttp.Problem(context, 404, "management.configuration.not_found");
        }
        ManagementConfigurationBindingResource? visible = await VisibleBinding(context, backend, applicationId,
            configurationId, schema, ManagementConfigurationCapabilities.DefinitionsRead);
        if (visible is null || !visible.Enabled)
        {
            return ManagementConfigurationHttp.Problem(context, 404, "management.configuration.not_found");
        }
        if (application.ApplicationId != applicationId || visible.ApplicationId != applicationId
            || visible.Application != application.Application || visible.Environment != application.Environment
            || visible.ConfigurationId != configurationId || visible.SchemaVersion != schema
            || !ManagementWire.IsUuid(visible.BindingId))
        {
            return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
        }
        if (backend is not IManagementConfigurationEditorBackend editor)
        {
            return ManagementConfigurationHttp.Problem(context, 501, "management.configuration.editor.unsupported");
        }
        ManagementStoreResult<ManagementConfigurationEditorBindingResource> result = await editor.ReadEditorBindingAsync(
            applicationId, configurationId, schema, context.RequestAborted);
        if (result.HasMore || result.AttemptCompletion is not
            {
                Outcome: TransactionOutcome.NotCommitted,
                CommitAttempted: false,
                RollbackAttempted: true,
                RollbackConfirmed: true,
                Error: null,
                CleanupErrors.Count: 0
            })
        {
            return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
        }
        if (!result.IsKnownCommitted || result.Value is not { } snapshot || result.Code is not null)
        {
            bool missing = result.Code == "management.configuration.not_found" && result.Value is null && !result.PreviouslyCommitted;
            return ManagementConfigurationHttp.Problem(context, missing ? 404 : 503,
                missing ? "management.configuration.not_found" : "management.configuration.unavailable");
        }
        ManagementConfigurationBindingResource binding = snapshot.Binding;
        if (binding is null || !binding.Enabled || binding.BindingId != visible.BindingId || binding.ApplicationId != applicationId
            || binding.Application != application.Application || binding.Environment != application.Environment
            || binding.HostApplicationName != visible.HostApplicationName
            || binding.CommonSourceId != visible.CommonSourceId || binding.ApplicationSourceId != visible.ApplicationSourceId
            || binding.ConfigurationId != configurationId || binding.SchemaVersion != schema
            || !EditorSourceMatches(snapshot.CommonSource, binding, "common")
            || !EditorSourceMatches(snapshot.ApplicationSource, binding, "application")
            || binding.CommonSourceId is not null && binding.CommonSourceId == binding.ApplicationSourceId)
        {
            return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
        }
        ManagementConfigurationEditorSource common = await DiscloseEditorSource(context, snapshot.CommonSource);
        ManagementConfigurationEditorSource local = await DiscloseEditorSource(context, snapshot.ApplicationSource);
        ManagementConfigurationEditorSource choice = selected == "common" ? common : local;
        ManagementConfigurationEditorDefaults defaults;
        if (choice.State == "available")
        {
            bool allowed = await ManagementConfigurationHttp.Allowed(context, ManagementConfigurationCapabilities.ContentRead,
                ManagementConfigurationCapabilities.SourceResource, choice.SourceId!);
            defaults = allowed ? new("available", binding.DefaultsJson) : new("redacted");
        }
        else
        {
            defaults = new(choice.State == "redacted" ? "redacted" : "unavailable");
        }
        context.RequestAborted.ThrowIfCancellationRequested();
        try
        {
            return ManagementConfigurationHttp.EditorContext(new ManagementConfigurationEditorContext(binding.BindingId,
                binding.ApplicationId, binding.Application, binding.Environment, binding.ConfigurationId, binding.SchemaVersion,
                binding.HostApplicationName, binding.Shape, selected, common, local, defaults, snapshot.Description), backend.ConfigurationWireLimits);
        }
        catch (ArgumentException)
        {
            return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
        }
    }

    private static bool EditorSourceMatches(ManagementConfigurationEditorSourceResource? source,
        ManagementConfigurationBindingResource binding, string kind)
    {
        string? expected = kind == "common" ? binding.CommonSourceId : binding.ApplicationSourceId;
        if (expected is null)
        {
            return source is null;
        }
        ManagementConfigurationDefinition? definition = source?.Definition;
        return definition is not null && ManagementWire.IsUuid(expected) && definition.SourceId == expected
            && definition.ConfigurationId == binding.ConfigurationId && definition.SchemaVersion == binding.SchemaVersion
            && definition.Environment == binding.Environment && definition.Kind == kind
            && (kind == "common"
                ? definition.ApplicationId is null && definition.BindingId is null && definition.HostApplicationName is null
                : definition.ApplicationId == binding.ApplicationId && definition.BindingId == binding.BindingId
                    && definition.HostApplicationName == binding.HostApplicationName);
    }

    private static async Task<ManagementConfigurationEditorSource> DiscloseEditorSource(HttpContext context,
        ManagementConfigurationEditorSourceResource? source)
    {
        if (source is null)
        {
            return new("absent");
        }
        string sourceId = source.Definition.SourceId;
        bool readable = await ManagementConfigurationHttp.Allowed(context, ManagementConfigurationCapabilities.DefinitionsRead,
            ManagementConfigurationCapabilities.SourceResource, sourceId);
        return readable ? new(source.Enabled ? "available" : "unavailable", sourceId) : new("redacted");
    }
}

