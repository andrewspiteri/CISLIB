using System.Diagnostics.Metrics;
using System.Net;
using CIS.Metrics;
using CIS.Metrics.OpenTelemetry;
using CIS.Heartbeat.Hosting;
using CIS.Tests.IntegrationTests.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Metrics;

[TestClass]
public sealed class HostingLifetimeTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task TimedOutScrapeAndCanceledHostBudgetRetainActualLifecycleOwnerUntilCallbackSettlement(bool modules)
    {
        var owner = new HostingCallbackOwner();
        int constructions = 0;
        var host = new StartupHost(services =>
        {
            // Deliberately register the owner after Metrics. The final Core
            // bridge supplies the callback barrier in this supported ordering.
            HostingTestSupport.AddMetrics(services, modules);
            services.AddSingleton<IHostedService>(provider =>
            { Interlocked.Increment(ref constructions); return owner.Bind(provider.GetRequiredService<IMetrics>()); });
        }, configureApp: HostingTestSupport.ConfigureApp);
        await using (host)
        {
            host.Release.Add(() => owner.Release.TrySetResult());
            await host.StartAsync().WaitAsync(HostingTestSupport.Deadline);
            using HttpClient client = HostingTestSupport.Client(host);
            Task<HttpResponseMessage> request = client.GetAsync("/_metrics");
            await owner.Entered.Task.WaitAsync(HostingTestSupport.Deadline);
            using HttpResponseMessage expired = await request.WaitAsync(HostingTestSupport.Deadline);
            Assert.AreEqual(HttpStatusCode.RequestTimeout, expired.StatusCode);
            Task stop = host.StopAsync(new CancellationToken(canceled: true));
            Assert.IsFalse(stop.IsCompleted, "Expired HTTP and host waits must not stand in for actual callback drain.");
            Assert.IsTrue(host.App.Services.GetRequiredService<MetricsCollectionContext>().IsStopping);
            Assert.IsFalse(owner.Stopping.Task.IsCompleted);
            Assert.AreEqual(0, owner.Disposals);
            using HttpResponseMessage rejected = await client.GetAsync("/_metrics").WaitAsync(HostingTestSupport.Deadline);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, rejected.StatusCode,
                "The Core callback cutoff must reject later scrapes before the earlier callback settles.");
            Assert.IsFalse(stop.IsCompleted);
            Assert.AreEqual(1, owner.Calls);
            owner.Release.TrySetResult();
            await HostingTestSupport.ObserveStopAsync(stop);
            await owner.Stopping.Task.WaitAsync(HostingTestSupport.Deadline);
            Assert.IsTrue(owner.WasUsable);
            Assert.AreEqual(1, owner.Calls);
            Assert.AreEqual(1, constructions);
            Assert.AreEqual(0, owner.Disposals);
        }
        Assert.AreEqual(1, owner.Disposals, "Only actual asynchronous root disposal owns this factory-created service.");
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task HeartbeatOnlyBridgeAlsoJoinsOrdinaryCallbacksBeforeDependencyStopping(bool modules)
    {
        var owner = new HostingCallbackOwner();
        IHost host = Host.CreateDefaultBuilder()
            .UseDefaultServiceProvider(options => { options.ValidateScopes = true; options.ValidateOnBuild = true; })
            .ConfigureLogging(logging => logging.ClearProviders())
            .ConfigureServices(services =>
            {
                HostingTestSupport.AddMetrics(services, modules);
                services.AddSingleton<IHostedService>(provider => owner.Bind(provider.GetRequiredService<IMetrics>()));
                services.AddCisHeartbeat();
            }).Build();
        Task? collection = null;
        MeterListener? listener = null;
        try
        {
            await host.StartAsync().WaitAsync(HostingTestSupport.Deadline);
            MetricsCollectionContext context = host.Services.GetRequiredService<MetricsCollectionContext>();
            listener = new MeterListener();
            listener.InstrumentPublished = (instrument, current) =>
            { if (ReferenceEquals(instrument.Meter, context.CisMeter)) { current.EnableMeasurementEvents(instrument); } };
            listener.SetMeasurementEventCallback<double>(static (_, _, _, _) => { });
            listener.Start();
            collection = Task.Run(listener.RecordObservableInstruments);
            await owner.Entered.Task.WaitAsync(HostingTestSupport.Deadline);
            Task stop = host.StopAsync(new CancellationToken(canceled: true));
            Assert.IsFalse(stop.IsCompleted);
            Assert.IsFalse(owner.Stopping.Task.IsCompleted);
            Assert.AreEqual(0, owner.Disposals);
            owner.Release.TrySetResult();
            await collection.WaitAsync(HostingTestSupport.Deadline);
            await HostingTestSupport.ObserveStopAsync(stop);
            Assert.IsTrue(owner.Stopping.Task.IsCompletedSuccessfully);
            Assert.IsTrue(owner.WasUsable);
        }
        finally
        {
            owner.Release.TrySetResult();
            if (collection is not null) { await collection.WaitAsync(HostingTestSupport.Deadline); }
            await host.StopAsync(CancellationToken.None).WaitAsync(HostingTestSupport.Deadline);
            listener?.Dispose();
            await HostingTestSupport.DisposeHostAsync(host);
        }
        Assert.AreEqual(1, owner.Disposals);
    }
}
