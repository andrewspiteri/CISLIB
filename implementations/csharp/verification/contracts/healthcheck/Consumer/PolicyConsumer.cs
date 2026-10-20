// HC00 compile-only usage; no entry point and no execution against the facade.
using CIS.Healthcheck;

namespace HealthcheckContractConsumer;

internal static class PolicyConsumer
{
    internal static HealthcheckOptions Options() => HealthcheckOptions.Resolve(new HealthcheckOptions
    {
        MaxConcurrentCollections = 4,
        MaxMonitors = 1024,
        DetailsEnabled = true,
        PipeEnabled = true,
        PipeName = "cis-health-contract",
        PipeMaxClients = 8,
        PipeRequestTimeoutMs = 1000,
        MaxReportBytes = 4194304,
    });

    internal static async Task PoliciesAsync(CancellationToken waitCancellation)
    {
        await using var service = new HealthcheckService(
            Options(),
            new HealthApplicationIdentity
            {
                Name = "contract-consumer",
                BuildVersion = "0.1",
                ServiceVersion = "1",
                InstanceId = Guid.NewGuid(),
                HostName = "test-host",
                OsDescription = "test-os",
            },
            TimeProvider.System);

        var diagnostic = new HealthDiagnostic(code: "DependencyUnavailable", message: "Not ready yet.");
        await using IConditionMonitor condition = service.CreateCondition(new ConditionMonitorOptions
        {
            Name = "startup",
            IsCritical = true,
            InitiallyFlagged = true,
            InitialDiagnostic = diagnostic,
        });
        await using IConsecutiveFailureMonitor failures = service.CreateConsecutiveFailures(
            new ConsecutiveFailureMonitorOptions { Name = "refresh", IsCritical = false, FailureThreshold = 3 });
        await using INumericThresholdMonitor numeric = service.CreateNumericThreshold(new NumericThresholdMonitorOptions
        {
            Name = "queue-size",
            IsCritical = true,
            Comparison = NumericComparison.GreaterThan,
            Threshold = 10,
            Unit = "items",
        });
        await using IAvailabilityMonitor availability = service.CreateAvailability(
            new AvailabilityMonitorOptions { Name = "published-data", IsCritical = true });

        condition.Flag(diagnostic);
        condition.Clear();
        failures.ReportFailure(diagnostic);
        failures.ReportSkipped();
        failures.ReportExpectedCancellation();
        failures.ReportSuccess();
        numeric.ReportValue(10);
        numeric.ReportValue(11, diagnostic);
        availability.ReportRefreshFailure(diagnostic);
        availability.ReportPublished();
        availability.Invalidate(diagnostic);

        DateTimeOffset observedAtUtc = DateTimeOffset.UtcNow;
        HealthMeasurement[] measurements =
        [
            HealthMeasurement.Unavailable("last-known-size", observedAtUtc, "items"),
            HealthMeasurement.String("source", "primary", observedAtUtc),
            HealthMeasurement.Boolean("connected", true, observedAtUtc),
            HealthMeasurement.Integer("row-count", 12L, observedAtUtc, "rows"),
            HealthMeasurement.Number("latency", 1.5, observedAtUtc, "ms", isDiagnostic: true),
        ];
        foreach (HealthMeasurement measurement in measurements)
        {
            numeric.ReportMeasurement(measurement);
            _ = (measurement.Key, measurement.ValueKind, measurement.StringValue,
                measurement.BooleanValue, measurement.IntegerValue, measurement.NumberValue,
                measurement.Unit, measurement.ObservedAtUtc, measurement.IsDiagnostic);
        }

        _ = (condition.MonitorId, condition.Name, condition.IsCritical);
        _ = HealthDiagnostic.FromException(new InvalidOperationException("Example"), "ExampleFailure");
        _ = (diagnostic.Code, diagnostic.Message, diagnostic.ExceptionType, diagnostic.IsTruncated);
        service.MarkStartupCompleted();
        _ = service.CaptureSnapshot();
        condition.Withdraw();
        await condition.WaitForDisposalAsync(waitCancellation);
        service.BeginStopping();
        await service.DisposeAsync();
        await service.WaitForDisposalAsync(waitCancellation);
    }
}
