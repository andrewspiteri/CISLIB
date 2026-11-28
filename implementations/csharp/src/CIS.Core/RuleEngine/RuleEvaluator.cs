using System.Data.Common;
using CIS.Audit;
using CIS.Security;

namespace CIS.RuleEngine;

internal sealed class RuleEvaluator(Func<RuleEvaluationRequest, CancellationToken, Task<RuleEngineSelection>> select,
    IAuditCapture? capture, IAuditWriter? audit, AuditLimits? auditLimits, TimeProvider clock,
    TimeSpan? finalizationDeadline = null) : IRuleEvaluator
{
    private static readonly RuleEvaluationError Cancellation = new("Cancelled", "The rule evaluation was cancelled.");

    public async Task<RuleEvaluationResult> EvaluateAsync(RuleEvaluationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Guid executionId = Guid.NewGuid();
        RuleEngineSelection selection = await select(request, cancellationToken).ConfigureAwait(false);
        if (selection is not { Prepared: { } prepared, Target: { } target })
        {
            RuleEvaluationError error = selection.Error
                ?? new("RulesetUnavailable", "The application has no admitted rule definition.");
            return new(executionId, request.RulesetId, null,
                error.Code == Cancellation.Code ? RuleExecutionStatus.Cancelled : RuleExecutionStatus.Failed, error: error);
        }
        string version = prepared.Definition.Version;
        if (cancellationToken.IsCancellationRequested)
        {
            return new(executionId, request.RulesetId, version, RuleExecutionStatus.Cancelled, error: Cancellation);
        }
        if (capture is null || audit is null || auditLimits is null)
        {
            return AuditUnavailable(executionId, request.RulesetId, version, AuditSubmissionOutcome.NotSubmitted);
        }

        var facts = new RuleExecutionAuditFacts(executionId, target, LocalApplicationIdentity.Capture(), request.RulesetId,
            prepared.Definition.SchemaRevision, version, request.CorrelationId, clock.GetUtcNow());
        CapturedAuditRecord? start = Capture(RuleExecutionAudit.Start, new(facts, request.Inputs),
            RuleExecutionAudit.CaptureOptions(facts, AuditOutcome.Accepted, facts.StartedAtUtc), cancellationToken);
        AuditSubmissionOutcome startOutcome = start is not null && RuleExecutionAudit.CompletionFits(start, facts, prepared, auditLimits)
            ? await SubmitAsync(start, cancellationToken).ConfigureAwait(false) : AuditSubmissionOutcome.NotSubmitted;
        if (startOutcome != AuditSubmissionOutcome.OutboxCommitted)
        {
            return AuditUnavailable(executionId, request.RulesetId, version, startOutcome,
                prior: cancellationToken.IsCancellationRequested ? Cancellation : null);
        }

        RuleEvaluationTrace trace = RuleMatcher.Evaluate(prepared, request.Inputs, cancellationToken);
        DateTimeOffset finished = clock.GetUtcNow();
        RuleEvaluationError? prior = null;
        if (cancellationToken.IsCancellationRequested && trace.Status != RuleExecutionStatus.Cancelled)
        {
            prior = trace.Error;
            trace = trace with
            {
                Status = RuleExecutionStatus.Cancelled,
                Matches = [],
                SelectedRowIndices = [],
                Error = Cancellation
            };
        }
        // Status is chosen above. Caller cancellation no longer changes this event
        // or its independently bounded submission.
        AuditOutcome observed = trace.Status switch
        {
            RuleExecutionStatus.Completed => AuditOutcome.Succeeded,
            RuleExecutionStatus.Cancelled => AuditOutcome.Canceled,
            _ => AuditOutcome.Failed
        };
        using var finalization = new CancellationTokenSource(finalizationDeadline ?? TimeSpan.FromSeconds(5), clock);
        CapturedAuditRecord? completion = Capture(RuleExecutionAudit.Complete, new(facts, trace, finished, prior),
            RuleExecutionAudit.CaptureOptions(facts, observed, finished, start!.AuditId), finalization.Token);
        AuditSubmissionOutcome completed = completion is null ? AuditSubmissionOutcome.NotSubmitted
            : await SubmitAsync(completion, finalization.Token).ConfigureAwait(false);
        return completed == AuditSubmissionOutcome.OutboxCommitted
            ? new(executionId, request.RulesetId, version, trace.Status, trace.Matches, trace.Rows, trace.Diagnostics, trace.Error)
            {
                PriorError = prior
            }
            : AuditUnavailable(executionId, request.RulesetId, version, completed, trace, trace.Error ?? prior);
    }

    private CapturedAuditRecord? Capture<T>(AuditEventDefinition<T> definition, T details, AuditCaptureOptions options,
        CancellationToken token)
    {
        try
        {
            CapturedAuditRecord result = capture!.Capture(definition, details, options, token);
            return RuleExecutionAudit.Fits(result, auditLimits!) ? result : null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception error) when (error is AuditCaptureException or ArgumentException)
        {
            return null;
        }
    }

    private async Task<AuditSubmissionOutcome> SubmitAsync(CapturedAuditRecord record, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return AuditSubmissionOutcome.NotSubmitted;
        }
        try
        {
            AuditSubmissionResult settled = await audit!.SubmitAsync(record, token).ConfigureAwait(false);
            return settled.AuditId == record.AuditId ? settled.Outcome : AuditSubmissionOutcome.Unknown;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return AuditSubmissionOutcome.Unknown;
        }
        catch (DbException)
        {
            return AuditSubmissionOutcome.Unknown;
        }
    }

    private static RuleEvaluationResult AuditUnavailable(Guid executionId, string rulesetId, string version,
        AuditSubmissionOutcome outcome, RuleEvaluationTrace? trace = null, RuleEvaluationError? prior = null)
        => new(executionId, rulesetId, version, RuleExecutionStatus.Failed, trace: trace?.Rows, diagnostics: trace?.Diagnostics,
            error: new("AuditUnavailable", "Durable audit acceptance could not be confirmed."))
        {
            PriorError = prior,
            AuditFailure = outcome
        };
}
