namespace CIS.Examples.CountryData;

// One catalog owns one registration. These two persistent budgets are independent
// of provider messages and transitions, so flapping cannot replenish either one.
internal sealed class CountryRefreshDiagnostics(ILogger<CountryCatalog> logger, TimeProvider clock)
{
    internal static readonly EventId FailureEvent = new(2101, "country.refresh.failed");
    internal static readonly EventId RecoveryEvent = new(2102, "country.refresh.recovered");
    private static readonly TimeSpan ReportingWindow = TimeSpan.FromMinutes(1);

    private readonly object gate = new();
    private int failing;
    private bool failureReported;
    private bool recoveryReported;
    private long lastFailureReport;
    private long lastRecoveryReport;
    private long suppressedFailures;
    private long suppressedRecoveries;

    internal void ExpectedFailure(string code) => Failure(code, null);

    internal void UnexpectedFailure(Exception exception) => Failure("country.unexpected_error", exception);

    private void Failure(string code, Exception? exception)
    {
        LogLevel level = exception is null ? LogLevel.Warning : LogLevel.Error;
        bool enabled = IsEnabled(level);
        long now = 0;
        bool clockAvailable = enabled && TryGetTimestamp(out now);
        long suppressed;

        lock (gate)
        {
            Volatile.Write(ref failing, 1);
            if (!clockAvailable || !CanReport(failureReported, lastFailureReport, now))
            {
                Increment(ref suppressedFailures);
                return;
            }

            failureReported = true;
            lastFailureReport = now;
            suppressed = suppressedFailures;
            suppressedFailures = 0;
        }

        try
        {
            logger.Log(level, FailureEvent, exception,
                "Country refresh failed: {Code}. Suppressed {SuppressedFailures} repeated failures.", code, suppressed);
        }
        catch (Exception)
        {
            // Diagnostics cannot replace an actual operation outcome or fault.
        }
    }

    internal void Succeeded()
    {
        if (Volatile.Read(ref failing) == 0)
        {
            return;
        }

        bool enabled = IsEnabled(LogLevel.Information);
        long now = 0;
        bool clockAvailable = enabled && TryGetTimestamp(out now);
        long suppressed;

        lock (gate)
        {
            if (failing == 0)
            {
                return;
            }

            Volatile.Write(ref failing, 0);
            if (!clockAvailable || !CanReport(recoveryReported, lastRecoveryReport, now))
            {
                Increment(ref suppressedRecoveries);
                return;
            }

            recoveryReported = true;
            lastRecoveryReport = now;
            suppressed = suppressedRecoveries;
            suppressedRecoveries = 0;
        }

        try
        {
            logger.LogInformation(RecoveryEvent,
                "Country refresh recovered. Suppressed {SuppressedRecoveries} recovery transitions.", suppressed);
        }
        catch (Exception)
        {
            // Reporting does not change successful publication or health handoff.
        }
    }

    private bool IsEnabled(LogLevel level)
    {
        try
        {
            return logger.IsEnabled(level);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool TryGetTimestamp(out long timestamp)
    {
        try
        {
            timestamp = clock.GetTimestamp();
            return true;
        }
        catch (Exception)
        {
            timestamp = 0;
            return false;
        }
    }

    private bool CanReport(bool reported, long previous, long now)
    {
        if (!reported)
        {
            return true;
        }

        try
        {
            return clock.GetElapsedTime(previous, now) >= ReportingWindow;
        }
        catch (Exception)
        {
            // A broken diagnostic clock cannot make a safe reporting window.
            return false;
        }
    }

    private static void Increment(ref long value)
    {
        if (value < long.MaxValue)
        {
            value++;
        }
    }
}
