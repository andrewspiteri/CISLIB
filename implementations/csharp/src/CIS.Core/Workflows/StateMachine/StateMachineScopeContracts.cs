namespace CIS.Workflows.Hosting;

/// <summary>Optional C# capability supporting a caller-provided provider for one trigger call only.</summary>
/// <remarks>Implementations borrow the provider and never dispose its caller-owned scope/container.
/// This separate capability also permits consumer mocks without exposing providers on IStateMachine.</remarks>
public interface IStateMachineScopeExecution<TState, TTrigger> where TState : struct, Enum where TTrigger : struct, Enum
{
    /// <summary>Runs one call against the borrowed scope, which must remain alive until the task settles.</summary>
    Task<StateTransitionResult<TState, TTrigger>> TriggerInScope(TTrigger trigger, IServiceProvider scopedProvider,
        CancellationToken cancellationToken = default);
}
