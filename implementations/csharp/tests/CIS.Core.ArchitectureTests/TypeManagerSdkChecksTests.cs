using System.Reflection;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class TypeManagerSdkChecksTests
{
    [TestMethod]
    [DataRow("valid-four")]
    [DataRow("valid-seven")]
    [DataRow("schema")]
    [DataRow("count")]
    [DataRow("missing")]
    [DataRow("extra")]
    [DataRow("duplicate")]
    [DataRow("order")]
    [DataRow("host")]
    [DataRow("deployed")]
    public void SourceInventoryRequiresExactOrderedDeployedMembership(string mode)
    {
        const string host = "CIS.TypeManager.Fixture.Host";
        string[] names = ["CIS.Core", host, "Microsoft.Extensions.DependencyInjection.Abstractions", "Microsoft.Extensions.Logging.Abstractions"];
        int count = mode == "valid-four" ? 4 : 7;
        if (count == 7) { names = [.. names, "CIS.TypeManager.Fixture.Contracts", "CIS.TypeManager.Fixture.Transitive", "CIS.TypeManager.Fixture.Unused"]; }
        string Identity(string name) => new AssemblyName(name) { Version = new Version(1, 0, 0, 0) }.FullName;
        var entries = new JsonArray(names.Order(StringComparer.Ordinal).Select(name => (JsonNode?)new JsonObject { ["assemblyName"] = Identity(name), ["relativePath"] = name + ".dll" }).ToArray());
        var inventory = new JsonObject { ["schemaVersion"] = 1, ["hostAssembly"] = Identity(host), ["assemblies"] = entries };
        if (mode == "schema") { inventory["schemaVersion"] = 2; }
        if (mode == "count") { count = 4; }
        if (mode == "missing") { entries.RemoveAt(0); }
        if (mode == "extra") { entries.Add(new JsonObject { ["assemblyName"] = Identity("Extra"), ["relativePath"] = "Extra.dll" }); }
        if (mode == "duplicate") { entries[1] = entries[0]!.DeepClone(); }
        if (mode == "order") { JsonNode first = entries[0]!; entries.RemoveAt(0); entries.Add(first); }
        if (mode == "host") { inventory["hostAssembly"] = Identity("Wrong.Host"); }
        if (mode == "deployed") { entries[0]!["relativePath"] = "Wrong.Asset.dll"; }
        string Deployed(string path) => Identity(Path.GetFileNameWithoutExtension(path));
        if (mode.StartsWith("valid", StringComparison.Ordinal)) { TypeManagerSourceInventoryChecks.ValidateInventory(inventory, count, Deployed); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => TypeManagerSourceInventoryChecks.ValidateInventory(inventory, count, Deployed)); }
    }

    [TestMethod]
    [DataRow("isolated")]
    [DataRow("canonical")]
    [DataRow("descendant")]
    [DataRow("missing")]
    public void BoundaryMutationsRequireAnExistingIsolatedTree(string mode)
    {
        using var fixture = new TemporaryDirectory();
        string canonical = Path.Combine(fixture.Path, "canonical");
        string isolated = mode switch { "canonical" => canonical, "descendant" => Path.Combine(canonical, "child"), _ => Path.Combine(fixture.Path, "isolated") };
        Directory.CreateDirectory(canonical);
        if (mode != "missing") { Directory.CreateDirectory(isolated); }
        if (mode == "isolated") { TypeManagerExampleBoundaryChecks.ValidateIsolated(canonical, isolated, []); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => TypeManagerExampleBoundaryChecks.ValidateIsolated(canonical, isolated, [])); }
    }
}
