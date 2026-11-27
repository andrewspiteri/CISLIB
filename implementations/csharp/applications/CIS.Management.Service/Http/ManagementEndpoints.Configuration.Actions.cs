using CIS.Management.Service.Backends;
using CIS.Management.Service.Configuration;

namespace CIS.Management.Service.Http;

internal static partial class ManagementEndpoints
{
    private static readonly ManagementFeature[] ConfigurationActionFeatures =
    [
        new("getConfigurationSourceActions", 1, "GET", "/configuration/sources/{sourceId}/actions",
            [ManagementConfigurationCapabilities.DefinitionsRead]),
        new("getConfigurationBindingActions", 1, "GET", "/applications/{applicationId}/configuration/{configurationId}/actions",
            [ManagementConfigurationCapabilities.DefinitionsRead])
    ];

    private static void MapConfigurationActions(RouteGroupBuilder group)
    {
        group.MapGet("/configuration/sources/{sourceId}/actions", ConfigurationSourceActions);
        group.MapGet("/applications/{applicationId}/configuration/{configurationId}/actions", ConfigurationBindingActions);
    }

    private static async Task<IResult> ConfigurationSourceActions(HttpContext context, string sourceId,
        IManagementConfigurationBackend backend)
    {
        ManagementConfigurationDefinition? source = await VisibleSource(context, backend, sourceId,
            ManagementConfigurationCapabilities.DefinitionsRead);
        if (source is null)
        {
            return ManagementConfigurationHttp.Problem(context, 404, "management.configuration.not_found");
        }
        if (source.SourceId != sourceId)
        {
            return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
        }
        if (context.Request.Query.Count != 0 || ManagementConfigurationHttp.HasRequestBody(context))
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.invalid_request");
        }
        ManagementAdministrativeAction validate = await ConfigurationAction(context, "validateConfiguration",
            ManagementConfigurationCapabilities.Validate, ManagementConfigurationCapabilities.SourceResource, source.SourceId);
        ManagementAdministrativeAction publish = await ConfigurationAction(context, "publishConfigurationSource",
            ManagementConfigurationCapabilities.Publish, ManagementConfigurationCapabilities.SourceResource, source.SourceId, durable: true);
        return ManagementConfigurationHttp.Actions(new(source.SourceId, [validate, publish]));
    }

    private static async Task<IResult> ConfigurationBindingActions(HttpContext context, string applicationId,
        string configurationId, IManagementConfigurationBackend backend)
    {
        if (!BindingRequest(context, applicationId, configurationId, out string schema))
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.invalid_request");
        }
        ManagementConfigurationBindingResource? binding = await VisibleBinding(context, backend, applicationId,
            configurationId, schema, ManagementConfigurationCapabilities.DefinitionsRead);
        if (binding is null)
        {
            return ManagementConfigurationHttp.Problem(context, 404, "management.configuration.not_found");
        }
        if (binding.ApplicationId != applicationId || binding.ConfigurationId != configurationId || binding.SchemaVersion != schema)
        {
            return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
        }
        if (ManagementConfigurationHttp.HasRequestBody(context))
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.invalid_request");
        }
        ManagementAdministrativeAction preview = await ConfigurationAction(context, "previewConfiguration",
            ManagementConfigurationCapabilities.Preview, ManagementConfigurationCapabilities.BindingResource, binding.BindingId);
        ManagementAdministrativeAction release = await ConfigurationAction(context, "createConfigurationRelease",
            ManagementConfigurationCapabilities.ReleaseCreate, ManagementConfigurationCapabilities.BindingResource, binding.BindingId, durable: true);
        ManagementAdministrativeAction assign = await ConfigurationAction(context, "replaceConfigurationAssignments",
            ManagementConfigurationCapabilities.AssignmentWrite, ManagementConfigurationCapabilities.BindingResource, binding.BindingId, durable: true);
        return ManagementConfigurationHttp.Actions(new(binding.BindingId, [preview, release, assign]));
    }

    private static async Task<ManagementAdministrativeAction> ConfigurationAction(HttpContext context, string operationId,
        string capability, string resourceType, string resourceId, bool durable = false)
    {
        bool allowed = await ManagementConfigurationHttp.AllowedBundle(context,
            durable ? [capability, ManagementConfigurationCapabilities.OperationsRead] : [capability], resourceType, resourceId);
        return new(operationId, Supported: true, allowed, allowed ? null : "management.action.not_authorized");
    }
}
