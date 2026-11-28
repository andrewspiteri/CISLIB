using System.Security.Cryptography;
using System.Text.Json;
using CIS.Management.Service.Backends;
using CIS.Management.Wire;

namespace CIS.Management.Service.Http;

internal static partial class ManagementEndpoints
{
    // This closed projection is the original v1 API contract, not the first64 rows of a growing inventory.
    private static readonly HashSet<string> LegacyApiOperationIds = new(StringComparer.Ordinal)
    {
        "applyConfigurationCatalog", "closeRegistration", "createConfiguration", "createConfigurationRelease",
        "getApplication", "getApplicationCapabilities", "getApplicationConfigurationDefinitions",
        "getConfigurationAssignmentOperation", "getConfigurationAssignments", "getConfigurationBindingActions",
        "getConfigurationBindingStatus", "getConfigurationCatalog", "getConfigurationCatalogOperation",
        "getConfigurationCohorts", "getConfigurationCreationContext", "getConfigurationCreationOperation",
        "getConfigurationDefinitions", "getConfigurationEditorContext", "getConfigurationPublication",
        "getConfigurationPublicationOperation", "getConfigurationRelease", "getConfigurationReleaseOperation",
        "getConfigurationSource", "getConfigurationSourceActions", "getConfigurationStatus", "getConfigurationSummaries",
        "getMetadataOperation", "getRegistrationConfiguration", "getRegistrationControl", "getServiceInfo",
        "listApplications", "listConfigurationReleases", "listEnvironments", "listInstances", "listInventory",
        "previewConfiguration", "previewConfigurationCandidate", "publishConfigurationSource", "queryApplications",
        "registerInstance", "replaceApplicationMetadata", "replaceConfigurationAssignments", "replaceInventory",
        "reportConfigurationStatus", "reportObservation", "validateConfiguration"
    };

    private static readonly ManagementFeature[] ApiDiscoveryFeatures =
    [
        new("listServiceFeatures", 1, "GET", "/service-features", []),
        new("listApplicationApiCapabilities", 1, "GET", "/applications/{applicationId}/api-capabilities", ["management.applications.read"])
    ];

    private static async Task<IReadOnlyList<ManagementFeature>> LegacyApiFeatures(IManagementBackend backend,
        IManagementConfigurationBackend configuration, CancellationToken cancellationToken)
        => ValidateLegacyApiFeatures(Features.Concat(DiscoveryFeatures(backend))
            .Concat(await ConfigurationFeatures(configuration, cancellationToken)));

    private static IReadOnlyList<ManagementFeature> ValidateLegacyApiFeatures(IEnumerable<ManagementFeature> features)
    {
        ManagementFeature[] selected = features.ToArray();
        if (selected.Length > 64 || selected.Any(feature => !LegacyApiOperationIds.Contains(feature.OperationId))
            || selected.Select(feature => feature.OperationId).Distinct(StringComparer.Ordinal).Count() != selected.Length)
        {
            throw new InvalidOperationException("The installed legacy-v1 operation projection is invalid.");
        }
        return Array.AsReadOnly(selected);
    }

    private static async Task<IReadOnlyList<ManagementFeature>> CompleteApiFeatures(IManagementBackend backend,
        IManagementConfigurationBackend configuration, ManagementServiceOptions options, CancellationToken cancellationToken)
    {
        ManagementFeature[] installed = (await LegacyApiFeatures(backend, configuration, cancellationToken))
            .Concat(ApiDiscoveryFeatures)
            .Concat(options.Configuration?.Manifest.RuleEngine is { Count: > 0 } ? RuleEngineApiFeatures : [])
            .OrderBy(feature => feature.OperationId, StringComparer.Ordinal).ToArray();
        // A finite installed descriptor inventory; page size and whole response byte bounds remain independently enforced.
        if (installed.Length > 128 || installed.Select(feature => feature.OperationId).Distinct(StringComparer.Ordinal).Count() != installed.Length)
        {
            throw new InvalidOperationException("The complete installed API inventory is invalid.");
        }
        return Array.AsReadOnly(installed);
    }

    private static string ApiFeatureScope(HttpContext context, IReadOnlyList<ManagementFeature> features, string? applicationId)
    {
        string inventoryDigest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(features)));
        return ManagementDiscoveryQuery.Scope(ManagementHttp.Caller(context)!, applicationId, principal: context.User)
            + ":complete-v1:" + inventoryDigest;
    }

    private static async Task<IResult> ServiceFeatures(HttpContext context, IManagementBackend backend,
        IManagementConfigurationBackend configuration, ManagementCursor cursors, ManagementServiceOptions options)
    {
        IReadOnlyList<ManagementFeature> installed = await CompleteApiFeatures(backend, configuration, options, context.RequestAborted);
        string scope = ApiFeatureScope(context, installed, null);
        if (!ManagementDiscoveryQuery.TryReadApiFeatures(context, out int size, out string? cursor)
            || !cursors.TryDecodeApiFeature(cursor, "service-features", scope, size, out string? after))
        {
            return ManagementHttp.Problem(context, 400, "management.cursor.invalid");
        }
        ManagementFeature[] remaining = installed.Where(feature => after is null || string.CompareOrdinal(feature.OperationId, after) > 0).ToArray();
        ManagementFeature[] items = remaining.Take(size).ToArray();
        string? next = remaining.Length > size ? cursors.EncodeApiFeature("service-features", scope, size, items[^1].OperationId) : null;
        return ManagementHttp.Json(new ManagementServiceFeaturePage("complete-v1", Array.AsReadOnly(items), next), limits: backend.WireLimits);
    }

    private static async Task<IResult> ApplicationApiCapabilities(HttpContext context, string applicationId,
        IManagementBackend backend, IManagementConfigurationBackend configuration, ManagementCursor cursors, ManagementServiceOptions options)
    {
        if (await VisibleApplication(context, applicationId, backend) is null)
        {
            return ManagementHttp.Problem(context, 404, "management.not_found");
        }
        IReadOnlyList<ManagementFeature> installed = await CompleteApiFeatures(backend, configuration, options, context.RequestAborted);
        string scope = ApiFeatureScope(context, installed, applicationId);
        if (!ManagementDiscoveryQuery.TryReadApiFeatures(context, out int size, out string? cursor)
            || !cursors.TryDecodeApiFeature(cursor, "application-api-capabilities", scope, size, out string? after))
        {
            return ManagementHttp.Problem(context, 400, "management.cursor.invalid");
        }
        ManagementFeature[] remaining = installed.Where(feature => after is null || string.CompareOrdinal(feature.OperationId, after) > 0).ToArray();
        var items = new List<ManagementApiCapability>();
        foreach (ManagementFeature feature in remaining.Take(size))
        {
            bool allowed = true;
            foreach (string capability in feature.RequiredCapabilities)
            {
                // Source/catalog/binding authorization requires that exact resolved resource, not an application hint.
                allowed &= !capability.StartsWith("management.configuration.", StringComparison.Ordinal)
                    && await ManagementHttp.Allowed(context, capability, capability == "management.registrations.write" ? null : applicationId);
            }
            items.Add(new(feature, allowed));
        }
        string? next = remaining.Length > size ? cursors.EncodeApiFeature("application-api-capabilities", scope, size, items[^1].Feature.OperationId) : null;
        return ManagementHttp.Json(new ManagementApplicationApiCapabilityPage(applicationId, "complete-v1", items.AsReadOnly(), next), limits: backend.WireLimits);
    }
}
