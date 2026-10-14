using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using CIS.TypeManagement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.BusinessCases;

[TestClass]
[DoNotParallelize]
public sealed class TypeManagerStartupStories
{
    private string directory = null!;
    private string manifest = null!;
    private static Assembly Host => typeof(TypeManagerStartupStories).Assembly;

    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void Initialize()
    {
        directory = Path.Combine(Path.GetTempPath(), "cis-type-manager-stories", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        // A selected file may identify an assembly already loaded from another path.
        File.Copy(Host.Location, Path.Combine(directory, "Host.dll"));
        manifest = Path.Combine(directory, "host.types.json");
        WriteInventory();
        Activation.Count = 0;
        TestContext.WriteLine(JsonSerializer.Serialize(new
        {
            runtime = RuntimeInformation.FrameworkDescription,
            os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "unsupported",
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            coreSha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(TypeManager).Assembly.Location)))
        }));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PreparationHandsUsableCatalogToApplicationBeforeItCreatesServices(bool precompute)
    {
        var events = new List<string>();
        var options = Options(precompute);
        var application = await StartAsync(options, manager =>
        {
            events.Add("application factory");
            Assert.AreEqual(0, Activation.Count, "Discovery must leave application construction to its owner.");
            var matches = manager.GetImplementations<ISettings>();
            CollectionAssert.AreEqual(new[] { typeof(MailSettings) }, matches.ToArray());
            Assert.AreSame(matches, manager.GetImplementations(typeof(ISettings)));
            Assert.IsTrue(manager.TryGetType(matches[0].AssemblyQualifiedName!, out var found));
            Assert.AreSame(typeof(MailSettings), found);
            var settings = MailSettings.Create("smtp.example.test");
            events.Add("application activation");
            return new Application(manager, settings);
        }, events);

        CollectionAssert.AreEqual(new[] { "preparing", "prepared", "application factory", "application activation" }, events);
        Assert.AreEqual(1, Activation.Count);
        Assert.AreEqual("smtp.example.test", application.Settings.Endpoint);
        Assert.AreEqual(precompute ? 1 : 0, application.Manager.PreparationReport.PreparedContractCount);
        Assert.AreEqual(1, application.Manager.PreparationReport.SelectedAssemblyCount);
        Assert.AreEqual(0, application.Manager.PreparationReport.LoadedAssemblyIdentities.Count);
        CollectionAssert.AreEqual(new[] { Host.FullName }, application.Manager.PreparationReport.ReusedAssemblyIdentities.ToArray());
        Assert.AreEqual(application.Manager.Types.Count, application.Manager.PreparationReport.TypeCount);
        CollectionAssert.AreEquivalent(application.Manager.Types.ToArray(), application.Manager.GetDefinedTypes(Host).ToArray());
    }

    [TestMethod]
    [DataRow("invalid inventory", TypeManagerFailureKind.InvalidInventory)]
    [DataRow("missing inventory", TypeManagerFailureKind.InvalidInventory)]
    [DataRow("missing assembly", TypeManagerFailureKind.InvalidInventory)]
    [DataRow("corrupt assembly", TypeManagerFailureKind.AssemblyLoadFailure)]
    public async Task FailedPreparationNeverInvokesApplicationFactory(string damage, TypeManagerFailureKind expectedKind)
    {
        switch (damage)
        {
            case "invalid inventory": File.WriteAllText(manifest, "{"); break;
            case "missing inventory": File.Delete(manifest); break;
            case "corrupt assembly": File.WriteAllText(Path.Combine(directory, "Host.dll"), "not a managed assembly"); break;
            case "missing assembly": File.Delete(Path.Combine(directory, "Host.dll")); break;
            default: throw new ArgumentException("Unknown deployment damage.", nameof(damage));
        }
        var events = new List<string>();
        int factoryCalls = 0;
        var error = await Assert.ThrowsExactlyAsync<TypeManagerInitializationException>(() => StartAsync(Options(), _ => ++factoryCalls, events));

        Assert.AreEqual(expectedKind, error.FailureKind);
        Assert.AreEqual(manifest, error.ManifestPath);
        Assert.IsNotNull(error.InnerException);
        if (damage is "missing assembly" or "corrupt assembly")
        {
            Assert.AreEqual(Path.Combine(directory, "Host.dll"), error.AssetPath);
            Assert.AreEqual(Host.FullName, error.AssemblyIdentity);
        }
        Assert.AreEqual(0, factoryCalls);
        Assert.AreEqual(0, Activation.Count);
        CollectionAssert.AreEqual(new[] { "preparing" }, events);
    }

    [TestMethod]
    public async Task PreCanceledPreparationNeverInvokesApplicationFactory()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var events = new List<string>();
        int factoryCalls = 0;
        var startup = StartAsync(Options(), _ => ++factoryCalls, events, cancellation.Token);
        await Assert.ThrowsAsync<OperationCanceledException>(() => startup);
        Assert.IsTrue(startup.IsCanceled);
        Assert.AreEqual(0, factoryCalls);
        Assert.AreEqual(0, Activation.Count);
        CollectionAssert.AreEqual(new[] { "preparing" }, events);
    }

    [TestMethod]
    public async Task ConcurrentConsumersReusePreparedCatalogWhileApplicationOwnsObjectLifetime()
    {
        var manager = await TypeManager.CreateAsync(Options(precompute: true));
        var prepared = manager.GetImplementations<ISettings>();
        var report = manager.PreparationReport;
        var consumers = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            Assert.AreSame(prepared, manager.GetImplementations<ISettings>());
            Assert.AreSame(report, manager.PreparationReport);
            return new Application(manager, MailSettings.Create("request.example.test"));
        })));
        Assert.AreEqual(16, Activation.Count);
        Assert.AreEqual(16, consumers.Select(item => item.Settings).Distinct().Count());
        foreach (var consumer in consumers) { Assert.AreSame(manager, consumer.Manager); }
        Assert.Throws<NotSupportedException>(() => ((IList<Type>)prepared)[0] = typeof(SettingsBase));
        CollectionAssert.AreEqual(new[] { typeof(MailSettings) }, manager.GetImplementations<ISettings>().ToArray());
        Assert.AreEqual(1, report.PreparedContractCount);
    }

    [TestMethod]
    public async Task RunningApplicationKeepsSnapshotWhileNextStartupValidatesChangedInventory()
    {
        var manager = await TypeManager.CreateAsync(Options());
        var types = manager.Types;
        var report = manager.PreparationReport;
        File.WriteAllText(manifest, "{");
        File.Delete(Path.Combine(directory, "Host.dll"));

        // The first query is deliberately after the deployment files change.
        CollectionAssert.AreEqual(new[] { typeof(MailSettings) }, manager.GetImplementations<ISettings>().ToArray());
        Assert.AreSame(types, manager.Types);
        Assert.AreSame(report, manager.PreparationReport);
        Assert.IsTrue(manager.TryGetType(typeof(MailSettings).AssemblyQualifiedName!, out var found));
        Assert.AreSame(typeof(MailSettings), found);
        var error = await Assert.ThrowsExactlyAsync<TypeManagerInitializationException>(() => TypeManager.CreateAsync(Options()));
        Assert.AreEqual(TypeManagerFailureKind.InvalidInventory, error.FailureKind);
        Assert.AreEqual(0, Activation.Count);
    }

    [TestMethod]
    public async Task FailedStartupCanBeRepairedWithIndependentPreparation()
    {
        File.WriteAllText(manifest, "{");
        var failed = TypeManager.CreateAsync(Options());
        await Assert.ThrowsExactlyAsync<TypeManagerInitializationException>(() => failed);
        WriteInventory();
        var events = new List<string>();
        var repaired = await StartAsync(Options(), manager => new Application(manager, MailSettings.Create("repaired.example.test")), events);

        Assert.IsTrue(failed.IsFaulted, "A new preparation must not mutate the original failed task.");
        CollectionAssert.AreEqual(new[] { "preparing", "prepared" }, events);
        Assert.AreEqual("repaired.example.test", repaired.Settings.Endpoint);
        Assert.AreEqual(1, Activation.Count);
        CollectionAssert.AreEqual(new[] { typeof(MailSettings) }, repaired.Manager.GetImplementations<ISettings>().ToArray());
        using var exclusive = new FileStream(manifest, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.IsTrue(exclusive.CanWrite);
    }

    // This is the application's ordering policy, not an extra Type Manager API.
    private static async Task<T> StartAsync<T>(TypeManagerOptions options, Func<ITypeManager, T> factory,
        List<string> events, CancellationToken cancellationToken = default)
    {
        events.Add("preparing");
        var manager = await TypeManager.CreateAsync(options, cancellationToken);
        events.Add("prepared");
        return factory(manager);
    }

    private TypeManagerOptions Options(bool precompute = false) => new(Host)
    {
        AssemblyManifestPath = manifest,
        PrecomputedContracts = precompute ? [typeof(ISettings)] : []
    };

    private void WriteInventory() => File.WriteAllText(manifest, JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        hostAssembly = Host.FullName,
        assemblies = new[] { new { assemblyName = Host.FullName, relativePath = "Host.dll" } }
    }));

    private sealed record Application(ITypeManager Manager, MailSettings Settings);
    private interface ISettings;
    private abstract class SettingsBase : ISettings;
    private sealed class OpenSettings<T> : SettingsBase;
    private readonly record struct ValueSettings : ISettings;
    private sealed class MailSettings : SettingsBase
    {
        private MailSettings(string endpoint)
        {
            Endpoint = endpoint;
            Interlocked.Increment(ref Activation.Count);
        }
        public string Endpoint { get; }
        internal static MailSettings Create(string endpoint) => new(endpoint);
    }
    private static class Activation
    {
        internal static int Count;
    }
}
