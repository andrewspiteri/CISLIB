using CIS.Management.Service.Backends;
using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;

namespace CIS.Management.Service.Http;

internal static partial class ManagementEndpoints
{
    private static readonly ManagementFeature[] ConfigurationCohortFeatures =
    [
        new("getConfigurationCohorts", 1, "GET", "/applications/{applicationId}/configuration/{configurationId}/cohorts",
            [ManagementConfigurationCapabilities.DefinitionsRead])
    ];

    private static void MapConfigurationCohorts(RouteGroupBuilder group)
    {
        group.MapGet("/applications/{applicationId}/configuration/{configurationId}/cohorts", ConfigurationCohorts);
    }

    private static async Task<IResult> ConfigurationCohorts(HttpContext context, string applicationId, string configurationId,
        IManagementConfigurationBackend backend, ManagementCursor cursors)
    {
        if (!ManagementWire.IsUuid(applicationId) || !ManagementWire.IsOperationKey(configurationId)
            || !ManagementDiscoveryQuery.TryReadConfigurationCohorts(context, out string schema, out int size, out string? cursor))
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
        if (backend is not IManagementConfigurationCohortBackend cohorts)
        {
            return ManagementConfigurationHttp.Problem(context, 501, "management.configuration.cohorts.unsupported");
        }
        string endpoint = "configuration.cohorts." + binding.BindingId + "." + schema;
        string actor = ManagementDiscoveryQuery.Scope(ManagementHttp.Caller(context)
            ?? throw new InvalidOperationException("Native caller scope is required."), binding.BindingId, principal: context.User);
        if (!cursors.TryDecodeConfigurationCohort(cursor, endpoint, actor, size, out string? after))
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.cursor_invalid");
        }
        ManagementStoreResult<IReadOnlyList<ManagementConfigurationCohortChoice>> result = await cohorts.ReadCohortsAsync(binding,
            after, size + 1, context.RequestAborted);
        if (!result.IsKnownCommitted || result.Value is not { } rows || result.Code is not null)
        {
            return ManagementConfigurationHttp.Problem(context, 503, result.Code ?? "management.configuration.unavailable");
        }
        int declaredCount = rows.Count;
        if (declaredCount < 0 || declaredCount > size + 1)
        {
            return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
        }
        var captured = new List<ManagementConfigurationCohortChoice>(declaredCount);
        using (IEnumerator<ManagementConfigurationCohortChoice> iterator = rows.GetEnumerator())
        {
            for (int index = 0; index < declaredCount; index++)
            {
                if (!iterator.MoveNext() || iterator.Current is not { } row)
                {
                    return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
                }
                captured.Add(row);
            }
            if (iterator.MoveNext())
            {
                return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
            }
        }
        string? previous = after;
        foreach (ManagementConfigurationCohortChoice row in captured)
        {
            if (!ManagementWire.IsOperationKey(row.CohortId) || !ManagementWire.IsScalarText(row.DisplayName, 256)
                || previous is not null && StringComparer.Ordinal.Compare(previous, row.CohortId) >= 0)
            {
                return ManagementConfigurationHttp.Problem(context, 503, "management.configuration.unavailable");
            }
            previous = row.CohortId;
        }
        ManagementConfigurationCohortChoice[] visible = captured.Take(size).ToArray();
        return ManagementConfigurationHttp.Cohorts(new(Array.AsReadOnly(visible), captured.Count > size
            ? cursors.EncodeConfigurationCohort(endpoint, actor, size, visible[^1].CohortId) : null));
    }
}
