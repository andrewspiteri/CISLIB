using System.Net;
using CIS.Healthcheck;
using CIS.Healthcheck.AspNetCore;
using CIS.Metrics.OpenTelemetry;
using CIS.Startup;
using CIS.Tests.IntegrationTests.Startup;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Metrics;

[TestClass]
public sealed class HostingStartupTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task BeforeOrAfterStartupRegistrationActivatesOneProviderBeforeInitializationAndServesMetricsWhileBusinessIsGated(bool installLate)
    {
        var entered = HostingTestSupport.Signal();
        var release = HostingTestSupport.Signal();
        var participant = new StartupParticipant(async _ => { entered.TrySetResult(); await release.Task; });
        await using var host = new StartupHost(services =>
        {
            services.AddSingleton<IStartable>(participant);
            if (!installLate) { HostingTestSupport.AddMetrics(services); }
        }, services =>
        {
            if (installLate) { HostingTestSupport.AddMetrics(services); }
        }, HostingTestSupport.ConfigureApp);
        host.Release.Add(() => release.TrySetResult());
        Task start = host.StartAsync();
        await entered.Task.WaitAsync(HostingTestSupport.Deadline);
        Assert.IsTrue(host.App.Services.GetRequiredService<MetricsCollectionContext>().IsActive);
        Assert.IsFalse(start.IsCompleted);
        using HttpClient client = HostingTestSupport.Client(host);
        string during = await HostingTestSupport.ScrapeAsync(client);
        string completed = CollectionText.Samples(during).Single(line => line.StartsWith("cis_startup_completed{", StringComparison.Ordinal));
        Assert.AreEqual(0d, CollectionText.Value(completed));
        string construction = CollectionText.Samples(during).Single(line =>
            line.StartsWith("cis_startup_operation_duration_seconds_count{", StringComparison.Ordinal)
            && line.Contains("stage=\"construction\"", StringComparison.Ordinal));
        Assert.AreEqual(1d, CollectionText.Value(construction));
        await host.AssertStatusAsync("/_health/ready", HttpStatusCode.InternalServerError);
        await host.AssertStatusAsync("/business", HttpStatusCode.ServiceUnavailable);
        Assert.AreEqual(0, host.BusinessCalls);
        release.TrySetResult();
        await start.WaitAsync(HostingTestSupport.Deadline);
        string after = await HostingTestSupport.ScrapeAsync(client);
        Assert.AreEqual(1d, CollectionText.Value(CollectionText.Samples(after).Single(line => line.StartsWith("cis_startup_completed{", StringComparison.Ordinal))));
        Assert.AreEqual(1, participant.Calls);
        await host.AssertStatusAsync("/_health/ready", HttpStatusCode.OK);
    }

    [TestMethod]
    public async Task HealthTransportAttachesAnActualApplicationCreatedSourceWithoutRegisteringOrOwningItInDi()
    {
        await using var health = new HealthcheckService(new());
        health.MarkStartupCompleted();
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Host.UseDefaultServiceProvider(options => { options.ValidateScopes = true; options.ValidateOnBuild = true; });
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        HostingTestSupport.AddMetrics(builder.Services);
        await using WebApplication app = builder.Build();
        HostingTestSupport.ConfigureApp(app);
        app.UseCisHealthcheck(health, new());
        try
        {
            await app.StartAsync().WaitAsync(HostingTestSupport.Deadline);
            Assert.IsNull(app.Services.GetService<HealthcheckService>());
            using var client = new HttpClient { BaseAddress = new(app.Urls.Single()), Timeout = HostingTestSupport.Deadline };
            string text = await HostingTestSupport.ScrapeAsync(client);
            Assert.AreEqual(1d, CollectionText.Value(CollectionText.Samples(text).Single(line => line.StartsWith("cis_healthcheck_ready{", StringComparison.Ordinal))));
            using HttpResponseMessage ready = await client.GetAsync("/_health/ready");
            Assert.AreEqual(HttpStatusCode.OK, ready.StatusCode);
        }
        finally { await app.StopAsync(CancellationToken.None).WaitAsync(HostingTestSupport.Deadline); }
        Assert.IsTrue(health.CaptureSnapshot().Ready, "Stopping collection does not take ownership of the supplied Healthcheck source.");
    }
}
