using CIS.Metrics.OpenTelemetry;
using CIS.Security;
using CIS.Security.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Metrics;

[TestClass]
public sealed class SecurityCatalogTests
{
    [TestMethod]
    [DataRow(false, true)]
    [DataRow(true, true)]
    [DataRow(false, false)]
    [DataRow(true, false)]
    public async Task ActualHostAttachesOnlySelectedCoreSecuritySourcesInEitherRegistrationOrder(
        bool metricsFirst, bool collectCore)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        int policyReads = 0;
        if (metricsFirst)
        {
            builder.Services.AddCisMetrics(options => options.CollectCoreModules = collectCore);
        }

        builder.Services.AddCisSecurity(_ =>
        {
            Interlocked.Increment(ref policyReads);
            return new SecurityOptions { PolicyVersion = "collection-fixture" };
        });
        if (!metricsFirst)
        {
            builder.Services.AddCisMetrics(options => options.CollectCoreModules = collectCore);
        }

        Assert.AreEqual(0, policyReads, "Registration cannot resolve deferred policy.");
        IHost host = builder.Build();
        try
        {
            MetricsCollectionContext context = host.Services.GetRequiredService<MetricsCollectionContext>();
            Assert.AreEqual(0, context.DefinitionCount, "Inspecting composition does not activate sources.");
            Assert.AreEqual(collectCore, context.Settings.CollectSecurity);
            await host.StartAsync().WaitAsync(CollectionTestHost.Deadline);

            Assert.AreEqual(collectCore ? 3 : 0, context.DefinitionCount,
                "Disabling Core collection must not register security as application metrics.");
            Assert.AreEqual(collectCore ? 3 : 0, context.CaptureCatalog()
                .Count(stream => stream.Name.StartsWith("cis.security.", StringComparison.Ordinal)));
        }
        finally
        {
            try
            {
                await host.StopAsync().WaitAsync(CollectionTestHost.Deadline);
            }
            finally
            {
                if (host is IAsyncDisposable asyncHost)
                {
                    await asyncHost.DisposeAsync();
                }
                else
                {
                    host.Dispose();
                }
            }
        }
    }

    [TestMethod]
    public void SecuritySelectionReservesOnlyItsThreeBoundedStreamsWithoutChangingExistingProfiles()
    {
        var configured = new MetricsSettings(new MetricsOptions());
        IReadOnlyList<MetricsStreamSnapshot> existing = new MetricsStreamCatalog(
            configured.ForComposition(true, true, true)).Capture();
        IReadOnlyList<MetricsStreamSnapshot> selected = new MetricsStreamCatalog(
            configured.ForComposition(true, true, true, securityProfileCapacity: 16)).Capture();

        Assert.IsFalse(existing.Any(stream => stream.Name.StartsWith("cis.security.", StringComparison.Ordinal)));
        Assert.AreEqual(existing.Count + 3, selected.Count);
        Assert.AreEqual(existing.Sum(stream => stream.CardinalityLimit) + 102,
            selected.Sum(stream => stream.CardinalityLimit));
        Assert.AreEqual(34, selected.Single(stream => stream.Name == "cis.security.authorization.decisions").CardinalityLimit);
        Assert.AreEqual(51, selected.Single(stream => stream.Name == "cis.security.certificate.reloads").CardinalityLimit);
        Assert.AreEqual(17, selected.Single(stream => stream.Name == "cis.security.certificate.remaining").CardinalityLimit);

        foreach (MetricsStreamSnapshot baseline in existing)
        {
            MetricsStreamSnapshot retained = selected.Single(stream => stream.Name == baseline.Name);
            Assert.AreEqual(baseline.CardinalityLimit, retained.CardinalityLimit);
            CollectionAssert.AreEqual(baseline.DimensionKeys.ToArray(), retained.DimensionKeys.ToArray());
            CollectionAssert.AreEqual(baseline.HistogramBounds.ToArray(), retained.HistogramBounds.ToArray());
        }
    }

    [TestMethod]
    [DataRow("cis.security.authorization.decisions", 33)]
    [DataRow("cis.security.certificate.reloads", 50)]
    [DataRow("cis.security.certificate.remaining", 16)]
    public void UndersizedSecurityOverridesFailBeforeProducerActivation(string name, int capacity)
    {
        var options = new MetricsOptions();
        options.Streams.Add(name, new()
        {
            CardinalityLimit = capacity
        });
        MetricsSettings selected = new MetricsSettings(options).ForComposition(false, securityProfileCapacity: 16);

        Assert.ThrowsExactly<ArgumentException>(() => new MetricsStreamCatalog(selected));
    }

    [TestMethod]
    public void HigherProfileCapacityMustFitTheExistingGlobalBudget()
    {
        var settings = new MetricsSettings(new MetricsOptions());
        IReadOnlyList<MetricsStreamSnapshot> catalog = new MetricsStreamCatalog(
            settings.ForComposition(false, securityProfileCapacity: 32)).Capture();
        Assert.AreEqual(66, catalog.Single(stream => stream.Name == "cis.security.authorization.decisions").CardinalityLimit);
        Assert.AreEqual(99, catalog.Single(stream => stream.Name == "cis.security.certificate.reloads").CardinalityLimit);
        Assert.AreEqual(33, catalog.Single(stream => stream.Name == "cis.security.certificate.remaining").CardinalityLimit);

        var limited = new MetricsOptions
        {
            TotalCardinalityLimit = 3_400
        };
        Assert.ThrowsExactly<ArgumentException>(() => new MetricsStreamCatalog(
            new MetricsSettings(limited).ForComposition(false, securityProfileCapacity: 16)));
    }

    [TestMethod]
    public void DisablingCoreCollectionDoesNotSelectSecurityStreams()
    {
        var options = new MetricsOptions
        {
            CollectCoreModules = false
        };
        MetricsSettings settings = new MetricsSettings(options).ForComposition(false, securityProfileCapacity: 16);

        Assert.IsFalse(settings.CollectSecurity);
        Assert.IsFalse(new MetricsStreamCatalog(settings).Capture()
            .Any(stream => stream.Name.StartsWith("cis.security.", StringComparison.Ordinal)));
    }
}
