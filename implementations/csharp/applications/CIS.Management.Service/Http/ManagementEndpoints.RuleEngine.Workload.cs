using CIS.Management.Service.Persistence;
using CIS.Management.Wire;

namespace CIS.Management.Service.Http;

internal static partial class ManagementEndpoints
{
    private const string RuleEngineWorkloadVersionPath = "/registrations/{registrationId}/configuration/{configurationId}/rule-versions/{versionId}";

    private static async Task<IResult> RegistrationRuleEngineVersion(HttpContext context, string registrationId, string configurationId,
        string versionId, PostgreSqlManagementStore store)
    {
        ManagementRegistrationOwner? owner = await RegistrationOwner(context);
        if (owner is null)
        {
            return ManagementConfigurationHttp.Problem(context, 403, "management.registration.forbidden");
        }
        string schema = context.Request.Query["schemaVersion"].ToString();
        if (!ManagementWire.IsUuid(registrationId) || !ManagementWire.IsUuid(versionId)
            || !PostgreSqlManagementStore.SnapshotPortableId(configurationId)
            || context.Request.Query.Count != 1 || context.Request.Query.Keys.Any(key => key != "schemaVersion")
            || context.Request.Query["schemaVersion"].Count != 1 || !PostgreSqlManagementStore.SnapshotSchema(schema, out _)
            || ManagementConfigurationHttp.HasRequestBody(context))
        {
            return ManagementConfigurationHttp.Problem(context, 400, "management.configuration.invalid_request");
        }
        ManagementStoreResult<ManagementRuleWorkloadVersion> result = await store.ReadRuleEngineWorkloadVersionAsync(owner,
            registrationId, configurationId, schema, versionId, context.RequestAborted);
        return result.IsKnownCommitted && result.Value is { } version ? RuleEngineJson(version)
            : ManagementConfigurationHttp.Problem(context, result.Code == "management.configuration.not_found" ? 404
                : result.Code == "management.registration.closed" ? 409 : 503, result.Code ?? "management.configuration.unavailable");
    }
}
