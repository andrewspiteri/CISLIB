using CIS.Security;

namespace CIS.Coordination;

/// <summary>Resolves and authorizes each scoped caller before detaching one fresh acquisition for the singleton engine.</summary>
public sealed class CoordinationService : ICoordinationService
{
    private readonly CoordinationRuntime runtime;
    private readonly ICoordinationTargetResolver resolver;
    private readonly ICapabilityAuthorizer authorizer;
    private readonly LocalApplicationIdentity application;

    /// <summary>Wires the existing scoped authorization port without performing I/O.</summary>
    public CoordinationService(CoordinationRuntime runtime, ICoordinationTargetResolver resolver,
        ICapabilityAuthorizer authorizer, LocalApplicationIdentity application)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(authorizer);
        ArgumentNullException.ThrowIfNull(application);
        this.runtime = runtime;
        this.resolver = resolver;
        this.authorizer = authorizer;
        this.application = application;
    }

    /// <inheritdoc />
    public async Task<ManagedCoordinationResult> AcquireAsync(CoordinationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        CoordinationTarget? target = await resolver.ResolveAsync(request, application, cancellationToken).ConfigureAwait(false);
        if (target is null
            || !string.Equals(target.Key.Environment, application.Environment, StringComparison.Ordinal)
            || !string.Equals(target.Resource.Environment, application.Environment, StringComparison.Ordinal))
        {
            return new(CoordinationAcquireOutcome.InvalidRequest, reasonCode: "coordination.target.invalid");
        }

        AuthorizationDecision decision = await authorizer.AuthorizeAsync(target.Capabilities, target.Resource,
            cancellationToken).ConfigureAwait(false);
        if (!decision.IsAllowed)
        {
            return new(CoordinationAcquireOutcome.Unauthorized, reasonCode: decision.Code);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var acquisition = new CoordinationAcquisition(target.Key, application.Application, application.InstanceId, Guid.NewGuid());
        return await runtime.AcquireAuthorizedAsync(acquisition, cancellationToken).ConfigureAwait(false);
    }
}
