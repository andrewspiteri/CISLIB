using System.Globalization;
using CIS.Management.Service.Backends;
using CIS.Management.Service.Configuration;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;

namespace CIS.Management.Service.Http;

internal static partial class ManagementEndpoints
{
    private const string RuleEnginePath = "/applications/{applicationId}/rule-engine";

    private static readonly ManagementFeature[] RuleEngineApiFeatures =
    [
        new("listRuleEngineRulesets", 1, "GET", RuleEnginePath + "/rulesets", [ManagementRuleEngineOperations.Read]),
        new("getRuleEngineSchema", 1, "GET", RuleEnginePath + "/schema", [ManagementRuleEngineOperations.Read]),
        new("listRuleEngineVersions", 1, "GET", RuleEnginePath + "/versions", [ManagementRuleEngineOperations.Read]),
        new("getRuleEngineVersion", 1, "GET", RuleEnginePath + "/version", [ManagementRuleEngineOperations.Read]),
        new("saveRuleEngineVersion", 1, "POST", RuleEnginePath + "/versions", [ManagementRuleEngineOperations.Save]),
        new("getRuleEngineVersionOperation", 1, "GET", RuleEnginePath + "/version-operations/{operationKey}",
            [ManagementRuleEngineOperations.ReadOperation]),
        new("getRuleEngineDeployment", 1, "GET", RuleEnginePath + "/deployment", [ManagementRuleEngineOperations.ReadDeployment]),
        new("getRuleEngineDeploymentInstances", 1, "GET", RuleEnginePath + "/deployment-instances", [ManagementRuleEngineOperations.ReadDeployment]),
        new("requestRuleEngineDeployment", 1, "POST", RuleEnginePath + "/deployments",
            [ManagementRuleEngineOperations.Deploy, ManagementRuleEngineOperations.Read]),
        new("getRuleEngineDeploymentOperation", 1, "GET", RuleEnginePath + "/deployment-operations/{operationKey}",
            [ManagementRuleEngineOperations.ReadOperation]),
        new("getRegistrationRuleEngineVersion", 1, "GET", RuleEngineWorkloadVersionPath, ["management.registrations.write"]),
        new("listRuleEngineExecutions", 1, "GET", RuleEnginePath + "/executions", [ManagementRuleEngineOperations.ReadExecutions]),
        new("getRuleEngineExecution", 1, "GET", RuleEnginePath + "/executions/{executionId}", [ManagementRuleEngineOperations.ReadExecutionDetail]),
        new("listRuleEngineCounters", 1, "GET", RuleEnginePath + "/counters", [ManagementRuleEngineOperations.ReadCounters]),
        new("resetRuleEngineCounters", 1, "POST", RuleEnginePath + "/counter-resets", [ManagementRuleEngineOperations.ResetCounters]),
        new("getRuleEngineCounterResetOperation", 1, "GET", RuleEnginePath + "/counter-reset-operations/{operationKey}",
            [ManagementRuleEngineOperations.ReadOperation]),
        new("listRuleEngineCounterResetOutcomes", 1, "GET", RuleEnginePath + "/counter-reset-operations/{operationKey}/outcomes",
            [ManagementRuleEngineOperations.ReadOperation])
    ];

    private static void MapRuleEngine(RouteGroupBuilder group, ManagementServiceOptions options)
    {
        if (options.Configuration?.Manifest.RuleEngine is not { Count: > 0 })
        {
            return;
        }
        RouteGroupBuilder engine = group.MapGroup(RuleEnginePath);
        engine.MapGet("/rulesets", RuleEngineRulesets);
        engine.MapGet("/schema", RuleEngineSchema);
        engine.MapGet("/versions", RuleEngineVersions);
        engine.MapGet("/version", RuleEngineVersion);
        engine.MapPost("/versions", SaveRuleEngineVersion);
        engine.MapGet("/version-operations/{operationKey}", RuleEngineVersionOperation);
        engine.MapGet("/deployment", RuleEngineDeployment);
        engine.MapGet("/deployment-instances", RuleEngineDeploymentInstances);
        engine.MapPost("/deployments", RequestRuleEngineDeployment);
        engine.MapGet("/deployment-operations/{operationKey}", RuleEngineDeploymentOperation);
        engine.MapGet("/executions", RuleEngineExecutions);
        engine.MapGet("/executions/{executionId}", RuleEngineExecution);
        engine.MapGet("/counters", RuleEngineCounters);
        engine.MapPost("/counter-resets", RequestRuleEngineCounterReset);
        engine.MapGet("/counter-reset-operations/{operationKey}", RuleEngineCounterResetOperation);
        engine.MapGet("/counter-reset-operations/{operationKey}/outcomes", RuleEngineCounterResetOutcomes);
        group.MapGet(RuleEngineWorkloadVersionPath, RegistrationRuleEngineVersion);
    }

    private static async Task<IResult> RuleEngineRulesets(HttpContext context, string applicationId,
        IManagementBackend backend, PostgreSqlManagementStore store, ManagementCursor cursors)
    {
        const string endpoint = "rule-engine-rulesets";
        if (!ManagementWire.IsUuid(applicationId) || !RuleEnginePageSize(context, out int size)
            || context.Request.Query.Any(pair => pair.Key is not ("pageSize" or "cursor") || pair.Value.Count != 1))
        {
            return RuleEngineProblem(context, 400, "management.rule-engine.invalid_request");
        }
        if (await VisibleApplication(context, applicationId, backend, ManagementRuleEngineOperations.Read) is null)
        {
            return RuleEngineProblem(context, 404, "management.rule-engine.not_found");
        }
        string actor = ManagementActorScope.Create(ManagementHttp.Caller(context)!, applicationId);
        if (!cursors.TryDecodeRuleEngineRuleset(context.Request.Query["cursor"].FirstOrDefault(), endpoint, actor, size, out string? after))
        {
            return RuleEngineProblem(context, 400, "management.rule-engine.invalid_request");
        }
        ManagementStoreResult<IReadOnlyList<ManagementRuleSetSummary>> result =
            await store.ListRuleEngineResourcesAsync(Guid.Parse(applicationId), after, size + 1, context.RequestAborted);
        if (!result.IsKnownCommitted || result.Value is not { } values)
        {
            return RuleEngineProblem(context, 503, result.Code ?? "management.rule-engine.unavailable");
        }
        ManagementRuleSetSummary[] items = values.Take(size).ToArray();
        string? next = values.Count > size
            ? cursors.EncodeRuleEngineRuleset(endpoint, actor, size, items[^1].RulesetId) : null;
        return RuleEngineJson(new ManagementPage<ManagementRuleSetSummary>(items, next));
    }

    private static async Task<IResult> RuleEngineSchema(HttpContext context, string applicationId, PostgreSqlManagementStore store)
    {
        if (!RuleEngineScope(context, applicationId, ["rulesetId", "schemaRevision"], out string ruleset)
            || context.Request.Query.TryGetValue("schemaRevision", out var revision) && !ManagementRuleEngineWire.IsRuleId(revision.ToString()))
        {
            return RuleEngineProblem(context, 400, "management.rule-engine.invalid_request");
        }
        RuleEngineResource? resource = await VisibleRuleEngine(context, applicationId, ruleset, store, ManagementRuleEngineOperations.Read);
        if (resource is null)
        {
            return RuleEngineProblem(context, 404, "management.rule-engine.not_found");
        }
        return RuleEngineRead(context, await store.ReadRuleEngineSchemaAsync(resource,
            context.Request.Query["schemaRevision"].FirstOrDefault(), context.RequestAborted));
    }

    private static async Task<IResult> RuleEngineVersions(HttpContext context, string applicationId, PostgreSqlManagementStore store,
        ManagementCursor cursors)
    {
        const string endpoint = "rule-engine-versions";
        if (!RuleEngineScope(context, applicationId, ["rulesetId", "pageSize", "cursor"], out string ruleset)
            || !RuleEnginePageSize(context, out int size))
        {
            return RuleEngineProblem(context, 400, "management.rule-engine.invalid_request");
        }
        RuleEngineResource? resource = await VisibleRuleEngine(context, applicationId, ruleset, store, ManagementRuleEngineOperations.Read);
        if (resource is null)
        {
            return RuleEngineProblem(context, 404, "management.rule-engine.not_found");
        }
        string scope = resource.Key.ToString("D");
        if (!cursors.TryDecode(context.Request.Query["cursor"].FirstOrDefault(), endpoint,
            ManagementActorScope.Create(ManagementHttp.Caller(context)!, scope), size, out string? after)
            || after is not null && !ManagementWire.IsUuid(after))
        {
            return RuleEngineProblem(context, 400, "management.rule-engine.invalid_request");
        }
        ManagementStoreResult<IReadOnlyList<ManagementRuleVersionSummary>> result =
            await store.ListRuleEngineVersionsAsync(resource, after is null ? null : Guid.Parse(after), size + 1, context.RequestAborted);
        if (!result.IsKnownCommitted || result.Value is not { } values)
        {
            return RuleEngineProblem(context, 503, result.Code ?? "management.rule-engine.unavailable");
        }
        var items = new List<ManagementRuleVersionSummary>(Math.Min(values.Count, size));
        int remainingBytes = ManagementRuleEngineWire.MaximumBodyBytes - 8192;
        foreach (ManagementRuleVersionSummary version in values.Take(size))
        {
            int bytes = ManagementRuleEngineWire.EncodeResponse(new ManagementPage<ManagementRuleVersionSummary>([version])).Length;
            if (bytes > remainingBytes)
            {
                break;
            }
            remainingBytes -= bytes;
            items.Add(version);
        }
        if (items.Count == 0 && values.Count != 0)
        {
            return RuleEngineProblem(context, 503, "management.configuration.response_too_large");
        }
        string? next = values.Count > items.Count
            ? ManagementHttp.Cursor(context, cursors, endpoint, scope, size, items[^1].Version) : null;
        return RuleEngineJson(new ManagementPage<ManagementRuleVersionSummary>(items.AsReadOnly(), next));
    }

    private static async Task<IResult> RuleEngineVersion(HttpContext context, string applicationId, PostgreSqlManagementStore store)
    {
        string version = context.Request.Query["version"].ToString();
        if (!RuleEngineScope(context, applicationId, ["rulesetId", "version"], out string ruleset) || !ManagementWire.IsUuid(version))
        {
            return RuleEngineProblem(context, 400, "management.rule-engine.invalid_request");
        }
        RuleEngineResource? resource = await VisibleRuleEngine(context, applicationId, ruleset, store, ManagementRuleEngineOperations.Read);
        if (resource is null)
        {
            return RuleEngineProblem(context, 404, "management.rule-engine.not_found");
        }
        return RuleEngineRead(context, await store.ReadRuleEngineVersionAsync(resource, Guid.Parse(version), context.RequestAborted));
    }

    private static async Task<IResult> SaveRuleEngineVersion(HttpContext context, string applicationId, PostgreSqlManagementStore store,
        ManagementRuleEngineService service)
    {
        string key = context.Request.Headers["Idempotency-Key"].ToString();
        if (!RuleEngineScope(context, applicationId, ["rulesetId"], out string ruleset)
            || context.Request.Headers["Idempotency-Key"].Count != 1 || !ManagementWire.IsOperationKey(key))
        {
            return RuleEngineProblem(context, 400, "management.rule-engine.invalid_request");
        }
        RuleEngineResource? resource = await VisibleRuleEngine(context, applicationId, ruleset, store, ManagementRuleEngineOperations.Save);
        if (resource is null)
        {
            return RuleEngineProblem(context, 404, "management.rule-engine.not_found");
        }
        if (!context.Request.Headers.ContainsKey("If-Match"))
        {
            return RuleEngineProblem(context, 428, "management.rule-engine.precondition_required");
        }
        string edit = context.Request.Headers.IfMatch.ToString();
        if (context.Request.Headers.IfMatch.Count != 1 || !ManagementWire.IsStrongETag(edit))
        {
            return RuleEngineProblem(context, 400, "management.rule-engine.invalid_request");
        }
        ManagementSaveRuleVersionCommand? command = await ManagementConfigurationHttp.Read(context,
            (ReadOnlyMemory<byte> body) => ManagementRuleEngineWire.DecodeRequest(body, ruleset));
        return command is null ? RuleEngineProblem(context, 400, "management.rule-engine.invalid_request")
            : RuleEngineResponse(context, await service.SaveAsync(resource, key, edit, command, context.RequestAborted));
    }

    private static async Task<IResult> RuleEngineVersionOperation(HttpContext context, string applicationId, string operationKey,
        PostgreSqlManagementStore store, ManagementRuleEngineService service)
    {
        if (!RuleEngineScope(context, applicationId, ["rulesetId"], out string ruleset) || !ManagementWire.IsOperationKey(operationKey)
            || ManagementConfigurationHttp.HasRequestBody(context))
        {
            return RuleEngineProblem(context, 400, "management.rule-engine.invalid_request");
        }
        RuleEngineResource? resource = await VisibleRuleEngine(context, applicationId, ruleset, store, ManagementRuleEngineOperations.ReadOperation);
        if (resource is null)
        {
            return RuleEngineProblem(context, 404, "management.rule-engine.not_found");
        }
        return RuleEngineResponse(context, await service.LookupAsync(resource, operationKey, context.RequestAborted));
    }

    private static async Task<IResult> RuleEngineDeployment(HttpContext context, string applicationId, PostgreSqlManagementStore store)
    {
        if (!RuleEngineScope(context, applicationId, ["rulesetId"], out string ruleset) || ManagementConfigurationHttp.HasRequestBody(context))
        {
            return RuleEngineProblem(context, 400, "management.rule-engine.invalid_request");
        }
        RuleEngineResource? resource = await VisibleRuleEngine(context, applicationId, ruleset, store, ManagementRuleEngineOperations.ReadDeployment);
        return resource is null ? RuleEngineProblem(context, 404, "management.rule-engine.not_found")
            : RuleEngineRead(context, await store.ReadRuleEngineDeploymentAsync(resource, context.RequestAborted));
    }

    private static async Task<IResult> RequestRuleEngineDeployment(HttpContext context, string applicationId, PostgreSqlManagementStore store,
        ManagementRuleEngineService service)
    {
        string key = context.Request.Headers["Idempotency-Key"].ToString();
        if (!RuleEngineScope(context, applicationId, ["rulesetId"], out string ruleset)
            || context.Request.Headers["Idempotency-Key"].Count != 1 || !ManagementWire.IsOperationKey(key))
        {
            return RuleEngineProblem(context, 400, "management.rule-engine.invalid_request");
        }
        if (!await ManagementHttp.Allowed(context, ManagementRuleEngineOperations.Read, applicationId))
        {
            return RuleEngineProblem(context, 404, "management.rule-engine.not_found");
        }
        RuleEngineResource? resource = await VisibleRuleEngine(context, applicationId, ruleset, store, ManagementRuleEngineOperations.Deploy);
        if (resource is null)
        {
            return RuleEngineProblem(context, 404, "management.rule-engine.not_found");
        }
        if (!context.Request.Headers.ContainsKey("If-Match"))
        {
            return RuleEngineProblem(context, 428, "management.rule-engine.precondition_required");
        }
        string assignment = context.Request.Headers.IfMatch.ToString();
        if (context.Request.Headers.IfMatch.Count != 1 || !ManagementWire.IsStrongETag(assignment))
        {
            return RuleEngineProblem(context, 400, "management.rule-engine.invalid_request");
        }
        ManagementRequestRuleDeploymentCommand? command = await ManagementConfigurationHttp.Read<ManagementRequestRuleDeploymentCommand>(
            context, ManagementRuleEngineWire.DecodeDeploymentRequest);
        return command is null || !ManagementWire.IsUuid(command.Version)
            ? RuleEngineProblem(context, 400, "management.rule-engine.invalid_request")
            : RuleEngineDeploymentResponse(context, await service.DeployAsync(resource, key, assignment, command, context.RequestAborted));
    }

    private static async Task<IResult> RuleEngineDeploymentOperation(HttpContext context, string applicationId, string operationKey,
        PostgreSqlManagementStore store, ManagementRuleEngineService service)
    {
        if (!RuleEngineScope(context, applicationId, ["rulesetId"], out string ruleset) || !ManagementWire.IsOperationKey(operationKey)
            || ManagementConfigurationHttp.HasRequestBody(context))
        {
            return RuleEngineProblem(context, 400, "management.rule-engine.invalid_request");
        }
        RuleEngineResource? resource = await VisibleRuleEngine(context, applicationId, ruleset, store, ManagementRuleEngineOperations.ReadOperation);
        return resource is null ? RuleEngineProblem(context, 404, "management.rule-engine.not_found")
            : RuleEngineDeploymentResponse(context, await service.LookupDeploymentAsync(resource, operationKey, context.RequestAborted));
    }

    private static async Task<RuleEngineResource?> VisibleRuleEngine(HttpContext context, string application, string ruleset,
        PostgreSqlManagementStore store, string capability)
    {
        if (!await ManagementHttp.Allowed(context, capability, application))
        {
            return null;
        }
        ManagementStoreResult<RuleEngineResource> result =
            await store.ReadRuleEngineResourceAsync(Guid.Parse(application), ruleset, context.RequestAborted);
        if (result.IsKnownCommitted)
        {
            return result.Value;
        }
        if (result.Code == "management.rule-engine.not_found")
        {
            return null;
        }
        throw new ManagementRequestException(503, result.Code ?? "management.rule-engine.unavailable");
    }

    private static bool RuleEngineScope(HttpContext context, string application, string[] keys, out string ruleset)
    {
        ruleset = context.Request.Query["rulesetId"].ToString();
        return ManagementWire.IsUuid(application) && ManagementRuleEngineWire.IsRuleId(ruleset)
            && context.Request.Query.All(pair => keys.Contains(pair.Key, StringComparer.Ordinal) && pair.Value.Count == 1);
    }

    private static bool RuleEnginePageSize(HttpContext context, out int size)
    {
        size = 50;
        return !context.Request.Query.TryGetValue("pageSize", out var raw)
            || int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out size) && size is >= 1 and <= 500;
    }

    private static IResult RuleEngineRead<T>(HttpContext context, ManagementStoreResult<T> result) where T : class
        => result.IsKnownCommitted && result.Value is { } value ? RuleEngineJson(value)
            : RuleEngineProblem(context, result.Code == "management.rule-engine.not_found" ? 404 : 503,
                result.Code ?? "management.rule-engine.unavailable");

    private static IResult RuleEngineResponse(HttpContext context, RuleEngineVersionResponse response)
    {
        if (response.Status == 200 && response.Result is { } value)
        {
            if (value.Result?.EditETag is { } etag)
            {
                context.Response.Headers.ETag = etag;
            }
            return RuleEngineJson(value);
        }
        return RuleEngineProblem(context, response.Status, response.Code ?? "management.rule-engine.unavailable", response.Operation, response.Result);
    }

    private static IResult RuleEngineDeploymentResponse(HttpContext context, RuleEngineDeploymentResponse response)
    {
        if (response.Status == 200 && response.Result is { } value)
        {
            if (value.Result?.AssignmentETag is { } etag)
            {
                context.Response.Headers.ETag = etag;
            }
            return RuleEngineJson(value);
        }
        string code = response.Code ?? "management.rule-engine.unavailable";
        return RuleEngineJson(new ManagementRuleEngineProblem("urn:cis:management:problem:" + code,
            "Rule Engine request could not be completed", response.Status, code,
            CorrelationId: ManagementWire.IsScalarText(context.TraceIdentifier, 128) ? context.TraceIdentifier : null,
            Operation: response.Operation)
        {
            DeploymentResult = response.Result
        }, response.Status);
    }

    private static IResult RuleEngineProblem(HttpContext context, int status, string code, ManagementOperationReference? operation = null,
        ManagementRuleVersionSaveResult? result = null)
        => RuleEngineJson(new ManagementRuleEngineProblem("urn:cis:management:problem:" + code,
            "Rule Engine request could not be completed", status, code,
            CorrelationId: ManagementWire.IsScalarText(context.TraceIdentifier, 128) ? context.TraceIdentifier : null,
            Operation: operation, Result: result), status);

    private static IResult RuleEngineJson<T>(T payload, int status = 200) where T : class
        => new RuleEngineHttpResult(ManagementRuleEngineWire.EncodeResponse(payload), status);

    private sealed record RuleEngineHttpResult(byte[] Payload, int Status) : IResult
    {
        public async Task ExecuteAsync(HttpContext context)
        {
            context.Response.StatusCode = Status;
            context.Response.ContentType = Status >= 400 ? "application/problem+json" : "application/json";
            context.Response.ContentLength = Payload.Length;
            context.Response.Headers.CacheControl = "no-store";
            await context.Response.Body.WriteAsync(Payload, context.RequestAborted);
        }
    }
}
