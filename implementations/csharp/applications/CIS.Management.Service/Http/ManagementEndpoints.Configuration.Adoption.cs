using CIS.Management.Service.Backends;
using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;
using System.Globalization;

namespace CIS.Management.Service.Http;

internal static partial class ManagementEndpoints
{
    private static readonly ManagementFeature[] AdoptionFeatures =
    [
        new("reportConfigurationStatus", 1, "PUT", "/registrations/{registrationId}/configuration-status", ["management.registrations.write"]),
        new("getConfigurationStatus", 1, "GET", "/applications/{applicationId}/configuration-status", [ManagementConfigurationCapabilities.AdoptionRead])
    ];

    static partial void MapConfigurationAdoption(RouteGroupBuilder group)
    {
        group.MapPut("/registrations/{registrationId}/configuration-status", ReportConfigurationStatus);
        group.MapGet("/applications/{applicationId}/configuration-status", ConfigurationAdoption);
    }

    private static async Task<IResult> ReportConfigurationStatus(HttpContext context, string registrationId,
        IManagementConfigurationBackend backend)
    {
        ManagementRegistrationOwner? owner = await RegistrationOwner(context);
        if (owner is null)
        {
            return ManagementConfigurationHttp.Problem(context, 403, "management.registration.forbidden");
        }
        if (!ManagementWire.IsUuid(registrationId) || context.Request.Query.Count != 0)
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.invalid_request");
        }
        ManagementConfigurationStatusReport? report = await ManagementConfigurationHttp.Read<ManagementConfigurationStatusReport>(context);
        if (report is null)
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.invalid_request");
        }
        ManagementStoreResult<ManagementReportReceipt> result = await backend.ReportConfigurationStatusAsync(owner,
            registrationId, report, context.RequestAborted);
        if (result.IsKnownCommitted && result.Value is { } receipt)
        {
            return ManagementHttp.Json(receipt);
        }
        return ManagementConfigurationHttp.Problem(context, result.Code switch
        {
            "management.registration.not_found" or "management.configuration.not_found" => 404,
            "management.registration.closed" or "management.sequence.conflict" or "management.sequence.obsolete" => 409,
            "management.configuration.unsupported" => 400,
            _ => 503
        }, result.Code ?? "management.configuration.unavailable");
    }

    private static async Task<IResult> ConfigurationAdoption(HttpContext context, string applicationId,
        IManagementBackend management, IManagementConfigurationBackend backend, ManagementCursor cursors)
    {
        // Resolve and authorize the actual application before paging any recipients or loading report bodies.
        if (await VisibleApplication(context, applicationId, management, ManagementConfigurationCapabilities.AdoptionRead) is null)
        {
            return ManagementConfigurationHttp.Problem(context, 404, "management.configuration.not_found");
        }
        const string endpoint = "configuration.adoption";
        int size = 100;
        if (context.Request.Query.Any(pair => pair.Key is not ("pageSize" or "cursor"))
            || context.Request.Query["pageSize"].Count > 1 || context.Request.Query["cursor"].Count > 1
            || context.Request.Query.TryGetValue("pageSize", out var raw)
                && (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out size) || size is < 1 or > 500))
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.cursor_invalid");
        }
        string actor = ManagementActorScope.Create(ManagementHttp.Caller(context)
            ?? throw new InvalidOperationException("Native caller scope is required."), applicationId);
        if (!cursors.TryDecodeConfigurationAdoption(context.Request.Query["cursor"].FirstOrDefault(), endpoint, actor, size, out string? after))
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.cursor_invalid");
        }
        ManagementStoreResult<IReadOnlyList<ManagementConfigurationAdoptionRow>> result = await backend.ReadConfigurationAdoptionAsync(
            applicationId, after, size, context.RequestAborted);
        if (!result.IsKnownCommitted || result.Value is not { } rows)
        {
            return ManagementConfigurationHttp.Problem(context, 503, result.Code ?? "management.configuration.unavailable");
        }
        var page = new ManagementPage<ManagementConfigurationAdoptionEntry>(Array.AsReadOnly(rows.Select(row => row.Entry).ToArray()),
            result.HasMore && rows.Count != 0 ? cursors.EncodeConfigurationAdoption(endpoint, actor, size, rows[^1].Position) : null);
        return ManagementConfigurationHttp.Json(page);
    }
}
