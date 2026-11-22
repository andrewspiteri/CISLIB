using CIS.CommonTesting;
using CIS.Coordination;
using CIS.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Coordination;

[TestClass]
public sealed class CoordinationServiceTests
{
    [TestMethod]
    public async Task TrustedCrossApplicationTargetUsesActualAuthorizationAndCapturedLocalOwner()
    {
        LocalApplicationIdentity application = LocalApplicationIdentity.Capture();
        string targetApplication = application.Application == "shared-service" ? "other-service" : "shared-service";
        var key = new CoordinationKey(application.Environment, "resolved-scope", "actual-duty", "partition-9");
        CoordinationTarget target = CoordinationTarget.ForDuty(key, targetApplication,
            ["coordination.execute", "wallet.maintain"]);
        var resolver = new Resolver(target);
        var authorizer = new Authorizer(AuthorizationDecision.Allow);
        var provider = new Provider();
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero));
        await using var runtime = new CoordinationRuntime(provider, new CoordinationOptions(), time);
        await runtime.StartAsync();
        var service = new CoordinationService(runtime, resolver, authorizer, application);
        var requested = new CoordinationRequest("requested-scope", "requested-duty", "requested-resource");

        ManagedCoordinationResult result = await service.AcquireAsync(requested);
        Assert.IsNotNull(result.Lease);
        await using ICoordinationLease lease = result.Lease;

        Assert.AreEqual(CoordinationAcquireOutcome.Acquired, result.Outcome);
        Assert.AreSame(requested, resolver.LastRequest);
        Assert.AreSame(application, resolver.LastApplication);
        Assert.AreEqual(1, authorizer.Calls);
        Assert.AreSame(target.Resource, authorizer.LastTarget);
        CollectionAssert.AreEqual(target.Capabilities.ToArray(), authorizer.LastCapabilities!.ToArray());
        Assert.AreEqual(targetApplication, authorizer.LastTarget!.Application);
        Assert.AreEqual(1, provider.Acquisitions);
        Assert.IsNotNull(provider.LastAcquisition);
        Assert.AreEqual(key, provider.LastAcquisition.Key);
        Assert.AreEqual(application.Application, provider.LastAcquisition.OwnerApplication);
        Assert.AreEqual(application.InstanceId, provider.LastAcquisition.InstanceId);
        Assert.AreEqual(application.Environment, provider.LastAcquisition.Key.Environment);
        Assert.AreNotEqual(Guid.Empty, provider.LastAcquisition.AcquisitionId);
        Assert.AreEqual(provider.LastAcquisition.AcquisitionId, lease.Grant.GrantId);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task UnknownOrWrongEnvironmentTargetIsRejectedBeforeAuthorizationOrProvider(bool wrongEnvironment)
    {
        LocalApplicationIdentity application = LocalApplicationIdentity.Capture();
        string otherEnvironment = application.Environment == "other" ? "different" : "other";
        CoordinationTarget? target = wrongEnvironment
            ? CoordinationTarget.ForDuty(new(otherEnvironment, "scope", "job"), "shared-service", ["coordination.execute"])
            : null;
        var resolver = new Resolver(target);
        var authorizer = new Authorizer(AuthorizationDecision.Allow);
        var provider = new Provider();
        await using var runtime = NewRuntime(provider);
        await runtime.StartAsync();
        var service = new CoordinationService(runtime, resolver, authorizer, application);

        ManagedCoordinationResult result = await service.AcquireAsync(new("scope", "job"));

        Assert.AreEqual(CoordinationAcquireOutcome.InvalidRequest, result.Outcome);
        Assert.IsNull(result.Lease);
        Assert.AreEqual(1, resolver.Calls);
        Assert.AreEqual(0, authorizer.Calls);
        Assert.AreEqual(0, provider.Acquisitions);
    }

    [TestMethod]
    public async Task DeniedActualTargetCannotReachProvider()
    {
        LocalApplicationIdentity application = LocalApplicationIdentity.Capture();
        CoordinationTarget target = Target(application);
        var resolver = new Resolver(target);
        AuthorizationDecision denied = AuthorizationDecision.Deny(new(SecurityFailureKind.CapabilityDenied, "Denied"));
        var authorizer = new Authorizer(denied);
        var provider = new Provider();
        await using var runtime = NewRuntime(provider);
        await runtime.StartAsync();
        var service = new CoordinationService(runtime, resolver, authorizer, application);

        ManagedCoordinationResult result = await service.AcquireAsync(new("caller-scope", "caller-duty"));

        Assert.AreEqual(CoordinationAcquireOutcome.Unauthorized, result.Outcome);
        Assert.AreEqual(denied.Code, result.ReasonCode);
        Assert.AreSame(target.Resource, authorizer.LastTarget);
        Assert.AreEqual(1, authorizer.Calls);
        Assert.AreEqual(0, provider.Acquisitions);
    }

    [TestMethod]
    public async Task PreCancellationReachesNeitherResolverAuthorizationNorProvider()
    {
        LocalApplicationIdentity application = LocalApplicationIdentity.Capture();
        var resolver = new Resolver(Target(application));
        var authorizer = new Authorizer(AuthorizationDecision.Allow);
        var provider = new Provider();
        await using var runtime = NewRuntime(provider);
        await runtime.StartAsync();
        var service = new CoordinationService(runtime, resolver, authorizer, application);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.AcquireAsync(new("scope", "job"), cancellation.Token));

        Assert.AreEqual(0, resolver.Calls);
        Assert.AreEqual(0, authorizer.Calls);
        Assert.AreEqual(0, provider.Acquisitions);
    }

    [TestMethod]
    public async Task CancellationAfterAuthorizationCannotStartDetachedProviderAcquisition()
    {
        LocalApplicationIdentity application = LocalApplicationIdentity.Capture();
        var resolver = new Resolver(Target(application));
        using var cancellation = new CancellationTokenSource();
        var authorizer = new Authorizer(AuthorizationDecision.Allow, cancellation.Cancel);
        var provider = new Provider();
        await using var runtime = NewRuntime(provider);
        await runtime.StartAsync();
        var service = new CoordinationService(runtime, resolver, authorizer, application);

        await Assert.ThrowsAsync<OperationCanceledException>(() => service.AcquireAsync(new("scope", "job"), cancellation.Token));

        Assert.AreEqual(1, resolver.Calls);
        Assert.AreEqual(1, authorizer.Calls);
        Assert.AreEqual(0, provider.Acquisitions);
    }

    private static CoordinationRuntime NewRuntime(Provider provider)
    {
        return new(provider, new CoordinationOptions(),
            new ManualTimeProvider(new DateTimeOffset(2026, 11, 22, 12, 0, 0, TimeSpan.Zero)));
    }

    private static CoordinationTarget Target(LocalApplicationIdentity application)
    {
        return CoordinationTarget.ForDuty(new(application.Environment, "actual-scope", "actual-duty"),
            application.Application, ["coordination.execute"]);
    }

    private sealed class Resolver(CoordinationTarget? target) : ICoordinationTargetResolver
    {
        internal int Calls;
        internal CoordinationRequest? LastRequest;
        internal LocalApplicationIdentity? LastApplication;

        public ValueTask<CoordinationTarget?> ResolveAsync(CoordinationRequest request,
            LocalApplicationIdentity application, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastRequest = request;
            LastApplication = application;
            return ValueTask.FromResult(target);
        }
    }

    private sealed class Authorizer(AuthorizationDecision decision, Action? beforeReturn = null) : ICapabilityAuthorizer
    {
        internal int Calls;
        internal CapabilityTarget? LastTarget;
        internal IReadOnlyList<string>? LastCapabilities;

        public ValueTask<AuthorizationDecision> AuthorizeAsync(IReadOnlyList<string> capabilities,
            CapabilityTarget target, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastTarget = target;
            LastCapabilities = capabilities;
            beforeReturn?.Invoke();
            return ValueTask.FromResult(decision);
        }
    }

    private sealed class Provider : ICoordinationProvider
    {
        internal int Acquisitions;
        internal CoordinationAcquisition? LastAcquisition;

        public Guid AuthorityId { get; } = Guid.Parse("18ac4286-fd9b-4f70-8b3e-cf207c02d3c5");

        public Task ValidateAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<CoordinationAcquireResult> AcquireAsync(CoordinationAcquireRequest request,
            CancellationToken cancellationToken = default)
        {
            Acquisitions++;
            LastAcquisition = request.Acquisition;
            var grant = new CoordinationGrant(AuthorityId, request.Acquisition, 1,
                new DateTimeOffset(2026, 11, 22, 12, 0, 30, TimeSpan.Zero));
            return Task.FromResult(new CoordinationAcquireResult(CoordinationAcquireOutcome.Acquired,
                grant, request.LeaseDuration));
        }

        public Task<CoordinationMutationResult> RenewAsync(CoordinationGrant grant, TimeSpan leaseDuration,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CoordinationMutationResult(CoordinationMutationOutcome.Succeeded,
                grant, leaseDuration));
        }

        public Task<CoordinationMutationResult> ReleaseAsync(CoordinationGrant grant,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CoordinationMutationResult(CoordinationMutationOutcome.Succeeded));
        }

        public Task<CoordinationObservation> ResolveAsync(CoordinationAcquisition acquisition,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CoordinationObservation(CoordinationObservationOutcome.NotObserved));
        }
    }
}
