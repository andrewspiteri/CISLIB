using System.Text.Json.Nodes;
using CIS.TestSupport;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class TypeManagerBaselinePolicyTests
{
    [TestMethod]
    [DataRow("valid")]
    [DataRow("uppercase-hash")]
    [DataRow("changed")]
    [DataRow("missing")]
    [DataRow("escape")]
    [DataRow("wrong-hash")]
    public void BaselineFilesRemainContainedAndHashChecked(string mode)
    {
        using var fixture = new TemporaryDirectory();
        string root = Path.Combine(fixture.Path, "baseline"), path = Path.Combine(root, "reference.dll");
        Directory.CreateDirectory(root);
        File.WriteAllText(path, "opaque baseline-policy fixture");
        string hash = TypeManagerVerificationInputs.Hash(path);
        var entry = new JsonObject { ["path"] = "reference.dll", ["sha256"] = mode == "uppercase-hash" ? hash.ToUpperInvariant() : hash };
        var manifest = new JsonObject { ["files"] = new JsonArray(entry) };
        if (mode == "changed") { File.AppendAllText(path, "changed"); }
        if (mode == "missing") { File.Delete(path); }
        if (mode == "wrong-hash") { entry["sha256"] = "wrong"; }
        if (mode == "escape") { File.WriteAllText(Path.Combine(fixture.Path, "outside.dll"), "outside"); entry["path"] = "../outside.dll"; entry["sha256"] = TypeManagerVerificationInputs.Hash(Path.Combine(fixture.Path, "outside.dll")); }
        File.WriteAllText(Path.Combine(root, "baseline-hashes.json"), manifest.ToJsonString());
        if (mode is "valid" or "uppercase-hash") { TypeManagerBaselineChecks.ValidateBaseline(root); }
        else if (mode == "missing") { Assert.ThrowsExactly<FileNotFoundException>(() => TypeManagerBaselineChecks.ValidateBaseline(root)); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => TypeManagerBaselineChecks.ValidateBaseline(root)); }
    }
}
