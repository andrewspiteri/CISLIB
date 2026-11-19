using System.Globalization;
using System.Text;
using CIS.Metrics;
using CIS.Text;
using Microsoft.Extensions.Logging;

namespace CIS.Workflows;

// One public call's observation, including calls which never acquire the instance gate.
internal sealed class StateMachineObservation(StateMachineCallInfo info, ILogger? logger,
    StateMachineMetrics? metrics) : IStateMachineObservation
{
    private static readonly EventId Beginning = new(13100, "statemachine.started");
    private static readonly EventId PhaseBeginning = new(13101, "statemachine.phase.started");
    private static readonly EventId PhaseEnded = new(13102, "statemachine.phase.completed");
    private static readonly EventId CleanupExpired = new(13103, "statemachine.cleanup.expired");
    private static readonly EventId LifecycleFault = new(13104, "statemachine.lifecycle.failed");
    private static readonly EventId Terminal = new(13105, "statemachine.completed");
    private int started;
    private int admitted;
    private int completed;

    public void Started()
    {
        if (Interlocked.Exchange(ref started, 1) != 0)
        {
            return;
        }

        Log(LogLevel.Debug, current => current.LogDebug(Beginning,
            "State machine {Machine} call {AttemptId} begins from {Source} with {Trigger}.",
            info.Name, info.AttemptId, info.Source, info.Trigger));
    }

    public void GateAcquired()
    {
        if (Interlocked.Exchange(ref admitted, 1) == 0)
        {
            metrics?.GateAcquired(info.Name);
        }
    }

    public void PhaseStarted(StateMachineHandlerRegistration handler, PipelinePhase phase)
    {
        Log(LogLevel.Debug, current => current.LogDebug(PhaseBeginning,
            "State machine {Machine} call {AttemptId} handler {Handler} begins {Phase}.",
            info.Name, info.AttemptId, handler.Name, phase));
    }

    public void PhaseCompleted(StateMachineHandlerRegistration handler, PipelinePhase phase, PipelineStatus outcome,
        TimeSpan? duration, StateTransitionFailure? failure)
    {
        metrics?.PhaseCompleted(info.Name, handler.MetricAlias, phase, outcome, duration);
        LogLevel level = outcome == PipelineStatus.Succeeded ? RoutineLevel
            : failure?.Kind == StateTransitionFailureKind.ReportedFailure ? LogLevel.Warning
            : failure?.Kind == StateTransitionFailureKind.CleanupTimeout ? LogLevel.Warning
            : outcome == PipelineStatus.Cancelled ? LogLevel.Debug : LogLevel.Error;
        Log(level, current => current.Log(level, PhaseEnded, failure?.Exception,
            "State machine {Machine} call {AttemptId} handler {Handler} completed {Phase} with {Outcome} in {DurationSeconds} seconds ({ErrorCode}, {ErrorMessage}).",
            info.Name, info.AttemptId, handler.Name, phase, outcome, duration?.TotalSeconds,
            Code(failure), failure?.Kind == StateTransitionFailureKind.ReportedFailure ? Safe(failure.Message, 512) : null));
    }

    public void CleanupBudgetExpired()
    {
        Log(LogLevel.Warning, current => current.LogWarning(CleanupExpired,
            "State machine {Machine} call {AttemptId} compensation budget requested cancellation; actual cleanup remains awaited.",
            info.Name, info.AttemptId));
    }

    public void LifecycleFailure(StateTransitionFailure failure)
    {
        Log(LogLevel.Error, current => current.LogError(LifecycleFault, failure.Exception,
            "State machine {Machine} call {AttemptId} lifecycle {Phase} failed with {FailureKind} ({ErrorCode}).",
            info.Name, info.AttemptId, failure.Phase, failure.Kind, Code(failure)));
    }

    public void Completed(StateMachineCompletion completion)
    {
        if (Interlocked.Exchange(ref completed, 1) != 0)
        {
            return;
        }

        metrics?.Completed(info, completion, Volatile.Read(ref admitted) != 0);
        LogLevel level = completion.SecondaryFailureCount != 0 ? LogLevel.Error
            : completion.Outcome == StateTransitionOutcome.Succeeded ? RoutineLevel
            : completion.Outcome != StateTransitionOutcome.Failed ? LogLevel.Debug
            : completion.Error?.Kind == StateTransitionFailureKind.ReportedFailure ? LogLevel.Warning : LogLevel.Error;
        Log(level, current => current.Log(level, Terminal,
            "State machine {Machine} call {AttemptId} from {Source} with {Trigger} towards {Target} settled {Outcome} at {CurrentState}, changed {StateChanged}, in {DurationSeconds} seconds with {SecondaryFailureCount} secondary failures ({ErrorCode}).",
            info.Name, info.AttemptId, info.Source, info.Trigger, completion.Target, completion.Outcome,
            completion.CurrentState, completion.StateChanged, completion.Duration?.TotalSeconds,
            completion.SecondaryFailureCount, Code(completion.Error)));
    }

    private LogLevel RoutineLevel => info.LogRoutineAsInformation ? LogLevel.Information : LogLevel.Debug;

    private static string? Code(StateTransitionFailure? failure) => failure is null ? null : Safe(failure.Code, 128);

    private static string Safe(string value, int maximumScalars)
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
            // Observations do not own transition commitment, compensation or cleanup.
        }
    }
}
