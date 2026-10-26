using CIS.Healthcheck;
using CIS.Healthcheck.Internal;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Healthcheck;

[TestClass]
public sealed class HealthExecutionStateMutationQualificationTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 10, 22, 14, 0, 0, TimeSpan.Zero);

    public static IEnumerable<object[]> InvalidDurations()
    {
        foreach (string field in new[] { "queue", "condition", "callback", "handler", "cleanup", "total" })
        {
            foreach (double value in new[] { -1d, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                yield return [field, value, false];
                if (field != "total") { yield return [field, value, true]; }
            }
        }
    }

    [TestMethod]
    [DynamicData(nameof(InvalidDurations))]
    public async Task StateBoundaryRejectsInvalidDurationScalarsWithoutPartiallyCommitting(string field, double value, bool outcome)
    {
        await using var scope = new Scope();
        BindingTarget target = scope.Bind();
        HealthSnapshot before = scope.Service.CaptureSnapshot();
        HealthExecutionFact fact = DurationFact(target.RegistrationId, field, value, outcome);

        Assert.ThrowsExactly<ArgumentException>(() => scope.Commit(target, fact, outcome));

        Assert.AreEqual(before.Revision, scope.Service.CaptureSnapshot().Revision);
        Assert.IsNull(scope.Snapshot().Outcomes!.Execution);
        Assert.AreEqual(0L, scope.Snapshot().Outcomes!.Successful);
        Assert.AreEqual(0L, scope.Snapshot().Outcomes!.Failed);
        Assert.AreEqual(HealthFactDisposition.Recorded, AcceptValidDurationFact(scope, target, field, 0, outcome));
    }

    [TestMethod]
    [DataRow("queue")]
    [DataRow("condition")]
    [DataRow("callback")]
    [DataRow("handler")]
    [DataRow("cleanup")]
    [DataRow("total")]
    public async Task StateBoundaryAcceptsZeroAndFiniteFractionalDurationsAsDetachedFacts(string field)
    {
        await using var scope = new Scope();
        BindingTarget target = scope.Bind();
        Assert.AreEqual(HealthFactDisposition.Recorded, AcceptValidDurationFact(scope, target, field, 0, false));
        Assert.AreEqual(0d, Duration(ObservedExecution(scope), field));
        Assert.AreEqual(HealthFactDisposition.Recorded, AcceptValidDurationFact(scope, target, field, 0.125, false));
        Assert.AreEqual(0.125, Duration(ObservedExecution(scope), field));
        Assert.AreEqual(0L, scope.Snapshot().Outcomes!.Successful);
        Assert.AreEqual(0L, scope.Snapshot().Outcomes!.Failed);
    }

    [TestMethod]
    [DataRow("foreign-registration")]
    [DataRow("undefined-kind")]
    [DataRow("zero-execution")]
    [DataRow("negative-execution")]
    [DataRow("missing-execution")]
    [DataRow("registration-with-execution")]
    [DataRow("retirement-with-execution")]
    [DataRow("undefined-source")]
    [DataRow("undefined-handler")]
    [DataRow("undefined-cleanup")]
    public async Task StateBoundaryIndependentlyChecksExecutionIdentityAndLifecycleEnums(string invalid)
    {
        await using var scope = new Scope();
        BindingTarget target = scope.Bind();
        HealthSnapshot before = scope.Service.CaptureSnapshot();
        var fact = new HealthExecutionFact
        {
            RegistrationId = invalid == "foreign-registration" ? Guid.NewGuid() : target.RegistrationId,
            Kind = invalid switch
            {
                "undefined-kind" => (HealthExecutionFactKind)999,
                "registration-with-execution" => HealthExecutionFactKind.Registered,
                "retirement-with-execution" => HealthExecutionFactKind.Retired,
                _ => HealthExecutionFactKind.CallbackStarted
            },
            ExecutionId = invalid switch { "zero-execution" => 0, "negative-execution" => -1, "missing-execution" => null, _ => 1 },
            OccurredAtUtc = Epoch,
            CallbackStartedAtUtc = Epoch,
            SourcePhase = invalid == "undefined-source" ? (HealthExecutionPhase)999 : null,
            ErrorHandlingStatus = invalid == "undefined-handler" ? (HealthExecutionStepStatus)999 : null,
            CleanupStatus = invalid == "undefined-cleanup" ? (HealthExecutionStepStatus)999 : null
        };

        Assert.ThrowsExactly<ArgumentException>(() => scope.State.UpdateLifecycle(target, fact));

        Assert.AreEqual(before.Revision, scope.Service.CaptureSnapshot().Revision);
        Assert.IsTrue(scope.State.IsActive(target));
        Assert.IsNull(scope.Snapshot().Outcomes!.Execution);
        Assert.AreEqual(HealthFactDisposition.Recorded, scope.State.UpdateLifecycle(target, new HealthExecutionFact
        { RegistrationId = target.RegistrationId, Kind = HealthExecutionFactKind.CallbackStarted, ExecutionId = 1, OccurredAtUtc = Epoch, CallbackStartedAtUtc = Epoch }));
    }

    [TestMethod]
    [DataRow("not-outcome")]
    [DataRow("missing-outcome")]
    [DataRow("undefined-outcome")]
    [DataRow("missing-source")]
    [DataRow("wrong-source")]
    [DataRow("missing-completion")]
    public async Task AtomicOutcomeCommitRequiresItsCompleteOutcomeIdentity(string invalid)
    {
        await using var scope = new Scope();
        BindingTarget target = scope.Bind();
        HealthSnapshot before = scope.Service.CaptureSnapshot();
        var fact = new HealthExecutionFact
        {
            RegistrationId = target.RegistrationId,
            ExecutionId = 1,
            OccurredAtUtc = Epoch,
            Kind = invalid == "not-outcome" ? HealthExecutionFactKind.CallbackStarted : HealthExecutionFactKind.Outcome,
            Outcome = invalid == "missing-outcome" ? null : invalid == "undefined-outcome" ? (HealthExecutionOutcome)999 : HealthExecutionOutcome.Succeeded,
            SourcePhase = invalid == "missing-source" ? null : invalid == "wrong-source" ? HealthExecutionPhase.Registered : HealthExecutionPhase.Callback,
            WorkCompletedAtUtc = invalid == "missing-completion" ? null : Epoch
        };

        Assert.ThrowsExactly<ArgumentException>(() => scope.State.ApplyOutcome(target, fact));

        Assert.AreEqual(before.Revision, scope.Service.CaptureSnapshot().Revision);
        Assert.AreEqual(0L, scope.Snapshot().Outcomes!.Successful);
        Assert.AreEqual(0L, scope.Snapshot().Outcomes!.Failed);
        Assert.IsNull(scope.Snapshot().Outcomes!.Execution);
    }

    [TestMethod]
    public async Task LifecycleNotificationCannotIntroduceAnOutcomeAndNullFactsPreserveState()
    {
        await using var scope = new Scope();
        BindingTarget target = scope.Bind();
        HealthSnapshot before = scope.Service.CaptureSnapshot();
        Assert.ThrowsExactly<ArgumentNullException>(() => scope.State.ApplyOutcome(target, null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => scope.State.UpdateLifecycle(target, null!));
        Assert.ThrowsExactly<ArgumentException>(() => scope.State.UpdateLifecycle(target, new HealthExecutionFact
        { RegistrationId = target.RegistrationId, Kind = HealthExecutionFactKind.CallbackStarted, ExecutionId = 1, OccurredAtUtc = Epoch, Outcome = HealthExecutionOutcome.Failed }));
        Assert.AreEqual(before.Revision, scope.Service.CaptureSnapshot().Revision);
    }

    [TestMethod]
    public async Task ZeroInitialDelayAndControlScalarsAreValidDetachedDisplayMetadata()
    {
        await using var scope = new Scope();
        var options = new HealthExecutionBindingOptions
        { RegistrationId = Guid.NewGuid(), InitialDelay = TimeSpan.Zero, Interval = TimeSpan.FromTicks(1), DisplayName = "line\n\t\0\U0001F680" };

        BindingTarget target = scope.State.Bind(BindingRequest.ForMonitor(scope.Monitor.MonitorId, options));
        scope.Targets.Add(target);

        Assert.AreEqual(options.RegistrationId, target.RegistrationId);
        Assert.IsTrue(scope.State.IsActive(target));
        Assert.AreEqual(MonitorState.Pending, scope.Snapshot().State);
    }

    [TestMethod]
    public async Task IntegrationDiagnosticBeforeExecutionDoesNotInventAStartedPhaseOrOutcome()
    {
        await using var scope = new Scope();
        BindingTarget target = scope.Bind();
        var diagnostic = AcceptedDiagnostic("AdapterFault");
        HealthSnapshot before = scope.Service.CaptureSnapshot();

        Assert.AreEqual(HealthFactDisposition.Recorded, scope.State.UpdateLifecycle(target, new HealthExecutionFact
        { RegistrationId = target.RegistrationId, Kind = HealthExecutionFactKind.IntegrationFault, ExecutionId = 17, OccurredAtUtc = Epoch, IntegrationError = diagnostic }));

        HealthMonitorSnapshot after = scope.Snapshot();
        Assert.IsTrue(scope.Service.CaptureSnapshot().Revision > before.Revision);
        Assert.IsNull(before.Monitors.Single().Outcomes!.Execution);
        Assert.AreEqual(HealthExecutionPhase.Registered, after.Outcomes!.Execution!.Phase);
        Assert.AreEqual(17L, after.Outcomes.Execution.ExecutionId);
        Assert.AreSame(diagnostic, after.Outcomes.Execution.IntegrationError);
        Assert.AreEqual(0L, after.Outcomes.Successful);
        Assert.AreEqual(0L, after.Outcomes.Failed);
        Assert.AreEqual(MonitorState.Pending, after.State);
        Assert.IsNull(after.LastObservationAtUtc);
    }

    [TestMethod]
    public async Task ReleasingAnOldTargetAgainCannotRetireAReplacementWithTheSameRegistrationIdentity()
    {
        await using var scope = new Scope();
        Guid identity = Guid.NewGuid();
        BindingTarget old = scope.Bind(identity);
        HealthSnapshot bound = scope.Service.CaptureSnapshot();
        Task released = scope.State.ReleaseBinding(old);
        Assert.IsTrue(released.IsCompletedSuccessfully);
        HealthSnapshot firstRelease = scope.Service.CaptureSnapshot();
        Assert.IsTrue(firstRelease.Revision > bound.Revision);
        Assert.IsFalse(scope.State.IsActive(old));
        BindingTarget replacement = scope.Bind(identity);
        HealthSnapshot beforeRepeat = scope.Service.CaptureSnapshot();

        Assert.AreSame(released, scope.State.ReleaseBinding(old));

        Assert.AreEqual(beforeRepeat.Revision, scope.Service.CaptureSnapshot().Revision);
        Assert.IsTrue(scope.State.IsActive(replacement));
        Assert.AreEqual(HealthFactDisposition.Retired, scope.State.ApplyOutcome(old, Outcome(identity, 1)));
        Assert.AreEqual(HealthFactDisposition.Recorded, scope.State.ApplyOutcome(replacement, Outcome(identity, 1)));
        Assert.AreEqual(1L, scope.Snapshot().Outcomes!.Successful);
    }

    [TestMethod]
    public async Task AStateIssuedTargetFromAnotherServiceCannotBeUsedForAnyLocalBindingOperation()
    {
        await using var local = new Scope();
        await using var foreign = new Scope();
        BindingTarget target = foreign.Bind();
        HealthSnapshot before = local.Service.CaptureSnapshot();
        Assert.ThrowsExactly<ArgumentException>(() => local.State.IsActive(target));
        Assert.ThrowsExactly<ArgumentException>(() => local.State.ApplyOutcome(target, Outcome(target.RegistrationId, 1)));
        Assert.ThrowsExactly<ArgumentException>(() => local.State.UpdateLifecycle(target, new HealthExecutionFact
        { RegistrationId = target.RegistrationId, Kind = HealthExecutionFactKind.Registered, OccurredAtUtc = Epoch }));
        Assert.ThrowsExactly<ArgumentException>(() => local.State.ReleaseBinding(target));
        Assert.AreEqual(before.Revision, local.Service.CaptureSnapshot().Revision);
        Assert.IsTrue(foreign.State.IsActive(target));
    }

    private static HealthExecutionFact Outcome(Guid registration, long execution) => new()
    {
        RegistrationId = registration,
        Kind = HealthExecutionFactKind.Outcome,
        ExecutionId = execution,
        OccurredAtUtc = Epoch,
        SourcePhase = HealthExecutionPhase.Callback,
        WorkCompletedAtUtc = Epoch,
        Outcome = HealthExecutionOutcome.Succeeded
    };

    private static HealthExecutionFact DurationFact(Guid registration, string field, double value, bool outcome) => new()
    {
        RegistrationId = registration,
        ExecutionId = 1,
        OccurredAtUtc = Epoch,
        Kind = outcome ? HealthExecutionFactKind.Outcome : HealthExecutionFactKind.Settled,
        Outcome = outcome ? HealthExecutionOutcome.Succeeded : null,
        SourcePhase = outcome ? HealthExecutionPhase.Callback : null,
        WorkCompletedAtUtc = Epoch,
        SettledAtUtc = outcome ? null : Epoch,
        QueueDelayMilliseconds = field == "queue" ? value : null,
        ConditionDurationMilliseconds = field == "condition" ? value : null,
        CallbackDurationMilliseconds = field == "callback" ? value : null,
        ErrorHandlingDurationMilliseconds = field == "handler" ? value : null,
        CleanupDurationMilliseconds = field == "cleanup" ? value : null,
        TotalDurationMilliseconds = field == "total" ? value : null,
        ErrorHandlingStatus = HealthExecutionStepStatus.Succeeded,
        CleanupStatus = HealthExecutionStepStatus.Succeeded
    };

    private static HealthFactDisposition AcceptValidDurationFact(Scope scope, BindingTarget target, string field, double value, bool outcome)
    {
        // This is only the admitted finite/nonnegative half of the receiving
        // boundary. Invalid facts still use the actual throwing Commit path.
        Assert.IsTrue(double.IsFinite(value) && value >= 0);
        HealthExecutionFact fact = DurationFact(target.RegistrationId, field, value, outcome);
        ArgumentException? unexpectedRejection = null;
        HealthFactDisposition? disposition = null;
        try { disposition = scope.Commit(target, fact, outcome); }
        catch (ArgumentException error)
        {
            // State issued this target; the matching identity, positive execution
            // ID, defined kind/statuses, UTC times and duration are all valid.
            // Argument-family rejection therefore contradicts input acceptance.
            unexpectedRejection = error;
        }
        Assert.IsNull(unexpectedRejection, "A state-issued target and finite nonnegative duration fact must be accepted.");
        Assert.IsTrue(disposition.HasValue);
        return disposition.GetValueOrDefault();
    }

    private static HealthExecutionSnapshot ObservedExecution(Scope scope)
    {
        HealthMonitorSnapshot monitor = scope.Snapshot();
        Assert.IsNotNull(monitor.Outcomes, "A consecutive-failure monitor retains its published outcome view.");
        Assert.IsNotNull(monitor.Outcomes.Execution, "An accepted execution-scoped duration fact must publish its execution snapshot.");
        return monitor.Outcomes.Execution;
    }

    private static double? Duration(HealthExecutionSnapshot execution, string field) => field switch
    {
        "queue" => execution.QueueDelayMilliseconds,
        "condition" => execution.ConditionDurationMilliseconds,
        "callback" => execution.CallbackDurationMilliseconds,
        "handler" => execution.ErrorHandlingDurationMilliseconds,
        "cleanup" => execution.CleanupDurationMilliseconds,
        "total" => execution.TotalDurationMilliseconds,
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    private sealed class Scope : IAsyncDisposable
    {
        internal Scope()
        {
            Service = new HealthcheckService(new HealthcheckOptions(), new HealthApplicationIdentity { Name = "execution state qualification", InstanceId = Guid.NewGuid() }, new ManualTimeProvider(Epoch));
            Monitor = Service.CreateConsecutiveFailures(new() { IsCritical = true, FailureThreshold = 1 });
        }
        internal HealthcheckService Service { get; }
        internal IConsecutiveFailureMonitor Monitor { get; }
        internal IHealthExecutionState State => ((IHealthStateAccess)Service).ExecutionState;
        internal List<BindingTarget> Targets { get; } = [];
        internal BindingTarget Bind(Guid? registration = null)
        {
            BindingTarget target = State.Bind(BindingRequest.ForMonitor(Monitor.MonitorId, new HealthExecutionBindingOptions { RegistrationId = registration ?? Guid.NewGuid() }));
            Targets.Add(target);
            return target;
        }
        internal HealthMonitorSnapshot Snapshot() => Service.CaptureSnapshot().Monitors.Single();
        internal HealthFactDisposition Commit(BindingTarget target, HealthExecutionFact fact, bool outcome) => outcome ? State.ApplyOutcome(target, fact) : State.UpdateLifecycle(target, fact);
        public async ValueTask DisposeAsync()
        {
            foreach (BindingTarget target in Targets) { await State.ReleaseBinding(target).WaitAsync(TimeSpan.FromSeconds(10)); }
            await Service.DisposeAsync();
            GC.KeepAlive(Monitor);
        }
    }
    private static HealthDiagnostic AcceptedDiagnostic(string? code = null, string? message = null, string? exceptionType = null)
    {
        HealthDiagnostic? diagnostic = null;
        ArgumentException? rejection = null;
        try { diagnostic = new HealthDiagnostic(code, message, exceptionType); }
        catch (ArgumentException error) { rejection = error; }
        Assert.IsNull(rejection, "Valid diagnostic constructor arguments must produce a detached diagnostic.");
        Assert.IsNotNull(diagnostic);
        return diagnostic;
    }

}
