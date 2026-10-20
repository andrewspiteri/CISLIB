using CIS.Text;

namespace CIS.Healthcheck.Internal;

internal sealed partial class HealthStateCoordinator
{
    public BindingTarget Bind(BindingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateBindingRequest(request);
        DateTimeOffset now = Now();
        var effects = new Effects();
        try
        {
            lock (gate)
            {
                EnsureAdmission();
                SweepDead(effects);
                if (bindings.ContainsKey(request.RegistrationId))
                {
                    throw new InvalidOperationException("The execution registration already has an active binding.");
                }

                IHealthMonitor monitor;
                HealthMonitorEntry entry;
                HealthBindingOwnership ownership;
                if (request.MonitorId is Guid monitorId)
                {
                    if (!active.TryGetValue(monitorId, out entry!) || entry.Withdrawn || !entry.Handle.TryGetTarget(out monitor!))
                    {
                        throw new ArgumentException("The monitor must be active and belong to this Healthcheck service.", nameof(request));
                    }
                    ValidateExecutionPolicy(entry.Policy, request.SuccessPublishesUsableData);
                    ownership = HealthBindingOwnership.ExternalMonitor;
                }
                else
                {
                    HealthMonitorOptions policy = (HealthMonitorOptions?)request.ConsecutiveFailurePolicy ?? request.AvailabilityPolicy!;
                    monitor = CreateLocked(policy, now);
                    entry = active[monitor.MonitorId];
                    ownership = HealthBindingOwnership.CreatedMonitor;
                }

                var binding = new ExecutionBinding(request.RegistrationId, entry, ownership);
                bindings.Add(binding.RegistrationId, binding);
                revision++;
                // Only the caller-owned target keeps the monitor alive. The registry stores detached binding state.
                var reporting = new ExecutionReportingHandle(this, binding, monitor);
                return new BindingTarget(binding.RegistrationId, entry.Id, ownership, reporting);
            }
        }
        finally
        {
            Dispatch(effects);
        }
    }

    private static void ValidateBindingRequest(BindingRequest request)
    {
        if (request.RegistrationId == Guid.Empty)
        {
            throw new ArgumentException("An execution registration identity is required.", nameof(request));
        }
        int selections = (request.MonitorId.HasValue ? 1 : 0)
            + (request.ConsecutiveFailurePolicy is not null ? 1 : 0)
            + (request.AvailabilityPolicy is not null ? 1 : 0);
        if (selections != 1 || request.MonitorId == Guid.Empty)
        {
            throw new ArgumentException("Select exactly one active monitor or supported policy.", nameof(request));
        }
        if (request.Interval is TimeSpan interval && interval <= TimeSpan.Zero)
        {
            throw new ArgumentException("An execution interval must be positive.", nameof(request));
        }
        if (request.InitialDelay is TimeSpan delay && delay < TimeSpan.Zero)
        {
            throw new ArgumentException("An execution initial delay cannot be negative.", nameof(request));
        }
        if (request.DisplayName is string displayName && !UnicodeScalarText.IsValid(displayName, 128, allowControls: true))
        {
            throw new ArgumentException("Use bounded, valid Unicode execution display metadata.", nameof(request));
        }
        if (request.ConsecutiveFailurePolicy is not null)
        {
            ValidatePolicy(request.ConsecutiveFailurePolicy);
        }
        if (request.AvailabilityPolicy is not null)
        {
            ValidatePolicy(request.AvailabilityPolicy);
            ValidateExecutionPolicy(HealthPolicyKind.FirstSuccessAvailability, request.SuccessPublishesUsableData);
        }
    }

    private static void ValidateExecutionPolicy(HealthPolicyKind policy, bool successPublishesUsableData)
    {
        if (policy is not (HealthPolicyKind.ConsecutiveFailures or HealthPolicyKind.FirstSuccessAvailability))
        {
            throw new ArgumentException("Execution bindings require a consecutive-failure or availability monitor.", "request");
        }
        if (policy == HealthPolicyKind.FirstSuccessAvailability && !successPublishesUsableData)
        {
            throw new ArgumentException("An availability binding requires an explicit usable-data publication declaration.", "request");
        }
    }

    public HealthFactDisposition ApplyOutcome(BindingTarget target, HealthExecutionFact fact)
    {
        try
        {
            ExecutionBinding binding = GetBinding(target);
            ValidateStateFact(binding, fact);
            if (fact.Kind != HealthExecutionFactKind.Outcome || !fact.Outcome.HasValue || !Enum.IsDefined(fact.Outcome.Value)
                || fact.SourcePhase is not (HealthExecutionPhase.Condition or HealthExecutionPhase.Callback)
                || fact.WorkCompletedAtUtc is null)
            {
                throw new ArgumentException("An outcome commit requires its outcome, source phase and work completion.", nameof(fact));
            }
            lock (gate)
            {
                if (!IsBindingActive(binding)) { return HealthFactDisposition.Retired; }
                if (!TryMergeExecution(binding, fact, out bool isCurrent)) { return HealthFactDisposition.Obsolete; }
                SetOutcome(binding.Entry, fact.Outcome.Value, fact.OriginalError, fact.OccurredAtUtc.ToUniversalTime());
                if (isCurrent) { binding.Entry.Execution = binding.Current; }
                return HealthFactDisposition.Recorded;
            }
        }
        finally { GC.KeepAlive(target); }
    }

    public HealthFactDisposition UpdateLifecycle(BindingTarget target, HealthExecutionFact fact)
    {
        ExecutionBinding binding = GetBinding(target);
        ValidateStateFact(binding, fact);
        if (fact.Kind == HealthExecutionFactKind.Outcome || fact.Outcome is not null)
        {
            throw new ArgumentException("Execution outcomes use the atomic outcome commit.", nameof(fact));
        }
        var effects = new Effects();
        try
        {
            lock (gate)
            {
                if (!IsBindingActive(binding)) { return HealthFactDisposition.Retired; }
                if (fact.Kind == HealthExecutionFactKind.Retired)
                {
                    binding.RegistrationDiagnostic = fact.IntegrationError ?? binding.RegistrationDiagnostic;
                    RetireBindingLocked(binding, effects);
                    return HealthFactDisposition.Recorded;
                }
                if (fact.ExecutionId is null)
                {
                    // Registration-lifetime diagnostics do not invent an execution record or health outcome.
                    binding.RegistrationDiagnostic = fact.IntegrationError ?? binding.RegistrationDiagnostic;
                }
                else
                {
                    if (!TryMergeExecution(binding, fact, out bool isCurrent)) { return HealthFactDisposition.Obsolete; }
                    if (isCurrent) { binding.Entry.Execution = binding.Current; }
                }
                revision++;
                return HealthFactDisposition.Recorded;
            }
        }
        finally
        {
            Dispatch(effects);
            GC.KeepAlive(target);
        }
    }

    // HC06 validates the complete fact matrix and deduplicates terminal notifications. These checks protect this
    // internal state boundary from malformed scalar/identity input without implementing a second binder.
    private static void ValidateStateFact(ExecutionBinding binding, HealthExecutionFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        if (fact.RegistrationId != binding.RegistrationId || !Enum.IsDefined(fact.Kind))
        {
            throw new ArgumentException("Use a defined fact for the target execution registration.", nameof(fact));
        }
        bool registrationFact = fact.Kind is HealthExecutionFactKind.Registered or HealthExecutionFactKind.Retired;
        if (registrationFact ? fact.ExecutionId.HasValue
            : fact.Kind != HealthExecutionFactKind.IntegrationFault && !fact.ExecutionId.HasValue)
        {
            throw new ArgumentException("The fact requires the corresponding registration or execution identity.", nameof(fact));
        }
        if (fact.ExecutionId is <= 0 || (fact.SourcePhase.HasValue && !Enum.IsDefined(fact.SourcePhase.Value))
            || (fact.ErrorHandlingStatus.HasValue && !Enum.IsDefined(fact.ErrorHandlingStatus.Value))
            || (fact.CleanupStatus.HasValue && !Enum.IsDefined(fact.CleanupStatus.Value)))
        {
            throw new ArgumentException("Execution identities and lifecycle statuses must be valid.", nameof(fact));
        }
        ValidateDuration(fact.QueueDelayMilliseconds);
        ValidateDuration(fact.ConditionDurationMilliseconds);
        ValidateDuration(fact.CallbackDurationMilliseconds);
        ValidateDuration(fact.ErrorHandlingDurationMilliseconds);
        ValidateDuration(fact.CleanupDurationMilliseconds);
        ValidateDuration(fact.TotalDurationMilliseconds);

        static void ValidateDuration(double? duration)
        {
            if (duration.HasValue && (!double.IsFinite(duration.Value) || duration.Value < 0))
            {
                throw new ArgumentException("Execution durations must be finite nonnegative milliseconds.", nameof(fact));
            }
        }
    }

    private ExecutionBinding GetBinding(BindingTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.ReportingHandle is not ExecutionReportingHandle reporting || !ReferenceEquals(reporting.State, this)
            || target.RegistrationId != reporting.Binding.RegistrationId || target.MonitorId != reporting.Binding.Entry.Id
            || target.Ownership != reporting.Binding.Ownership)
        {
            throw new ArgumentException("Use the reporting target issued by this Healthcheck service.", nameof(target));
        }
        return reporting.Binding;
    }

    private bool IsBindingActive(ExecutionBinding binding) => !binding.Retired && !binding.Entry.Withdrawn
        && active.TryGetValue(binding.Entry.Id, out var entry) && ReferenceEquals(entry, binding.Entry)
        && bindings.TryGetValue(binding.RegistrationId, out var current) && ReferenceEquals(current, binding);

    public Task ReleaseBinding(BindingTarget target)
    {
        ExecutionBinding binding = GetBinding(target);
        var effects = new Effects();
        Task settled;
        lock (gate)
        {
            if (!binding.Retired) { RetireBindingLocked(binding, effects); }
            settled = binding.ReleaseTask!;
        }
        Dispatch(effects);
        return settled;
    }

    private void RetireBindingLocked(ExecutionBinding binding, Effects effects)
    {
        if (binding.Retired) { return; }
        binding.Retired = true;
        binding.ReleaseTask = binding.Ownership == HealthBindingOwnership.CreatedMonitor ? binding.Entry.Disposal.Task : Task.CompletedTask;
        if (bindings.TryGetValue(binding.RegistrationId, out var current) && ReferenceEquals(current, binding))
        {
            bindings.Remove(binding.RegistrationId);
        }
        if (binding.Ownership == HealthBindingOwnership.CreatedMonitor) { WithdrawLocked(binding.Entry, effects); }
        revision++;
    }

    // Called by monitor withdrawal; all associations retire, including multiple external bindings to one monitor.
    private void RetireBindingsLocked(HealthMonitorEntry entry)
    {
        foreach (var binding in bindings.Values.Where(binding => ReferenceEquals(binding.Entry, entry)).ToArray())
        {
            binding.Retired = true;
            binding.ReleaseTask = binding.Ownership == HealthBindingOwnership.CreatedMonitor ? entry.Disposal.Task : Task.CompletedTask;
            bindings.Remove(binding.RegistrationId);
        }
    }

    private static bool TryMergeExecution(ExecutionBinding binding, HealthExecutionFact fact, out bool isCurrent)
    {
        long executionId = fact.ExecutionId!.Value;
        if (binding.Current is null || executionId > binding.Current.ExecutionId)
        {
            HealthExecutionSnapshot next = MergeExecution(null, fact);
            binding.Previous = binding.Current;
            binding.Current = next;
            isCurrent = true;
            return true;
        }
        if (executionId == binding.Current.ExecutionId)
        {
            binding.Current = MergeExecution(binding.Current, fact);
            isCurrent = true;
            return true;
        }
        isCurrent = false;
        if (binding.Previous?.ExecutionId != executionId) { return false; }
        binding.Previous = MergeExecution(binding.Previous, fact);
        return true;
    }

    private static HealthExecutionSnapshot MergeExecution(HealthExecutionSnapshot? previous, HealthExecutionFact fact)
    {
        HealthExecutionPhase phase = fact.Kind switch
        {
            HealthExecutionFactKind.Eligible => HealthExecutionPhase.Eligible,
            HealthExecutionFactKind.Admitted => HealthExecutionPhase.Admitted,
            HealthExecutionFactKind.ConditionStarted => HealthExecutionPhase.Condition,
            HealthExecutionFactKind.CallbackStarted => HealthExecutionPhase.Callback,
            HealthExecutionFactKind.Outcome => fact.SourcePhase!.Value,
            HealthExecutionFactKind.ErrorHandlingStarted or HealthExecutionFactKind.ErrorHandlingCompleted => HealthExecutionPhase.ErrorHandling,
            HealthExecutionFactKind.CleanupCompleted => HealthExecutionPhase.Cleanup,
            HealthExecutionFactKind.Settled => HealthExecutionPhase.Settled,
            _ => fact.SourcePhase ?? previous?.Phase ?? HealthExecutionPhase.Registered
        };
        if (previous is not null && (int)previous.Phase > (int)phase) { phase = previous.Phase; }
        return new HealthExecutionSnapshot(new HealthExecutionSnapshotData
        {
            RegistrationId = fact.RegistrationId,
            ExecutionId = fact.ExecutionId!.Value,
            Phase = phase,
            EligibleAtUtc = fact.EligibleAtUtc ?? previous?.EligibleAtUtc,
            AdmittedAtUtc = fact.AdmittedAtUtc ?? previous?.AdmittedAtUtc,
            ConditionStartedAtUtc = fact.ConditionStartedAtUtc ?? previous?.ConditionStartedAtUtc,
            CallbackStartedAtUtc = fact.CallbackStartedAtUtc ?? previous?.CallbackStartedAtUtc,
            WorkCompletedAtUtc = fact.WorkCompletedAtUtc ?? previous?.WorkCompletedAtUtc,
            ErrorHandlingStartedAtUtc = fact.ErrorHandlingStartedAtUtc ?? previous?.ErrorHandlingStartedAtUtc,
            ErrorHandlingCompletedAtUtc = fact.ErrorHandlingCompletedAtUtc ?? previous?.ErrorHandlingCompletedAtUtc,
            CleanupCompletedAtUtc = fact.CleanupCompletedAtUtc ?? previous?.CleanupCompletedAtUtc,
            SettledAtUtc = fact.SettledAtUtc ?? previous?.SettledAtUtc,
            QueueDelayMilliseconds = fact.QueueDelayMilliseconds ?? previous?.QueueDelayMilliseconds,
            ConditionDurationMilliseconds = fact.ConditionDurationMilliseconds ?? previous?.ConditionDurationMilliseconds,
            CallbackDurationMilliseconds = fact.CallbackDurationMilliseconds ?? previous?.CallbackDurationMilliseconds,
            ErrorHandlingDurationMilliseconds = fact.ErrorHandlingDurationMilliseconds ?? previous?.ErrorHandlingDurationMilliseconds,
            CleanupDurationMilliseconds = fact.CleanupDurationMilliseconds ?? previous?.CleanupDurationMilliseconds,
            TotalDurationMilliseconds = fact.TotalDurationMilliseconds ?? previous?.TotalDurationMilliseconds,
            ErrorHandlingStatus = fact.ErrorHandlingStatus ?? previous?.ErrorHandlingStatus ?? HealthExecutionStepStatus.NotStarted,
            CleanupStatus = fact.CleanupStatus ?? previous?.CleanupStatus ?? HealthExecutionStepStatus.NotStarted,
            OriginalError = fact.OriginalError ?? previous?.OriginalError,
            ErrorHandlingError = fact.ErrorHandlingError ?? previous?.ErrorHandlingError,
            CleanupError = fact.CleanupError ?? previous?.CleanupError,
            IntegrationError = fact.IntegrationError ?? previous?.IntegrationError
        });
    }

    private sealed class ExecutionBinding(Guid registrationId, HealthMonitorEntry entry, HealthBindingOwnership ownership)
    {
        internal Guid RegistrationId { get; } = registrationId;
        internal HealthMonitorEntry Entry { get; } = entry;
        internal HealthBindingOwnership Ownership { get; } = ownership;
        internal bool Retired;
        internal Task? ReleaseTask;
        internal HealthExecutionSnapshot? Current;
        internal HealthExecutionSnapshot? Previous;
        internal HealthDiagnostic? RegistrationDiagnostic;
    }

    private sealed class ExecutionReportingHandle(HealthStateCoordinator state, ExecutionBinding binding, IHealthMonitor monitor)
        : IHealthExecutionReportingHandle
    {
        internal HealthStateCoordinator State { get; } = state;
        internal ExecutionBinding Binding { get; } = binding;
        // A target is caller-owned. Keeping it alive must keep its binding-created monitor alive as well.
        internal IHealthMonitor Monitor { get; } = monitor;
    }
}
