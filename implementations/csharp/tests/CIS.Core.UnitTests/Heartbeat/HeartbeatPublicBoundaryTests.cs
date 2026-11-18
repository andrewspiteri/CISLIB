using System.Reflection;
using CIS.Healthcheck;
using CIS.Heartbeat;
using CIS.Heartbeat.Healthcheck;
using CIS.Metrics;
using CIS.RestClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Heartbeat;

[TestClass]
public sealed class HeartbeatPublicBoundaryTests
{
    [TestMethod]
    public async Task PublicOwnerFactoryPreservesConcreteCreationAndTheActualOwnerService()
    {
        await using var scope = new HeartbeatTestScope();
        IHeartbeatOwnerFactory factory = scope.Runtime;
        await using IHeartbeatOwner owner = factory.CreateOwner();

        Assert.IsInstanceOfType<HeartbeatOwner>(owner);
        Assert.AreSame(scope.Runtime, ((HeartbeatOwner)owner).Runtime);
        Assert.AreSame(((HeartbeatOwner)owner).Service, owner.Service);
        Assert.IsInstanceOfType<IHeartbeatHealthcheckService>(owner.Service);
        Assert.AreEqual(typeof(HeartbeatOwner), typeof(HeartbeatRuntime).GetMethod(nameof(HeartbeatRuntime.CreateOwner))!.ReturnType);
        CollectionAssert.AreEquivalent(new[] { "Register", "UnregisterAsync" },
            typeof(IHeartbeatService).GetMethods().Select(method => method.Name).ToArray());
        await owner.CloseAsync();
        await owner.CloseAsync();
    }

    [TestMethod]
    public void OrdinaryMonitoredExtensionDispatchesToPublicCapabilityWithPublicMonitor()
    {
        var service = new PublicMonitoredService();
        var subscriber = new HeartbeatProbe();
        var monitor = new PublicAvailabilityMonitor();
        var options = new HeartbeatRegistrationOptions { Interval = TimeSpan.FromHours(1) };
        var monitoring = new HeartbeatHealthcheckOptions { Monitor = monitor, SuccessPublishesUsableData = true };
        Func<CancellationToken, Task<bool>> condition = _ => Task.FromResult(true);

        ((IHeartbeatService)service).Register(subscriber, options, monitoring, condition);

        Assert.AreEqual(1, service.MonitoredCalls);
        Assert.AreSame(subscriber, service.Subscriber);
        Assert.AreSame(options, service.Options);
        Assert.AreSame(monitoring, service.Monitoring);
        Assert.AreSame(condition, service.Condition);
        Assert.AreEqual(0, subscriber.Calls, "Registration cannot execute user work inline.");
    }

    [TestMethod]
    public void ExistingPlainServiceRemainsUsableAndUnsupportedMonitoringFailsClearly()
    {
        var service = new PlainService();
        var subscriber = new HeartbeatProbe();
        service.Register(subscriber, new HeartbeatRegistrationOptions());
        Assert.AreEqual(1, service.Calls);

        ArgumentException failure = Assert.ThrowsExactly<ArgumentException>(() => ((IHeartbeatService)service)
            .Register(subscriber, new HeartbeatRegistrationOptions(), new HeartbeatHealthcheckOptions
            {
                Monitor = new PublicAvailabilityMonitor(),
                SuccessPublishesUsableData = true
            }));

        Assert.AreEqual("service", failure.ParamName);
        Assert.AreEqual(1, service.Calls);
    }

    [TestMethod]
    public async Task ProductionCapabilityRetainsItsCoreMonitorValidation()
    {
        await using var scope = new HeartbeatTestScope();
        IHeartbeatOwnerFactory factory = scope.Runtime;
        await using IHeartbeatOwner owner = factory.CreateOwner();

        Assert.ThrowsExactly<ArgumentException>(() => owner.Service.Register(new HeartbeatProbe(), new(),
            new HeartbeatHealthcheckOptions
            {
                Monitor = new PublicAvailabilityMonitor(),
                SuccessPublishesUsableData = true
            }));

        Assert.AreEqual(0, scope.Runtime.Inspect().Registrations);
    }

    [TestMethod]
    public async Task PublicOwnerPathUsesTheRealAtomicBindingAndRetiresOnlyItsBorrowedAssociation()
    {
        await using var scope = new HeartbeatTestScope();
        await using var health = new HealthcheckService(new HealthcheckOptions());
        await using IAvailabilityMonitor monitor = health.CreateAvailability(new()
        {
            IsCritical = true
        });
        await using IHeartbeatOwner owner = ((IHeartbeatOwnerFactory)scope.Runtime).CreateOwner();
        var subscriber = new HeartbeatProbe();
        var monitoring = new HeartbeatHealthcheckOptions { Monitor = monitor, SuccessPublishesUsableData = true };

        owner.Service.Register(subscriber, new(), monitoring);
        owner.Service.Register(subscriber, new(), monitoring);
        scope.Activate();
        scope.Executor.RunNext();
        await owner.Service.UnregisterAsync(subscriber).WaitAsync(HeartbeatTestScope.Deadline);
        await owner.CloseAsync().WaitAsync(HeartbeatTestScope.Deadline);

        HealthMonitorSnapshot snapshot = health.CaptureSnapshot().Monitors.Single();
        Assert.AreEqual(1L, snapshot.Outcomes!.Successful);
        Assert.AreEqual(HealthExecutionPhase.Settled, snapshot.Outcomes.Execution!.Phase);
        Assert.AreEqual(1, subscriber.Calls);
        Assert.AreEqual(0, subscriber.Errors);
        monitor.ReportPublished();
        Assert.AreEqual(2L, health.CaptureSnapshot().Monitors.Single().Outcomes!.Successful);
        Assert.IsFalse(scope.Runtime.InfrastructureFailure.IsCompleted);
    }

    [TestMethod]
    public void PublicResultConstructionKeepsPresenceAndFailureGuardsAndImmutableProperties()
    {
        Assert.IsTrue(typeof(RestResultContext).GetConstructors().Single().IsPublic);
        foreach (string name in new[] { "FromValue", "Empty", "Failed" })
        {
            Assert.IsTrue(typeof(RestResult<string?>).GetMethod(name, BindingFlags.Public | BindingFlags.Static)!.IsPublic);
        }
        Assert.IsTrue(typeof(RestResult<string?>).GetProperties().All(property => property.SetMethod is null));
        Assert.IsTrue(typeof(RestResultContext).GetProperties().All(property => property.SetMethod is null));

        RestResult<string?> present = RestResult<string?>.FromValue(null, new(hasValue: true, httpStatusCode: 200));
        Assert.IsTrue(present.Success);
        Assert.IsTrue(present.Context.HasValue);
        Assert.IsNull(present.Value);
        Assert.IsFalse(RestResult<int>.Empty(new(httpStatusCode: 204)).Context.HasValue);
        Assert.ThrowsExactly<ArgumentException>(() => RestResult<int>.FromValue(1, new(httpStatusCode: 503, hasValue: true)));
        Assert.ThrowsExactly<ArgumentException>(() => RestResult<int>.Failed("http.failed", "Rejected.",
            new(httpStatusCode: 200, failureKind: RestFailureKind.Http)));
    }

    [TestMethod]
    public void PublicDisabledFacadeHasNoPublicConstructionRoute()
    {
        Assert.IsTrue(typeof(DisabledMetrics).IsPublic);
        Assert.AreEqual(0, typeof(DisabledMetrics).GetConstructors().Length);
    }

    private sealed class PlainService : IHeartbeatService
    {
        internal int Calls;

        public void Register(IHeartbeatSubscriber subscriber, HeartbeatRegistrationOptions options,
            Func<CancellationToken, Task<bool>>? condition = null)
        {
            Calls++;
        }

        public Task UnregisterAsync(IHeartbeatSubscriber subscriber, CancellationToken waitCancellation = default)
            => Task.CompletedTask;
    }

    private sealed class PublicMonitoredService : IHeartbeatHealthcheckService
    {
        internal int MonitoredCalls;
        internal IHeartbeatSubscriber? Subscriber;
        internal HeartbeatRegistrationOptions? Options;
        internal HeartbeatHealthcheckOptions? Monitoring;
        internal Func<CancellationToken, Task<bool>>? Condition;

        public void Register(IHeartbeatSubscriber subscriber, HeartbeatRegistrationOptions options,
            Func<CancellationToken, Task<bool>>? condition = null)
        {
            throw new AssertFailedException("The monitored extension must use the public capability.");
        }

        public void Register(IHeartbeatSubscriber subscriber, HeartbeatRegistrationOptions options,
            HeartbeatHealthcheckOptions monitoring, Func<CancellationToken, Task<bool>>? condition = null)
        {
            MonitoredCalls++;
            Subscriber = subscriber;
            Options = options;
            Monitoring = monitoring;
            Condition = condition;
        }

        public Task UnregisterAsync(IHeartbeatSubscriber subscriber, CancellationToken waitCancellation = default)
            => Task.CompletedTask;
    }

    private sealed class PublicAvailabilityMonitor : IAvailabilityMonitor
    {
        public Guid MonitorId { get; } = Guid.NewGuid();
        public string? Name => "Public substitute";
        public bool IsCritical => true;
        public void ReportPublished()
        {
        }

        public void ReportRefreshFailure(HealthDiagnostic? diagnostic = null)
        {
        }

        public void Invalidate(HealthDiagnostic? diagnostic = null)
        {
        }

        public void ReportMeasurement(HealthMeasurement measurement)
        {
        }

        public void Withdraw()
        {
        }
        public Task WaitForDisposalAsync(CancellationToken waitCancellation = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
