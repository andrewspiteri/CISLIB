using System.Globalization;
using System.Text;
using CIS.Metrics;
using CIS.Text;
using Microsoft.Extensions.Logging;

namespace CIS.Workflows;

// Operation-local counters only. No business context, service or activated step is retained here.
internal sealed class PipelineObservation(PipelineRunInfo info, ILogger? logger, PipelineMetrics? metrics) : IPipelineObservation
{
    private static readonly EventId Accepted = new(13000, "pipeline.accepted");
    private static readonly EventId PhaseBeginning = new(13001, "pipeline.phase.started");
    private static readonly EventId PhaseEnded = new(13002, "pipeline.phase.completed");
    private static readonly EventId CleanupExpired = new(13003, "pipeline.cleanup.expired");
    private static readonly EventId LifecycleFault = new(13004, "pipeline.lifecycle.failed");
    private static readonly EventId Terminal = new(13005, "pipeline.completed");
    private int started;
    private int completed;
    private int forwardCount;
    private int errorCount;

    public void Started()
    {
        if (Interlocked.Exchange(ref started, 1) != 0)
        {
            return;
        }

        metrics?.Started(info.Name);
        Log(LogLevel.Information, current => current.LogInformation(Accepted,
            "Pipeline {Pipeline} accepted {ExecutionId} with {RegistrationCount} registrations.",
            info.Name, info.ExecutionId, info.Registrations.Count));
    }

    public void PhaseStarted(PipelineRegistration registration, PipelinePhase phase)
    {
        if (phase == PipelinePhase.Execute)
        {
            forwardCount++;
        }
        else
        {
            errorCount++;
        }

        Log(LogLevel.Debug, current => current.LogDebug(PhaseBeginning,
            "Pipeline {Pipeline} {ExecutionId} registration {Position} {Step} begins {Phase}.",
            info.Name, info.ExecutionId, registration.Position, registration.Name, phase));
    }

    public void PhaseCompleted(PipelineRegistration registration, PipelinePhase phase, PipelineStatus outcome,
        TimeSpan duration, PipelineFailure? failure)
    {
        metrics?.PhaseCompleted(info.Name, registration.MetricAlias, phase, outcome, duration);
        LogLevel level = PhaseLevel(phase, outcome, failure);
        Log(level, current => current.Log(level, PhaseEnded, failure?.Exception,
            "Pipeline {Pipeline} {ExecutionId} registration {Position} {Step} completed {Phase} with {Outcome} in {DurationSeconds} seconds ({ErrorCode}, {ErrorMessage}).",
            info.Name, info.ExecutionId, registration.Position, registration.Name, phase, outcome,
            duration.TotalSeconds, SafeCode(failure), SafeMessage(failure)));
    }

    public void CleanupBudgetExpired()
    {
        Log(LogLevel.Warning, current => current.LogWarning(CleanupExpired,
            "Pipeline {Pipeline} {ExecutionId} cleanup budget requested cancellation; handlers settle cooperatively.",
            info.Name, info.ExecutionId));
    }

    public void LifecycleFailure(PipelineFailure failure)
    {
        Log(LogLevel.Error, current => current.LogError(LifecycleFault, failure.Exception,
            "Pipeline {Pipeline} {ExecutionId} lifecycle {Phase} failed at {Position} with {FailureKind} ({ErrorCode}).",
            info.Name, info.ExecutionId, failure.Phase, failure.StepPosition, failure.Kind, SafeCode(failure)));
    }

    public void Completed(PipelineStatus status, TimeSpan? duration, PipelineFailure? primary, int secondaryFailureCount)
    {
        if (Interlocked.Exchange(ref completed, 1) != 0)
        {
            return;
        }

        if (Volatile.Read(ref started) != 0)
        {
            metrics?.Completed(info.Name, status, duration);
        }

        LogLevel level = TerminalLevel(status, primary, secondaryFailureCount);
        Log(level, current => current.Log(level, Terminal,
            "Pipeline {Pipeline} {ExecutionId} settled {Outcome} in {DurationSeconds} seconds; entered {ForwardCount}, handlers {ErrorCount}, secondary failures {SecondaryFailureCount} ({ErrorCode}).",
            info.Name, info.ExecutionId, status, duration?.TotalSeconds, forwardCount, errorCount,
            secondaryFailureCount, SafeCode(primary)));
    }

    private static LogLevel PhaseLevel(PipelinePhase phase, PipelineStatus status, PipelineFailure? failure)
    {
        if (status == PipelineStatus.Succeeded)
        {
            return LogLevel.Information;
        }

        if (phase == PipelinePhase.Error)
        {
            return failure?.Kind == PipelineFailureKind.CleanupTimeout ? LogLevel.Warning : LogLevel.Error;
        }

        return status == PipelineStatus.Cancelled ? LogLevel.Information
            : failure?.Kind == PipelineFailureKind.ReportedFailure ? LogLevel.Warning : LogLevel.Error;
    }

    private static LogLevel TerminalLevel(PipelineStatus status, PipelineFailure? primary, int secondaryFailureCount)
    {
        if (secondaryFailureCount != 0)
        {
            return LogLevel.Error;
        }

        return status != PipelineStatus.Failed ? LogLevel.Information
            : primary?.Kind == PipelineFailureKind.ReportedFailure ? LogLevel.Warning : LogLevel.Error;
    }

    private static string? SafeCode(PipelineFailure? failure)
    {
        return failure is null ? null : SafeText(failure.Code, 128);
    }

    private static string? SafeMessage(PipelineFailure? failure)
    {
        return failure?.Kind == PipelineFailureKind.ReportedFailure ? SafeText(failure.Message, 512) : null;
    }

    private static string SafeText(string value, int maximumScalars)
    {
        string prefix;
        try
        {
            prefix = UnicodeScalarText.Prefix(value, maximumScalars, out _);
        }
        catch (ArgumentException)
        {
            return "invalid text";
        }

        var safe = new StringBuilder(prefix.Length);
        foreach (Rune rune in prefix.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format
                or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
            {
                safe.Append(' ');
            }
            else
            {
                safe.Append(rune.ToString());
            }
        }

        return safe.ToString();
    }

    private void Log(LogLevel level, Action<ILogger> write)
    {
        if (!info.LogCalls || logger is null)
        {
            return;
        }

        try
        {
            if (logger.IsEnabled(level))
            {
                write(logger);
            }
        }
        catch (Exception error) when (MetricsRuntime.IsOrdinary(error))
        {
            // Diagnostics never become the business outcome or interrupt reverse handlers.
        }
    }
}
