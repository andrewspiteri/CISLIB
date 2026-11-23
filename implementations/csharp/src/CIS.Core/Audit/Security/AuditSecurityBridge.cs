using CIS.Messaging;
using CIS.Security;

namespace CIS.Audit;

/// <summary>An opt-in awaited adapter from immutable trusted Security observations to standalone Audit submission.</summary>
/// <remarks>There is no queue, retry owner or captured scoped accessor. Submission does not undo a prior business effect.</remarks>
public sealed class AuditSecurityBridge : ISecurityAuditSink
{
    private readonly AuditCaptureEngine capture;
    private readonly IAuditWriter writer;
    private readonly AuditSecurityBridgeOptions options;
    private readonly TimeProvider clock;

    internal AuditSecurityBridge(AuditCaptureEngine capture, IAuditWriter writer, AuditSecurityBridgeOptions options,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        this.capture = capture;
        this.writer = writer;
        this.options = options;
        this.clock = clock;
    }

    /// <inheritdoc />
    public async ValueTask AuthorizationCompletedAsync(SecurityAuthorizationAudit audit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(audit);
        if (options.IngressBindings.Any(binding => binding.Matches(audit.Target, audit.Capabilities)))
        {
            return;
        }

        var facts = new AuditCaptureOptions(audit.Decision.IsAllowed ? AuditOutcome.Allowed : AuditOutcome.Denied,
            target: Target(audit.Target), reason: new(audit.Decision.Code, audit.Decision.Failure?.Message),
            correlationId: audit.CorrelationId);
        await SubmitAsync(SecurityAuditEvents.AuthorizationDecision, audit, facts, audit.Caller, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask OperationCompletedAsync(SecurityOperationAudit audit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(audit);
        AuditOutcome outcome = audit.Outcome switch
        {
            SecurityOperationOutcome.Completed => AuditOutcome.Succeeded,
            SecurityOperationOutcome.Failed => AuditOutcome.Failed,
            SecurityOperationOutcome.Canceled => AuditOutcome.Canceled,
            _ => throw new ArgumentOutOfRangeException(nameof(audit))
        };
        var facts = new AuditCaptureOptions(outcome, target: Target(audit.Target),
            reason: audit.OutcomeCode is { } code ? new(code) : null, correlationId: audit.CorrelationId);
        await SubmitAsync(SecurityAuditEvents.OperationReported, audit, facts, audit.Caller, cancellationToken).ConfigureAwait(false);
    }

    private async Task SubmitAsync<T>(AuditEventDefinition<T> definition, T details, AuditCaptureOptions facts,
        CallerContext? caller, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(options.SubmissionTimeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        CapturedAuditRecord record;
        try
        {
            record = capture.Capture(definition, details, facts, caller, cancellationToken: linked.Token);
        }
        catch (AuditCaptureException refusal)
        {
            string code = refusal.Failure == AuditCaptureFailure.Overloaded ? "audit.capture_overloaded" : "audit.capture_unavailable";
            throw new AuditSecurityBridgeException(new(refusal.AuditId, new(MessageSubmissionOutcome.NotSubmitted,
                error: new(code, "Audit capture could not admit the observation."))));
        }

        Task<AuditSubmissionResult> pending = writer.SubmitAsync(record, linked.Token)
            ?? throw new InvalidOperationException("The Audit writer returned no submission operation.");
        AuditSubmissionResult result = await pending.ConfigureAwait(false)
            ?? throw new InvalidOperationException("The Audit writer returned no submission result.");
        if (result.AuditId != record.AuditId)
        {
            throw new InvalidOperationException("The Audit writer returned inconsistent standalone submission facts.");
        }
        if (result.Outcome != AuditSubmissionOutcome.OutboxCommitted)
        {
            throw new AuditSecurityBridgeException(result);
        }
        // Actual committed settlement remains committed even if the deadline or caller just canceled.
    }

    private static AuditTarget? Target(CapabilityTarget? target) => target is null ? null
        : new(target.Application, target.Environment, target.ResourceType, target.ResourceIdentifier);
}
