using System.Data.Common;
using CIS.Audit;
using CIS.Management.Service.Backends;
using CIS.Management.Service.Http;
using CIS.Management.Service.Persistence;
using CIS.Operations;
using CIS.Security;
using CIS.Transactions;

namespace CIS.Management.Service.Configuration;

public sealed record ManagementConfigurationResponse(int Status, string? Code = null,
    ManagementOperationReference? Operation = null, ManagementConfigurationOperationResult? Result = null);

internal sealed partial class ManagementConfigurationService(IDurableOperationService operations, ILocalTransactionFactory transactions,
    PostgreSqlManagementStore store, ICallerContextAccessor callers, ManagementConfigurationTarget target,
    IAuditCapture capture, IAuditWriter audit, PostgreSqlManagementAuthority authority, ManagementServiceOptions options)
{
    internal Task<ManagementConfigurationResponse> ApplyCatalogAsync(ManagementConfigurationCatalog catalog, string key,
        string ifMatch, ManagementConfigurationCatalogCommand command, CancellationToken token)
    {
        ManagementOperationReference reference = Bind(ManagementConfigurationOperations.CatalogName,
            ManagementConfigurationCapabilities.CatalogResource, catalog.CatalogId, key);
        var input = new ManagementCatalogInput(catalog.CatalogId, ifMatch, command);
        return Execute(reference, input, "catalog", command.Reason,
            (context, owned, cancellation) => store.ApplyConfigurationCatalogAsync(context, owned.CatalogId, owned.IfMatch,
                owned.Command.ManifestDigest, options.Configuration ?? throw new InvalidOperationException("Select configuration deployment input."), cancellation), token);
    }

    internal Task<ManagementConfigurationResponse> PublishAsync(ManagementConfigurationDefinition source, string key,
        string ifMatch, ManagementConfigurationPublicationCommand command, CancellationToken token)
    {
        ManagementOperationReference reference = Bind(ManagementConfigurationOperations.PublicationName,
            ManagementConfigurationCapabilities.SourceResource, source.SourceId, key);
        var input = new ManagementPublicationInput(source, ifMatch, command);
        string actor = callers.Current!.ActorKind + ":" + ManagementActorScope.Create(callers.Current, source.SourceId);
        return Execute(reference, input, "publication", command.Reason,
            (context, owned, cancellation) => store.PublishConfigurationAsync(context, owned, actor, cancellation), token);
    }

    internal Task<ManagementConfigurationResponse> LookupCatalogAsync(ManagementConfigurationCatalog catalog, string key,
        CancellationToken token)
        => Lookup(Bind(ManagementConfigurationOperations.CatalogName, ManagementConfigurationCapabilities.CatalogResource,
            catalog.CatalogId, key, lookup: true), token);

    internal Task<ManagementConfigurationResponse> LookupPublicationAsync(ManagementConfigurationDefinition source, string key,
        CancellationToken token)
        => Lookup(Bind(ManagementConfigurationOperations.PublicationName, ManagementConfigurationCapabilities.SourceResource,
            source.SourceId, key, lookup: true), token);

    internal Task<ManagementConfigurationResponse> CreateReleaseAsync(ManagementConfigurationBindingResource binding, string key,
        string ifMatch, ManagementConfigurationReleaseCommand command, CancellationToken token)
    {
        ManagementOperationReference reference = Bind(ManagementConfigurationOperations.ReleaseName,
            ManagementConfigurationCapabilities.BindingResource, binding.BindingId, key);
        string actor = callers.Current!.ActorKind + ":" + ManagementActorScope.Create(callers.Current, binding.BindingId);
        return Execute(reference, new ManagementReleaseInput(binding, ifMatch, command), "release", command.Reason,
            (context, owned, cancellation) => store.CreateConfigurationReleaseAsync(context, owned, actor, cancellation), token);
    }

    internal Task<ManagementConfigurationResponse> ReplaceAssignmentsAsync(ManagementConfigurationBindingResource binding, string key,
        string ifMatch, ManagementConfigurationAssignmentCommand command, CancellationToken token)
    {
        ManagementOperationReference reference = Bind(ManagementConfigurationOperations.AssignmentName,
            ManagementConfigurationCapabilities.BindingResource, binding.BindingId, key);
        return Execute(reference, new ManagementAssignmentInput(binding, ifMatch, command), "assignment", command.Reason,
            (context, owned, cancellation) => store.ReplaceConfigurationAssignmentsAsync(context, owned, cancellation), token);
    }

    internal Task<ManagementConfigurationResponse> LookupReleaseAsync(ManagementConfigurationBindingResource binding, string key, CancellationToken token)
        => Lookup(Bind(ManagementConfigurationOperations.ReleaseName, ManagementConfigurationCapabilities.BindingResource, binding.BindingId, key, lookup: true), token);

    internal Task<ManagementConfigurationResponse> LookupAssignmentAsync(ManagementConfigurationBindingResource binding, string key, CancellationToken token)
        => Lookup(Bind(ManagementConfigurationOperations.AssignmentName, ManagementConfigurationCapabilities.BindingResource, binding.BindingId, key, lookup: true), token);

    private ManagementOperationReference Bind(string name, string resourceType, string resourceId, string key, bool lookup = false)
    {
        CallerContext caller = callers.Current ?? throw new InvalidOperationException("A validated native caller is required.");
        target.Bind(resourceType, resourceId, lookup);
        return new(name, ManagementConfigurationOperations.Scope(name, caller, resourceId), key);
    }

    private async Task<ManagementConfigurationResponse> Execute<T>(ManagementOperationReference reference, T input,
        string family, string reason, Func<ILocalTransactionContext, T, CancellationToken, Task<ConfigurationEffect>> effect,
        CancellationToken token) where T : class
    {
        ILocalTransaction owner;
        try
        {
            owner = await transactions.BeginAsync("management", token);
        }
        catch (DbException)
        {
            authority.MarkUnavailable();
            return new(503, "management.configuration.unavailable", reference);
        }
        await using (owner)
        {
            try
            {
                _ = await authority.ReadAsync(owner.Context, true, token);
                OperationExecution<ManagementConfigurationDecision> execution = await operations.ExecuteAsync<T, ManagementConfigurationDecision>(
                    owner.Context, new(reference.Name, reference.Scope, reference.Key, input), async (context, owned, cancellation) =>
                    {
                        ConfigurationEffect decided = await effect(context, owned, cancellation);
                        await StageAudit(context, reference, family, reason, decided, cancellation);
                        return decided.Decision is { } value ? OperationDecision<ManagementConfigurationDecision>.Succeed(value)
                            : OperationDecision<ManagementConfigurationDecision>.Reject(decided.RejectionCode!, "The original configuration command was refused.");
                    }, token);
                if (execution.Disposition == OperationExecutionDisposition.Prepared)
                {
                    TransactionCompletion completion = await owner.CommitAsync(token);
                    if (completion.Outcome != TransactionOutcome.Committed || completion.CleanupErrors.Count != 0)
                    {
                        authority.MarkUnavailable();
                    }
                    if (completion.Outcome != TransactionOutcome.Committed)
                    {
                        return new(503, completion.Outcome == TransactionOutcome.Unknown ? "management.configuration.operation_unknown"
                            : "management.configuration.not_committed", reference);
                    }
                    return DecisionResponse(Map(reference, execution.Decision!, "prepared", "committed", completion));
                }
                TransactionCompletion attempt = await owner.RollbackAsync();
                if (attempt.CleanupErrors.Count != 0)
                {
                    authority.MarkUnavailable();
                }
                if (execution.Disposition == OperationExecutionDisposition.Replayed)
                {
                    return DecisionResponse(Map(reference, execution.Decision!, "replayed", "committed", attempt));
                }
                return execution.Disposition switch
                {
                    OperationExecutionDisposition.Denied => new(404, "management.configuration.not_found"),
                    OperationExecutionDisposition.KeyConflict => new(409, "management.configuration.operation_conflict", reference),
                    OperationExecutionDisposition.ResultExpired => new(410, "management.configuration.result_expired", reference),
                    OperationExecutionDisposition.Incompatible => new(409, "management.configuration.incompatible", reference),
                    OperationExecutionDisposition.Contended => new(409, "management.configuration.contended", reference),
                    OperationExecutionDisposition.RetryRequired => new(409, "management.configuration.retry_required", reference),
                    _ => new(503, "management.configuration.not_committed", reference)
                };
            }
            catch (ConfigurationAuditException)
            {
                await owner.RollbackAsync();
                return new(503, "management.configuration.audit_unavailable", reference);
            }
            catch (DbException)
            {
                authority.MarkUnavailable();
                await owner.RollbackAsync();
                return new(503, "management.configuration.unavailable", reference);
            }
            catch (ManagementBackendException error)
            {
                await owner.RollbackAsync();
                return new(503, error.Code, reference);
            }
        }
    }

    private async Task<ManagementConfigurationResponse> Lookup(ManagementOperationReference reference, CancellationToken token)
    {
        OperationLookup<ManagementConfigurationDecision> result = await operations.GetResultAsync<ManagementConfigurationDecision>(
            new(reference.Name, reference.Scope, reference.Key), token);
        if (result.ReadScopeCompletion?.CleanupErrors.Count > 0)
        {
            authority.MarkUnavailable();
        }
        return result.Disposition switch
        {
            OperationLookupDisposition.Found => new(200, Result: Map(reference, result.Decision!, "found", "committed", result.ReadScopeCompletion)),
            OperationLookupDisposition.NotFound => new(404, "management.configuration.operation_not_found", reference),
            OperationLookupDisposition.Denied => new(404, "management.configuration.not_found"),
            OperationLookupDisposition.ResultExpired => new(410, "management.configuration.result_expired", reference),
            OperationLookupDisposition.Incompatible => new(409, "management.configuration.incompatible", reference),
            _ => new(503, "management.configuration.unavailable", reference)
        };
    }

    private async Task StageAudit(ILocalTransactionContext context, ManagementOperationReference reference, string family,
        string reason, ConfigurationEffect effect, CancellationToken token)
    {
        try
        {
            ManagementConfigurationDecision? value = effect.Decision;
            string code = effect.RejectionCode ?? value!.Code;
            CapturedAuditRecord record = capture.Capture(ManagementConfigurationAudit.Definition,
                new(target.ResourceId!, family, code, reason, value?.PublicationId, value?.Sha256 ?? value?.ManifestDigest, value?.ETag,
                    value?.ReleaseId, effect.PreviousETag, value?.Generation, effect.BeforeAssignment,
                    value?.Assignment ?? (value is null ? effect.BeforeAssignment : null)),
                new(value is null ? AuditOutcome.Rejected : AuditOutcome.Succeeded,
                    target: new(EnvironmentOptions.ApplicationName, EnvironmentOptions.EnvironmentName, target.ResourceType!, target.ResourceId!),
                    reason: new(code), operationReference: new(reference.Name, reference.Scope, reference.Key)), token);
            if ((await audit.StageAsync(context, record, token)).Disposition != AuditStageDisposition.Staged)
            {
                context.MarkRollbackOnly("management.configuration.audit_unavailable");
                throw new ConfigurationAuditException();
            }
        }
        catch (AuditCaptureException)
        {
            context.MarkRollbackOnly("management.configuration.audit_unavailable");
            throw new ConfigurationAuditException();
        }
    }

    private static ManagementConfigurationResponse DecisionResponse(ManagementConfigurationOperationResult result)
        => result.Rejection is null ? new(200, Result: result)
            : new(result.Rejection.Code == "management.configuration.precondition_failed" ? 412 : 409,
                result.Rejection.Code, result.Operation, result);

    private static ManagementConfigurationOperationResult Map(ManagementOperationReference reference,
        OperationDecision<ManagementConfigurationDecision> decision, string source, string original, TransactionCompletion? attempt)
        => new(reference, source, decision.Value, original, attempt is null ? null : new(Outcome(attempt),
            attempt.CleanupErrors.Count == 0, attempt.CleanupErrors.Count == 0 ? null
                : Array.AsReadOnly(attempt.CleanupErrors.Select(error => error.Code).ToArray())),
            decision.Error is null ? null : new(decision.Error.Code, decision.Error.Message));

    private static string Outcome(TransactionCompletion completion) => completion.Outcome switch
    {
        TransactionOutcome.Committed => "committed",
        TransactionOutcome.NotCommitted => "notCommitted",
        _ => "unknown"
    };

    private sealed class ConfigurationAuditException : Exception
    {
    }
}
