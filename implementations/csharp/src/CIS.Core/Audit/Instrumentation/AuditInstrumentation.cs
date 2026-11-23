using CIS.Audit.Hosting;
using CIS.Metrics;
using Microsoft.Extensions.Logging;

namespace CIS.Audit;

internal enum AuditCaptureResult
{
    Captured,
    Rejected,
    Faulted
}

// One optional host binding, one counter, no history, collection callback or delivery observations.
internal sealed class AuditInstrumentation
{
    internal const string MetricName = "cis.audit.capture.results";
    private readonly object gate = new();
    private readonly MetricsAttachment attachment = new();
    private readonly TimeProvider clock;
    private readonly ILogger? logger;
    private ICounter? counter;
    private int remainingReports = 8;
    private long? lastReport;

    internal AuditInstrumentation(AuditHostingRegistration registration, TimeProvider? clock = null, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(registration);
        RegistrationIdentity = registration;
        this.clock = clock ?? TimeProvider.System;
        try
        {
            logger = loggerFactory?.CreateLogger("CIS.Audit");
        }
        catch (Exception error) when (MetricsRuntime.IsOrdinary(error))
        {
        }
    }

    internal object RegistrationIdentity
    {
        get;
    }

    internal void AttachMetrics(MetricsRuntime runtime)
    {
        if (attachment.Attach(runtime))
        {
            Volatile.Write(ref counter, runtime.CreateCounter(new(MetricName,
                description: "One completed local capture attempt, without persistence or delivery evidence.",
                allowedDimensionKeys: ["category", "outcome"])));
        }
    }

    internal AuditCaptureObservation Capture(AuditCategory category)
        => new(this, Volatile.Read(ref counter), category);

    internal void Complete(ICounter? captured, AuditCategory category, AuditCaptureResult result)
    {
        string categoryName = category switch
        {
            AuditCategory.Business => "business",
            AuditCategory.Administration => "administration",
            AuditCategory.Security => "security",
            _ => throw new ArgumentOutOfRangeException(nameof(category))
        };
        string outcome = result switch
        {
            AuditCaptureResult.Captured => "captured",
            AuditCaptureResult.Rejected => "rejected",
            AuditCaptureResult.Faulted => "faulted",
            _ => throw new ArgumentOutOfRangeException(nameof(result))
        };
        Attempt(() => captured?.Add(1, new(("category", categoryName), ("outcome", outcome))));
        if (result != AuditCaptureResult.Captured)
        {
            Attempt(() =>
            {
                if (logger is not null && logger.IsEnabled(LogLevel.Warning) && TryTakeReport())
                {
                    logger.LogWarning(new EventId(20000, "audit.capture"),
                        "Audit capture {Outcome} ({Category}).", outcome, categoryName);
                }
            });
        }
    }

    private bool TryTakeReport()
    {
        lock (gate)
        {
            long now = clock.GetTimestamp();
            if (remainingReports == 0 || lastReport is { } previous
                && clock.GetElapsedTime(previous, now) < TimeSpan.FromSeconds(30))
            {
                return false;
            }
            remainingReports--;
            lastReport = now;
            return true;
        }
    }

    private static void Attempt(Action action)
    {
        try
        {
            action();
        }
        catch (Exception error) when (MetricsRuntime.IsOrdinary(error))
        {
            // Optional telemetry cannot replace a capture, its original fault or cancellation.
        }
    }
}

internal sealed class AuditCaptureObservation(AuditInstrumentation owner, ICounter? counter, AuditCategory category)
{
    private int completed;

    internal void Complete(AuditCaptureResult result)
    {
        if (Interlocked.Exchange(ref completed, 1) == 0)
        {
            owner.Complete(counter, category, result);
        }
    }
}
