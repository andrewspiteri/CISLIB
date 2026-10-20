// HC00 compile-only registration against a caller-provided real owner/lease contract.
using CIS.Healthcheck;

namespace HealthcheckContractConsumer;

internal static class CollectionConsumer
{
    internal static async Task RegisterOwnerAsync(
        HealthcheckService service,
        IHealthCollector owner,
        CancellationToken waitCancellation)
    {
        await using IAvailabilityMonitor monitor = service.CreateAvailability(
            new AvailabilityMonitorOptions { IsCritical = true, Name = "owned-cache" });
        await using var runner = new HealthCollectorRunner(service);
        await using IHealthCollectionRegistration registration = runner.Register(
            monitor,
            owner,
            new HealthCollectorOptions { Interval = TimeSpan.FromMinutes(1), IsRequired = true });
        await runner.StartAsync(waitCancellation);
        HealthCollectionAttempt attempt = await registration.CollectAsync(waitCancellation);
        _ = (registration.MonitorId, attempt.Disposition, attempt.EvaluationFailed, attempt.Diagnostic);
        registration.ActivatePeriodic();
        registration.Withdraw();
        await registration.WaitForDisposalAsync(waitCancellation);
        await runner.StopAsync(waitCancellation);
    }

    internal static async Task UseAcquiredOwnerLeaseAsync(IHealthCollector owner, CancellationToken lifetimeCancellation)
    {
        // The runner performs this protocol at runtime. This method checks its public shape only.
        if (owner.TryAcquire(out IHealthCollectionLease? lease) && lease is not null)
        {
            await using (lease)
            {
                HealthCollectionResult result = await lease.CollectAsync(lifetimeCancellation);
                _ = (result.Kind, result.IsFlagged, result.Outcome, result.Value, result.Diagnostic, result.Measurements);
            }
        }
    }

    internal static IReadOnlyList<HealthCollectionResult> ResultFactories(DateTimeOffset observedAtUtc)
    {
        HealthMeasurement[] measurements = [HealthMeasurement.Integer("rows", 42, observedAtUtc, "rows")];
        var diagnostic = new HealthDiagnostic(code: "RefreshFailed");
        return
        [
            HealthCollectionResult.Condition(isFlagged: false),
            HealthCollectionResult.Execution(HealthExecutionOutcome.Succeeded),
            HealthCollectionResult.Number(10, diagnostic),
            HealthCollectionResult.AvailabilityPublished().WithMeasurements(measurements),
            HealthCollectionResult.AvailabilityRefreshFailed(diagnostic),
            HealthCollectionResult.Diagnostics(measurements),
        ];
    }
}
