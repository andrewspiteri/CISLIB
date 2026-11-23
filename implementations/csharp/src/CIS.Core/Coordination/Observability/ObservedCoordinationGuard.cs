using CIS.Transactions;

namespace CIS.Coordination;

internal sealed class ObservedCoordinationGuard(ICoordinationGuard inner, CoordinationInstrumentation instrumentation) : ICoordinationGuard
{
    public async Task<CoordinationGuardResult> ValidateAsync(ILocalTransactionContext context, CoordinationGrant grant,
        CancellationToken cancellationToken = default)
    {
        CoordinationMetrics? captured = instrumentation.CaptureMetrics();
        CoordinationGuardResult result = await inner.ValidateAsync(context, grant, cancellationToken).ConfigureAwait(false);
        if (!result.Admitted)
        {
            instrumentation.GuardRejected(captured, result.RejectionReason);
        }

        return result;
    }
}
