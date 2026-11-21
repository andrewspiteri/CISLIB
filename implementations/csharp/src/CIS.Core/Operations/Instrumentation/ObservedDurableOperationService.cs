using CIS.Operations.Hosting;
using CIS.Transactions;

namespace CIS.Operations;

// One public-method boundary; the sequential protocol remains in DurableOperationService.
internal sealed class ObservedDurableOperationService(IDurableOperationService inner,
    OperationDefinition[] definitions, OperationInstrumentation instrumentation) : IDurableOperationService
{
    public async Task<OperationExecution<TResult>> ExecuteAsync<TRequest, TResult>(ILocalTransactionContext context,
        OperationRequest<TRequest> request,
        Func<ILocalTransactionContext, TRequest, CancellationToken, Task<OperationDecision<TResult>>> handler,
        CancellationToken cancellationToken = default)
    {
        OperationObservation? observation = Capture(request?.OperationName, lookup: false);
        string outcome = "failed";
        try
        {
            OperationExecution<TResult> result = await inner.ExecuteAsync(context, request!, handler, cancellationToken).ConfigureAwait(false);
            outcome = ExecuteOutcome(result);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "cancelled";
            throw;
        }
        finally
        {
            OperationObservation.Attempt(() => observation?.Complete(outcome));
        }
    }

    public async Task<OperationLookup<TResult>> GetResultAsync<TResult>(OperationLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        OperationObservation? observation = Capture(request?.OperationName, lookup: true);
        string outcome = "failed";
        try
        {
            OperationLookup<TResult> result = await inner.GetResultAsync<TResult>(request!, cancellationToken).ConfigureAwait(false);
            outcome = LookupOutcome(result);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            outcome = "cancelled";
            throw;
        }
        finally
        {
            OperationObservation.Attempt(() => observation?.Complete(outcome));
        }
    }

    private OperationObservation? Capture(string? name, bool lookup)
    {
        OperationDefinition? definition = definitions.SingleOrDefault(item => item.Name == name);
        if (definition is null)
        {
            return null;
        }

        try
        {
            return instrumentation.Capture(definition.Name, lookup, definition.LogCalls);
        }
        catch (Exception error) when (OperationObservation.IsOrdinary(error))
        {
            return null;
        }
    }

    private static string ExecuteOutcome<TResult>(OperationExecution<TResult> result) => result.Disposition switch
    {
        OperationExecutionDisposition.Prepared => result.Decision!.Decision == OperationBusinessDecision.Succeeded
            ? "prepared_succeeded" : "prepared_rejected",
        OperationExecutionDisposition.Replayed => result.Decision!.Decision == OperationBusinessDecision.Succeeded
            ? "replayed_succeeded" : "replayed_rejected",
        OperationExecutionDisposition.KeyConflict => "key_conflict",
        OperationExecutionDisposition.ResultExpired => "result_expired",
        OperationExecutionDisposition.Incompatible => "incompatible",
        OperationExecutionDisposition.Contended => "contended",
        OperationExecutionDisposition.RetryRequired => "retry_required",
        OperationExecutionDisposition.Denied => "denied",
        OperationExecutionDisposition.RollbackOnly => "rollback_only",
        _ => "failed"
    };

    private static string LookupOutcome<TResult>(OperationLookup<TResult> result) => result.Disposition switch
    {
        OperationLookupDisposition.Found => result.Decision!.Decision == OperationBusinessDecision.Succeeded
            ? "found_succeeded" : "found_rejected",
        OperationLookupDisposition.NotFound => "not_found",
        OperationLookupDisposition.ResultExpired => "result_expired",
        OperationLookupDisposition.Incompatible => "incompatible",
        OperationLookupDisposition.Unavailable => "unavailable",
        OperationLookupDisposition.Denied => "denied",
        _ => "failed"
    };
}
