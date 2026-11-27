using System.Security.Cryptography;
using System.Text;
using CIS.Management.Service.Backends;
using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;

namespace CIS.Management.Service.Http;

internal static partial class ManagementEndpoints
{
    private static readonly ManagementFeature[] DistributionFeatures =
    [
        new("previewConfiguration", 1, "POST", "/configuration/preview", [ManagementConfigurationCapabilities.Preview]),
        new("createConfigurationRelease", 1, "POST", "/applications/{applicationId}/configuration/{configurationId}/releases", [ManagementConfigurationCapabilities.ReleaseCreate, ManagementConfigurationCapabilities.OperationsRead]),
        new("getConfigurationRelease", 1, "GET", "/configuration/releases/{releaseId}", [ManagementConfigurationCapabilities.DefinitionsRead]),
        new("getConfigurationAssignments", 1, "GET", "/applications/{applicationId}/configuration/{configurationId}/assignments", [ManagementConfigurationCapabilities.DefinitionsRead]),
        new("replaceConfigurationAssignments", 1, "PUT", "/applications/{applicationId}/configuration/{configurationId}/assignments", [ManagementConfigurationCapabilities.AssignmentWrite, ManagementConfigurationCapabilities.OperationsRead]),
        new("getConfigurationReleaseOperation", 1, "GET", "/applications/{applicationId}/configuration/{configurationId}/release-operations/{operationKey}", [ManagementConfigurationCapabilities.OperationsRead]),
        new("getConfigurationAssignmentOperation", 1, "GET", "/applications/{applicationId}/configuration/{configurationId}/assignment-operations/{operationKey}", [ManagementConfigurationCapabilities.OperationsRead])
    ];

    static partial void MapConfigurationDistribution(RouteGroupBuilder group)
    {
        group.MapPost("/configuration/preview", PreviewConfiguration);
        group.MapPost("/applications/{applicationId}/configuration/{configurationId}/releases", CreateConfigurationRelease);
        group.MapGet("/configuration/releases/{releaseId}", ConfigurationRelease);
        group.MapGet("/applications/{applicationId}/configuration/{configurationId}/assignments", ConfigurationAssignments);
        group.MapPut("/applications/{applicationId}/configuration/{configurationId}/assignments", ReplaceConfigurationAssignments);
        group.MapGet("/applications/{applicationId}/configuration/{configurationId}/release-operations/{operationKey}", ConfigurationReleaseOperation);
        group.MapGet("/applications/{applicationId}/configuration/{configurationId}/assignment-operations/{operationKey}", ConfigurationAssignmentOperation);
    }

    private static bool BindingRequest(HttpContext context, string applicationId, string configurationId, out string schemaVersion)
    {
        schemaVersion = context.Request.Query["schemaVersion"].ToString();
        return ManagementWire.IsUuid(applicationId) && ManagementWire.IsOperationKey(configurationId)
            && context.Request.Query.Count == 1 && context.Request.Query["schemaVersion"].Count == 1
            && ManagementWire.IsOrderedInteger(schemaVersion);
    }

    private static async Task<ManagementConfigurationBindingResource?> VisibleBinding(HttpContext context, IManagementConfigurationBackend backend,
        string application, string configuration, string schema, string capability)
    {
        ManagementStoreResult<ManagementConfigurationBindingResource> result = await backend.ReadBindingAsync(application, configuration, schema, context.RequestAborted);
        if (result.Code is not null && result.Code != "management.configuration.not_found")
        {
            throw new ManagementRequestException(503, result.Code);
        }
        return result.IsKnownCommitted && result.Value is { } binding && await ManagementConfigurationHttp.Allowed(context, capability,
            ManagementConfigurationCapabilities.BindingResource, binding.BindingId) ? binding : null;
    }

    private static async Task<IResult> CreateConfigurationRelease(HttpContext context, string applicationId, string configurationId,
        IManagementConfigurationBackend backend)
    {
        if (!BindingRequest(context, applicationId, configurationId, out string schema))
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.invalid_request");
        }
        ManagementConfigurationBindingResource? binding = await VisibleBinding(context, backend, applicationId, configurationId, schema,
            ManagementConfigurationCapabilities.ReleaseCreate);
        if (binding is null)
        {
            return ManagementConfigurationHttp.Problem(context, 404, "management.configuration.not_found");
        }
        if (!ManagementConfigurationHttp.MutationHeaders(context, out string key, out string etag, out IResult? refusal, schemaQuery: true))
        {
            return refusal!;
        }
        ManagementConfigurationReleaseCommand? command = await ManagementConfigurationHttp.Read<ManagementConfigurationReleaseCommand>(context);
        return command is null ? ManagementConfigurationHttp.Problem(context, 400, "management.configuration.invalid_request")
            : ManagementConfigurationHttp.Operation(context, await backend.CreateReleaseAsync(binding, key, etag, command, context.RequestAborted));
    }

    private static async Task<IResult> ConfigurationRelease(HttpContext context, string releaseId, IManagementConfigurationBackend backend)
    {
        if (context.Request.Query.Count != 0 || !ManagementWire.IsUuid(releaseId))
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.invalid_request");
        }
        ManagementStoreResult<ManagementConfigurationRelease> result = await backend.ReadReleaseAsync(releaseId, context.RequestAborted);
        if (!result.IsKnownCommitted || result.Value is not { } release)
        {
            return ManagementConfigurationHttp.Problem(context, result.Code == "management.configuration.not_found" ? 404 : 503,
                result.Code ?? "management.configuration.unavailable");
        }
        if (!await ManagementConfigurationHttp.Allowed(context, ManagementConfigurationCapabilities.DefinitionsRead,
            ManagementConfigurationCapabilities.ReleaseResource, release.ReleaseId))
        {
            return ManagementConfigurationHttp.Problem(context, 404, "management.configuration.not_found");
        }
        context.Response.Headers.ETag = release.ETag;
        return ManagementConfigurationHttp.Json(release);
    }

    private static async Task<IResult> ConfigurationAssignments(HttpContext context, string applicationId, string configurationId,
        IManagementConfigurationBackend backend)
    {
        if (!BindingRequest(context, applicationId, configurationId, out string schema))
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.invalid_request");
        }
        ManagementConfigurationBindingResource? binding = await VisibleBinding(context, backend, applicationId, configurationId, schema,
            ManagementConfigurationCapabilities.DefinitionsRead);
        if (binding is null)
        {
            return ManagementConfigurationHttp.Problem(context, 404, "management.configuration.not_found");
        }
        ManagementStoreResult<ManagementConfigurationAssignmentMap> result = await backend.ReadAssignmentsAsync(binding, context.RequestAborted);
        if (!result.IsKnownCommitted || result.Value is not { } map)
        {
            return ManagementConfigurationHttp.Problem(context, 503, result.Code ?? "management.configuration.unavailable");
        }
        context.Response.Headers.ETag = map.ETag;
        return ManagementConfigurationHttp.Json(map);
    }

    private static async Task<IResult> ReplaceConfigurationAssignments(HttpContext context, string applicationId, string configurationId,
        IManagementConfigurationBackend backend)
    {
        if (!BindingRequest(context, applicationId, configurationId, out string schema))
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.invalid_request");
        }
        ManagementConfigurationBindingResource? binding = await VisibleBinding(context, backend, applicationId, configurationId, schema,
            ManagementConfigurationCapabilities.AssignmentWrite);
        if (binding is null)
        {
            return ManagementConfigurationHttp.Problem(context, 404, "management.configuration.not_found");
        }
        if (!ManagementConfigurationHttp.MutationHeaders(context, out string key, out string etag, out IResult? refusal, schemaQuery: true))
        {
            return refusal!;
        }
        ManagementConfigurationAssignmentCommand? command = await ManagementConfigurationHttp.Read<ManagementConfigurationAssignmentCommand>(context);
        return command is null ? ManagementConfigurationHttp.Problem(context, 400, "management.configuration.invalid_request")
            : ManagementConfigurationHttp.Operation(context, await backend.ReplaceAssignmentsAsync(binding, key, etag, command, context.RequestAborted));
    }

    private static Task<IResult> ConfigurationReleaseOperation(HttpContext context, string applicationId, string configurationId,
        string operationKey, IManagementConfigurationBackend backend)
        => ConfigurationBindingOperation(context, applicationId, configurationId, operationKey, backend, release: true);

    private static Task<IResult> ConfigurationAssignmentOperation(HttpContext context, string applicationId, string configurationId,
        string operationKey, IManagementConfigurationBackend backend)
        => ConfigurationBindingOperation(context, applicationId, configurationId, operationKey, backend, release: false);

    private static async Task<IResult> ConfigurationBindingOperation(HttpContext context, string applicationId, string configurationId,
        string operationKey, IManagementConfigurationBackend backend, bool release)
    {
        if (!BindingRequest(context, applicationId, configurationId, out string schema) || !ManagementWire.IsOperationKey(operationKey))
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.invalid_request");
        }
        ManagementConfigurationBindingResource? binding = await VisibleBinding(context, backend, applicationId, configurationId, schema,
            ManagementConfigurationCapabilities.OperationsRead);
        if (binding is null)
        {
            return ManagementConfigurationHttp.Problem(context, 404, "management.configuration.not_found");
        }
        return ManagementConfigurationHttp.Operation(context, release
            ? await backend.LookupReleaseAsync(binding, operationKey, context.RequestAborted)
            : await backend.LookupAssignmentAsync(binding, operationKey, context.RequestAborted));
    }

    private static async Task<IResult> PreviewConfiguration(HttpContext context, IManagementConfigurationBackend backend, ManagementCursor cursors)
    {
        ManagementConfigurationPreviewCommand? command = await ManagementConfigurationHttp.Read<ManagementConfigurationPreviewCommand>(context);
        if (command is null)
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.invalid_request");
        }
        ManagementConfigurationBindingResource? binding = await VisibleBinding(context, backend, command.ApplicationId, command.ConfigurationId,
            command.SchemaVersion, ManagementConfigurationCapabilities.Preview);
        if (binding is null)
        {
            return ManagementConfigurationHttp.Problem(context, 404, "management.configuration.not_found");
        }
        ManagementStoreResult<ManagementConfigurationAssignmentMap> observed = await backend.ReadAssignmentsAsync(binding, context.RequestAborted);
        if (!observed.IsKnownCommitted || observed.Value is not { } map)
        {
            return ManagementConfigurationHttp.Problem(context, 503, observed.Code ?? "management.configuration.unavailable");
        }
        if (command.Assignment is { } proposal)
        {
            command = command with
            {
                Assignment = proposal with
                {
                    Overrides = Array.AsReadOnly(proposal.Overrides.OrderBy(item => item.CohortId, StringComparer.Ordinal).ToArray())
                }
            };
        }
        string endpoint = "configuration.preview." + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(binding.BindingId + "\n" + map.ETag + "\n"
            + Convert.ToHexStringLower(SHA256.HashData(ManagementConfigurationWire.EncodeRequest(command))))));
        if (!ManagementHttp.TryPage(context, cursors, endpoint, binding.BindingId, out int size, out string? after))
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.cursor_invalid");
        }
        string[] sourceIds = new[] { binding.CommonSourceId, binding.ApplicationSourceId }.OfType<string>().ToArray();
        bool values = sourceIds.Length > 0;
        foreach (string sourceId in sourceIds)
        {
            values &= await ManagementConfigurationHttp.Allowed(context, ManagementConfigurationCapabilities.ContentRead,
                ManagementConfigurationCapabilities.SourceResource, sourceId);
        }
        ManagementStoreResult<ManagementConfigurationPreview> result = await backend.PreviewAsync(binding, command, after, size + 1, values, map.ETag, context.RequestAborted);
        if (!result.IsKnownCommitted || result.Value is not { } preview)
        {
            int status = result.Code == "management.configuration.preview_stale" ? 409
                : result.Code is "management.configuration.reference_not_found" or "management.configuration.reference_scope" ? 404 : 503;
            return ManagementConfigurationHttp.Problem(context, status, result.Code ?? "management.configuration.unavailable");
        }
        ManagementConfigurationPreviewRecipient[] items = preview.Recipients.Items.Take(size).ToArray();
        preview = preview with
        {
            Recipients = new(Array.AsReadOnly(items), preview.Recipients.Items.Count > size
            ? ManagementHttp.Cursor(context, cursors, endpoint, binding.BindingId, size, items[^1].RegistrationId) : null)
        };
        try
        {
            return ManagementConfigurationHttp.Json(preview);
        }
        catch (ArgumentException)
        {
            return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.response_too_large");
        }
    }
}
