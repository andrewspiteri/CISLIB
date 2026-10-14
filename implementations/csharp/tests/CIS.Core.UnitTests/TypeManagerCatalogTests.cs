using System.Reflection;
using System.Text.Json;
using CIS.TypeManagement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class TypeManagerCatalogTests
{
    private string directory = null!;
    private string manifest = null!;
    private static Assembly Host => typeof(TypeManagerCatalogTests).Assembly;

    [TestInitialize]
    public void Initialize()
    {
        directory = Path.Combine(Path.GetTempPath(), "cis-type-manager-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.Copy(Host.Location, Path.Combine(directory, "Host.dll"));
        manifest = Path.Combine(directory, "host.types.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            hostAssembly = Host.FullName,
            assemblies = new[] { new { assemblyName = Host.FullName, relativePath = "Host.dll" } }
        }));
        DiscoveryCounters.Constructors = 0;
        DiscoveryCounters.Attributes = 0;
        DiscoveryCounters.Initializers = 0;
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(directory, recursive: true);

    [TestMethod]
    public async Task FullCatalogKeepsAllDefinedKindsWithoutActivatingApplicationCode()
    {
        var manager = await Create();
        foreach (var type in new[] { typeof(IMarker), typeof(RecordStruct), typeof(MarkerBase), typeof(GenericCandidate<>), typeof(PrivateCandidate), typeof(StaticCandidate) })
        {
            Assert.Contains(type, manager.Types);
            Assert.IsTrue(manager.TryGetType(type.AssemblyQualifiedName!, out var observed));
            Assert.AreSame(type, observed);
        }
        Assert.AreSame(Host, manager.Assemblies.Single());
        CollectionAssert.AreEqual(manager.Types.ToArray(), manager.GetDefinedTypes(Host).ToArray());
        Assert.AreEqual(0, DiscoveryCounters.Constructors);
        Assert.AreEqual(0, DiscoveryCounters.Attributes);
        Assert.AreEqual(0, DiscoveryCounters.Initializers);
    }

    [TestMethod]
    public async Task MatchingIncludesInheritedPrivateClassesAndClrVarianceButExcludesOpenAndAbstractTypes()
    {
        var manager = await Create();
        CollectionAssert.AreEqual(new[] { typeof(PrivateCandidate) }, manager.GetImplementations<IMarker>().ToArray());
        CollectionAssert.AreEqual(new[] { typeof(PrivateCandidate) }, manager.GetImplementations<MarkerBase>().ToArray());
        CollectionAssert.AreEqual(new[] { typeof(PrivateCandidate) }, manager.GetImplementations<PrivateCandidate>().ToArray());
        CollectionAssert.AreEqual(new[] { typeof(StringProducer) }, manager.GetImplementations<IProducer<object>>().ToArray());
        CollectionAssert.AreEqual(new[] { typeof(ObjectConsumer) }, manager.GetImplementations<IConsumer<string>>().ToArray());
        Assert.HasCount(0, manager.GetImplementations<IEquatable<DateTime>>());
        Assert.IsFalse(manager.Assemblies.Contains(typeof(object).Assembly));
    }

    [TestMethod]
    public async Task ExactLookupDoesNotNormalizeOrLoadAndCollectionsRejectMutation()
    {
        var manager = await Create();
        string identity = typeof(PrivateCandidate).AssemblyQualifiedName!;
        int assemblyCount = AppDomain.CurrentDomain.GetAssemblies().Length;
        foreach (string miss in new[] { " " + identity, identity + " ", identity.ToUpperInvariant(), typeof(PrivateCandidate).FullName!, "Unknown.Type, Missing.Assembly" })
        {
            Assert.IsFalse(manager.TryGetType(miss, out var type));
            Assert.IsNull(type);
        }
        Assert.AreEqual(assemblyCount, AppDomain.CurrentDomain.GetAssemblies().Length);
        Assert.Throws<ArgumentNullException>(() => manager.TryGetType(null!, out _));
        Assert.Throws<ArgumentException>(() => manager.TryGetType(" ", out _));
        Assert.AreEqual("assembly", Assert.ThrowsExactly<ArgumentNullException>(() => manager.GetDefinedTypes(null!)).ParamName);
        Assert.AreEqual("contractType", Assert.ThrowsExactly<ArgumentNullException>(() => manager.GetImplementations(null!)).ParamName);
        Assert.Throws<ArgumentException>(() => manager.GetDefinedTypes(typeof(object).Assembly));
        AssertProtected(manager.Assemblies);
        AssertProtected(manager.Types);
        AssertProtected(manager.GetDefinedTypes(Host));
        AssertProtected(manager.GetImplementations<IMarker>());
        AssertProtected(manager.GetImplementations<IEquatable<DateTime>>());
        AssertProtected(manager.PreparationReport.SelectedAssemblyIdentities);
        AssertProtected(manager.PreparationReport.LoadedAssemblyIdentities);
        AssertProtected(manager.PreparationReport.ReusedAssemblyIdentities);
    }

    [TestMethod]
    public async Task PreparedContractsUseCapturedInputAndConcurrentQueriesStayStableWithoutFiles()
    {
        var requested = new List<Type> { typeof(IMarker), typeof(IMarker) };
        var pending = TypeManager.CreateAsync(new TypeManagerOptions(Host) { AssemblyManifestPath = manifest, PrecomputedContracts = requested });
        requested.Clear();
        requested.Add(typeof(int));
        var manager = await pending;
        Assert.AreSame(manager, await pending);
        Assert.AreEqual(1, manager.PreparationReport.PreparedContractCount);
        Assert.AreEqual(1, manager.PreparationReport.SelectedAssemblyCount);
        Assert.AreEqual(manager.Types.Count, manager.PreparationReport.TypeCount);
        Assert.HasCount(1, manager.PreparationReport.ReusedAssemblyIdentities);
        Assert.HasCount(0, manager.PreparationReport.LoadedAssemblyIdentities);
        Assert.IsGreaterThanOrEqualTo(TimeSpan.Zero, manager.PreparationReport.Elapsed);
        File.Delete(manifest);
        File.Delete(Path.Combine(directory, "Host.dll"));
        await Task.WhenAll(Enumerable.Range(0, 128).Select(_ => Task.Run(() =>
        {
            Assert.AreSame(typeof(PrivateCandidate), manager.GetImplementations<IMarker>().Single());
            Assert.HasCount(0, manager.GetImplementations<IEquatable<DateTime>>());
            Assert.AreSame(typeof(StringProducer), manager.GetImplementations<IProducer<object>>().Single());
        })));
    }

    [TestMethod]
    public async Task IndependentManagersAndEveryResultUseStableOrdinalOrdering()
    {
        var first = await Create();
        var second = await Create();
        Assert.AreNotSame(first, second);
        CollectionAssert.AreEqual(first.Types.ToArray(), second.Types.ToArray());
        AssertOrder(first.Types);
        AssertOrder(first.GetDefinedTypes(Host));
        AssertOrder(first.GetImplementations<object>());
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task GenericAndNongenericQueriesAgreeForPreparedAndLazyContracts(bool precompute)
    {
        Type[] contracts = [typeof(IMarker), typeof(MarkerBase), typeof(PrivateCandidate),
            typeof(IProducer<object>), typeof(IConsumer<string>), typeof(IEquatable<DateTime>)];
        var manager = await TypeManager.CreateAsync(new TypeManagerOptions(Host)
        {
            AssemblyManifestPath = manifest,
            PrecomputedContracts = precompute ? contracts : []
        });
        AssertEquivalent<IMarker>(manager);
        AssertEquivalent<MarkerBase>(manager);
        AssertEquivalent<PrivateCandidate>(manager);
        AssertEquivalent<IProducer<object>>(manager);
        AssertEquivalent<IConsumer<string>>(manager);
        AssertEquivalent<IEquatable<DateTime>>(manager);
        Assert.AreEqual(precompute ? contracts.Length : 0, manager.PreparationReport.PreparedContractCount);
    }

    [TestMethod]
    public async Task ConcurrentAwaitersShareOneManagerAndCancellationAfterSuccessCannotRevokeIt()
    {
        using var cancellation = new CancellationTokenSource();
        var preparation = TypeManager.CreateAsync(new TypeManagerOptions(Host) { AssemblyManifestPath = manifest }, cancellation.Token);
        var observers = Enumerable.Range(0, 16).Select(_ => Task.Run(async () => await preparation)).ToArray();
        var managers = await Task.WhenAll(observers);
        var manager = managers[0];
        foreach (var observed in managers) { Assert.AreSame(manager, observed); }
        var types = manager.Types.ToArray();
        var report = manager.PreparationReport;
        cancellation.Cancel();
        Assert.IsTrue(preparation.IsCompletedSuccessfully);
        Assert.AreSame(manager, await preparation);
        CollectionAssert.AreEqual(types, manager.Types.ToArray());
        Assert.AreSame(report, manager.PreparationReport);
        Assert.AreSame(typeof(PrivateCandidate), manager.GetImplementations<IMarker>().Single());
        // Reading the inventory must not leave an application-owned file handle.
        using var exclusive = new FileStream(manifest, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.IsTrue(exclusive.CanWrite);
    }

    [TestMethod]
    public async Task ConcurrentAwaitersShareOneFailureWhileSeparateCreationsRemainIndependent()
    {
        File.WriteAllText(manifest, "{");
        var preparation = Create();
        var observers = Enumerable.Range(0, 16).Select(_ => Task.Run(async () =>
            await Assert.ThrowsExactlyAsync<TypeManagerInitializationException>(() => preparation))).ToArray();
        var errors = await Task.WhenAll(observers);
        foreach (var error in errors) { Assert.AreSame(errors[0], error); }
        Assert.IsTrue(preparation.IsFaulted);
        Assert.AreEqual(TypeManagerFailureKind.InvalidInventory, errors[0].FailureKind);
        var other = Create();
        Assert.AreNotSame(preparation, other);
        var otherError = await Assert.ThrowsExactlyAsync<TypeManagerInitializationException>(() => other);
        Assert.AreNotSame(errors[0], otherError);
        using var exclusive = new FileStream(manifest, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.IsTrue(exclusive.CanWrite);
    }

    private static void AssertEquivalent<T>(ITypeManager manager) => CollectionAssert.AreEqual(
        manager.GetImplementations(typeof(T)).ToArray(), manager.GetImplementations<T>().ToArray());

    [TestMethod]
    public void CollectionProtectionCheckRejectsMutableFixedSizeArrays()
    {
        Assert.ThrowsExactly<AssertFailedException>(() => AssertProtected<int>(new[] { 1 }));
    }

    private static void AssertProtected<T>(IReadOnlyList<T> values)
    {
        var list = (IList<T>)values;
        Assert.Throws<NotSupportedException>(() => list.Clear());
        if (list.Count > 0) { Assert.Throws<NotSupportedException>(() => list[0] = values[0]); }
    }

    private Task<ITypeManager> Create() => TypeManager.CreateAsync(new TypeManagerOptions(Host) { AssemblyManifestPath = manifest });
    private static void AssertOrder(IReadOnlyList<Type> types) => CollectionAssert.AreEqual(
        types.OrderBy(type => type.AssemblyQualifiedName, StringComparer.Ordinal).ToArray(), types.ToArray());

    private interface IMarker;
    private interface IProducer<out T>;
    private interface IConsumer<in T>;
    private abstract class MarkerBase : IMarker;
    private sealed class StringProducer : IProducer<string>;
    private sealed class ObjectConsumer : IConsumer<object>;
    private sealed class GenericCandidate<T> : IMarker;
    private readonly record struct RecordStruct(int Value) : IMarker;
    private static class StaticCandidate;
    [Poison]
    private sealed class PrivateCandidate : MarkerBase
    {
        static PrivateCandidate() => DiscoveryCounters.Initializers++;
        private PrivateCandidate()
        {
            DiscoveryCounters.Constructors++;
            throw new InvalidOperationException("Do not construct discovered instances.");
        }
    }
    [AttributeUsage(AttributeTargets.Class)]
    private sealed class PoisonAttribute : Attribute
    {
        public PoisonAttribute()
        {
            DiscoveryCounters.Attributes++;
            throw new InvalidOperationException("Do not construct application attributes.");
        }
    }
    private static class DiscoveryCounters
    {
        internal static int Constructors;
        internal static int Attributes;
        internal static int Initializers;
    }
}
