using CIS.Metrics;
using CIS.Metrics.OpenTelemetry;
using CIS.Startup;
using CIS.Tests.IntegrationTests.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Metrics;

[TestClass]
public sealed class HostingOrderTests
{
    [TestMethod]
    [DataRow("owner-before")]
    [DataRow("owner-after")]
    [DataRow("keyed-tail")]
    [DataRow("concurrent-stop")]
    public async Task GenericHostRequiresTheMetricsBarrierAfterOrdinaryOwnersAndNeverResolvesAnotherHostedGraph(string mode)
    {
        var owner = new HostingCallbackOwner();
        int constructions = 0, keyedConstructions = 0;
        IHost host = Host.CreateDefaultBuilder()
            .UseDefaultServiceProvider(options => { options.ValidateScopes = true; options.ValidateOnBuild = true; })
            .ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureServices(services =>
            {
                void AddOwner() => services.AddSingleton<IHostedService>(provider =>
                { Interlocked.Increment(ref constructions); return owner.Bind(provider.GetRequiredService<IMetrics>()); });
                if (mode != "owner-after") { AddOwner(); }
                HostingTestSupport.AddMetrics(services, modules: false);
                if (mode == "owner-after") { AddOwner(); }
                if (mode == "keyed-tail")
                {
                    services.AddKeyedSingleton<IHostedService>("not-enrolled", (_, _) =>
                    { Interlocked.Increment(ref keyedConstructions); throw new InvalidOperationException("A keyed service must not be resolved by host-order validation."); });
                }
                services.Configure<HostOptions>(options => options.ServicesStopConcurrently = mode == "concurrent-stop");
            }).Build();
        try
        {
            if (mode is "owner-after" or "concurrent-stop")
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync()).WaitAsync(HostingTestSupport.Deadline);
                Assert.AreEqual(0, owner.Starts);
            }
            else
            {
                await host.StartAsync().WaitAsync(HostingTestSupport.Deadline);
                Assert.AreEqual(1, owner.Starts);
            }
            Assert.AreEqual(1, constructions);
            Assert.AreEqual(0, keyedConstructions);
        }
        finally
        {
            owner.Release.TrySetResult();
            await host.StopAsync(CancellationToken.None).WaitAsync(HostingTestSupport.Deadline);
            await HostingTestSupport.DisposeHostAsync(host);
        }
        Assert.AreEqual(1, owner.Disposals);
    }

    [TestMethod]
    public async Task LateMetricsInstallationPreservesAnExistingUnsupportedForeignTailRatherThanMovingItBeforeTheCoreBridge()
    {
        int constructions = 0;
        var owner = new HostingCallbackOwner();
        var participant = new StartupParticipant();
        await using var host = new StartupHost(services => services.AddSingleton<IStartable>(participant), services =>
        {
            services.AddSingleton<IHostedService>(provider =>
            { Interlocked.Increment(ref constructions); return owner.Bind(provider.GetRequiredService<IMetrics>()); });
            HostingTestSupport.AddMetrics(services);
        }, HostingTestSupport.ConfigureApp);
        host.Release.Add(() => owner.Release.TrySetResult());
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.StartAsync()).WaitAsync(HostingTestSupport.Deadline);
        Assert.AreEqual(0, participant.Calls);
        Assert.AreEqual(1, constructions, "The actual host constructs its ordinary graph once; validation must not resolve it again.");
        Assert.IsFalse(host.Health.CaptureSnapshot().Application.StartupCompleted);
    }
}
