namespace CIS.Workflows;

internal sealed record StateMachineHandlerRegistration(string Name, string MetricAlias, Type HandlerType);
internal sealed record StateMachineDefinitionMetadata(string Name, IReadOnlyList<string> States,
    IReadOnlyList<string> Triggers, IReadOnlyList<StateMachineHandlerRegistration> Handlers);
internal sealed record StateMachineCallInfo(string Name, Guid AttemptId, string Source, string Trigger,
    bool LogCalls, bool LogRoutineAsInformation);
internal sealed record StateMachineCompletion(StateTransitionOutcome Outcome, string? Target, string CurrentState,
    bool StateChanged, TimeSpan? Duration, StateTransitionFailure? Error, int SecondaryFailureCount, bool CleanupBudgetExpired);

internal interface IStateMachineObservation
{
    void Started();
    void GateAcquired();
    void PhaseStarted(StateMachineHandlerRegistration handler, PipelinePhase phase);
    void PhaseCompleted(StateMachineHandlerRegistration handler, PipelinePhase phase, PipelineStatus outcome,
        TimeSpan? duration, StateTransitionFailure? failure);
    void CleanupBudgetExpired();
    void LifecycleFailure(StateTransitionFailure failure);
    void Completed(StateMachineCompletion completion);
}

internal sealed record StateMachineEnvironment(TimeProvider Clock, Func<IPipelineActivation> CreateActivation,
    IPipelineLifetime Lifetime, Action<StateMachineDefinitionMetadata> ValidateDefinition,
    Func<StateMachineCallInfo, IStateMachineObservation?> Observe);

internal sealed class NullStateMachineObservation : IStateMachineObservation
{
    internal static readonly NullStateMachineObservation Instance = new();
    private NullStateMachineObservation()
    {
    }
    public void Started()
    {
    }
    public void GateAcquired()
    {
    }
    public void PhaseStarted(StateMachineHandlerRegistration handler, PipelinePhase phase)
    {
    }
    public void PhaseCompleted(StateMachineHandlerRegistration handler, PipelinePhase phase, PipelineStatus outcome,
        TimeSpan? duration, StateTransitionFailure? failure)
    {
    }
    public void CleanupBudgetExpired()
    {
    }
    public void LifecycleFailure(StateTransitionFailure failure)
    {
    }
    public void Completed(StateMachineCompletion completion)
    {
    }
}
