using CIS.Coordination.Hosting;
using CIS.Jobs.Hosting;
using CIS.Metrics.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Coordination.Observability;

[TestClass]
public sealed class CoordinationCatalogTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FinalCompositionReservesSevenStreamsAndSixtyCombinationsInEitherRegistrationOrder(bool metricsFirst)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        if (metricsFirst)
        {
            builder.Services.AddCisMetrics();
        }

        builder.Services.AddSingleton(new CoordinationComposition());
        builder.Services.AddSingleton(new JobsComposition());
        if (!metricsFirst)
        {
            builder.Services.AddCisMetrics();
        }

        IHost host = builder.Build();
        try
        {
            MetricsCollectionContext context = host.Services.GetRequiredService<MetricsCollectionContext>();
            MetricsStreamSnapshot[] selected = context.CaptureCatalog().Where(item => item.Name.StartsWith("cis.coordination.", StringComparison.Ordinal)
                || item.Name.StartsWith("cis.jobs.", StringComparison.Ordinal)).ToArray();
            Assert.HasCount(7, selected);
            Assert.AreEqual(60, selected.Sum(item => item.CardinalityLimit));
            Assert.IsTrue(context.Settings.CollectCoordination);
            Assert.IsTrue(context.Settings.CollectJobs);
            Assert.AreEqual(96, context.Settings.MaxDefinitions);
            Assert.AreEqual(128, context.Settings.MaxStreams);
            Assert.AreEqual(16_384, context.Settings.TotalCardinalityLimit);
        }
        finally
        {
            await ((IAsyncDisposable)host).DisposeAsync();
        }
    }

    [TestMethod]
    [DataRow("cis.coordination.operation.duration", 31)]
    [DataRow("cis.coordination.active.leases", 0)]
    [DataRow("cis.coordination.ownership.losses", 5)]
    [DataRow("cis.coordination.guard.rejections", 7)]
    [DataRow("cis.jobs.invocation.duration", 9)]
    [DataRow("cis.jobs.active.invocations", 1)]
    [DataRow("cis.jobs.pending.dispatches", 0)]
    public void OverridesCannotShrinkAwayTheFiniteProfile(string name, int capacity)
    {
        var options = new MetricsOptions();
        options.Streams.Add(name, new()
        {
            CardinalityLimit = capacity
        });
        Assert.ThrowsExactly<ArgumentException>(() => new MetricsStreamCatalog(new MetricsSettings(options).ForComposition(false,
            hasCoordination: true, hasJobs: true)));
    }

    [TestMethod]
    public void AbsentOrDisabledModuleCollectionAddsNoCoordinationReservations()
    {
        foreach (MetricsSettings settings in new[] { new MetricsSettings(new MetricsOptions()).ForComposition(false),
            new MetricsSettings(new MetricsOptions { CollectCoreModules = false }).ForComposition(false, hasCoordination: true, hasJobs: true) })
        {
            Assert.IsFalse(new MetricsStreamCatalog(settings).Capture().Any(item => item.Name.StartsWith("cis.coordination.", StringComparison.Ordinal)
                || item.Name.StartsWith("cis.jobs.", StringComparison.Ordinal)));
        }
    }
}
