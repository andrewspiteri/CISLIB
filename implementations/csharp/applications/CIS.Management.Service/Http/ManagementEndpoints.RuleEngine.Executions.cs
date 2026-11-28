using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;

namespace CIS.Management.Service.Http;

internal static partial class ManagementEndpoints
{
    private static async Task<IResult> RuleEngineExecutions(HttpContext context, string applicationId,
        PostgreSqlManagementStore store, ManagementCursor cursors)
    {
        const string endpoint = "rule-engine.executions";
        string[] keys = ["rulesetId", "executionId", "version", "instanceId", "status", "startedFromUtc", "startedThroughUtc", "pageSize", "cursor"];
        if (!RuleEngineScope(context, applicationId, keys, out string ruleset)
            || !RuleEnginePageSize(context, out int size) || ManagementConfigurationHttp.HasRequestBody(context)
            || !TryExecutionFilter(context, out RuleEngineExecutionFilter filter))
        {
            return RuleEngineProblem(context, 400, "management.rule-engine.invalid_request");
        }
        RuleEngineResource? resource = await VisibleRuleExecutionResource(context, applicationId, ruleset, store, ManagementRuleEngineOperations.ReadExecutions);
        if (resource is null)
        {
            return RuleEngineProblem(context, 404, "management.rule-engine.not_found");
        }
        string filterDigest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(filter)));
        string actor = ManagementDiscoveryQuery.Scope(ManagementHttp.Caller(context)!,
            resource.Key.ToString("D") + ":" + filterDigest, principal: context.User);
        if (!cursors.TryDecodeRuleEngineExecution(context.Request.Query["cursor"].FirstOrDefault(), endpoint, actor, size,
            out RuleEngineExecutionPosition? position))
        {
            return RuleEngineProblem(context, 400, "management.rule-engine.cursor_invalid");
        }
        ManagementStoreResult<IReadOnlyList<ManagementRuleExecutionSummary>> result = await store.ListRuleEngineExecutionsAsync(
            resource, filter, position, size + 1, context.RequestAborted);
        if (!result.IsKnownCommitted || result.Value is not { } rows)
        {
            return RuleEngineProblem(context, 503, result.Code ?? "management.rule-engine.unavailable");
        }
        var items = new List<ManagementRuleExecutionSummary>();
        int remaining = ManagementRuleEngineWire.MaximumBodyBytes - 8192;
        foreach (ManagementRuleExecutionSummary row in rows.Take(size))
        {
            int bytes = ManagementRuleEngineWire.EncodeResponse(new ManagementPage<ManagementRuleExecutionSummary>([row])).Length;
            if (bytes > remaining)
            {
                break;
            }
            remaining -= bytes;
            items.Add(row);
        }
        if (items.Count == 0 && rows.Count != 0)
        {
            return RuleEngineProblem(context, 503, "management.rule-engine.response_too_large");
        }
        string? next = null;
        if (items.Count < rows.Count)
        {
            ManagementRuleExecutionSummary last = items[^1];
            var key = new RuleEngineExecutionPosition(DateTimeOffset.Parse(last.StartedAtUtc, CultureInfo.InvariantCulture).UtcTicks,
                Guid.Parse(last.ExecutionId));
            next = cursors.EncodeRuleEngineExecution(endpoint, actor, size, key);
        }
        return RuleEngineJson(new ManagementPage<ManagementRuleExecutionSummary>(items.AsReadOnly(), next));
    }

    private static async Task<IResult> RuleEngineExecution(HttpContext context, string applicationId, string executionId,
        PostgreSqlManagementStore store)
    {
        if (!RuleEngineScope(context, applicationId, ["rulesetId"], out string ruleset)
            || !ManagementWire.IsUuid(executionId) || ManagementConfigurationHttp.HasRequestBody(context))
        {
            return RuleEngineProblem(context, 400, "management.rule-engine.invalid_request");
        }
        RuleEngineResource? resource = await VisibleRuleExecutionResource(context, applicationId, ruleset, store, ManagementRuleEngineOperations.ReadExecutionDetail);
        return resource is null ? RuleEngineProblem(context, 404, "management.rule-engine.not_found")
            : RuleEngineRead(context, await store.ReadRuleEngineExecutionAsync(resource, Guid.Parse(executionId), context.RequestAborted));
    }

    private static async Task<RuleEngineResource?> VisibleRuleExecutionResource(HttpContext context, string applicationId,
        string ruleset, PostgreSqlManagementStore store, string capability)
    {
        if (!await ManagementHttp.Allowed(context, capability, applicationId))
        {
            return null;
        }
        ManagementStoreResult<RuleEngineResource> result = await store.ReadRetainedRuleEngineResourceAsync(Guid.Parse(applicationId),
            ruleset, context.RequestAborted);
        if (result.IsKnownCommitted && result.Value is { } resource)
        {
            return resource;
        }
        if (result.Code == "management.rule-engine.not_found")
        {
            return null;
        }
        throw new ManagementRequestException(503, result.Code ?? "management.rule-engine.unavailable");
    }

    private static bool TryExecutionFilter(HttpContext context, out RuleEngineExecutionFilter filter)
    {
        filter = null!;
        string? execution = context.Request.Query["executionId"].FirstOrDefault();
        string? version = context.Request.Query["version"].FirstOrDefault();
        string? instance = context.Request.Query["instanceId"].FirstOrDefault();
        string? status = context.Request.Query["status"].FirstOrDefault();
        string? from = context.Request.Query["startedFromUtc"].FirstOrDefault();
        string? through = context.Request.Query["startedThroughUtc"].FirstOrDefault();
        if ((execution is not null && !ManagementWire.IsUuid(execution))
            || (version is not null && !ManagementWire.IsUuid(version))
            || (instance is not null && !ManagementWire.IsScalarText(instance, 255))
            || status is not (null or "Completed" or "Failed" or "Cancelled" or "Incomplete")
            || !TryExecutionTicks(from, out long? minimum) || !TryExecutionTicks(through, out long? maximum)
            || minimum > maximum)
        {
            return false;
        }
        filter = new(execution is null ? null : Guid.Parse(execution), version is null ? null : Guid.Parse(version),
            instance, status, minimum, maximum);
        return true;
    }

    private static bool TryExecutionTicks(string? value, out long? result)
    {
        result = null;
        if (value is null)
        {
            return true;
        }
        if (!ManagementWire.IsTimestamp(value)
            || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset date))
        {
            return false;
        }
        result = date.UtcTicks;
        return true;
    }

}
