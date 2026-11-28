using CIS.Management.Service.Backends;
using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;

namespace CIS.Management.Service.Http;

internal static partial class ManagementEndpoints
{
    private static async Task<IResult> RuleEngineDeploymentInstances(HttpContext context, string applicationId,
        PostgreSqlManagementStore store, ManagementCursor cursors)
    {
        const string endpoint = "rule-engine.deployment-instances";
        if (!RuleEngineScope(context, applicationId, ["rulesetId", "pageSize", "cursor"], out string ruleset)
            || !RuleEnginePageSize(context, out int size) || ManagementConfigurationHttp.HasRequestBody(context))
        {
            return RuleEngineProblem(context, 400, "management.rule-engine.invalid_request");
        }
        RuleEngineResource? resource = await VisibleRuleEngine(context, applicationId, ruleset, store, ManagementRuleEngineOperations.ReadDeployment);
        if (resource is null)
        {
            return RuleEngineProblem(context, 404, "management.rule-engine.not_found");
        }
        ManagementStoreResult<ManagementConfigurationBindingResource> bindingResult = await store.ReadRuleEngineDeploymentBindingAsync(
            resource, context.RequestAborted);
        if (!bindingResult.IsKnownCommitted || bindingResult.Value is not { } binding)
        {
            return DeploymentInstanceReadRefusal(context, bindingResult.Code);
        }
        string actor = ManagementDiscoveryQuery.Scope(ManagementHttp.Caller(context)!,
            resource.Key.ToString("D") + ":" + binding.BindingId, principal: context.User);
        if (!cursors.TryDecodeConfigurationAdoption(context.Request.Query["cursor"].FirstOrDefault(), endpoint, actor, size, out string? after))
        {
            return RuleEngineProblem(context, 400, "management.rule-engine.cursor_invalid");
        }
        ManagementStoreResult<IReadOnlyList<ManagementConfigurationAdoptionRow>> status = await store.ReadBindingConfigurationAdoptionAsync(
            binding, after, size, context.RequestAborted);
        if (!status.IsKnownCommitted || status.Value is not { } rows)
        {
            return DeploymentInstanceReadRefusal(context, status.Code);
        }
        Guid[] releases = rows.SelectMany(row => DeploymentInstanceReleases(row.Entry)).Distinct().ToArray();
        IReadOnlyDictionary<Guid, Guid> versions = new Dictionary<Guid, Guid>();
        if (releases.Length > 0)
        {
            ManagementStoreResult<IReadOnlyDictionary<Guid, Guid>> history = await store.ReadRuleEngineReleaseVersionsAsync(resource,
                releases, context.RequestAborted);
            if (!history.IsKnownCommitted || history.Value is not { } mappings)
            {
                return DeploymentInstanceReadRefusal(context, history.Code);
            }
            versions = mappings;
        }

        var items = new List<ManagementRuleDeploymentInstance>(rows.Count);
        // The strict single-entry encoding includes more overhead than its contribution to the page.
        // Reserve the signed cursor and envelope separately and continue at the last emitted row.
        int remainingBytes = ManagementRuleEngineWire.MaximumBodyBytes - 8192;
        foreach (ManagementConfigurationAdoptionRow row in rows)
        {
            ManagementConfigurationAdoptionEntry entry = row.Entry;
            var item = new ManagementRuleDeploymentInstance(resource.RulesetId, entry,
                Version(entry.Desired), Version(entry.Reported?.Desired), Version(entry.Reported?.Applied), Version(entry.Reported?.LastSuccessful));
            int bytes = ManagementRuleEngineWire.EncodeResponse(new ManagementPage<ManagementRuleDeploymentInstance>([item])).Length;
            if (bytes > remainingBytes)
            {
                break;
            }
            remainingBytes -= bytes;
            items.Add(item);
        }
        if (items.Count == 0 && (rows.Count != 0 || status.HasMore))
        {
            return RuleEngineProblem(context, 503, "management.configuration.response_too_large");
        }
        string? next = status.HasMore || items.Count < rows.Count
            ? cursors.EncodeConfigurationAdoption(endpoint, actor, size, rows[items.Count - 1].Position) : null;
        return RuleEngineJson(new ManagementPage<ManagementRuleDeploymentInstance>(items.AsReadOnly(), next));

        string? Version(ManagementConfigurationVersion? release)
            => release is not null && versions.TryGetValue(Guid.Parse(release.ReleaseId), out Guid version) ? version.ToString("D") : null;
    }

    private static IEnumerable<Guid> DeploymentInstanceReleases(ManagementConfigurationAdoptionEntry entry)
    {
        foreach (ManagementConfigurationVersion? release in new[] { entry.Desired, entry.Reported?.Desired,
            entry.Reported?.Applied, entry.Reported?.LastSuccessful })
        {
            if (release is not null)
            {
                yield return Guid.Parse(release.ReleaseId);
            }
        }
    }

    private static IResult DeploymentInstanceReadRefusal(HttpContext context, string? code)
        => RuleEngineProblem(context, code is "management.rule-engine.not_found" or "management.configuration.not_found" ? 404 : 503,
            code ?? "management.rule-engine.unavailable");
}
