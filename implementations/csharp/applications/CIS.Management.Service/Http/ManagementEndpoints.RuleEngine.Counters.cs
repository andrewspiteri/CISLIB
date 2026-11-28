using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;
using CIS.Messaging;

namespace CIS.Management.Service.Http;

internal static partial class ManagementEndpoints
{
    private static async Task<IResult> RuleEngineCounters(HttpContext context, string applicationId, PostgreSqlManagementStore store,
        ManagementServiceOptions options, IMessageTransport transport, ManagementCursor cursors)
    {
        const string endpoint = "rule-engine.counters";
        if (!RuleEngineScope(context, applicationId, ["rulesetId", "pageSize", "cursor"], out string ruleset)
            || !RuleEnginePageSize(context, out int size) || ManagementConfigurationHttp.HasRequestBody(context))
        {
            return RuleEngineProblem(context, 400, "management.rule-engine.invalid_request");
        }
        RuleEngineResource? resource = await VisibleRuleEngine(context, applicationId, ruleset, store, ManagementRuleEngineOperations.ReadCounters);
        if (resource is null)
        {
            return RuleEngineProblem(context, 404, "management.rule-engine.not_found");
        }
        string actor = ManagementDiscoveryQuery.Scope(ManagementHttp.Caller(context)!,
            resource.Key.ToString("D"), principal: context.User);
        if (!cursors.TryDecodeRuleEngineCounter(context.Request.Query["cursor"].FirstOrDefault(), endpoint, actor, size, out string? after))
        {
            return RuleEngineProblem(context, 400, "management.rule-engine.cursor_invalid");
        }
        bool live = transport.IsReady && options.Delivery?.RuleEngineAuditSources.Any(source =>
            source.ManagedApplication == resource.ManagedApplication && source.ProducerEnvironment == resource.Environment) == true;
        ManagementStoreResult<ManagementRuleCounterPage> result = await store.ReadRuleEngineCountersAsync(resource, after, size + 1,
            live, context.RequestAborted);
        if (!result.IsKnownCommitted || result.Value is not { } page)
        {
            return RuleEngineProblem(context, 503, result.Code ?? "management.rule-engine.counters.unavailable");
        }
        ManagementRuleCounterItem[] items = page.Items.Take(size).ToArray();
        return RuleEngineJson(page with
        {
            Items = Array.AsReadOnly(items),
            NextCursor = page.Items.Count > size ? cursors.EncodeRuleEngineCounter(endpoint, actor, size, items[^1].RuleId) : null
        });
    }

    private static async Task<IResult> RequestRuleEngineCounterReset(HttpContext context, string applicationId,
        PostgreSqlManagementStore store, ManagementRuleEngineService service)
    {
        string key = context.Request.Headers["Idempotency-Key"].ToString();
        if (!RuleEngineScope(context, applicationId, ["rulesetId"], out string ruleset)
            || context.Request.Headers["Idempotency-Key"].Count != 1 || !ManagementWire.IsOperationKey(key))
        {
            return RuleEngineProblem(context, 400, "management.rule-engine.invalid_request");
        }
        RuleEngineResource? resource = await VisibleRuleEngine(context, applicationId, ruleset, store, ManagementRuleEngineOperations.ResetCounters);
        if (resource is null)
        {
            return RuleEngineProblem(context, 404, "management.rule-engine.not_found");
        }
        ManagementRuleCounterResetCommand? command = await ManagementConfigurationHttp.Read<ManagementRuleCounterResetCommand>(
            context, ManagementRuleEngineWire.DecodeCounterResetRequest);
        return command is null ? RuleEngineProblem(context, 400, "management.rule-engine.invalid_request")
            : RuleCounterResetResponse(context, await service.ResetCountersAsync(resource, key, command, context.RequestAborted));
    }

    private static async Task<IResult> RuleEngineCounterResetOperation(HttpContext context, string applicationId, string operationKey,
        PostgreSqlManagementStore store, ManagementRuleEngineService service)
    {
        if (!RuleEngineScope(context, applicationId, ["rulesetId"], out string ruleset) || !ManagementWire.IsOperationKey(operationKey)
            || ManagementConfigurationHttp.HasRequestBody(context))
        {
            return RuleEngineProblem(context, 400, "management.rule-engine.invalid_request");
        }
        RuleEngineResource? resource = await VisibleRuleEngine(context, applicationId, ruleset, store, ManagementRuleEngineOperations.ReadOperation);
        return resource is null ? RuleEngineProblem(context, 404, "management.rule-engine.not_found")
            : RuleCounterResetResponse(context, await service.LookupCounterResetAsync(resource, operationKey, context.RequestAborted));
    }

    private static async Task<IResult> RuleEngineCounterResetOutcomes(HttpContext context, string applicationId, string operationKey,
        PostgreSqlManagementStore store, ManagementRuleEngineService service, ManagementCursor cursors)
    {
        const string endpoint = "rule-engine.counter-reset-outcomes";
        if (!RuleEngineScope(context, applicationId, ["rulesetId", "pageSize", "cursor"], out string ruleset)
            || !RuleEnginePageSize(context, out int size) || !ManagementWire.IsOperationKey(operationKey)
            || ManagementConfigurationHttp.HasRequestBody(context))
        {
            return RuleEngineProblem(context, 400, "management.rule-engine.invalid_request");
        }
        RuleEngineResource? resource = await VisibleRuleEngine(context, applicationId, ruleset, store, ManagementRuleEngineOperations.ReadOperation);
        if (resource is null)
        {
            return RuleEngineProblem(context, 404, "management.rule-engine.not_found");
        }
        string actor = ManagementDiscoveryQuery.Scope(ManagementHttp.Caller(context)!,
            resource.Key.ToString("D") + ":" + operationKey, principal: context.User);
        if (!cursors.TryDecodeRuleEngineCounter(context.Request.Query["cursor"].FirstOrDefault(), endpoint, actor, size, out string? after))
        {
            return RuleEngineProblem(context, 400, "management.rule-engine.cursor_invalid");
        }
        RuleEngineCounterResetResponse original = await service.LookupCounterResetAsync(resource, operationKey, context.RequestAborted);
        if (original.Status != 200 || original.Result?.Result is null || original.Operation is not { } operation)
        {
            return RuleEngineProblem(context, original.Status == 200 ? 404 : original.Status,
                original.Code ?? "management.rule-engine.not_found");
        }
        ManagementStoreResult<IReadOnlyList<ManagementRuleCounterResetOutcome>> result = await store.ReadRuleCounterResetOutcomesAsync(
            resource, operation, after, size + 1, context.RequestAborted);
        if (!result.IsKnownCommitted || result.Value is not { } values)
        {
            return RuleEngineProblem(context, result.Code == "management.rule-engine.not_found" ? 404 : 503,
                result.Code ?? "management.rule-engine.counters.unavailable");
        }
        ManagementRuleCounterResetOutcome[] items = values.Take(size).ToArray();
        string? next = values.Count > size ? cursors.EncodeRuleEngineCounter(endpoint, actor, size, items[^1].RuleId) : null;
        return RuleEngineJson(new ManagementRuleCounterResetOutcomesPage(applicationId, resource.Environment, ruleset, operation,
            Array.AsReadOnly(items), next));
    }

    private static IResult RuleCounterResetResponse(HttpContext context, RuleEngineCounterResetResponse response)
    {
        if (response.Status == 200 && response.Result is { } result)
        {
            return RuleEngineJson(result);
        }
        string code = response.Code ?? "management.rule-engine.counters.unavailable";
        return RuleEngineJson(new ManagementRuleEngineProblem("urn:cis:management:problem:" + code,
            "Rule Engine request could not be completed", response.Status, code,
            CorrelationId: ManagementWire.IsScalarText(context.TraceIdentifier, 128) ? context.TraceIdentifier : null,
            Operation: response.Operation)
        {
            CounterResetResult = response.Result
        }, response.Status);
    }
}
