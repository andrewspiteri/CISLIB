using CIS.Healthcheck;
using CIS.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Startup;

[TestClass]
public sealed class StartupCoordinatorEnrollmentTests
{
    [TestMethod]
    [DataRow("empty")]
    [DataRow("concrete-only")]
    [DataRow("other-interface")]
    [DataRow("keyed-singleton")]
    [DataRow("keyed-scoped")]
    public async Task OnlyOrdinaryInterfaceRegistrationsEnrollAndAnEmptyScheduledSequenceSucceeds(string registration)
    {
        await using var health = new HealthcheckService(new());
        var services = new ServiceCollection();
        int constructions = 0;
        var ignored = new UnenrolledParticipant();
        switch (registration)
        {
            case "empty": break;
            case "concrete-only": services.AddSingleton<UnenrolledParticipant>(_ => { constructions++; return ignored; }); break;
            case "other-interface": services.AddSingleton<IApplicationParticipant>(_ => { constructions++; return ignored; }); break;
            case "keyed-singleton": services.AddKeyedSingleton<IStartable>("only-keyed", (_, _) => { constructions++; return ignored; }); break;
            case "keyed-scoped": services.AddKeyedScoped<IStartable>("only-keyed", (_, _) => { constructions++; return ignored; }); break;
            default: throw new ArgumentOutOfRangeException(nameof(registration));
        }
        ServiceProvider provider = StartupTestSupport.BuildProvider(services);
        var coordinator = new StartupCoordinator(provider, services.ToArray(), health);
        try
        {
            Assert.IsFalse(health.CaptureSnapshot().Ready);
            Task operation = coordinator.StartAsync(CancellationToken.None);
            await operation.WaitAsync(StartupTestSupport.Deadline);
            Assert.AreEqual(0, constructions);
            Assert.AreEqual(0, ignored.Calls);
            Assert.IsTrue(health.CaptureSnapshot().Application.StartupCompleted);
            Assert.IsTrue(health.CaptureSnapshot().Ready);
            Assert.AreSame(operation, coordinator.StartAsync(new CancellationToken(canceled: true)));
        }
        finally { await StartupTestSupport.StopAndDisposeAsync(coordinator, provider); }
    }

    [TestMethod]
    public async Task EveryScheduledFactoryFinishesBeforeTheFirstStartAndUnrelatedServicesRemainLazy()
    {
        var events = new List<string>();
        var services = new ServiceCollection();
        foreach (string name in new[] { "A", "B", "C" })
        {
            services.AddSingleton<IStartable>(_ =>
            {
                events.Add("construct-" + name);
                return new StartupProbe { Start = _ => { events.Add("start-" + name); return Task.CompletedTask; } };
            });
        }
        services.AddSingleton<UnenrolledParticipant>(_ => { events.Add("unrelated"); return new(); });
        ServiceProvider provider = StartupTestSupport.BuildProvider(services);
        var coordinator = new StartupCoordinator(provider, services.ToArray());
        try
        {
            Assert.AreEqual(0, events.Count);
            await coordinator.StartAsync(CancellationToken.None).WaitAsync(StartupTestSupport.Deadline);
            CollectionAssert.AreEqual(new[] { "construct-A", "construct-B", "construct-C", "start-A", "start-B", "start-C" }, events);
        }
        finally { await StartupTestSupport.StopAndDisposeAsync(coordinator, provider); }
    }

    [TestMethod]
    public async Task ALateFactoryFailureCallsNoStartupMethodAndTheSameFailureDoesNotResolveAgain()
    {
        await using var health = new HealthcheckService(new());
        var services = new ServiceCollection();
        var first = new StartupProbe();
        var expected = new InvalidOperationException("Last scheduled construction failed.");
        int resolutions = 0;
        int criticalMonitorsAtConstruction = -1;
        bool? readyAtConstruction = null;
        services.AddSingleton<IStartable>(_ =>
        {
            resolutions++;
            HealthSnapshot snapshot = health.CaptureSnapshot();
            criticalMonitorsAtConstruction = snapshot.MonitorCounts.Critical;
            readyAtConstruction = snapshot.Ready;
            return first;
        });
        services.AddSingleton<IStartable>(_ => { resolutions++; throw expected; });
        ServiceProvider provider = StartupTestSupport.BuildProvider(services);
        var coordinator = new StartupCoordinator(provider, services.ToArray(), health);
        try
        {
            Task operation = coordinator.StartAsync(CancellationToken.None);
            Exception actual = await StartupTestSupport.ObserveFailureAsync(operation);
            Assert.AreSame(expected, actual);
            Assert.AreEqual(2, resolutions);
            Assert.AreEqual(1, criticalMonitorsAtConstruction);
            Assert.AreEqual(false, readyAtConstruction);
            Assert.AreEqual(0, first.Calls);
            Assert.IsFalse(health.CaptureSnapshot().Application.StartupCompleted);
            Assert.IsFalse(health.CaptureSnapshot().Ready);
            Assert.AreEqual(1, health.CaptureSnapshot().MonitorCounts.CriticalFailing);
            Task repeated = coordinator.StartAsync(CancellationToken.None);
            Assert.AreSame(operation, repeated);
            Assert.AreSame(expected, await StartupTestSupport.ObserveFailureAsync(repeated));
            Assert.AreEqual(2, resolutions);
            Assert.AreEqual(0, first.Calls);
        }
        finally { await StartupTestSupport.StopAndDisposeAsync(coordinator, provider); }
    }

    [TestMethod]
    public async Task ConstructorDependencyOrderDoesNotReplaceTheInterfaceRegistrationStartupOrder()
    {
        var order = new ConstructionOrder();
        var services = new ServiceCollection();
        services.AddSingleton(order);
        services.AddSingleton<DependencyParticipant>();
        services.AddSingleton<DependentParticipant>();
        services.AddSingleton<IStartable>(provider => provider.GetRequiredService<DependentParticipant>());
        services.AddSingleton<IStartable>(provider => provider.GetRequiredService<DependencyParticipant>());
        ServiceProvider provider = StartupTestSupport.BuildProvider(services);
        var coordinator = new StartupCoordinator(provider, services.ToArray());
        try
        {
            await coordinator.StartAsync(CancellationToken.None).WaitAsync(StartupTestSupport.Deadline);
            CollectionAssert.AreEqual(new[] { "construct-B", "construct-A", "start-A", "start-B" }, order.Events);
            Assert.AreSame(provider.GetRequiredService<DependencyParticipant>(), provider.GetRequiredService<DependentParticipant>().Dependency);
        }
        finally { await StartupTestSupport.StopAndDisposeAsync(coordinator, provider); }
    }

    [TestMethod]
    public async Task ReferenceIdentityDeduplicatesAliasesAndPreservesDistinctEqualObjects()
    {
        var order = new List<string>();
        var first = new EqualStartupProbe { Start = _ => { order.Add("A"); return Task.CompletedTask; } };
        var second = new EqualStartupProbe { Start = _ => { order.Add("B"); return Task.CompletedTask; } };
        var services = new ServiceCollection();
        services.AddSingleton<IStartable>(first);
        services.AddSingleton<IStartable>(second);
        services.AddSingleton<IStartable>(first);
        ServiceProvider provider = StartupTestSupport.BuildProvider(services);
        var coordinator = new StartupCoordinator(provider, services.ToArray());
        try
        {
            await coordinator.StartAsync(CancellationToken.None).WaitAsync(StartupTestSupport.Deadline);
            CollectionAssert.AreEqual(new[] { "A", "B" }, order);
            Assert.AreEqual(1, first.Calls);
            Assert.AreEqual(1, second.Calls);
        }
        finally { await StartupTestSupport.StopAndDisposeAsync(coordinator, provider); }
    }

    [TestMethod]
    public async Task NormalResolutionAndStartupUseTheSameRootSingletonAndOnlyItsProviderDisposesIt()
    {
        var services = new ServiceCollection();
        int constructions = 0;
        services.AddSingleton<OwnedParticipant>(_ => { constructions++; return new(); });
        services.AddSingleton<IApplicationParticipant>(provider => provider.GetRequiredService<OwnedParticipant>());
        services.AddSingleton<IStartable>(provider => provider.GetRequiredService<OwnedParticipant>());
        ServiceProvider provider = StartupTestSupport.BuildProvider(services);
        var coordinator = new StartupCoordinator(provider, services.ToArray());
        OwnedParticipant? actual = null;
        try
        {
            await coordinator.StartAsync(CancellationToken.None).WaitAsync(StartupTestSupport.Deadline);
            actual = provider.GetRequiredService<OwnedParticipant>();
            Assert.AreSame(provider.GetRequiredService<IApplicationParticipant>(), actual);
            Assert.AreSame(provider.GetRequiredService<IStartable>(), actual);
            Assert.AreEqual(1, constructions);
            Assert.AreEqual(1, actual.Calls);
            await coordinator.StopAsync().WaitAsync(StartupTestSupport.Deadline);
            Assert.AreEqual(0, actual.Disposals, "The coordinator must not own the startable or injected dependencies.");
        }
        finally { await StartupTestSupport.StopAndDisposeAsync(coordinator, provider); }
        Assert.IsNotNull(actual);
        Assert.IsTrue(actual.Disposals > 0, "The actual DI owner must retain disposal responsibility.");
    }

    [TestMethod]
    [DataRow(ServiceLifetime.Scoped)]
    [DataRow(ServiceLifetime.Transient)]
    public async Task NonSingletonUnkeyedEnrollmentIsRejectedBeforeAnyStartupMethod(ServiceLifetime lifetime)
    {
        await using var health = new HealthcheckService(new());
        var services = new ServiceCollection();
        var valid = new StartupProbe();
        var invalid = new StartupProbe();
        int invalidResolutions = 0;
        services.AddSingleton<IStartable>(valid);
        ((IServiceCollection)services).Add(new ServiceDescriptor(typeof(IStartable), _ => { invalidResolutions++; return invalid; }, lifetime));
        ServiceProvider provider = StartupTestSupport.BuildProvider(services);
        var coordinator = new StartupCoordinator(provider, services.ToArray(), health);
        try
        {
            Task operation = coordinator.StartAsync(CancellationToken.None);
            Assert.IsInstanceOfType<InvalidOperationException>(await StartupTestSupport.ObserveFailureAsync(operation));
            Assert.AreEqual(0, invalidResolutions, "Descriptor lifetime must be rejected before creating an invalid root participant.");
            Assert.AreEqual(0, valid.Calls);
            Assert.AreEqual(0, invalid.Calls);
            Assert.IsFalse(health.CaptureSnapshot().Application.StartupCompleted);
            Assert.AreEqual(1, health.CaptureSnapshot().MonitorCounts.CriticalFailing);
        }
        finally { await StartupTestSupport.StopAndDisposeAsync(coordinator, provider); }
    }

    [TestMethod]
    public void StandardRootScopeValidationRejectsASingletonScopedCaptureBeforeStageTwo()
    {
        var services = new ServiceCollection();
        var first = new StartupProbe();
        services.AddSingleton<IStartable>(_ => first);
        services.AddScoped<ScopedDependency>();
        services.AddSingleton<IStartable, CapturingScopedParticipant>();
        // Use the actual provider's standard validation, before any coordinator
        // or participant can run. A factory preserves this descriptor's DI slot;
        // supplied-instance validator limitations are documented in the guide.
        AggregateException error = Assert.ThrowsExactly<AggregateException>(() => StartupTestSupport.BuildProvider(services));
        Assert.IsTrue(error.InnerExceptions.Any(item => item is InvalidOperationException
            && item.Message.Contains("Cannot consume scoped service", StringComparison.Ordinal)));
        Assert.AreEqual(0, first.Calls);
    }

    [TestMethod]
    public async Task ExternallySuppliedStartupInstancesRemainExternallyOwnedAfterStopAndProviderDisposal()
    {
        await using var external = new OwnedParticipant();
        var services = new ServiceCollection();
        services.AddSingleton<IStartable>(external);
        ServiceProvider provider = StartupTestSupport.BuildProvider(services);
        var coordinator = new StartupCoordinator(provider, services.ToArray());
        try
        {
            await coordinator.StartAsync(CancellationToken.None).WaitAsync(StartupTestSupport.Deadline);
            await coordinator.StopAsync().WaitAsync(StartupTestSupport.Deadline);
            Assert.AreEqual(0, external.Disposals);
        }
        finally { await StartupTestSupport.StopAndDisposeAsync(coordinator, provider); }
        Assert.AreEqual(0, external.Disposals);
    }

    private interface IApplicationParticipant { }
    private sealed class UnenrolledParticipant : StartupProbe, IApplicationParticipant { }
    private sealed class OwnedParticipant : StartupProbe, IApplicationParticipant, IAsyncDisposable
    {
        internal int Disposals;
        public ValueTask DisposeAsync() { Interlocked.Increment(ref Disposals); return ValueTask.CompletedTask; }
    }
    private sealed class ConstructionOrder
    {
        internal List<string> Events { get; } = [];
    }
    private sealed class DependencyParticipant : IStartable
    {
        private readonly ConstructionOrder order;
        public DependencyParticipant(ConstructionOrder order) { this.order = order; order.Events.Add("construct-B"); }
        public Task StartAsync(CancellationToken cancellationToken) { order.Events.Add("start-B"); return Task.CompletedTask; }
    }
    private sealed class DependentParticipant : IStartable
    {
        private readonly ConstructionOrder order;
        internal DependencyParticipant Dependency { get; }
        public DependentParticipant(DependencyParticipant dependency, ConstructionOrder order)
        { Dependency = dependency; this.order = order; order.Events.Add("construct-A"); }
        public Task StartAsync(CancellationToken cancellationToken) { order.Events.Add("start-A"); return Task.CompletedTask; }
    }
    private sealed class ScopedDependency
    {
        public ScopedDependency() { }
    }
    private sealed class CapturingScopedParticipant : IStartable
    {
        private readonly ScopedDependency dependency;
        public CapturingScopedParticipant(ScopedDependency dependency) { this.dependency = dependency; }
        public Task StartAsync(CancellationToken cancellationToken)
        {
            GC.KeepAlive(dependency);
            return Task.CompletedTask;
        }
    }
}
