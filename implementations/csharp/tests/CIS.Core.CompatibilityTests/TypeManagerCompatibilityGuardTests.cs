using System.Text.Json.Nodes;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Tests.CompatibilityTests.TypeManagerCompatibilityInputs;

namespace CIS.Tests.CompatibilityTests;

[TestClass]
public sealed class TypeManagerCompatibilityGuardTests
{
    [TestMethod]
    [DataRow("true", "1")]
    [DataRow("false", "0")]
    [DataRow("2", "2.0")]
    [DataRow("2", "2e0")]
    [DataRow("[1,2]", "[2,1]")]
    [DataRow("{\"a\":1}", "{\"a\":1,\"b\":2}")]
    public void ObservationComparerRejectsChangedKindsAndShape(string left, string right) => Assert.IsFalse(Same(JsonNode.Parse(left), JsonNode.Parse(right)));

    [TestMethod]
    public void ObservationComparerIgnoresOnlyObjectPropertyOrder() => Assert.IsTrue(Same(JsonNode.Parse("{\"a\":1,\"b\":true}"), JsonNode.Parse("{\"b\":true,\"a\":1}")));

    [TestMethod]
    [DataRow("missing", "after")]
    [DataRow("a", "a")]
    [DataRow("a", "b")]
    public void MutationAnchorsRejectMissingNoOpAndAmbiguousChanges(string before, string after) => Assert.ThrowsExactly<InvalidDataException>(() => TypeManagerCompatibilityFixture.Replace("a a", before, after, true));

    [TestMethod]
    [DataRow("hash")]
    [DataRow("incomplete")]
    [DataRow("numeric-complete")]
    [DataRow("source")]
    [DataRow("missing-source")]
    [DataRow("extra-source")]
    public void CandidateProvenanceRejectsCorruption(string corruption)
    {
        using var directory = new TemporaryDirectory();
        string candidate = Path.Combine(directory.Path, "CIS.Core.dll"); File.WriteAllText(candidate, "candidate");
        var sources = new Dictionary<string, string> { ["TypeManagement/TypeManager.cs"] = "source-hash" };
        var producer = new JsonObject { ["complete"] = true, ["coreSha256"] = Hash(candidate), ["coreSourceInputs"] = new JsonObject { ["TypeManagement/TypeManager.cs"] = "source-hash" } };
        Producer(producer, candidate, Hash(candidate), sources);
        switch (corruption)
        {
            case "hash": producer["coreSha256"] = "wrong"; break;
            case "incomplete": producer["complete"] = false; break;
            case "numeric-complete": producer["complete"] = 1; break;
            case "source": producer["coreSourceInputs"]!["TypeManagement/TypeManager.cs"] = "wrong"; break;
            case "missing-source": producer["coreSourceInputs"]!.AsObject().Clear(); break;
            case "extra-source": producer["coreSourceInputs"]!["TypeManagement/Extra.cs"] = "extra"; break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => Producer(producer, candidate, Hash(candidate), sources));
    }

    [TestMethod]
    [DataRow("manifest")]
    [DataRow("content")]
    [DataRow("unlisted")]
    [DataRow("duplicate")]
    [DataRow("escape")]
    public void BaselineRejectsCorruption(string corruption)
    {
        using var directory = new TemporaryDirectory();
        string file = Path.Combine(directory.Path, "frozen.bin"), manifest = Path.Combine(directory.Path, "baseline-hashes.json");
        File.WriteAllText(file, "frozen");
        var item = new JsonObject { ["path"] = "frozen.bin", ["sha256"] = Hash(file) };
        var files = new JsonArray(item);
        void Write() => File.WriteAllText(manifest, new JsonObject { ["files"] = files.DeepClone() }.ToJsonString());
        Write(); string expected = Hash(manifest); Baseline(directory.Path, expected);
        switch (corruption)
        {
            case "manifest": File.AppendAllText(manifest, " "); break;
            case "content": File.WriteAllText(file, "changed"); break;
            case "unlisted": File.WriteAllText(Path.Combine(directory.Path, "extra.bin"), "extra"); break;
            case "duplicate": files.Add(item.DeepClone()); Write(); expected = Hash(manifest); break;
            case "escape": item["path"] = "../outside.bin"; Write(); expected = Hash(manifest); break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => Baseline(directory.Path, expected));
    }

    [TestMethod]
    [DataRow("source")]
    [DataRow("pdb-id")]
    [DataRow("pdb-content")]
    public void ProjectProvenanceRejectsStaleSourcesAndAlteredPdb(string corruption)
    {
        using var directory = new TemporaryDirectory();
        string original = typeof(CIS.TypeManagement.TypeManagerOptions).Assembly.Location;
        string candidate = Path.Combine(directory.Path, "CIS.Core.dll"), pdb = Path.ChangeExtension(candidate, ".pdb");
        File.Copy(original, candidate); File.Copy(Path.ChangeExtension(original, ".pdb"), pdb);
        Dictionary<string, string> sources = ModuleSources(Path.Combine(TypeManagerCompatibilityFixture.Repository(), "implementations/csharp"));
        ProjectProducer(candidate, sources);
        if (corruption == "source") { sources["TypeManagement/TypeManager.cs"] = "stale-source"; }
        else
        {
            byte[] bytes = File.ReadAllBytes(pdb);
            using var provider = System.Reflection.Metadata.MetadataReaderProvider.FromPortablePdbStream(new MemoryStream(bytes));
            int offset = corruption == "pdb-id" ? provider.GetMetadataReader().DebugMetadataHeader!.IdStartOffset : bytes.Length - 1;
            bytes[offset] ^= 1; File.WriteAllBytes(pdb, bytes);
        }
        Assert.ThrowsExactly<InvalidDataException>(() => ProjectProducer(candidate, sources));
    }
}
