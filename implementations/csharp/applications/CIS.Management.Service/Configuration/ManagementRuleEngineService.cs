using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CIS.Audit;
using CIS.Management.Service.Backends;
using CIS.Management.Service.Http;
using CIS.Management.Service.Persistence;
using CIS.Management.Wire;
using CIS.Operations;
using CIS.Operations.Hosting;
using CIS.Security;
using CIS.Transactions;

namespace CIS.Management.Service.Configuration;

internal sealed record RuleEngineVersionInput(RuleEngineResource Target, string EditETag, ManagementSaveRuleVersionCommand Command);
internal sealed record RuleEngineDeploymentInput(RuleEngineResource Target, string AssignmentETag, ManagementRequestRuleDeploymentCommand Command);

internal sealed record RuleEngineVersionResponse(int Status, string? Code, ManagementOperationReference? Operation = null,
    ManagementRuleVersionSaveResult? Result = null);

internal sealed class RuleEngineOperationTarget
{
    private RuleEngineResource? resource;

    internal RuleEngineResource Resource => resource ?? throw new InvalidOperationException("Resolve the Rule Engine resource first.");

    internal void Bind(RuleEngineResource target)
    {
        if (resource is not null)
        {
            throw new InvalidOperationException("A request can bind only one Rule Engine target.");
        }
        resource = target;
    }
}

internal static partial class ManagementRuleEngineOperations
{
    internal const string Name = "management.rule-engine.version.save";
    internal const string DeploymentName = ManagementRuleEngineWire.DeploymentOperationName;
    internal const string Read = "management.rule-engine.read";
    internal const string Save = "management.rule-engine.save";
    internal const string ReadOperation = "management.rule-engine.operations.read";
    internal const string ReadDeployment = "management.rule-engine.deployment.read";
    internal const string ReadExecutions = "management.rule-engine.executions.read";
    internal const string ReadExecutionDetail = "management.rule-engine.executions.detail.read";
    internal const string Deploy = "management.rule-engine.deploy";
    internal const string ApplicationResource = "management.application";
    private static readonly OperationContract InputContract = new("cis.management.rule-engine.save.input", 1);
    private static readonly OperationContract ResultContract = new("cis.management.rule-engine.save.result", 1);
    private static readonly OperationContract DeploymentInputContract = new("cis.management.rule-engine.deploy.input", 1);
    private static readonly OperationContract DeploymentResultContract = new("cis.management.rule-engine.deploy.result", 1);

    internal static string Scope(CallerContext caller, RuleEngineResource resource)
    {
        string key = resource.Key.ToString("D");
        return "management.rule-engine.v1/" + key + "/" + ManagementActorScope.Create(caller, key);
    }

    internal static OperationDefinition<RuleEngineVersionInput, ManagementRuleVersionDecision> Definition()
        => new(Name, "management", InputContract, new Normalizer(), new ResultCodec(),
            services => new Resolver(services.GetRequiredService<RuleEngineOperationTarget>(), Name), new()
            {
                ExecuteCapabilities = [Save],
                LookupCapabilities = [ReadOperation],
                MaximumCanonicalBytes = 16384,
                MaximumResultBytes = 4096,
                LogCalls = false
            });

    internal static OperationDefinition<RuleEngineDeploymentInput, ManagementRuleDeploymentDecision> DeploymentDefinition()
        => new(DeploymentName, "management", DeploymentInputContract, new DeploymentNormalizer(), new DeploymentResultCodec(),
            services => new Resolver(services.GetRequiredService<RuleEngineOperationTarget>(), DeploymentName), new()
            {
                ExecuteCapabilities = [Deploy, Read],
                LookupCapabilities = [ReadOperation],
                MaximumCanonicalBytes = 4096,
                MaximumResultBytes = 4096,
                LogCalls = false
            });

    private sealed class Resolver(RuleEngineOperationTarget target, string name) : IOperationScopeResolver
    {
        public ValueTask<OperationScope> ResolveAsync(string operationName, string requestedScope, CallerContext? caller,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RuleEngineResource resource = target.Resource;
            if (caller is null || operationName != name || requestedScope != Scope(caller, resource))
            {
                throw new ArgumentException("The operation must use the resolved Rule Engine target and verified caller.", nameof(requestedScope));
            }
            return ValueTask.FromResult(new OperationScope(requestedScope, ApplicationResource, resource.Application.ToString("D")));
        }
    }

    private sealed class DeploymentNormalizer : IOperationRequestNormalizer<RuleEngineDeploymentInput>
    {
        public NormalizedOperationInput<RuleEngineDeploymentInput> Normalize(RuleEngineDeploymentInput input, OperationScope scope)
        {
            if (scope.ResourceType != ApplicationResource || scope.ResourceIdentifier != input.Target.Application.ToString("D")
                || !scope.Scope.StartsWith("management.rule-engine.v1/" + input.Target.Key.ToString("D") + "/", StringComparison.Ordinal)
                || !ManagementWire.IsStrongETag(input.AssignmentETag) || !ManagementWire.IsUuid(input.Command.Version))
            {
                throw new ArgumentException("A complete deployment needs its exact saved version, resource and strong assignment precondition.", nameof(input));
            }
            byte[] command = ManagementRuleEngineWire.EncodeRequest(input.Command);
            OperationCanonicalValue canonical = OperationCanonicalValue.Sequence([
                OperationCanonicalValue.Bytes(Encoding.UTF8.GetBytes(input.Target.Key.ToString("D"))),
                OperationCanonicalValue.Bytes(Encoding.UTF8.GetBytes(input.AssignmentETag)),
                OperationCanonicalValue.Bytes(SHA256.HashData(command))
            ]);
            return new(input, [new(DeploymentInputContract, canonical)]);
        }
    }

    private sealed class Normalizer : IOperationRequestNormalizer<RuleEngineVersionInput>
    {
        public NormalizedOperationInput<RuleEngineVersionInput> Normalize(RuleEngineVersionInput input, OperationScope scope)
        {
            ManagementSaveRuleVersionCommand command = input.Command;
            if (scope.ResourceType != ApplicationResource || scope.ResourceIdentifier != input.Target.Application.ToString("D")
                || !scope.Scope.StartsWith("management.rule-engine.v1/" + input.Target.Key.ToString("D") + "/", StringComparison.Ordinal)
                || !ManagementWire.IsStrongETag(input.EditETag) || !ManagementWire.IsUtf8Text(command.Reason, 1024))
            {
                throw new ArgumentException("A complete Rule Engine save needs its exact resource and edit precondition.", nameof(input));
            }

            OperationCanonicalValue canonical = OperationCanonicalValue.Sequence([
                OperationCanonicalValue.Bytes(Encoding.UTF8.GetBytes(input.Target.Key.ToString("D"))),
                OperationCanonicalValue.Bytes(Encoding.UTF8.GetBytes(input.EditETag)),
                OperationCanonicalValue.Bytes(Encoding.UTF8.GetBytes(command.SchemaRevision)),
                OperationCanonicalValue.Bytes(SHA256.HashData(Encoding.UTF8.GetBytes(command.DefinitionJson))),
                OperationCanonicalValue.Bytes(Encoding.UTF8.GetBytes(command.Reason)),
                command.BaseVersion is { } basis ? OperationCanonicalValue.Bytes(Encoding.UTF8.GetBytes(basis)) : OperationCanonicalValue.Null
            ]);
            return new(input, [new(InputContract, canonical)]);
        }
    }

    private sealed class ResultCodec : IOperationResultCodec<ManagementRuleVersionDecision>
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public OperationContract CurrentContract => ResultContract;

        public IReadOnlyList<OperationContract> ReadableContracts { get; } = Array.AsReadOnly(new[] { ResultContract });

        public byte[] Encode(ManagementRuleVersionDecision? value)
            => JsonSerializer.SerializeToUtf8Bytes(value ?? throw new ArgumentNullException(nameof(value)), Json);

        public bool TryDecode(OperationContract contract, ReadOnlyMemory<byte> value, out ManagementRuleVersionDecision? decoded)
        {
            decoded = null;
            if (contract != ResultContract || value.Length is < 1 or > 4096)
            {
                return false;
            }
            try
            {
                ManagementRuleVersionDecision? candidate = JsonSerializer.Deserialize<ManagementRuleVersionDecision>(value.Span, Json);
                if (candidate is not null && ManagementWire.IsUuid(candidate.Version)
                    && ManagementRuleEngineWire.IsRuleId(candidate.SchemaRevision)
                    && ManagementWire.IsStrongETag(candidate.EditETag) && ManagementWire.IsTimestamp(candidate.SavedAtUtc))
                {
                    decoded = candidate;
                }
            }
            catch (JsonException)
            {
                return false;
            }
            return decoded is not null;
        }
    }

    private sealed class DeploymentResultCodec : IOperationResultCodec<ManagementRuleDeploymentDecision>
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public OperationContract CurrentContract => DeploymentResultContract;

        public IReadOnlyList<OperationContract> ReadableContracts { get; } = Array.AsReadOnly(new[] { DeploymentResultContract });

        public byte[] Encode(ManagementRuleDeploymentDecision? value)
            => JsonSerializer.SerializeToUtf8Bytes(value ?? throw new ArgumentNullException(nameof(value)), Json);

        public bool TryDecode(OperationContract contract, ReadOnlyMemory<byte> value, out ManagementRuleDeploymentDecision? decoded)
        {
            decoded = null;
            if (contract != DeploymentResultContract || value.Length is < 1 or > 4096)
            {
                return false;
            }
            try
            {
                ManagementRuleDeploymentDecision? candidate = JsonSerializer.Deserialize<ManagementRuleDeploymentDecision>(value.Span, Json);
                if (candidate?.Desired is { } desired && candidate.Intent is "deploy" or "rollback"
                    && ManagementWire.IsStrongETag(candidate.AssignmentETag) && ManagementWire.IsTimestamp(candidate.RequestedAtUtc)
                    && ManagementWire.IsUuid(desired.Version) && ManagementRuleEngineWire.IsRuleId(desired.SchemaRevision)
                    && ManagementWire.IsUuid(desired.ReleaseId) && ManagementWire.IsOrderedInteger(desired.Generation))
                {
                    decoded = candidate;
                }
            }
            catch (JsonException)
            {
                return false;
            }
            return decoded is not null;
        }
    }
}

internal sealed record RuleEngineDeploymentResponse(int Status, string? Code, ManagementOperationReference? Operation = null,
    ManagementRuleDeploymentOperationResult? Result = null);

internal sealed class ManagementRuleEngineService(IDurableOperationService operations, ILocalTransactionFactory transactions,
    PostgreSqlManagementStore store, ICallerContextAccessor callers, RuleEngineOperationTarget target, IAuditCapture capture,
    IAuditWriter audit, PostgreSqlManagementAuthority authority)
{
    internal async Task<RuleEngineVersionResponse> SaveAsync(RuleEngineResource resource, string key, string editETag,
        ManagementSaveRuleVersionCommand command, CancellationToken token)
    {
        ManagementOperationReference reference = Bind(resource, key, ManagementRuleEngineOperations.Name);
        NativeOriginal<ManagementRuleVersionDecision> response = await ExecuteAsync<RuleEngineVersionInput, ManagementRuleVersionDecision>(
            reference, new(resource, editETag, command), async (context, input, cancellation) =>
            {
                RuleEngineSaveEffect effect = await store.SaveRuleEngineVersionAsync(context, input.Target, input.EditETag, input.Command, cancellation);
                string code = effect.Refusal ?? "management.rule-engine.version_saved";
                await StageAuditAsync(context, RuleEngineSaveAudit.Definition,
                    new(resource.Key.ToString("D"), resource.RulesetId, resource.SchemaRevision, input.Command.Reason, code,
                        effect.Decision?.Version, effect.Digest, effect.PreviousETag, effect.Decision?.EditETag),
                    resource, reference, effect.Decision is not null, code, cancellation);
                return effect.Decision is { } result ? OperationDecision<ManagementRuleVersionDecision>.Succeed(result)
                    : OperationDecision<ManagementRuleVersionDecision>.Reject(code, "The original Rule Engine save was refused.");
            }, token);
        return SaveResponse(resource, reference, response);
    }

    internal async Task<RuleEngineVersionResponse> LookupAsync(RuleEngineResource resource, string key, CancellationToken token)
    {
        ManagementOperationReference reference = Bind(resource, key, ManagementRuleEngineOperations.Name);
        return SaveResponse(resource, reference, await LookupOriginalAsync<ManagementRuleVersionDecision>(reference, token));
    }

    internal async Task<RuleEngineDeploymentResponse> DeployAsync(RuleEngineResource resource, string key, string editETag,
        ManagementRequestRuleDeploymentCommand command, CancellationToken token)
    {
        ManagementOperationReference reference = Bind(resource, key, ManagementRuleEngineOperations.DeploymentName);
        CallerContext caller = callers.Current ?? throw new InvalidOperationException("A verified native caller is required.");
        string actor = caller.ActorKind + ":" + ManagementActorScope.Create(caller, resource.Key.ToString("D"));
        NativeOriginal<ManagementRuleDeploymentDecision> response =
            await ExecuteAsync<RuleEngineDeploymentInput, ManagementRuleDeploymentDecision>(reference,
                new(resource, editETag, command), async (context, input, cancellation) =>
                {
                    RuleEngineDeploymentEffect effect = await store.AssignRuleEngineVersionAsync(context, input.Target,
                        input.AssignmentETag, input.Command, actor, cancellation);
                    string code = effect.Refusal ?? "management.rule-engine.deployment_requested";
                    await StageAuditAsync(context, RuleEngineDeploymentAudit.Definition,
                        new(resource.Key.ToString("D"), resource.RulesetId, input.Command.Version, input.Command.Intent,
                            input.Command.Reason, code, effect.PreviousETag, effect.PreviousRelease, effect.Decision?.AssignmentETag,
                            effect.Decision?.Desired.ReleaseId, effect.Decision?.Desired.Generation),
                        resource, reference, effect.Decision is not null, code, cancellation);
                    return effect.Decision is { } result ? OperationDecision<ManagementRuleDeploymentDecision>.Succeed(result)
                        : OperationDecision<ManagementRuleDeploymentDecision>.Reject(code, "The original Rule Engine deployment was refused.");
                }, token);
        return DeployResponse(resource, reference, response);
    }

    internal async Task<RuleEngineDeploymentResponse> LookupDeploymentAsync(RuleEngineResource resource, string key, CancellationToken token)
    {
        ManagementOperationReference reference = Bind(resource, key, ManagementRuleEngineOperations.DeploymentName);
        return DeployResponse(resource, reference, await LookupOriginalAsync<ManagementRuleDeploymentDecision>(reference, token));
    }

    internal async Task<RuleEngineCounterResetResponse> ResetCountersAsync(RuleEngineResource resource, string key,
        ManagementRuleCounterResetCommand command, CancellationToken token)
    {
        ManagementOperationReference reference = Bind(resource, key, ManagementRuleEngineOperations.CounterResetName);
        NativeOriginal<ManagementRuleCounterResetDecision> response =
            await ExecuteAsync<RuleEngineCounterResetInput, ManagementRuleCounterResetDecision>(reference, new(resource, command),
                async (context, input, cancellation) =>
                {
                    RuleEngineCounterResetEffect effect = await store.ResetRuleEngineCountersAsync(context, resource, input.Command,
                        reference, cancellation);
                    string code = effect.Refusal ?? "management.rule-engine.counters_reset";
                    await StageAuditAsync(context, RuleEngineCounterResetAudit.Definition,
                        new(resource.Key.ToString("D"), resource.RulesetId, input.Command.Selection, input.Command.RuleIds,
                            input.Command.Reason, code, effect.Decision), resource, reference, effect.Decision is not null, code, cancellation);
                    return effect.Decision is { } decision ? OperationDecision<ManagementRuleCounterResetDecision>.Succeed(decision)
                        : OperationDecision<ManagementRuleCounterResetDecision>.Reject(code, "The original counter reset was refused.");
                }, token);
        return CounterResetResponse(resource, reference, response);
    }

    internal async Task<RuleEngineCounterResetResponse> LookupCounterResetAsync(RuleEngineResource resource, string key, CancellationToken token)
    {
        ManagementOperationReference reference = Bind(resource, key, ManagementRuleEngineOperations.CounterResetName);
        return CounterResetResponse(resource, reference, await LookupOriginalAsync<ManagementRuleCounterResetDecision>(reference, token));
    }

    private static RuleEngineCounterResetResponse CounterResetResponse(RuleEngineResource resource, ManagementOperationReference reference,
        NativeOriginal<ManagementRuleCounterResetDecision> response)
        => new(response.Status, response.Code, response.IncludeReference ? reference : null,
            response.Source is { } source ? new(resource.Application.ToString("D"), resource.Environment, resource.RulesetId,
                reference, source, response.Completion!, response.Decision, response.Attempt, response.Rejection) : null);

    private async Task<NativeOriginal<TDecision>> ExecuteAsync<TInput, TDecision>(ManagementOperationReference reference, TInput input,
        Func<ILocalTransactionContext, TInput, CancellationToken, Task<OperationDecision<TDecision>>> apply, CancellationToken token)
        where TInput : class where TDecision : class
    {
        ILocalTransaction owner;
        try
        {
            owner = await transactions.BeginAsync("management", token);
        }
        catch (DbException)
        {
            authority.MarkUnavailable();
            return Unestablished<TDecision>("management.rule-engine.unavailable");
        }
        await using (owner)
        {
            try
            {
                _ = await authority.ReadAsync(owner.Context, true, token);
                OperationExecution<TDecision> execution = await operations.ExecuteAsync<TInput, TDecision>(
                    owner.Context, new(reference.Name, reference.Scope, reference.Key, input), apply, token);
                if (execution.Disposition == OperationExecutionDisposition.Prepared)
                {
                    TransactionCompletion completion = await owner.CommitAsync(token);
                    Observe(completion);
                    return completion.Outcome switch
                    {
                        TransactionOutcome.Committed => Settled(execution.Decision!, "prepared", completion, lookup: false),
                        TransactionOutcome.Unknown => Unestablished<TDecision>("management.rule-engine.operation_unknown", completion),
                        _ => Unestablished<TDecision>("management.rule-engine.not_committed", completion, original: "notCommitted")
                    };
                }

                TransactionCompletion attempt = await owner.RollbackAsync();
                Observe(attempt);
                return execution.Disposition switch
                {
                    OperationExecutionDisposition.Replayed => Settled(execution.Decision!, "replayed", attempt, lookup: false),
                    OperationExecutionDisposition.Denied => new(404, "management.rule-engine.not_found", IncludeReference: false),
                    OperationExecutionDisposition.KeyConflict => new(409, "management.rule-engine.operation_conflict"),
                    OperationExecutionDisposition.ResultExpired => new(410, "management.rule-engine.result_expired"),
                    OperationExecutionDisposition.Incompatible => new(409, "management.rule-engine.incompatible"),
                    OperationExecutionDisposition.Contended => new(409, "management.rule-engine.contended", "unknown", "unknown", Attempt: Facts(attempt)),
                    OperationExecutionDisposition.RetryRequired => new(409, "management.rule-engine.retry_required", "unknown", "unknown", Attempt: Facts(attempt)),
                    _ => Unestablished<TDecision>("management.rule-engine.unavailable", attempt)
                };
            }
            catch (RuleEngineAuditUnavailableException)
            {
                TransactionCompletion completion = await owner.RollbackAsync();
                Observe(completion);
                return Unestablished<TDecision>("management.rule-engine.audit_unavailable", completion,
                    completion.Outcome == TransactionOutcome.Unknown ? "unknown" : "notCommitted");
            }
            catch (DbException)
            {
                authority.MarkUnavailable();
                TransactionCompletion completion = await owner.RollbackAsync();
                return Unestablished<TDecision>("management.rule-engine.unavailable", completion);
            }
            catch (ManagementBackendException error)
            {
                TransactionCompletion completion = await owner.RollbackAsync();
                Observe(completion);
                return Unestablished<TDecision>(error.Code, completion);
            }
        }
    }

    private async Task<NativeOriginal<T>> LookupOriginalAsync<T>(ManagementOperationReference reference, CancellationToken token) where T : class
    {
        OperationLookup<T> result;
        try
        {
            result = await operations.GetResultAsync<T>(new(reference.Name, reference.Scope, reference.Key), token);
        }
        catch (DbException)
        {
            authority.MarkUnavailable();
            return Unestablished<T>("management.rule-engine.unavailable");
        }
        Observe(result.ReadScopeCompletion);
        return result.Disposition switch
        {
            OperationLookupDisposition.Found => Settled(result.Decision!, "found", result.ReadScopeCompletion, lookup: true),
            OperationLookupDisposition.NotFound => new(404, "management.rule-engine.operation_not_found"),
            OperationLookupDisposition.Denied => new(404, "management.rule-engine.not_found", IncludeReference: false),
            OperationLookupDisposition.ResultExpired => new(410, "management.rule-engine.result_expired"),
            OperationLookupDisposition.Incompatible => new(409, "management.rule-engine.incompatible"),
            _ => Unestablished<T>("management.rule-engine.unavailable", result.ReadScopeCompletion)
        };
    }

    private ManagementOperationReference Bind(RuleEngineResource resource, string key, string name)
    {
        CallerContext caller = callers.Current ?? throw new InvalidOperationException("A verified native caller is required.");
        target.Bind(resource);
        return new(name, ManagementRuleEngineOperations.Scope(caller, resource), key);
    }

    private async Task StageAuditAsync<T>(ILocalTransactionContext context, AuditEventDefinition<T> definition, T details,
        RuleEngineResource resource, ManagementOperationReference reference, bool succeeded, string code, CancellationToken token) where T : class
    {
        try
        {
            CapturedAuditRecord record = capture.Capture(definition, details,
                new(succeeded ? AuditOutcome.Succeeded : AuditOutcome.Rejected,
                    target: new(EnvironmentOptions.ApplicationName, EnvironmentOptions.EnvironmentName,
                        "management.rule-engine.ruleset", resource.Key.ToString("D")),
                    reason: new(code), operationReference: new(reference.Name, reference.Scope, reference.Key)), token);
            if ((await audit.StageAsync(context, record, token)).Disposition != AuditStageDisposition.Staged)
            {
                context.MarkRollbackOnly("management.rule-engine.audit_unavailable");
                throw new RuleEngineAuditUnavailableException();
            }
        }
        catch (AuditCaptureException)
        {
            context.MarkRollbackOnly("management.rule-engine.audit_unavailable");
            throw new RuleEngineAuditUnavailableException();
        }
    }

    private void Observe(TransactionCompletion? completion)
    {
        if (completion is not null && (completion.CleanupErrors.Count != 0 || completion.Outcome == TransactionOutcome.Unknown))
        {
            authority.MarkUnavailable();
        }
    }

    private static NativeOriginal<T> Settled<T>(OperationDecision<T> decision, string source, TransactionCompletion? completion, bool lookup)
        where T : class
    {
        ManagementOperationRejection? rejection = decision.Error is { } error ? new(error.Code, error.Message) : null;
        int status = lookup || rejection is null ? 200 : rejection.Code == "management.rule-engine.precondition_failed" ? 412 : 409;
        return new(status, rejection?.Code, source, "committed", decision.Value, Facts(completion), rejection);
    }

    private static NativeOriginal<T> Unestablished<T>(string code, TransactionCompletion? attempt = null, string original = "unknown")
        where T : class
        => new(503, code, "unknown", original, Attempt: Facts(attempt));

    private static RuleEngineVersionResponse SaveResponse(RuleEngineResource resource, ManagementOperationReference reference,
        NativeOriginal<ManagementRuleVersionDecision> response)
        => new(response.Status, response.Code, response.IncludeReference ? reference : null,
            response.Source is { } source ? new(resource.Application.ToString("D"), resource.Environment, resource.RulesetId, reference,
                source, response.Completion!, response.Decision, response.Attempt, response.Rejection) : null);

    private static RuleEngineDeploymentResponse DeployResponse(RuleEngineResource resource, ManagementOperationReference reference,
        NativeOriginal<ManagementRuleDeploymentDecision> response)
        => new(response.Status, response.Code, response.IncludeReference ? reference : null,
            response.Source is { } source ? new(resource.Application.ToString("D"), resource.Environment, resource.RulesetId, reference,
                source, response.Completion!, response.Decision, response.Attempt, response.Rejection) : null);

    private static ManagementTransactionFacts? Facts(TransactionCompletion? completion) => completion is null ? null
        : new(completion.Outcome switch
        {
            TransactionOutcome.Committed => "committed",
            TransactionOutcome.NotCommitted => "notCommitted",
            _ => "unknown"
        }, completion.CleanupErrors.Count == 0, completion.CleanupErrors.Count == 0
            ? null : Array.AsReadOnly(completion.CleanupErrors.Select(fault => fault.Code).ToArray()));

    private sealed record NativeOriginal<T>(int Status, string? Code, string? Source = null, string? Completion = null, T? Decision = null,
        ManagementTransactionFacts? Attempt = null, ManagementOperationRejection? Rejection = null, bool IncludeReference = true) where T : class;

    private sealed class RuleEngineAuditUnavailableException : Exception;
}

internal sealed record RuleEngineSaveAuditDetails(string ResourceId, string RulesetId, string SchemaRevision, string Reason, string Code,
    string? Version, string? Digest, string? PreviousETag, string? ResultingETag);

internal sealed record RuleEngineDeploymentAuditDetails(string ResourceId, string RulesetId, string RequestedVersion, string Intent,
    string Reason, string Code, string? PreviousETag, string? PreviousReleaseId, string? AssignmentETag, string? ReleaseId, string? Generation);

internal static class RuleEngineDeploymentAudit
{
    internal static readonly AuditEventDefinition<RuleEngineDeploymentAuditDetails> Definition = new(
        "cis.management.rule-engine.deployment", 1, AuditCategory.Administration, new DetailsCodec(),
        requireTarget: true, requireOperationReference: true);

    private sealed class DetailsCodec : IAuditDetailsCodec<RuleEngineDeploymentAuditDetails>
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public void Validate(RuleEngineDeploymentAuditDetails details)
        {
            if (!ManagementWire.IsUuid(details.ResourceId) || !ManagementRuleEngineWire.IsRuleId(details.RulesetId)
                || !ManagementWire.IsUuid(details.RequestedVersion) || details.Intent is not ("deploy" or "rollback")
                || !ManagementWire.IsUtf8Text(details.Reason, 1024)
                || details.PreviousETag is not null && !ManagementWire.IsStrongETag(details.PreviousETag)
                || details.AssignmentETag is not null && !ManagementWire.IsStrongETag(details.AssignmentETag)
                || details.ReleaseId is not null && !ManagementWire.IsUuid(details.ReleaseId)
                || details.PreviousReleaseId is not null && !ManagementWire.IsUuid(details.PreviousReleaseId)
                || details.Generation is not null && !ManagementWire.IsOrderedInteger(details.Generation))
            {
                throw new ArgumentException("Audit requires the bounded actual Rule Engine deployment decision.", nameof(details));
            }
        }

        public void Write(Utf8JsonWriter writer, RuleEngineDeploymentAuditDetails details)
            => JsonSerializer.Serialize(writer, details, Json);
    }
}

internal static class RuleEngineSaveAudit
{
    internal static readonly AuditEventDefinition<RuleEngineSaveAuditDetails> Definition = new(
        "cis.management.rule-engine.save", 1, AuditCategory.Administration, new DetailsCodec(),
        requireTarget: true, requireOperationReference: true);

    private sealed class DetailsCodec : IAuditDetailsCodec<RuleEngineSaveAuditDetails>
    {
        public void Validate(RuleEngineSaveAuditDetails details)
        {
            if (!ManagementWire.IsUuid(details.ResourceId) || !ManagementRuleEngineWire.IsRuleId(details.RulesetId)
                || !ManagementRuleEngineWire.IsRuleId(details.SchemaRevision) || !ManagementWire.IsUtf8Text(details.Reason, 1024)
                || details.Version is not null && !ManagementWire.IsUuid(details.Version))
            {
                throw new ArgumentException("Audit requires the bounded Rule Engine save identity and reason.", nameof(details));
            }
        }

        public void Write(Utf8JsonWriter writer, RuleEngineSaveAuditDetails details)
        {
            writer.WriteStartObject();
            writer.WriteString("resourceId", details.ResourceId);
            writer.WriteString("rulesetId", details.RulesetId);
            writer.WriteString("schemaRevision", details.SchemaRevision);
            writer.WriteString("reason", details.Reason);
            writer.WriteString("code", details.Code);
            writer.WriteString("version", details.Version);
            writer.WriteString("digest", details.Digest);
            writer.WriteString("previousETag", details.PreviousETag);
            writer.WriteString("resultingETag", details.ResultingETag);
            writer.WriteEndObject();
        }
    }
}
