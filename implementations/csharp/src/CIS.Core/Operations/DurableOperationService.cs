using System.Data.Common;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using CIS.Operations.Hosting;
using CIS.Security;
using CIS.Transactions;

namespace CIS.Operations;

internal sealed class DurableOperationService(IServiceProvider services, OperationDefinition[] definitions,
    LocalApplicationIdentity application, ICapabilityAuthorizer authorizer, ICallerContextAccessor callers,
    IOperationJournal journal, ILocalTransactionFactory transactions) : IDurableOperationService
{
    public async Task<OperationExecution<TResult>> ExecuteAsync<TRequest, TResult>(ILocalTransactionContext context,
        OperationRequest<TRequest> request,
        Func<ILocalTransactionContext, TRequest, CancellationToken, Task<OperationDecision<TResult>>> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(handler);
        OperationDefinition<TRequest, TResult> definition = Definition(request.OperationName) as OperationDefinition<TRequest, TResult>
            ?? throw new ArgumentException("The request and result types must match the registered operation definition.", nameof(request));
        ValidateContext(context, definition);
        cancellationToken.ThrowIfCancellationRequested();
        OperationScope scope = await ResolveScopeAsync(definition, request.RequestedScope, cancellationToken).ConfigureAwait(false);
        OperationAdmission.CheckBinding(context, Identity(definition, scope, request.Key),
            new(application.Application, application.Environment, scope.ResourceType, scope.ResourceIdentifier));
        if (!await AuthorizeAsync(definition.ExecuteCapabilities, scope, cancellationToken).ConfigureAwait(false))
        {
            return new(OperationExecutionDisposition.Denied);
        }

        cancellationToken.ThrowIfCancellationRequested();
        NormalizedOperationInput<TRequest> input = definition.Normalizer.Normalize(request.Input, scope)
            ?? throw new InvalidOperationException("The operation normalizer returned no owned input.");
        OperationFingerprint[] fingerprints = Fingerprints(definition, input.Fingerprints);
        var identity = Identity(definition, scope, request.Key);
        cancellationToken.ThrowIfCancellationRequested();
        OperationAdmission.Enter(context);
        try
        {
            if (context.IsRollbackOnly)
            {
                return new(OperationExecutionDisposition.RollbackOnly);
            }

            OperationFingerprint current = fingerprints.Single(item => item.Contract == definition.FingerprintContract);
            OperationClaim claim = await journal.TryClaimAsync(context, identity, current, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The operation journal returned no claim outcome.");
            cancellationToken.ThrowIfCancellationRequested();
            switch (claim.Disposition)
            {
                case OperationClaimDisposition.Existing:
                    return Replay(definition, identity, fingerprints, claim.Existing!);
                case OperationClaimDisposition.MissingAfterConflict:
                    return new(OperationExecutionDisposition.Contended);
                case OperationClaimDisposition.Contended:
                    Poison(context, "operations.claim_contended");
                    return new(OperationExecutionDisposition.Contended);
                case OperationClaimDisposition.RetryRequired:
                    Poison(context, "operations.claim_retry_required");
                    return new(OperationExecutionDisposition.RetryRequired);
                case OperationClaimDisposition.Acquired:
                    break;
                default:
                    throw new InvalidOperationException("The operation journal returned an unknown claim outcome.");
            }

            if (context.IsRollbackOnly)
            {
                return new(OperationExecutionDisposition.RollbackOnly);
            }

            // No native callback or module lock surrounds application orchestration.
            OperationDecision<TResult> decision = await handler(context, input.Value, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The operation handler returned no completed business decision.");
            cancellationToken.ThrowIfCancellationRequested();
            if (context.IsRollbackOnly)
            {
                return new(OperationExecutionDisposition.RollbackOnly);
            }

            OperationStoredResult stored = OperationResultReader.Encode(definition, definition.Codec, decision);
            if (!OperationResultReader.TryDecode(definition, definition.Codec, stored, out OperationDecision<TResult>? persisted))
            {
                throw new InvalidOperationException("The operation codec cannot decode its current encoded result.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (context.IsRollbackOnly)
            {
                return new(OperationExecutionDisposition.RollbackOnly);
            }

            _ = await journal.WriteCompletedAsync(context, identity, current, stored, definition.Retention, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The operation journal returned no finalization facts.");
            cancellationToken.ThrowIfCancellationRequested();
            return context.IsRollbackOnly
                ? new(OperationExecutionDisposition.RollbackOnly)
                : new(OperationExecutionDisposition.Prepared, persisted);
        }
        catch
        {
            Poison(context, "operations.attempt_failed");
            throw;
        }
    }

    public async Task<OperationLookup<TResult>> GetResultAsync<TResult>(OperationLookupRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        OperationDefinition definition = Definition(request.OperationName);
        if (definition is not IOperationResultDefinition<TResult> resultDefinition)
        {
            throw new ArgumentException("The result type must match the registered operation definition.", nameof(request));
        }

        cancellationToken.ThrowIfCancellationRequested();
        OperationScope scope = await ResolveScopeAsync(definition, request.RequestedScope, cancellationToken).ConfigureAwait(false);
        if (!await AuthorizeAsync(definition.LookupCapabilities, scope, cancellationToken).ConfigureAwait(false))
        {
            return new(OperationLookupDisposition.Denied);
        }

        var identity = Identity(definition, scope, request.Key);
        cancellationToken.ThrowIfCancellationRequested();
        ILocalTransaction owner;
        try
        {
            owner = await transactions.BeginAsync(definition.DatabaseName,
                new LocalTransactionOptions { Isolation = TransactionIsolation.ReadCommitted }, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The transaction factory returned no read owner.");
        }
        catch (DbException error) when (error.IsTransient && !cancellationToken.IsCancellationRequested)
        {
            return new(OperationLookupDisposition.Unavailable);
        }

        OperationLookup<TResult>? observed = null;
        ExceptionDispatchInfo? primary = null;
        try
        {
            ValidateContext(owner.Context, definition);
            cancellationToken.ThrowIfCancellationRequested();
            OperationJournalRecord? row;
            try
            {
                row = await journal.ReadAsync(owner.Context, identity, cancellationToken).ConfigureAwait(false);
            }
            catch (DbException error) when (error.IsTransient && !cancellationToken.IsCancellationRequested)
            {
                observed = new(OperationLookupDisposition.Unavailable);
                row = null;
            }

            cancellationToken.ThrowIfCancellationRequested();
            observed ??= Lookup(definition, resultDefinition.Codec, identity, row);
        }
        catch (Exception error)
        {
            primary = ExceptionDispatchInfo.Capture(error);
        }

        ExceptionDispatchInfo? cleanup = await SettleReadAsync(owner).ConfigureAwait(false);
        primary?.Throw();
        if (observed?.Disposition != OperationLookupDisposition.Found)
        {
            cleanup?.Throw();
        }

        return new(observed!.Disposition, observed.Decision, owner.Completion);
    }

    private OperationDefinition Definition(string name) => definitions.SingleOrDefault(item => item.Name == name)
        ?? throw new ArgumentException("The operation alias is not registered.", nameof(name));

    private async ValueTask<OperationScope> ResolveScopeAsync(OperationDefinition definition, string requestedScope,
        CancellationToken cancellationToken)
    {
        IOperationScopeResolver resolver = definition.ScopeResolver(services)
            ?? throw new InvalidOperationException("The operation scope resolver factory returned no resolver.");
        OperationScope scope = await resolver.ResolveAsync(definition.Name, requestedScope, callers.Current, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The operation resolver returned no trusted scope.");
        cancellationToken.ThrowIfCancellationRequested();
        return scope;
    }

    private async ValueTask<bool> AuthorizeAsync(IReadOnlyList<string> capabilities, OperationScope scope,
        CancellationToken cancellationToken)
    {
        var target = new CapabilityTarget(application.Application, application.Environment, scope.ResourceType, scope.ResourceIdentifier);
        AuthorizationDecision decision = await authorizer.AuthorizeAsync(capabilities, target, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The capability authorizer returned no complete decision.");
        cancellationToken.ThrowIfCancellationRequested();
        return decision.IsAllowed;
    }

    private OperationIdentity Identity(OperationDefinition definition, OperationScope scope, string key)
        => new(application.Application, application.Environment, scope.Scope, definition.Name, key);

    private static void ValidateContext(ILocalTransactionContext context, OperationDefinition definition)
    {
        if (context.DatabaseName != definition.DatabaseName)
        {
            throw new ArgumentException("Use the registered operation's exact authoritative database context.", nameof(context));
        }

        if (context.Isolation != TransactionIsolation.ReadCommitted)
        {
            throw new NotSupportedException("Durable operations v1 require an explicit ReadCommitted context.");
        }
    }

    private static OperationFingerprint[] Fingerprints(OperationDefinition definition, IReadOnlyList<OperationFingerprintInput> inputs)
    {
        if (!inputs.Any(item => item.Contract == definition.FingerprintContract)
            || inputs.Any(item => item.Contract.Identifier != definition.FingerprintContract.Identifier
                || !definition.FingerprintVersions.Contains(item.Contract.Version)))
        {
            throw new InvalidOperationException("The normalizer must provide the current and only explicitly registered comparison contracts.");
        }

        return inputs.Select(item => OperationCanonicalFrame.Fingerprint(item, definition.MaximumCanonicalBytes)).ToArray();
    }

    private static OperationExecution<TResult> Replay<TRequest, TResult>(OperationDefinition<TRequest, TResult> definition,
        OperationIdentity identity, OperationFingerprint[] fingerprints, OperationJournalRecord row)
    {
        if (row.Identity != identity || row.IsCorrupt || !row.IsFinalized || row.Fingerprint is null)
        {
            return new(OperationExecutionDisposition.Incompatible);
        }

        OperationFingerprint? comparison = fingerprints.SingleOrDefault(item => item.Contract == row.Fingerprint.Contract);
        if (comparison is null)
        {
            return new(OperationExecutionDisposition.Incompatible);
        }

        if (!CryptographicOperations.FixedTimeEquals(comparison.DigestSpan, row.Fingerprint.DigestSpan))
        {
            return new(OperationExecutionDisposition.KeyConflict);
        }

        if (row.Times.IsResultExpired)
        {
            return new(OperationExecutionDisposition.ResultExpired);
        }

        return row.Result is { } stored
            && OperationResultReader.TryDecode(definition, definition.Codec, stored, out OperationDecision<TResult>? decision)
                ? new(OperationExecutionDisposition.Replayed, decision)
                : new(OperationExecutionDisposition.Incompatible);
    }

    private static OperationLookup<TResult> Lookup<TResult>(OperationDefinition definition, IOperationResultCodec<TResult> codec,
        OperationIdentity identity, OperationJournalRecord? row)
    {
        if (row is null)
        {
            return new(OperationLookupDisposition.NotFound);
        }

        if (row.Identity != identity || row.IsCorrupt || !row.IsFinalized || row.Fingerprint is null)
        {
            return new(OperationLookupDisposition.Unavailable);
        }

        if (row.Times.IsResultExpired)
        {
            return new(OperationLookupDisposition.ResultExpired);
        }

        return row.Result is { } stored
            && OperationResultReader.TryDecode(definition, codec, stored, out OperationDecision<TResult>? decision)
                ? new(OperationLookupDisposition.Found, decision)
                : new(OperationLookupDisposition.Incompatible);
    }

    private static void Poison(ILocalTransactionContext context, string reason)
    {
        try
        {
            context.MarkRollbackOnly(reason);
        }
        catch
        {
            // A disposed/misused owner can refuse a late mark; never replace the original operation fault.
        }
    }

    private static async Task<ExceptionDispatchInfo?> SettleReadAsync(ILocalTransaction owner)
    {
        ExceptionDispatchInfo? first = null;
        try
        {
            _ = await owner.RollbackAsync().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            first = ExceptionDispatchInfo.Capture(error);
        }

        try
        {
            await owner.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception error)
        {
            first ??= ExceptionDispatchInfo.Capture(error);
        }

        return first;
    }
}
