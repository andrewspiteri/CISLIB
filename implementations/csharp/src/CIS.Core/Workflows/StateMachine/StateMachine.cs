using CIS.Workflows.Hosting;

namespace CIS.Workflows;

internal sealed class StateMachine<TState, TTrigger, TContext> : IStateMachine<TState, TTrigger>,
    IStateMachineScopeExecution<TState, TTrigger>
    where TState : struct, Enum where TTrigger : struct, Enum where TContext : class
{
    private readonly object gate = new();
    private readonly StateMachineEnvironment environment;
    private readonly StateMachineDefinition<TState, TTrigger> definition;
    private readonly TContext context;
    private readonly StateMachineSettings settings;
    private Snapshot snapshot;
    private bool executing;

    internal StateMachine(StateMachineEnvironment environment, StateMachineDefinition<TState, TTrigger> definition,
        TContext context, StateMachineSettings settings, TState initialState)
    {
        this.environment = environment;
        this.definition = definition;
        this.context = context;
        this.settings = settings;
        snapshot = new(initialState, null);
    }

    public TState CurrentState
    {
        get
        {
            lock (gate)
            {
                return snapshot.State;
            }
        }
    }
    public bool IsExecuting
    {
        get
        {
            lock (gate)
            {
                return executing;
            }
        }
    }
    public StateTransitionResult<TState, TTrigger>? LastTransition
    {
        get
        {
            lock (gate)
            {
                return snapshot.Last;
            }
        }
    }
    public IReadOnlyCollection<TTrigger> PermittedTriggers
    {
        get
        {
            lock (gate)
            {
                return definition.Permitted[snapshot.State];
            }
        }
    }
    public bool CanTrigger(TTrigger trigger)
    {
        lock (gate)
        {
            return definition.Mappings.ContainsKey((snapshot.State, trigger));
        }
    }
    public Task<StateTransitionResult<TState, TTrigger>> Trigger(TTrigger trigger, CancellationToken cancellationToken = default)
    {
        return BeginCall(trigger, environment.CreateActivation, cancellationToken);
    }

    Task<StateTransitionResult<TState, TTrigger>> IStateMachineScopeExecution<TState, TTrigger>.TriggerInScope(
        TTrigger trigger, IServiceProvider scopedProvider, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scopedProvider);
        // The supplied request provider exists only in this call's local closure.
        return BeginCall(trigger, () => PipelineServiceActivation.Borrowed(scopedProvider), cancellationToken);
    }

    private Task<StateTransitionResult<TState, TTrigger>> BeginCall(TTrigger trigger,
        Func<IPipelineActivation> createActivation, CancellationToken token)
    {
        // A closed host is usage failure before ordinary observation/admission.
        environment.Lifetime.Enter();
        var completion = new TaskCompletionSource<StateTransitionResult<TState, TTrigger>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = RunCallAsync(trigger, createActivation, token, completion);
        return completion.Task;
    }

    private async Task RunCallAsync(TTrigger trigger, Func<IPipelineActivation> createActivation, CancellationToken token,
        TaskCompletionSource<StateTransitionResult<TState, TTrigger>> completion)
    {
        bool admitted = false;
        StateTransitionResult<TState, TTrigger>? result = null;
        Exception? engineFault = null;
        IStateMachineObservation observation = NullStateMachineObservation.Instance;
        TState source = default;
        var attempt = new Attempt(Guid.NewGuid());
        long? origin = null;
        try
        {
            origin = environment.Clock.GetTimestamp();
            bool preCancelled = token.IsCancellationRequested;
            lock (gate)
            {
                source = snapshot.State;
                if (!preCancelled && !executing)
                {
                    executing = true;
                    admitted = true;
                }
            }
            string sourceLabel = definition.StateLabels[source];
            string triggerLabel = definition.TriggerLabels.GetValueOrDefault(trigger) ?? "invalid";
            var info = new StateMachineCallInfo(definition.Metadata.Name, attempt.Id, sourceLabel, triggerLabel,
                settings.LogCalls, settings.LogRoutineAsInformation);
            try
            {
                observation = environment.Observe(info) ?? NullStateMachineObservation.Instance;
            }
            catch (Exception exception) when (IsOrdinary(exception)) { }
            Observe(observation.Started);
            if (admitted)
            {
                Observe(observation.GateAcquired);
            }

            if (preCancelled)
            {
                attempt.SetPrimary(Cancellation(StateTransitionPhase.Admission, token));
            }
            else if (!admitted)
            {
                attempt.SetPrimary(new("StateMachine.Busy", "Another attempt owns this machine.",
                    StateTransitionPhase.Admission, StateTransitionFailureKind.Rejection));
                attempt.RejectedOutcome = StateTransitionOutcome.Busy;
            }
            else if (token.IsCancellationRequested)
            {
                attempt.SetPrimary(Cancellation(StateTransitionPhase.Validation, token));
            }
            else if (!definition.TriggerLabels.ContainsKey(trigger))
            {
                attempt.SetPrimary(new("StateMachine.InvalidTrigger", "The trigger value is not declared.",
                    StateTransitionPhase.Validation, StateTransitionFailureKind.Rejection));
                attempt.RejectedOutcome = StateTransitionOutcome.Rejected;
            }
            else if (!definition.Mappings.TryGetValue((source, trigger), out StateMachineMapping<TState, TTrigger>? mapping))
            {
                attempt.SetPrimary(new("StateMachine.TriggerNotAllowed", "The trigger has no mapping from the source state.",
                    StateTransitionPhase.Validation, StateTransitionFailureKind.Rejection));
                attempt.RejectedOutcome = StateTransitionOutcome.Rejected;
            }
            else
            {
                attempt.Target = mapping.Target;
                if (mapping.Handler is { } handler)
                {
                    await RunSelectedAsync(attempt, handler, new(source, mapping.Target, trigger), createActivation, token, observation)
                        .ConfigureAwait(false);
                }
                else if (token.IsCancellationRequested)
                {
                    attempt.SetPrimary(Cancellation(StateTransitionPhase.Execute, token));
                }
                // Otherwise a direct transition has its final cancellation cutoff here.
            }

            TimeSpan? duration = Measure(origin, attempt, StateTransitionPhase.Disposal, observation);
            StateTransitionOutcome outcome = attempt.Outcome;
            TState current = outcome == StateTransitionOutcome.Succeeded ? attempt.Target!.Value : source;
            result = StateTransitionResult<TState, TTrigger>.Create(definition.Metadata.Name, attempt.Id, trigger,
                source, attempt.Target, current, outcome, attempt.Primary, duration, attempt.Report,
                attempt.Handling, attempt.Lifecycle, attempt.Expired);
            if (admitted)
            {
                lock (gate)
                {
                    snapshot = new(current, result);
                }
            }
            Observe(() => observation.Completed(new(outcome,
                attempt.Target is { } target ? definition.StateLabels[target] : null,
                definition.StateLabels[current], result.StateChanged, duration, attempt.Primary,
                attempt.SecondaryCount, attempt.Expired)));
        }
        catch (Exception exception)
        {
            // Broken engine invariants/fatal errors fault the Task; they are never
            // disguised as operational results. Application boundaries catch below.
            engineFault = exception;
            Observe(() => observation.Completed(new(StateTransitionOutcome.Failed, null,
                definition.StateLabels.GetValueOrDefault(source) ?? string.Empty, false, null, null, 0, attempt.Expired)));
        }
        finally
        {
            lock (gate)
            {
                if (admitted)
                {
                    executing = false;
                }
                if (engineFault is not null)
                {
                    completion.TrySetException(engineFault);
                }
                else
                {
                    completion.TrySetResult(result!);
                }
                // The shared drain covers the complete returned Task, including
                // Busy/pre-cancelled reporting. Exit is strict nonthrowing bookkeeping.
                environment.Lifetime.Exit();
            }
        }
    }

    private async Task RunSelectedAsync(Attempt attempt, StateMachineHandlerRegistration registration,
        TransitionInfo<TState, TTrigger> transition, Func<IPipelineActivation> createActivation,
        CancellationToken token, IStateMachineObservation observation)
    {
        IPipelineActivation? activation = null;
        object? instance = null;
        bool entered = false;
        PipelineStepExecutionStatus executionStatus = PipelineStepExecutionStatus.NotStarted;
        PipelineStepErrorStatus errorStatus = PipelineStepErrorStatus.NotCalled;
        StateTransitionFailure? forwardFailure = null;
        StateTransitionFailure? errorFailure = null;
        TimeSpan? executionDuration = null;
        TimeSpan? errorDuration = null;
        try
        {
            if (token.IsCancellationRequested)
            {
                attempt.SetPrimary(Cancellation(StateTransitionPhase.Construction, token));
            }
            if (attempt.Primary is null)
            {
                try
                {
                    activation = createActivation();
                }
                catch (Exception exception) when (IsOrdinary(exception))
                {
                    Lifecycle(attempt, Fault(exception, StateTransitionPhase.Construction), observation);
                }
                if (attempt.Primary is null && activation is null)
                {
                    throw new InvalidOperationException("The activation adapter returned no scope.");
                }
            }
            if (attempt.Primary is null)
            {
                try
                {
                    instance = activation!.Activate(registration.HandlerType);
                }
                catch (Exception exception) when (IsOrdinary(exception))
                {
                    Lifecycle(attempt, Fault(exception, StateTransitionPhase.Construction), observation);
                }
                if (attempt.Primary is null && instance is not IStateTransitionHandler<TState, TTrigger, TContext>)
                {
                    throw new InvalidOperationException("The activation adapter returned an incompatible handler.");
                }
            }
            if (attempt.Primary is null && token.IsCancellationRequested)
            {
                attempt.SetPrimary(Cancellation(StateTransitionPhase.Construction, token));
            }

            if (attempt.Primary is null)
            {
                var handler = (IStateTransitionHandler<TState, TTrigger, TContext>)instance!;
                long? started = Timestamp(attempt, StateTransitionPhase.Execute, observation);
                if (attempt.Primary is null)
                {
                    entered = true;
                    PipelineStatus phaseOutcome = PipelineStatus.Succeeded;
                    Observe(() => observation.PhaseStarted(registration, PipelinePhase.Execute));
                    try
                    {
                        Task<TransitionActionResult>? task = handler.Execute(context, transition, token);
                        if (task is null)
                        {
                            throw new ContractException("A handler Execute returned no task.");
                        }
                        TransitionActionResult? action = await task.ConfigureAwait(false);
                        if (action is null || action.Success && (action.ErrorCode is not null || action.ErrorMessage is not null)
                            || !action.Success && string.IsNullOrWhiteSpace(action.ErrorCode))
                        {
                            throw new ContractException("A handler Execute returned an invalid or null result.");
                        }
                        if (!action.Success)
                        {
                            forwardFailure = new(action.ErrorCode!, string.IsNullOrWhiteSpace(action.ErrorMessage)
                                    ? "The transition handler reported a failure." : action.ErrorMessage,
                                StateTransitionPhase.Execute, StateTransitionFailureKind.ReportedFailure);
                            attempt.SetPrimary(forwardFailure);
                            phaseOutcome = PipelineStatus.Failed;
                        }
                    }
                    catch (OperationCanceledException exception) when (exception.CancellationToken == token && token.IsCancellationRequested)
                    {
                        forwardFailure = Cancellation(StateTransitionPhase.Execute, token, exception);
                        attempt.SetPrimary(forwardFailure);
                        phaseOutcome = PipelineStatus.Cancelled;
                    }
                    catch (Exception exception) when (IsOrdinary(exception))
                    {
                        forwardFailure = Fault(exception, StateTransitionPhase.Execute);
                        attempt.SetPrimary(forwardFailure);
                        phaseOutcome = PipelineStatus.Failed;
                    }
                    executionStatus = phaseOutcome switch
                    {
                        PipelineStatus.Succeeded => PipelineStepExecutionStatus.Succeeded,
                        PipelineStatus.Cancelled => PipelineStepExecutionStatus.Cancelled,
                        _ => PipelineStepExecutionStatus.Failed
                    };
                    executionDuration = Measure(started, attempt, StateTransitionPhase.Execute, observation);
                    Observe(() => observation.PhaseCompleted(registration, PipelinePhase.Execute, phaseOutcome, executionDuration, forwardFailure));
                    if (attempt.Primary is null && token.IsCancellationRequested)
                    {
                        attempt.SetPrimary(Cancellation(StateTransitionPhase.Execute, token));
                    }
                }
            }

            // This is the cancellation cutoff. Disposal never starts compensation.
            if (entered && attempt.Primary is not null)
            {
                using var cleanup = new CancellationTokenSource();
                System.Threading.ITimer? timer = null;
                Exception? cancellationFailure = null;
                Task? cancellationWork = null;
                try
                {
                    if (settings.ErrorHandlingTimeout != Timeout.InfiniteTimeSpan)
                    {
                        try
                        {
                            timer = environment.Clock.CreateTimer(_ =>
                            {
                                if (Interlocked.Exchange(ref attempt.Expiry, 1) == 0)
                                {
                                    try
                                    {
                                        Volatile.Write(ref cancellationWork, cleanup.CancelAsync());
                                    }
                                    catch (Exception exception) when (IsOrdinary(exception))
                                    {
                                        Volatile.Write(ref cancellationFailure, exception);
                                    }
                                    Observe(observation.CleanupBudgetExpired);
                                }
                            }, null, settings.ErrorHandlingTimeout, Timeout.InfiniteTimeSpan);
                        }
                        catch (Exception exception) when (IsOrdinary(exception))
                        {
                            Lifecycle(attempt, Fault(exception, StateTransitionPhase.Disposal), observation);
                        }
                    }
                    long? started = Timestamp(attempt, StateTransitionPhase.Error, observation);
                    Observe(() => observation.PhaseStarted(registration, PipelinePhase.Error));
                    PipelineStatus phaseOutcome = PipelineStatus.Succeeded;
                    try
                    {
                        Task? task = ((IStateTransitionHandler<TState, TTrigger, TContext>)instance!).Error(
                            context, transition, attempt.Primary!, cleanup.Token);
                        if (task is null)
                        {
                            throw new ContractException("A handler Error returned no task.");
                        }
                        await task.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException exception) when (exception.CancellationToken == cleanup.Token && cleanup.IsCancellationRequested)
                    {
                        errorFailure = new("StateMachine.CleanupCancelled", "Compensation ended with requested cleanup cancellation.",
                            StateTransitionPhase.Error, StateTransitionFailureKind.CleanupTimeout, exception);
                        phaseOutcome = PipelineStatus.Cancelled;
                    }
                    catch (Exception exception) when (IsOrdinary(exception))
                    {
                        errorFailure = Fault(exception, StateTransitionPhase.Error);
                        phaseOutcome = PipelineStatus.Failed;
                    }
                    errorStatus = phaseOutcome switch
                    {
                        PipelineStatus.Succeeded => PipelineStepErrorStatus.Succeeded,
                        PipelineStatus.Cancelled => PipelineStepErrorStatus.Cancelled,
                        _ => PipelineStepErrorStatus.Failed
                    };
                    if (errorFailure is not null)
                    {
                        attempt.Handling.Add(errorFailure);
                    }
                    errorDuration = Measure(started, attempt, StateTransitionPhase.Error, observation);
                    Observe(() => observation.PhaseCompleted(registration, PipelinePhase.Error, phaseOutcome, errorDuration, errorFailure));
                }
                finally
                {
                    if (timer is not null)
                    {
                        try
                        {
                            await timer.DisposeAsync().ConfigureAwait(false);
                        }
                        catch (Exception exception) when (IsOrdinary(exception))
                        {
                            Lifecycle(attempt, Fault(exception, StateTransitionPhase.Disposal), observation);
                        }
                    }
                    if (cancellationFailure is { } failure)
                    {
                        Lifecycle(attempt, Fault(failure, StateTransitionPhase.Disposal), observation);
                    }
                    if (Volatile.Read(ref cancellationWork) is { } callbacks)
                    {
                        try
                        {
                            await callbacks.ConfigureAwait(false);
                        }
                        catch (Exception exception) when (IsOrdinary(exception))
                        {
                            Lifecycle(attempt, Fault(exception, StateTransitionPhase.Disposal), observation);
                        }
                    }
                }
            }
        }
        finally
        {
            try
            {
                if (instance is IAsyncDisposable asynchronous)
                {
                    await asynchronous.DisposeAsync().ConfigureAwait(false);
                }
                else if (instance is IDisposable synchronous)
                {
                    synchronous.Dispose();
                }
            }
            catch (Exception exception) when (IsOrdinary(exception))
            {
                Lifecycle(attempt, Fault(exception, StateTransitionPhase.Disposal), observation);
            }
            finally
            {
                if (activation is not null)
                {
                    try
                    {
                        await activation.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception) when (IsOrdinary(exception))
                    {
                        Lifecycle(attempt, Fault(exception, StateTransitionPhase.Disposal), observation);
                    }
                }
            }
        }
        attempt.Report = new(registration.Name, executionStatus, errorStatus, forwardFailure, errorFailure, executionDuration, errorDuration);
    }

    private long? Timestamp(Attempt attempt, StateTransitionPhase phase, IStateMachineObservation observation)
    {
        try
        {
            return environment.Clock.GetTimestamp();
        }
        catch (Exception exception) when (IsOrdinary(exception))
        {
            Lifecycle(attempt, Fault(exception, phase), observation);
            return null;
        }
    }
    private TimeSpan? Measure(long? started, Attempt attempt, StateTransitionPhase phase, IStateMachineObservation observation)
    {
        if (!started.HasValue)
        {
            return null;
        }
        try
        {
            return environment.Clock.GetElapsedTime(started.Value);
        }
        catch (Exception exception) when (IsOrdinary(exception))
        {
            Lifecycle(attempt, Fault(exception, phase), observation);
            return null;
        }
    }
    private static void Lifecycle(Attempt attempt, StateTransitionFailure failure, IStateMachineObservation observation)
    {
        attempt.Lifecycle.Add(failure);
        attempt.SetPrimary(failure);
        Observe(() => observation.LifecycleFailure(failure));
    }
    private static StateTransitionFailure Cancellation(StateTransitionPhase phase, CancellationToken token,
        OperationCanceledException? exception = null)
    {
        return new("StateMachine.Cancelled", "Execution cancellation was observed.", phase,
            StateTransitionFailureKind.Cancellation, exception ?? new OperationCanceledException(token));
    }
    private static StateTransitionFailure Fault(Exception exception, StateTransitionPhase phase)
    {
        string code = exception is ContractException ? "StateMachine.HandlerContract" : phase switch
        {
            StateTransitionPhase.Construction => "StateMachine.Construction",
            StateTransitionPhase.Disposal => "StateMachine.Disposal",
            _ => "StateMachine.Exception"
        };
        return new(code, "A transition phase failed. Inspect the retained original exception.", phase,
            exception is ContractException ? StateTransitionFailureKind.ContractViolation : StateTransitionFailureKind.Exception, exception);
    }
    private static bool IsOrdinary(Exception exception)
    {
        return exception is not OutOfMemoryException and not StackOverflowException and not AccessViolationException;
    }
    private static void Observe(Action callback)
    {
        try
        {
            callback();
        }
        catch (Exception exception) when (IsOrdinary(exception)) { }
    }
    private sealed record Snapshot(TState State, StateTransitionResult<TState, TTrigger>? Last);
    private sealed class ContractException(string message) : InvalidOperationException(message)
    {
    }
    private sealed class Attempt(Guid id)
    {
        internal Guid Id { get; } = id;
        internal TState? Target;
        internal StateTransitionFailure? Primary;
        internal StateTransitionOutcome? RejectedOutcome;
        internal StateTransitionHandlerReport? Report;
        internal List<StateTransitionFailure> Handling { get; } = [];
        internal List<StateTransitionFailure> Lifecycle { get; } = [];
        internal int Expiry;
        internal bool Expired => Volatile.Read(ref Expiry) != 0;
        internal int SecondaryCount => Handling.Count + Lifecycle.Count(failure => !ReferenceEquals(failure, Primary));
        internal StateTransitionOutcome Outcome => RejectedOutcome ?? (Primary is null ? StateTransitionOutcome.Succeeded
            : Primary.Kind == StateTransitionFailureKind.Cancellation ? StateTransitionOutcome.Cancelled : StateTransitionOutcome.Failed);
        internal void SetPrimary(StateTransitionFailure failure)
        {
            Primary ??= failure;
        }
    }
}
