// HC00 compile-only reads. Immutable values are obtained from the future real service.
using CIS.Healthcheck;

namespace HealthcheckContractConsumer;

internal static class SnapshotConsumer
{
    internal static void Read(IHealthSnapshotSource source, HealthcheckOptions options)
    {
        HealthSnapshot snapshot = source.CaptureSnapshot();
        _ = (snapshot.SchemaVersion, snapshot.Revision, snapshot.CapturedAtUtc,
            snapshot.HealthStatus, snapshot.Ready);
        HealthApplicationSnapshot application = snapshot.Application;
        _ = (application.Name, application.BuildVersion, application.ServiceVersion,
            application.InstanceId, application.HostName, application.OsDescription,
            application.StartupCompleted, application.IsStopping);
        HealthMonitorCounts counts = snapshot.MonitorCounts;
        _ = (counts.Active, counts.Critical, counts.Failing, counts.CriticalFailing, counts.Pending);

        foreach (HealthMonitorSnapshot monitor in snapshot.Monitors)
        {
            _ = (monitor.MonitorId, monitor.Name, monitor.Policy, monitor.IsCritical,
                monitor.State, monitor.RegisteredAtUtc, monitor.LastObservationAtUtc,
                monitor.Diagnostic, monitor.Measurements);
            if (monitor.Outcomes is { } outcomes)
            {
                _ = (outcomes.Successful, outcomes.Failed, outcomes.Skipped, outcomes.ExpectedCanceled,
                    outcomes.ConsecutiveFailures, outcomes.CountersSaturated, outcomes.LastOutcome,
                    outcomes.LastSuccessAtUtc, outcomes.LastFailureAtUtc,
                    outcomes.LastFailureDiagnostic, outcomes.Execution);
            }

            if (monitor.Numeric is { } numeric)
            {
                _ = (numeric.Comparison, numeric.Threshold, numeric.Unit,
                    numeric.LastValue, numeric.LastValueAtUtc);
            }

            if (monitor.Collection is { } collection)
            {
                _ = (collection.InProgress, collection.LastCompletedAtUtc,
                    collection.LastError, collection.LastErrorAtUtc);
            }
        }

        foreach (HealthReportView view in new[] { HealthReportView.Health, HealthReportView.Readiness, HealthReportView.Details })
        {
            HealthReportBuffer report = HealthReportFormatter.FormatSnapshot(snapshot, view, options);
            HealthReportBuffer pipe = HealthReportFormatter.FormatPipeResponse(snapshot, view, options);
            ReadOnlyMemory<byte> reportBytes = report.Utf8Bytes;
            ReadOnlyMemory<byte> pipeBytes = pipe.Utf8Bytes;
            _ = (reportBytes, report.IsError, report.ErrorCode, report.SelectedHealthy);
            _ = (pipeBytes, pipe.IsError, pipe.ErrorCode, pipe.SelectedHealthy);
        }
    }
}
