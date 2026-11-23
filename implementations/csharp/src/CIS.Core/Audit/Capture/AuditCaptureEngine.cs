using CIS.Messaging;
using CIS.Metrics;
using CIS.Security;

namespace CIS.Audit;

// Singleton-safe: trusted hook observations are passed as detached immutable values, never scope accessors.
internal sealed class AuditCaptureEngine : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly LocalApplicationIdentity identity;
    private readonly AuditLimits limits;
    private readonly TimeProvider clock;
    private readonly MessagingResourceBudget budget;
    private readonly AuditInstrumentation? instrumentation;
    private bool closing;
    private int activeCaptures;

    internal AuditCaptureEngine(LocalApplicationIdentity identity, AuditLimits limits, TimeProvider clock, MessagingResourceBudget budget,
        AuditInstrumentation? instrumentation = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(budget);

        this.identity = identity;
        this.limits = limits;
        this.clock = clock;
        this.budget = budget;
        this.instrumentation = instrumentation;
    }

    internal CapturedAuditRecord Capture<T>(AuditEventDefinition<T> definition, T details,
        AuditCaptureOptions? options, CallerContext? caller, bool localSystem = false,
        CancellationToken cancellationToken = default)
    {
        Guid id = Guid.NewGuid();
        ArgumentNullException.ThrowIfNull(definition);
        AuditCaptureObservation? observation = instrumentation?.Capture(definition.Category);
        IDisposable? operation = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            operation = Enter(id);
            AuditCaptureOptions selected = options ?? new();
            ValidateRequiredFields(definition, selected);

            // Nonblocking native admission covers ordinary encoding. Larger encoder hints acquire
            // any additional actual capacity before allocation, inside the same existing budget.
            if (!budget.TryAcquireBytes(checked(4 * limits.MaximumPayloadBytes), out IDisposable? admission))
            {
                throw new AuditCaptureException(id, AuditCaptureFailure.Overloaded);
            }
            using (admission)
            {
                cancellationToken.ThrowIfCancellationRequested();
                definition.DetailsCodec.Validate(details);
                DateTimeOffset recordedAtUtc = clock.GetUtcNow().ToUniversalTime();
                var producer = new AuditProducer(identity.Application, identity.Environment, new(identity.InstanceId));
                AuditActor actor = localSystem ? new(AuditActorKind.System, application: identity.Application, environment: identity.Environment)
                    : FromCaller(caller);
                AuditRecord record = AuditRecordCodec.EncodeCapture(id, definition, details, selected, producer, actor, recordedAtUtc, limits, budget);
                cancellationToken.ThrowIfCancellationRequested();
                observation?.Complete(AuditCaptureResult.Captured);
                return new(record);
            }
        }
        catch (Exception error) when (MetricsRuntime.IsOrdinary(error))
        {
            AuditCaptureResult result = error is AuditCaptureException or ArgumentException
                || error is OperationCanceledException && cancellationToken.IsCancellationRequested
                ? AuditCaptureResult.Rejected : AuditCaptureResult.Faulted;
            observation?.Complete(result);
            throw;
        }
        finally
        {
            operation?.Dispose();
        }
    }

    internal void BeginClose()
    {
        lock (gate)
        {
            closing = true;
            Settle();
        }
    }

    internal Task DrainAsync()
    {
        BeginClose();
        return drained.Task;
    }

    public ValueTask DisposeAsync() => new(DrainAsync());

    private IDisposable Enter(Guid id)
    {
        lock (gate)
        {
            if (closing)
            {
                throw new AuditCaptureException(id, AuditCaptureFailure.Unavailable);
            }
            activeCaptures++;
            return new CaptureLease(this);
        }
    }

    private void Exit()
    {
        lock (gate)
        {
            activeCaptures--;
            Settle();
        }
    }

    private void Settle()
    {
        if (closing && activeCaptures == 0)
        {
            drained.TrySetResult();
        }
    }

    private static AuditActor FromCaller(CallerContext? caller)
    {
        if (caller is null)
        {
            return new(AuditActorKind.Unknown);
        }
        var instance = new AuditInstance(caller.Instance.ReportedIdentifier, caller.Instance.VerifiedIdentifier);
        AuditSubject? human = caller.HumanSubject is { } supplied ? new(supplied.Issuer, supplied.Identifier) : null;
        return new(caller.ActorKind == SecurityActorKind.User ? AuditActorKind.User : AuditActorKind.Application,
            new(caller.Subject.Issuer, caller.Subject.Identifier), caller.Application, caller.Environment,
            caller.Profile, caller.AuthenticationMethod, caller.PolicyVersion, instance, human, caller.VerifiedDelegation);
    }

    private static void ValidateRequiredFields<T>(AuditEventDefinition<T> definition, AuditCaptureOptions options)
    {
        if (definition.RequireTarget && options.Target is null || definition.RequireReason && options.Reason is null
            || definition.RequireOperationReference && options.OperationReference is null)
        {
            throw new ArgumentException("The selected audit event requires missing common facts.", nameof(options));
        }
    }

    private sealed class CaptureLease(AuditCaptureEngine owner) : IDisposable
    {
        private AuditCaptureEngine? owner = owner;

        public void Dispose() => Interlocked.Exchange(ref owner, null)?.Exit();
    }
}
