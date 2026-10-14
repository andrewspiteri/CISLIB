using System.Globalization;
using System.Reflection;
using System.Text.Json;
using CIS.TypeManagement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
[DoNotParallelize]
public sealed class TypeManagerOrderingTests
{
    [TestMethod]
    [DataRow("en-US")]
    [DataRow("tr-TR")]
    [DataRow("sv-SE")]
    public async Task CultureAndValidInventoryOrderCannotChangePreparedOrLazyResults(string culture)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo previousUi = CultureInfo.CurrentUICulture;
        string directory = Path.Combine(Path.GetTempPath(), "cis-type-manager-ordering", Guid.NewGuid().ToString("N"));
        Assembly host = typeof(TypeManagerOrderingTests).Assembly;
        Assembly core = typeof(TypeManager).Assembly;
        Assembly abstractions = typeof(Microsoft.Extensions.Logging.ILogger).Assembly;
        Assembly[] selected = [host, abstractions, core];
        string manifest = Path.Combine(directory, "host.types.json");
        try
        {
            Directory.CreateDirectory(directory);
            foreach (var assembly in selected) { File.Copy(assembly.Location, Path.Combine(directory, assembly.GetName().Name + ".dll")); }
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            WriteManifest(selected);
            var baseline = await Create(precompute: false);
            var baselineTypes = baseline.Types.ToArray();
            var baselineAssemblies = baseline.Assemblies.ToArray();
            CollectionAssert.AreEqual(new[] { core, host, abstractions }, baselineAssemblies);
            var baselineObjects = baseline.GetImplementations<object>().ToArray();

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
            // These fixture names deliberately distinguish linguistic and ordinal
            // ordering. The expected query order is specified independently.
            Assert.IsLessThan(0, StringComparer.CurrentCulture.Compare(nameof(ÉclairCandidate), nameof(ZebraCandidate)));
            Assert.AreEqual('\u00C9', nameof(ÉclairCandidate)[0]);
            Type[] expected = [typeof(ZebraCandidate), typeof(ÉclairCandidate)];
            foreach (bool reverse in new[] { false, true })
            {
                WriteManifest(reverse ? selected.Reverse().ToArray() : selected);
                foreach (bool precompute in new[] { false, true })
                {
                    var manager = await Create(precompute);
                    CollectionAssert.AreEqual(baselineAssemblies, manager.Assemblies.ToArray());
                    CollectionAssert.AreEqual(baselineTypes, manager.Types.ToArray());
                    AssertOrdinalFixtureOrder(manager.Types);
                    AssertOrdinalFixtureOrder(manager.GetDefinedTypes(host));
                    CollectionAssert.AreEqual(baselineObjects, manager.GetImplementations<object>().ToArray());
                    var candidates = manager.GetImplementations<ICultureMarker>();
                    CollectionAssert.AreEqual(expected, candidates.ToArray());
                    Assert.AreSame(candidates, manager.GetImplementations(typeof(ICultureMarker)));
                    Assert.AreEqual(precompute ? 2 : 0, manager.PreparationReport.PreparedContractCount);
                    foreach (var assembly in selected)
                    {
                        CollectionAssert.AreEqual(baseline.GetDefinedTypes(assembly).ToArray(), manager.GetDefinedTypes(assembly).ToArray());
                    }
                    foreach (var type in expected)
                    {
                        Assert.IsTrue(manager.TryGetType(type.AssemblyQualifiedName!, out var found));
                        Assert.AreSame(type, found);
                    }
                    await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
                    {
                        Assert.AreSame(candidates, manager.GetImplementations<ICultureMarker>());
                        CollectionAssert.AreEqual(baselineObjects, manager.GetImplementations<object>().ToArray());
                    })));
                }
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
            if (Directory.Exists(directory)) { Directory.Delete(directory, recursive: true); }
        }

        void WriteManifest(IEnumerable<Assembly> assemblies) => File.WriteAllText(manifest, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            hostAssembly = host.FullName,
            assemblies = assemblies.Select(assembly => new
            {
                assemblyName = assembly.FullName,
                relativePath = assembly.GetName().Name + ".dll"
            }).ToArray()
        }));

        Task<ITypeManager> Create(bool precompute) => TypeManager.CreateAsync(new TypeManagerOptions(host)
        {
            AssemblyManifestPath = manifest,
            PrecomputedContracts = precompute ? [typeof(ICultureMarker), typeof(object)] : []
        });
    }

    private static void AssertOrdinalFixtureOrder(IReadOnlyList<Type> types)
    {
        var values = types.ToArray();
        int zebraIndex = Array.IndexOf(values, typeof(ZebraCandidate));
        int accentedIndex = Array.IndexOf(values, typeof(ÉclairCandidate));
        Assert.IsTrue(zebraIndex >= 0 && accentedIndex > zebraIndex,
            "Ordinal ordering must place ZebraCandidate before ÉclairCandidate.");
    }

    private interface ICultureMarker { }
    private sealed class ZebraCandidate : ICultureMarker { }
    private sealed class ÉclairCandidate : ICultureMarker { }
}
