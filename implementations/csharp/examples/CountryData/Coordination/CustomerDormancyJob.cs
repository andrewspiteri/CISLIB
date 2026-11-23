using CIS.Coordination;
using CIS.Jobs;
using CIS.Messaging;
using CIS.Operations;
using CIS.Transactions;

namespace CIS.Examples.CountryData.Coordination;

/// <summary>Application persistence port for one finite, journal-admitted dormancy occurrence.</summary>
/// <remarks>Write protected customer changes and stage any IMessageOutbox outgoing intent in this original context.
/// Derive stable outgoing IDs from the original operation/input, never the lease or generation.
/// Do not publish directly or acknowledge a scheduler occurrence here.</remarks>
public interface ICustomerDormancyWork
{
    Task<CustomerDormancyResult> ApplyAsync(ILocalTransactionContext context, CustomerDormancyInput input,
        MessageOperationReference originalOperation, CancellationToken cancellationToken = default);
}

/// <summary>Preserves the scheduler's original RM-03 identity and actual RM-02 completion through ownership changes.</summary>
public sealed class CustomerDormancyJob(ILocalTransactionFactory transactions, IDurableOperationService operations,
    ICoordinationGuard guard, ICustomerDormancyWork work) : IScheduledJobHandler<CustomerDormancyInput>
{
    public async Task<ScheduledJobResult> ExecuteAsync(JobExecutionContext context, CustomerDormancyInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);
        AdmittedJobRun? run = context.AdmittedRun;
        if (context.Definition.ExecutionScope != CountryMaintenanceJobs.CustomerScope
            || context.Definition.JobId != CountryMaintenanceJobs.CustomerJob || run is null
            || run.Operation.OperationName != CountryMaintenanceJobs.CustomerJob || input.PartitionId != context.ResourceId)
        {
            return new(ScheduledJobOutcome.Unsupported, "customer.dormancy_target_mismatch");
        }

        MessageOperationReference original = run.Operation;
        await using ILocalTransaction transaction = await transactions.BeginAsync(CountryMaintenanceJobs.Database, cancellationToken);
        OperationExecution<CustomerDormancyResult> execution;
        try
        {
            execution = await operations.ExecuteAsync<CustomerDormancyInput, CustomerDormancyResult>(transaction.Context,
                new(original.OperationName, original.RequestedScope, original.Key, input), async (sameContext, ownedInput, token) =>
                {
                    // Keep the shared lock order: Operations claim, then ownership row, then business/outgoing writes.
                    CoordinationGuardResult admission = await guard.ValidateAsync(sameContext, context.Lease.Grant, token);
                    admission.ThrowIfRejected();
                    CustomerDormancyResult result = await work.ApplyAsync(sameContext, ownedInput, original, token);
                    return OperationDecision<CustomerDormancyResult>.Succeed(result);
                }, cancellationToken);
        }
        catch (CoordinationGuardRejectedException refused)
        {
            TransactionCompletion rolledBack = await transaction.RollbackAsync();
            ScheduledJobOutcome outcome = refused.Reason is CoordinationRejectionReason.Missing or CoordinationRejectionReason.Released
                or CoordinationRejectionReason.Expired or CoordinationRejectionReason.Superseded or CoordinationRejectionReason.WrongAuthority
                ? ScheduledJobOutcome.OwnershipLost : ScheduledJobOutcome.Failed;
            return new(outcome, "customer.ownership_guard_refused", rolledBack);
        }

        if (execution.Disposition == OperationExecutionDisposition.Replayed)
        {
            await transaction.RollbackAsync();
            // This new owner's read cleanup is not persistence evidence for the already committed original result.
            return execution.Decision!.Decision == OperationBusinessDecision.Succeeded
                ? new(ScheduledJobOutcome.PreviouslyCompleted)
                : new(ScheduledJobOutcome.Failed, execution.Decision.Error!.Code);
        }

        if (execution.Disposition != OperationExecutionDisposition.Prepared)
        {
            TransactionCompletion refused = await transaction.RollbackAsync();
            ScheduledJobOutcome outcome = execution.Disposition switch
            {
                OperationExecutionDisposition.Contended => ScheduledJobOutcome.Busy,
                OperationExecutionDisposition.RetryRequired => ScheduledJobOutcome.Deferred,
                OperationExecutionDisposition.Denied => ScheduledJobOutcome.Unauthorized,
                _ => ScheduledJobOutcome.Failed
            };
            return new(outcome, "customer.operation_not_prepared", refused);
        }

        TransactionCompletion completion = await transaction.CommitAsync(cancellationToken);
        if (completion.Outcome == TransactionOutcome.Unknown)
        {
            return new(ScheduledJobOutcome.Indeterminate, "customer.commit_unknown", completion);
        }

        if (completion.Outcome != TransactionOutcome.Committed)
        {
            return new(ScheduledJobOutcome.Failed, "customer.not_committed", completion);
        }

        return execution.Decision!.Decision == OperationBusinessDecision.Succeeded
            ? new(ScheduledJobOutcome.Completed, completion: completion)
            : new(ScheduledJobOutcome.Failed, execution.Decision.Error!.Code, completion);
    }

    /// <summary>Uses a fresh authorized Operations lookup after Unknown; absence cannot prove original rollback.</summary>
    public Task<OperationLookup<CustomerDormancyResult>> RecoverAsync(MessageOperationReference original,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (original.OperationName != CountryMaintenanceJobs.CustomerJob)
        {
            throw new ArgumentException("Use the original registered dormancy operation.", nameof(original));
        }

        return operations.GetResultAsync<CustomerDormancyResult>(new(original.OperationName, original.RequestedScope, original.Key), cancellationToken);
    }
}
