namespace CIS.Workflows;

internal sealed record PipelineRegistration(int Position, string Name, string MetricAlias, Type StepType);

internal sealed record PipelineRunInfo(string Name, Guid ExecutionId,
    IReadOnlyList<PipelineRegistration> Registrations, bool LogCalls);

// Owner-local admission/count logic only. No callbacks into a pipeline while holding its gate.
internal interface IPipelineLifetime
{
    void Enter();
    void Exit();
}

// This C# adapter owns only its explicitly created scope, never activated step objects.
internal interface IPipelineActivation : IAsyncDisposable
{
    object Activate(Type stepType);
}

internal interface IPipelineObservation
{
    void Started();
    void PhaseStarted(PipelineRegistration registration, PipelinePhase phase);
    void PhaseCompleted(PipelineRegistration registration, PipelinePhase phase, PipelineStatus outcome,
        TimeSpan duration, PipelineFailure? failure);
    void CleanupBudgetExpired();
    void LifecycleFailure(PipelineFailure failure);
    void Completed(PipelineStatus status, TimeSpan? duration, PipelineFailure? primary, int secondaryFailureCount);
}

internal sealed record PipelineEnvironment(TimeProvider Clock, Func<IPipelineActivation> CreateActivation,
    IPipelineLifetime Lifetime, Action<string, IReadOnlyList<PipelineRegistration>> ValidateDefinition,
    Func<PipelineRunInfo, IPipelineObservation?> Observe);

internal sealed class NullPipelineObservation : IPipelineObservation
{
    internal static readonly NullPipelineObservation Instance = new();

    private NullPipelineObservation()
    {
    }

    public void Started() { }
    public void PhaseStarted(PipelineRegistration registration, PipelinePhase phase) { }
    public void PhaseCompleted(PipelineRegistration registration, PipelinePhase phase, PipelineStatus outcome,
        TimeSpan duration, PipelineFailure? failure) { }
    public void CleanupBudgetExpired() { }
    public void LifecycleFailure(PipelineFailure failure) { }
    public void Completed(PipelineStatus status, TimeSpan? duration, PipelineFailure? primary, int secondaryFailureCount) { }
}
