using System.Diagnostics.Metrics;
using System.Reflection;
using System.Text.Json;
using CIS.Metrics;
using CIS.TestSupport;
using CIS.TypeManagement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Metrics;

[TestClass]
public sealed class BootstrapModuleTypeManagerTests
{
    [TestMethod]
    public async Task AnActualPreparedReportIsObservableInTwoHostsWithoutReplayingDiscoveryOrClaimingEitherRuntime()
    {
        using var directory = new TemporaryDirectory();
        Assembly host = typeof(BootstrapModuleTypeManagerTests).Assembly;
        File.Copy(host.Location, Path.Combine(directory.Path, "Host.dll"));
        string manifest = Path.Combine(directory.Path, "host.types.json");
        await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            hostAssembly = host.FullName,
            assemblies = new[] { new { assemblyName = host.FullName, relativePath = "Host.dll" } }
        }));
        ITypeManager manager = await TypeManager.CreateAsync(new TypeManagerOptions(host) { AssemblyManifestPath = manifest });
        TypeManagerPreparationReport report = manager.PreparationReport;
        int types = manager.Types.Count;
        using var firstMeter = new Meter("CIS.Metrics");
        using var secondMeter = new Meter("CIS.Metrics");
        using var first = new MetricsTestListener(firstMeter);
        using var second = new MetricsTestListener(secondMeter);
        await using var firstRuntime = new MetricsRuntime(firstMeter);
        await using var secondRuntime = new MetricsRuntime(secondMeter);
        report.AttachMetrics(firstRuntime);
        report.AttachMetrics(firstRuntime);
        report.AttachMetrics(secondRuntime);
        first.Collect();
        second.Collect();
        foreach (MetricsTestListener listener in new[] { first, second })
        {
            Assert.AreEqual(1d, listener.Points("cis.typemanager.assemblies").Single().Value);
            Assert.AreEqual((double)types, listener.Points("cis.typemanager.types").Single().Value);
            Assert.AreEqual(report.Elapsed.TotalSeconds, listener.Points("cis.typemanager.initialization.duration").Single().Value);
            Assert.AreEqual(3, listener.Instruments.Count);
            Assert.IsTrue(listener.Instruments.All(instrument => instrument is ObservableGauge<double>));
        }
        await firstRuntime.StopAsync();
        second.Collect();
        Assert.AreEqual(2, second.Points("cis.typemanager.types").Length);
        Assert.AreSame(report, manager.PreparationReport);
        Assert.AreEqual(types, manager.Types.Count);
        Assert.AreSame(host, manager.Assemblies.Single());
    }

    [TestMethod]
    public async Task FailedPreparationKeepsItsRealFailureAndDoesNotFabricateAnAvailableReport()
    {
        using var meter = new Meter("CIS.Metrics");
        using var listener = new MetricsTestListener(meter);
        await using var runtime = new MetricsRuntime(meter);
        string missing = Path.Combine(Path.GetTempPath(), "cis-metrics-missing-" + Guid.NewGuid().ToString("N"), "host.types.json");
        TypeManagerInitializationException failure = await Assert.ThrowsExactlyAsync<TypeManagerInitializationException>(
            () => TypeManager.CreateAsync(new TypeManagerOptions(typeof(BootstrapModuleTypeManagerTests).Assembly)
            { AssemblyManifestPath = missing }));
        Assert.AreEqual(TypeManagerFailureKind.InvalidInventory, failure.FailureKind);
        Assert.AreEqual(missing, failure.ManifestPath);
        listener.Collect();
        Assert.IsEmpty(listener.Instruments);
        Assert.IsEmpty(runtime.Definitions);
    }
}
