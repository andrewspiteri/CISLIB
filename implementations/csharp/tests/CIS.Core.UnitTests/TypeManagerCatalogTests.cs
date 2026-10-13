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
        Assert.Throws<ArgumentNullException>(() => manager.GetDefinedTypes(null!));
        Assert.Throws<ArgumentException>(() => manager.GetDefinedTypes(typeof(object).Assembly));
        Assert.Throws<NotSupportedException>(() => ((IList<Assembly>)manager.Assemblies).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<Type>)manager.Types).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<Type>)manager.GetDefinedTypes(Host)).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<Type>)manager.GetImplementations<IMarker>()).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<Type>)manager.GetImplementations<IEquatable<DateTime>>()).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<string>)manager.PreparationReport.SelectedAssemblyIdentities).Clear());
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
