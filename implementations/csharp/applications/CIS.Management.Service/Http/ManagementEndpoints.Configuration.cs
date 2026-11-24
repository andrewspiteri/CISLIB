using CIS.Management.Service.Backends;
using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;

namespace CIS.Management.Service.Http;

internal static partial class ManagementEndpoints
{
    private static readonly ManagementFeature[] CatalogFeatures =
    [
        new("getConfigurationCatalog", 1, "GET", "/configuration/catalog", [ManagementConfigurationCapabilities.CatalogRead]),
        new("applyConfigurationCatalog", 1, "POST", "/configuration/catalog/apply", [ManagementConfigurationCapabilities.CatalogApply]),
        new("getConfigurationCatalogOperation", 1, "GET", "/configuration/catalog/operations/{operationKey}", [ManagementConfigurationCapabilities.OperationsRead])
    ];
    private static readonly ManagementFeature[] PublicationFeatures =
    [
        new("getConfigurationDefinitions", 1, "GET", "/configuration/definitions", [ManagementConfigurationCapabilities.DefinitionsRead]),
        new("validateConfiguration", 1, "POST", "/configuration/validate", [ManagementConfigurationCapabilities.Validate]),
        new("getConfigurationSource", 1, "GET", "/configuration/sources/{sourceId}", [ManagementConfigurationCapabilities.DefinitionsRead]),
        new("publishConfigurationSource", 1, "POST", "/configuration/sources/{sourceId}/publications", [ManagementConfigurationCapabilities.Publish]),
        new("getConfigurationPublication", 1, "GET", "/configuration/publications/{publicationId}", [ManagementConfigurationCapabilities.ContentRead]),
        new("getConfigurationPublicationOperation", 1, "GET", "/configuration/sources/{sourceId}/publication-operations/{operationKey}", [ManagementConfigurationCapabilities.OperationsRead])
    ];

    private static void MapConfiguration(RouteGroupBuilder group, ManagementServiceOptions options)
    {
        if (options.Configuration is null)
        {
            return;
        }
        group.MapGet("/configuration/catalog", ConfigurationCatalog);
        group.MapPost("/configuration/catalog/apply", ApplyConfigurationCatalog);
        group.MapGet("/configuration/catalog/operations/{operationKey}", ConfigurationCatalogOperation);
        group.MapGet("/configuration/definitions", ConfigurationDefinitions);
        group.MapPost("/configuration/validate", ValidateConfiguration);
        group.MapGet("/configuration/sources/{sourceId}", ConfigurationSource);
        group.MapPost("/configuration/sources/{sourceId}/publications", PublishConfiguration);
        group.MapGet("/configuration/publications/{publicationId}", ConfigurationPublication);
        group.MapGet("/configuration/sources/{sourceId}/publication-operations/{operationKey}", ConfigurationPublicationOperation);
        MapConfigurationDistribution(group);
        MapConfigurationSnapshots(group);
        MapConfigurationAdoption(group);
    }

    private static async Task<IReadOnlyList<ManagementFeature>> ConfigurationFeatures(IManagementConfigurationBackend backend,
        CancellationToken token)
    {
        if (!backend.ConfigurationSelected)
        {
            return [];
        }
        ManagementStoreResult<ManagementConfigurationCatalog> catalog = await backend.ReadCatalogAsync(token);
        return catalog.IsKnownCommitted && catalog.Value?.Ready is true
            ? Array.AsReadOnly(CatalogFeatures.Concat(PublicationFeatures).Concat(DistributionFeatures).Concat(SnapshotFeatures).Concat(AdoptionFeatures).ToArray())
            : Array.AsReadOnly(CatalogFeatures);
    }

    static partial void MapConfigurationDistribution(RouteGroupBuilder group);
    static partial void MapConfigurationSnapshots(RouteGroupBuilder group);
    static partial void MapConfigurationAdoption(RouteGroupBuilder group);

    private static async Task<ManagementConfigurationCatalog?> VisibleCatalog(HttpContext context,
        IManagementConfigurationBackend backend, string capability)
    {
        ManagementStoreResult<ManagementConfigurationCatalog> result = await backend.ReadCatalogAsync(context.RequestAborted);
        if (!result.IsKnownCommitted || result.Value is null)
        {
            throw new ManagementRequestException(503, result.Code ?? "management.configuration.unavailable");
        }
        return await ManagementConfigurationHttp.Allowed(context, capability, ManagementConfigurationCapabilities.CatalogResource, result.Value.CatalogId)
            ? result.Value : null;
    }

    private static async Task<ManagementConfigurationDefinition?> VisibleSource(HttpContext context,
        IManagementConfigurationBackend backend, string sourceId, string capability)
    {
        if (!ManagementWire.IsUuid(sourceId))
        {
            return null;
        }
        ManagementStoreResult<ManagementConfigurationDefinition> result = await backend.ReadDefinitionAsync(sourceId, context.RequestAborted);
        if (result.Code is not null && result.Code != "management.configuration.not_found")
        {
            throw new ManagementRequestException(503, result.Code);
        }
        return result.IsKnownCommitted && result.Value is { } value
            && await ManagementConfigurationHttp.Allowed(context, capability, ManagementConfigurationCapabilities.SourceResource, value.SourceId) ? value : null;
    }

    private static async Task<IResult> ConfigurationCatalog(HttpContext context, IManagementConfigurationBackend backend)
    {
        ManagementConfigurationCatalog? catalog = await VisibleCatalog(context, backend, ManagementConfigurationCapabilities.CatalogRead);
        if (catalog is null)
        {
            return ManagementConfigurationHttp.Problem(context, 404, "management.configuration.not_found");
        }
        if (context.Request.Query.Count != 0)
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.invalid_request");
        }
        context.Response.Headers.ETag = catalog.ETag;
        return ManagementConfigurationHttp.Json(catalog);
    }

    private static async Task<IResult> ApplyConfigurationCatalog(HttpContext context, IManagementConfigurationBackend backend)
    {
        ManagementConfigurationCatalog? catalog = await VisibleCatalog(context, backend, ManagementConfigurationCapabilities.CatalogApply);
        if (catalog is null)
        {
            return ManagementConfigurationHttp.Problem(context, 404, "management.configuration.not_found");
        }
        if (!ManagementConfigurationHttp.MutationHeaders(context, out string key, out string etag, out IResult? refusal))
        {
            return refusal!;
        }
        ManagementConfigurationCatalogCommand? command = await ManagementConfigurationHttp.Read<ManagementConfigurationCatalogCommand>(context);
        return command is null ? ManagementConfigurationHttp.Problem(context, 400, "management.configuration.invalid_request")
            : ManagementConfigurationHttp.Operation(context, await backend.ApplyCatalogAsync(catalog, key, etag, command, context.RequestAborted));
    }

    private static async Task<IResult> ConfigurationCatalogOperation(HttpContext context, string operationKey, IManagementConfigurationBackend backend)
    {
        ManagementConfigurationCatalog? catalog = await VisibleCatalog(context, backend, ManagementConfigurationCapabilities.OperationsRead);
        if (catalog is null)
        {
            return ManagementConfigurationHttp.Problem(context, 404, "management.configuration.not_found");
        }
        return context.Request.Query.Count != 0 || !ManagementWire.IsOperationKey(operationKey)
            ? ManagementConfigurationHttp.Problem(context, 400, "management.configuration.invalid_request")
            : ManagementConfigurationHttp.Operation(context, await backend.LookupCatalogAsync(catalog, operationKey, context.RequestAborted));
    }

    private static async Task<IResult> ConfigurationDefinitions(HttpContext context, IManagementConfigurationBackend backend, ManagementCursor cursors)
    {
        const string endpoint = "configuration.definitions";
        if (!ManagementHttp.TryPage(context, cursors, endpoint, null, out int size, out string? after))
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.cursor_invalid");
        }
        var items = new List<ManagementConfigurationDefinition>();
        int remaining = 1024 * 1024 - 4096;
        bool more = false;
        while (true)
        {
            ManagementStoreResult<IReadOnlyList<ManagementConfigurationDefinition>> batch = await backend.ReadDefinitionsAsync(after, 16, context.RequestAborted);
            if (!batch.IsKnownCommitted || batch.Value is null)
            {
                return ManagementConfigurationHttp.Problem(context, 503, batch.Code ?? "management.configuration.unavailable");
            }
            foreach (ManagementConfigurationDefinition row in batch.Value)
            {
                after = row.SourceId;
                if (!await ManagementConfigurationHttp.Allowed(context, ManagementConfigurationCapabilities.DefinitionsRead,
                    ManagementConfigurationCapabilities.SourceResource, row.SourceId))
                {
                    continue;
                }
                bool values = await ManagementConfigurationHttp.Allowed(context, ManagementConfigurationCapabilities.ContentRead,
                    ManagementConfigurationCapabilities.SourceResource, row.SourceId);
                ManagementConfigurationDefinition visible = values ? row : row with
                {
                    DefaultsJson = null
                };
                int bytes = ManagementConfigurationWire.EncodeResponse(visible).Length + 1;
                if (items.Count == size || bytes > remaining)
                {
                    if (items.Count == 0)
                    {
                        return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.response_too_large");
                    }
                    more = true;
                    break;
                }
                items.Add(visible);
                remaining -= bytes;
            }
            if (more || batch.Value.Count < 16)
            {
                break;
            }
        }
        return ManagementConfigurationHttp.Json(new ManagementPage<ManagementConfigurationDefinition>(items.AsReadOnly(), more
            ? ManagementHttp.Cursor(context, cursors, endpoint, null, size, items[^1].SourceId) : null));
    }

    private static async Task<IResult> ValidateConfiguration(HttpContext context, IManagementConfigurationBackend backend)
    {
        if (context.Request.Query.Count != 0)
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.invalid_request");
        }
        ManagementConfigurationValidationCommand? command = await ManagementConfigurationHttp.Read<ManagementConfigurationValidationCommand>(context);
        if (command is null)
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.invalid_request");
        }
        ManagementConfigurationDefinition? source = await VisibleSource(context, backend, command.SourceId, ManagementConfigurationCapabilities.Validate);
        return source is null ? ManagementConfigurationHttp.Problem(context, 404, "management.configuration.not_found")
            : ManagementConfigurationHttp.Json(ManagementConfigurationStructure.Validate(source.Shape, command.PayloadJson));
    }

    private static async Task<IResult> ConfigurationSource(HttpContext context, string sourceId,
        IManagementConfigurationBackend backend, ManagementCursor cursors)
    {
        if (await VisibleSource(context, backend, sourceId, ManagementConfigurationCapabilities.DefinitionsRead) is null)
        {
            return ManagementConfigurationHttp.Problem(context, 404, "management.configuration.not_found");
        }
        string endpoint = "configuration.source." + sourceId;
        if (!ManagementHttp.TryPage(context, cursors, endpoint, null, out int size, out string? after))
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.cursor_invalid");
        }
        ManagementStoreResult<ManagementConfigurationSource> result = await backend.ReadSourceAsync(sourceId, after, size + 1, context.RequestAborted);
        if (!result.IsKnownCommitted || result.Value is not { } source)
        {
            return ManagementConfigurationHttp.Problem(context, 503, result.Code ?? "management.configuration.unavailable");
        }
        var items = new List<ManagementConfigurationPublicationMetadata>();
        int remaining = 1024 * 1024 - 4096;
        foreach (ManagementConfigurationPublicationMetadata publication in source.Publications.Items)
        {
            int bytes = ManagementConfigurationWire.EncodeResponse(publication).Length + 1;
            if (items.Count == size || bytes > remaining)
            {
                break;
            }
            items.Add(publication);
            remaining -= bytes;
        }
        if (source.Publications.Items.Count > 0 && items.Count == 0)
        {
            return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.response_too_large");
        }
        context.Response.Headers.ETag = source.ETag;
        return ManagementConfigurationHttp.Json(source with
        {
            Publications = new(items.AsReadOnly(), items.Count < source.Publications.Items.Count
            ? ManagementHttp.Cursor(context, cursors, endpoint, null, size, items[^1].PublicationId) : null)
        });
    }

    private static async Task<IResult> PublishConfiguration(HttpContext context, string sourceId, IManagementConfigurationBackend backend)
    {
        ManagementConfigurationDefinition? source = await VisibleSource(context, backend, sourceId, ManagementConfigurationCapabilities.Publish);
        if (source is null)
        {
            return ManagementConfigurationHttp.Problem(context, 404, "management.configuration.not_found");
        }
        if (!ManagementConfigurationHttp.MutationHeaders(context, out string key, out string etag, out IResult? refusal))
        {
            return refusal!;
        }
        ManagementConfigurationPublicationCommand? command = await ManagementConfigurationHttp.Read<ManagementConfigurationPublicationCommand>(context);
        return command is null ? ManagementConfigurationHttp.Problem(context, 400, "management.configuration.invalid_request")
            : ManagementConfigurationHttp.Operation(context, await backend.PublishAsync(source, key, etag, command, context.RequestAborted));
    }

    private static async Task<IResult> ConfigurationPublication(HttpContext context, string publicationId, IManagementConfigurationBackend backend)
    {
        if (context.Request.Query.Count != 0 || !ManagementWire.IsUuid(publicationId))
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.invalid_request");
        }
        ManagementStoreResult<string> resolved = await backend.ResolvePublicationSourceAsync(publicationId, context.RequestAborted);
        if (!resolved.IsKnownCommitted || resolved.Value is not { } sourceId)
        {
            return ManagementConfigurationHttp.Problem(context, resolved.Code == "management.configuration.not_found" ? 404 : 503,
                resolved.Code ?? "management.configuration.unavailable");
        }
        if (!await ManagementConfigurationHttp.Allowed(context, ManagementConfigurationCapabilities.ContentRead,
            ManagementConfigurationCapabilities.SourceResource, sourceId))
        {
            return ManagementConfigurationHttp.Problem(context, 404, "management.configuration.not_found");
        }
        ManagementStoreResult<ManagementConfigurationPublication> result = await backend.ReadPublicationAsync(publicationId, context.RequestAborted);
        if (!result.IsKnownCommitted || result.Value is not { } publication)
        {
            return ManagementConfigurationHttp.Problem(context, result.Code == "management.configuration.not_found" ? 404 : 503,
                result.Code ?? "management.configuration.unavailable");
        }
        return publication.SourceId == sourceId ? ManagementConfigurationHttp.Json(publication)
            : ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
    }

    private static async Task<IResult> ConfigurationPublicationOperation(HttpContext context, string sourceId, string operationKey,
        IManagementConfigurationBackend backend)
    {
        ManagementConfigurationDefinition? source = await VisibleSource(context, backend, sourceId, ManagementConfigurationCapabilities.OperationsRead);
        if (source is null)
        {
            return ManagementConfigurationHttp.Problem(context, 404, "management.configuration.not_found");
        }
        return context.Request.Query.Count != 0 || !ManagementWire.IsOperationKey(operationKey)
            ? ManagementConfigurationHttp.Problem(context, 400, "management.configuration.invalid_request")
            : ManagementConfigurationHttp.Operation(context, await backend.LookupPublicationAsync(source, operationKey, context.RequestAborted));
    }
}
