using System.Runtime.ExceptionServices;

namespace CIS.Workflows;

internal sealed class Pipeline<TContext> : IPipeline<TContext> where TContext : class
{
    private readonly object gate = new();
    private readonly PipelineSettings settings;
    private readonly IReadOnlyList<PipelineRegistration> registrations;
    private readonly StepState[] states;
    private readonly List<PipelineFailure> handlingFailures = [];
    private readonly List<PipelineFailure> lifecycleFailures = [];
    private PipelineEnvironment? environment;
    private Task? operation;
    private PipelineStatus status;
    private PipelineFailure? error;
    private TimeSpan? duration;
    private bool cleanupBudgetExpired;

    internal Pipeline(PipelineEnvironment environment, string name, TContext context,
        PipelineSettings settings, IReadOnlyList<PipelineRegistration> registrations)
    {
        this.environment = environment;
        this.settings = settings;
        this.registrations = registrations;
        Name = name;
        Context = context;
        ExecutionId = Guid.NewGuid();
        states = registrations.Select(registration => new StepState(registration)).ToArray();
    }

    public string Name
    {
        get;
    }
    public Guid ExecutionId
    {
        get;
    }
    public TContext Context
    {
        get;
    }
    public bool Success => Status == PipelineStatus.Succeeded;
    public PipelineStatus Status
    {
        get
        {
            lock (gate)
            {
                return status;
            }
        }
    }
    public PipelineFailure? Error
    {
        get
        {
            lock (gate)
            {
                return error;
            }
        }
    }
    public TimeSpan? Duration
    {
        get
        {
            lock (gate)
            {
                return duration;
            }
        }
    }
    public bool CleanupBudgetExpired
    {
        get
        {
            lock (gate)
            {
                return cleanupBudgetExpired;
            }
        }
    }
    public IReadOnlyList<PipelineStepReport> Steps
    {
        get
        {
            lock (gate)
            {
                return Array.AsReadOnly(states.Select(state => state.Snapshot()).ToArray());
            }
        }
    }
    public IReadOnlyList<PipelineStepReport> ExecutedSteps
    {
        get
        {
            lock (gate)
            {
                return Array.AsReadOnly(states.Where(state => state.ExecutionStatus != PipelineStepExecutionStatus.NotStarted)
                    .Select(state => state.Snapshot()).ToArray());
            }
        }
    }
    public PipelineStepReport? FailedStep
    {
        get
        {
            lock (gate)
            {
                return states.FirstOrDefault(state => state.ExecutionStatus == PipelineStepExecutionStatus.Failed)?.Snapshot();
            }
        }
    }
    public IReadOnlyList<PipelineFailure> ErrorHandlingFailures
    {
        get
        {
            lock (gate)
            {
                return Array.AsReadOnly(handlingFailures.ToArray());
            }
        }
    }
    public IReadOnlyList<PipelineFailure> LifecycleFailures
    {
        get
        {
            lock (gate)
            {
                return Array.AsReadOnly(lifecycleFailures.ToArray());
            }
        }
    }

    public Task Execute(CancellationToken cancellationToken = default)
    {
        PipelineEnvironment captured;
        TaskCompletionSource completion;
        lock (gate)
        {
            if (operation is not null)
            {
                throw new InvalidOperationException("A built pipeline accepts exactly one execution.");
            }

            captured = environment!;
            // This is owner-local count/gate logic, never application code.
            // A closed host rejects before this instance is consumed or observed.
            captured.Lifetime.Enter();
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            operation = completion.Task;
            status = PipelineStatus.Running;
        }

        _ = RunAsync(captured, completion, cancellationToken);
        return completion.Task;
    }

    private async Task RunAsync(PipelineEnvironment captured, TaskCompletionSource completion, CancellationToken token)
    {
        IPipelineObservation observation = NullPipelineObservation.Instance;
        IPipelineActivation? activation = null;
        object?[] instances = new object?[registrations.Count];
        PipelineFailure? primary = null;
        ExceptionDispatchInfo? outward = null;
        bool cancelled = false;
        bool unwindStarted = false;
        PipelinePhase currentPhase = PipelinePhase.Construction;
        int entered = 0;
        long origin = 0;
        bool timingStarted = false;

        void SelectFailure(Exception exception, PipelinePhase phase, PipelineRegistration? registration, bool lifecycle)
        {
            PipelineFailure failure = Failure(exception, phase, registration);
            if (lifecycle)
            {
                AddLifecycle(failure, observation);
            }
            if (primary is null)
            {
                primary = failure;
                outward = ExceptionDispatchInfo.Capture(exception);
                lock (gate)
                {
                    error = failure;
                }
            }
        }

        void SelectCancellation(PipelinePhase phase, PipelineRegistration? registration, OperationCanceledException? exception = null)
        {
            primary = new("Pipeline.Cancelled", "Execution cancellation was observed.", phase,
                PipelineFailureKind.Cancellation, registration?.Position, registration?.Name,
                exception ?? new OperationCanceledException(token));
            cancelled = true;
            lock (gate)
            {
                error = primary;
            }
        }

        try
        {
            origin = captured.Clock.GetTimestamp();
            timingStarted = true;
            var info = new PipelineRunInfo(Name, ExecutionId, registrations, settings.LogCalls);
            try
            {
                observation = captured.Observe(info) ?? NullPipelineObservation.Instance;
            }
            catch (Exception) { /* Observation availability never owns execution. */ }
            Observe(observation.Started);

            if (token.IsCancellationRequested)
            {
                SelectCancellation(PipelinePhase.Construction, null);
            }
            else
            {
                try
                {
                    activation = captured.CreateActivation()
                        ?? throw new InvalidOperationException("The pipeline activation adapter returned no scope.");
                    for (int index = 0; index < registrations.Count; index++)
                    {
                        if (token.IsCancellationRequested)
                        {
                            SelectCancellation(PipelinePhase.Construction, null);
                            break;
                        }

                        PipelineRegistration registration = registrations[index];
                        try
                        {
                            object instance = activation.Activate(registration.StepType);
                            instances[index] = instance;
                            if (instance is not IPipelineStep<TContext>)
                            {
                                throw new ContractException("The activation adapter returned no compatible pipeline step.");
                            }
                        }
                        catch (Exception exception)
                        {
                            // Constructors are synchronous phase faults, even for OCE.
                            SelectFailure(exception, PipelinePhase.Construction, registration, lifecycle: true);
                            break;
                        }
                    }
                }
                catch (Exception exception)
                {
                    SelectFailure(exception, PipelinePhase.Construction, null, lifecycle: true);
                }
            }

            currentPhase = PipelinePhase.Execute;
            for (int index = 0; primary is null && index < instances.Length; index++)
            {
                if (token.IsCancellationRequested)
                {
                    SelectCancellation(PipelinePhase.Execute, null);
                    break;
                }

                PipelineRegistration registration = registrations[index];
                long started = captured.Clock.GetTimestamp();
                lock (gate)
                {
                    states[index].ExecutionStatus = PipelineStepExecutionStatus.Running;
                }
                entered++;
                Observe(() => observation.PhaseStarted(registration, PipelinePhase.Execute));
                PipelineFailure? stepFailure = null;
                PipelineStatus outcome = PipelineStatus.Succeeded;
                try
                {
                    Task<PipelineStepResult>? task = ((IPipelineStep<TContext>)instances[index]!).Execute(Context, token);
                    if (task is null)
                    {
                        throw new ContractException("A pipeline Execute method returned a null task.");
                    }

                    PipelineStepResult? result = await task.ConfigureAwait(false);
                    if (result is null || result.Success && (result.ErrorCode is not null || result.ErrorMessage is not null)
                        || !result.Success && string.IsNullOrWhiteSpace(result.ErrorCode))
                    {
                        throw new ContractException("A pipeline Execute method returned an invalid or null result.");
                    }
                    if (!result.Success)
                    {
                        stepFailure = new(result.ErrorCode!, result.ErrorMessage ?? "The pipeline step reported a failure.",
                            PipelinePhase.Execute, PipelineFailureKind.ReportedFailure, registration.Position, registration.Name);
                        primary = stepFailure;
                        outcome = PipelineStatus.Failed;
                        lock (gate)
                        {
                            error = primary;
                        }
                    }
                }
                catch (OperationCanceledException exception) when (exception.CancellationToken == token && token.IsCancellationRequested)
                {
                    SelectCancellation(PipelinePhase.Execute, registration, exception);
                    stepFailure = primary;
                    outcome = PipelineStatus.Cancelled;
                }
                catch (Exception exception)
                {
                    SelectFailure(exception, PipelinePhase.Execute, registration, lifecycle: false);
                    stepFailure = primary;
                    outcome = PipelineStatus.Failed;
                }

                TimeSpan? elapsed = null;
                try
                {
                    elapsed = captured.Clock.GetElapsedTime(started);
                }
                catch (Exception exception) { SelectFailure(exception, PipelinePhase.Execute, null, lifecycle: false); }
                lock (gate)
                {
                    StepState state = states[index];
                    state.ExecutionStatus = outcome switch
                    {
                        PipelineStatus.Succeeded => PipelineStepExecutionStatus.Succeeded,
                        PipelineStatus.Cancelled => PipelineStepExecutionStatus.Cancelled,
                        _ => PipelineStepExecutionStatus.Failed
                    };
                    state.ExecutionError = stepFailure;
                    state.ExecutionDuration = elapsed;
                }
                if (elapsed is { } measured)
                {
                    Observe(() => observation.PhaseCompleted(registration, PipelinePhase.Execute, outcome, measured, stepFailure));
                }

                // A real failure wins before any subsequent cancellation checkpoint.
                if (primary is null && token.IsCancellationRequested)
                {
                    SelectCancellation(PipelinePhase.Execute, null);
                }
            }

            // The last successful method's checkpoint is the forward-success decision.
            // Later cancellation during disposal cannot initiate another unwind.
            if (primary is not null && entered != 0)
            {
                unwindStarted = true;
                currentPhase = PipelinePhase.Error;
                lock (gate)
                {
                    status = PipelineStatus.Unwinding;
                }
                await UnwindAsync(captured.Clock, instances, entered, primary, observation).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            SelectFailure(exception, currentPhase, null, lifecycle: currentPhase == PipelinePhase.Construction);
            if (entered != 0 && !unwindStarted)
            {
                unwindStarted = true;
                lock (gate)
                {
                    status = PipelineStatus.Unwinding;
                }
                try
                {
                    await UnwindAsync(captured.Clock, instances, entered, primary!, observation).ConfigureAwait(false);
                }
                catch (Exception secondary) { SelectFailure(secondary, PipelinePhase.Disposal, null, lifecycle: true); }
            }
        }
        finally
        {
            for (int index = instances.Length - 1; index >= 0; index--)
            {
                object? instance = instances[index];
                instances[index] = null;
                if (instance is null)
                {
                    continue;
                }

                try
                {
                    await DisposeObjectAsync(instance).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    SelectFailure(exception, PipelinePhase.Disposal, registrations[index], lifecycle: true);
                }
            }

            if (activation is not null)
            {
                try
                {
                    await activation.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception) { SelectFailure(exception, PipelinePhase.Disposal, null, lifecycle: true); }
                activation = null;
            }
        }

        TimeSpan? elapsedTotal = null;
        if (timingStarted)
        {
            try
            {
                elapsedTotal = captured.Clock.GetElapsedTime(origin);
            }
            catch (Exception exception) { SelectFailure(exception, PipelinePhase.Disposal, null, lifecycle: true); }
        }
        PipelineStatus terminal = cancelled ? PipelineStatus.Cancelled
            : primary is null ? PipelineStatus.Succeeded : PipelineStatus.Failed;
        int secondaryCount;
        lock (gate)
        {
            duration = elapsedTotal;
            status = terminal;
            secondaryCount = handlingFailures.Count + lifecycleFailures.Count(failure => !ReferenceEquals(failure, primary));
        }

        Observe(() => observation.Completed(terminal, elapsedTotal, primary, secondaryCount));
        lock (gate)
        {
            environment = null;
            if (cancelled)
            {
                completion.TrySetCanceled(token);
            }
            else if (outward is not null)
            {
                Exception original = outward.SourceException;
                completion.TrySetException(original is OperationCanceledException cancellation
                    ? new PipelineExecutionException(cancellation) : original);
            }
            else
            {
                completion.TrySetResult();
            }

            // Strict owner bookkeeping, without callbacks or faults. A zero-count
            // drain certifies that the accepted public task is already complete.
            captured.Lifetime.Exit();
        }
    }

    private async Task UnwindAsync(TimeProvider clock, object?[] instances, int entered,
        PipelineFailure primary, IPipelineObservation observation)
    {
        CleanupBudget? budget = null;
        try
        {
            try
            {
                budget = new CleanupBudget(clock, settings.ErrorHandlingTimeout, () =>
                {
                    bool firstExpiry;
                    lock (gate)
                    {
                        firstExpiry = !cleanupBudgetExpired;
                        cleanupBudgetExpired = true;
                    }
                    if (firstExpiry)
                    {
                        Observe(observation.CleanupBudgetExpired);
                    }
                });
            }
            catch (Exception exception)
            {
                AddLifecycle(Failure(exception, PipelinePhase.Disposal, null), observation);
            }
            CancellationToken cleanupToken = budget?.Token ?? CancellationToken.None;
            for (int index = entered - 1; index >= 0; index--)
            {
                PipelineRegistration registration = registrations[index];
                long? started = null;
                try
                {
                    started = clock.GetTimestamp();
                }
                catch (Exception exception) { AddLifecycle(Failure(exception, PipelinePhase.Disposal, null), observation); }
                lock (gate)
                {
                    states[index].ErrorStatus = PipelineStepErrorStatus.Running;
                }
                Observe(() => observation.PhaseStarted(registration, PipelinePhase.Error));
                PipelineFailure? failure = null;
                PipelineStatus outcome = PipelineStatus.Succeeded;
                try
                {
                    Task? task = ((IPipelineStep<TContext>)instances[index]!).Error(Context, primary, cleanupToken);
                    if (task is null)
                    {
                        throw new ContractException("A pipeline Error method returned a null task.");
                    }
                    await task.ConfigureAwait(false);
                }
                catch (OperationCanceledException exception) when (exception.CancellationToken == cleanupToken && cleanupToken.IsCancellationRequested)
                {
                    failure = new("Pipeline.CleanupCancelled", "The handler ended with cleanup-budget cancellation.",
                        PipelinePhase.Error, PipelineFailureKind.CleanupTimeout, registration.Position, registration.Name, exception);
                    outcome = PipelineStatus.Cancelled;
                }
                catch (Exception exception)
                {
                    failure = Failure(exception, PipelinePhase.Error, registration);
                    outcome = PipelineStatus.Failed;
                }

                TimeSpan? elapsed = null;
                if (started.HasValue)
                {
                    try
                    {
                        elapsed = clock.GetElapsedTime(started.Value);
                    }
                    catch (Exception exception) { AddLifecycle(Failure(exception, PipelinePhase.Disposal, null), observation); }
                }
                lock (gate)
                {
                    StepState state = states[index];
                    state.ErrorStatus = outcome switch
                    {
                        PipelineStatus.Succeeded => PipelineStepErrorStatus.Succeeded,
                        PipelineStatus.Cancelled => PipelineStepErrorStatus.Cancelled,
                        _ => PipelineStepErrorStatus.Failed
                    };
                    state.ErrorHandlingError = failure;
                    state.ErrorDuration = elapsed;
                    if (failure is not null)
                    {
                        handlingFailures.Add(failure);
                    }
                }
                if (elapsed is { } measured)
                {
                    Observe(() => observation.PhaseCompleted(registration, PipelinePhase.Error, outcome, measured, failure));
                }
            }
        }
        finally
        {
            if (budget is not null)
            {
                try
                {
                    await budget.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception) { AddLifecycle(Failure(exception, PipelinePhase.Disposal, null), observation); }
                if (budget.CancellationFailure is { } cancellationFailure)
                {
                    AddLifecycle(Failure(cancellationFailure, PipelinePhase.Disposal, null), observation);
                }
            }
        }
    }

    private void AddLifecycle(PipelineFailure failure, IPipelineObservation observation)
    {
        lock (gate)
        {
            lifecycleFailures.Add(failure);
        }
        Observe(() => observation.LifecycleFailure(failure));
    }

    private static PipelineFailure Failure(Exception exception, PipelinePhase phase, PipelineRegistration? registration)
    {
        return new(exception is ContractException ? "Pipeline.StepContract" : "Pipeline.Exception",
            "A pipeline phase failed. Inspect the retained original exception.", phase,
            exception is ContractException ? PipelineFailureKind.ContractViolation : PipelineFailureKind.Exception,
            registration?.Position, registration?.Name, exception);
    }

    private static async ValueTask DisposeObjectAsync(object instance)
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

    private static void Observe(Action callback)
    {
        try
        {
            callback();
        }
        catch (Exception) { /* All instrumentation is observational. */ }
    }

    private sealed class ContractException(string message) : InvalidOperationException(message)
    {
    }

    private sealed class StepState(PipelineRegistration registration)
    {
        internal PipelineStepExecutionStatus ExecutionStatus;
        internal PipelineStepErrorStatus ErrorStatus;
        internal PipelineFailure? ExecutionError;
        internal PipelineFailure? ErrorHandlingError;
        internal TimeSpan? ExecutionDuration;
        internal TimeSpan? ErrorDuration;

        internal PipelineStepReport Snapshot()
        {
            return new(registration.Position, registration.Name, ExecutionStatus, ErrorStatus,
                ExecutionError, ErrorHandlingError, ExecutionDuration, ErrorDuration);
        }
    }

    private sealed class CleanupBudget : IAsyncDisposable
    {
        private readonly CancellationTokenSource source = new();
        private readonly System.Threading.ITimer? timer;
        private Exception? cancellationFailure;

        internal CleanupBudget(TimeProvider clock, TimeSpan timeout, Action expired)
        {
            if (timeout != Timeout.InfiniteTimeSpan)
            {
                try
                {
                    timer = clock.CreateTimer(_ =>
                    {
                        expired();
                        try
                        {
                            source.Cancel();
                        }
                        catch (Exception exception) { Volatile.Write(ref cancellationFailure, exception); }
                    }, null, timeout, Timeout.InfiniteTimeSpan);
                }
                catch
                {
                    source.Dispose();
                    throw;
                }
            }
        }

        internal CancellationToken Token => source.Token;
        internal Exception? CancellationFailure => Volatile.Read(ref cancellationFailure);

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (timer is not null)
                {
                    await timer.DisposeAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                source.Dispose();
            }
        }
    }
}
