using System.Data.Common;
using CIS.Audit;
using CIS.Management.Service.Persistence;
using CIS.Management.Service.Backends;
using CIS.Operations;
using CIS.Security;
using CIS.Transactions;
using CIS.Transactions.AdoNet;

namespace CIS.Management.Service.Metadata;

public sealed record ManagementMetadataResponse(int Status, string? Code = null,
    ManagementOperationReference? Operation = null, ManagementMetadataResult? Result = null);

// A short application-owned orchestration over the existing RM02/RM03/RM06 ports.
// Only this owner completes the transaction; the original participant is shared by all three effects.
internal sealed class ManagementMetadataService(IDurableOperationService operations, ILocalTransactionFactory transactions,
    PostgreSqlManagementStore store, ICallerContextAccessor callers, ManagementMetadataTarget target,
    IAuditCapture capture, IAuditWriter audit, PostgreSqlManagementAuthority authority)
{
    internal async Task<ManagementMetadataResponse> ReplaceAsync(ManagementApplication application, string key,
        string ifMatch, ManagementMetadataCommand command, CancellationToken cancellationToken)
    {
        ManagementOperationReference reference = Bind(application, key);
        ILocalTransaction owner;
        try
        {
            owner = await transactions.BeginAsync("management", cancellationToken);
        }
        catch (DbException)
        {
            authority.MarkUnavailable();
            return new(503, "management.operation.unavailable", reference);
        }

        await using (owner)
        {
            try
            {
                _ = await authority.ReadAsync(owner.Context, true, cancellationToken);
                OperationExecution<ManagementMetadataDecision> execution = await operations.ExecuteAsync<ManagementMetadataInput, ManagementMetadataDecision>(
                    owner.Context, new(reference.Name, reference.Scope, reference.Key,
                        new(application.ApplicationId, ifMatch, command)),
                    (context, input, token) => Decide(context, input, reference, token), cancellationToken);
                if (execution.Disposition == OperationExecutionDisposition.Prepared)
                {
                    TransactionCompletion completion = await owner.CommitAsync(cancellationToken);
                    if (completion.Outcome != TransactionOutcome.Committed || completion.CleanupErrors.Count != 0)
                    {
                        authority.MarkUnavailable();
                    }
                    ManagementMetadataResult result = Map(reference, execution.Decision!, "prepared", Outcome(completion), completion);
                    if (completion.Outcome != TransactionOutcome.Committed)
                    {
                        return new(503, completion.Outcome == TransactionOutcome.Unknown
                            ? "management.operation_unknown" : "management.operation.not_committed", reference, result);
                    }
                    return DecisionResponse(result);
                }

                TransactionCompletion attempt = await owner.RollbackAsync();
                if (execution.Disposition == OperationExecutionDisposition.Replayed)
                {
                    return DecisionResponse(Map(reference, execution.Decision!, "replayed", "committed", attempt));
                }
                return execution.Disposition switch
                {
                    OperationExecutionDisposition.Denied => new(404, "management.not_found"),
                    OperationExecutionDisposition.KeyConflict => new(409, "management.operation.conflict", reference),
                    OperationExecutionDisposition.ResultExpired => new(410, "management.operation.result_expired", reference),
                    OperationExecutionDisposition.Incompatible => new(409, "management.operation.incompatible", reference),
                    OperationExecutionDisposition.Contended => new(409, "management.operation.contended", reference),
                    OperationExecutionDisposition.RetryRequired => new(409, "management.operation.retry_required", reference),
                    _ => new(503, "management.operation.not_committed", reference)
                };
            }
            catch (RequiredAuditException)
            {
                await owner.RollbackAsync();
                return new(503, "management.audit.unavailable", reference);
            }
            catch (DbException)
            {
                authority.MarkUnavailable();
                await owner.RollbackAsync();
                return new(503, "management.operation.unavailable", reference);
            }
            catch (ManagementBackendException error)
            {
                await owner.RollbackAsync();
                return new(503, error.Code, reference);
            }
        }
    }

    internal async Task<ManagementMetadataResponse> LookupAsync(ManagementApplication application, string key,
        CancellationToken cancellationToken)
    {
        ManagementOperationReference reference = Bind(application, key);
        OperationLookup<ManagementMetadataDecision> lookup = await operations.GetResultAsync<ManagementMetadataDecision>(
            new(reference.Name, reference.Scope, reference.Key), cancellationToken);
        if (lookup.ReadScopeCompletion?.CleanupErrors.Count > 0)
        {
            authority.MarkUnavailable();
        }
        return lookup.Disposition switch
        {
            OperationLookupDisposition.Found => new(200, Result: Map(reference, lookup.Decision!, "found", "committed", lookup.ReadScopeCompletion)),
            OperationLookupDisposition.NotFound => new(404, "management.operation.not_found", reference),
            OperationLookupDisposition.Denied => new(404, "management.not_found"),
            OperationLookupDisposition.ResultExpired => new(410, "management.operation.result_expired", reference),
            OperationLookupDisposition.Incompatible => new(409, "management.operation.incompatible", reference),
            _ => new(503, "management.operation.unavailable", reference)
        };
    }

    private ManagementOperationReference Bind(ManagementApplication application, string key)
    {
        CallerContext caller = callers.Current ?? throw new InvalidOperationException("A validated native caller is required.");
        target.Bind(application);
        return new(ManagementMetadataDefinition.Name, ManagementMetadataDefinition.Scope(caller, application.ApplicationId), key);
    }

    private async Task<OperationDecision<ManagementMetadataDecision>> Decide(ILocalTransactionContext context,
        ManagementMetadataInput input, ManagementOperationReference reference, CancellationToken cancellationToken)
    {
        // Lock before observing the precondition. ReadCommitted then sees the actual preceding committed edit.
        // No capacity lock or second lease is needed by this metadata-only participant.
        await context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            await using DbCommand command = ManagementSql.Command(connection, transaction,
                $"SELECT application_id FROM {ManagementSql.Table(store.Options.Schema, "management_application")} WHERE application_id=@id FOR UPDATE",
                ("id", Guid.Parse(input.ApplicationId)));
            await command.ExecuteScalarAsync(token);
            return true;
        }, cancellationToken);
        ManagementApplication before = await store.FindApplicationAsync(context, input.ApplicationId, cancellationToken)
            ?? throw new InvalidOperationException("The resolved application no longer exists.");
        string? rejected = before.MetadataETag != input.IfMatch ? "management.precondition_failed" : null;
        ManagementApplication after = before;
        if (rejected is null)
        {
            after = await store.TryReplaceMetadataAsync(context, input.ApplicationId, before.MetadataRevision,
                new(input.Command.DisplayName, input.Command.Description), cancellationToken) ?? before;
            if (ReferenceEquals(after, before))
            {
                rejected = "management.metadata.revision_exhausted";
            }
        }
        string code = rejected ?? "management.metadata.applied";
        try
        {
            CapturedAuditRecord record = capture.Capture(ManagementMetadataAudit.Definition,
                new(input.ApplicationId, before.MetadataRevision, after.MetadataRevision, before.Metadata, after.Metadata),
                new(rejected is null ? AuditOutcome.Succeeded : AuditOutcome.Rejected,
                    target: new(EnvironmentOptions.ApplicationName, EnvironmentOptions.EnvironmentName, "management.application", input.ApplicationId),
                    reason: new(code), operationReference: new(reference.Name, reference.Scope, reference.Key)), cancellationToken);
            AuditStageResult staged = await audit.StageAsync(context, record, cancellationToken);
            if (staged.Disposition != AuditStageDisposition.Staged)
            {
                context.MarkRollbackOnly("management.audit.unavailable");
                throw new RequiredAuditException();
            }
        }
        catch (AuditCaptureException)
        {
            context.MarkRollbackOnly("management.audit.unavailable");
            throw new RequiredAuditException();
        }
        if (rejected is not null)
        {
            return OperationDecision<ManagementMetadataDecision>.Reject(rejected,
                rejected == "management.precondition_failed" ? "The original metadata precondition does not match." : "The metadata revision cannot advance.");
        }
        string timestamp = await context.UseAdoNetAsync(async (connection, transaction, token) =>
        {
            await using DbCommand command = ManagementSql.Command(connection, transaction, "SELECT clock_timestamp()");
            return ManagementSql.Timestamp((DateTime)(await command.ExecuteScalarAsync(token))!);
        }, cancellationToken);
        return OperationDecision<ManagementMetadataDecision>.Succeed(new(1, after.ApplicationId, "applied", code,
            after.Metadata, after.MetadataRevision, after.MetadataETag, timestamp));
    }

    private static ManagementMetadataResponse DecisionResponse(ManagementMetadataResult result)
        => result.Rejection is null ? new(200, Result: result)
            : new(result.Rejection.Code == "management.precondition_failed" ? 412 : 409,
                result.Rejection.Code, result.Operation, result);

    private static ManagementMetadataResult Map(ManagementOperationReference reference,
        OperationDecision<ManagementMetadataDecision> decision, string source, string original,
        TransactionCompletion? attempt)
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

    private sealed class RequiredAuditException : Exception
    {
    }
}
