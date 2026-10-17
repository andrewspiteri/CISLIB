using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.TestSupport;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class TypeManagerFrozenTestsPolicyTests
{
    [TestMethod]
    [DataRow(90, 90, 0, 0, true)]
    [DataRow(89, 89, 0, 0, false)]
    [DataRow(91, 91, 0, 0, false)]
    [DataRow(90, 89, 1, 0, false)]
    [DataRow(90, 89, 0, 1, false)]
    public void FrozenRuntimeRequiresItsOwnNinetyPassingCases(int total, int passed, int failed, int skipped, bool valid)
    {
        var native = new JsonObject { ["total"] = total, ["passed"] = passed, ["failed"] = failed, ["skipped"] = skipped };
        if (valid) { TypeManagerFrozenTests.ValidateNative(native, false); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => TypeManagerFrozenTests.ValidateNative(native, false)); }
    }

    [TestMethod]
    [DataRow("valid")]
    [DataRow("missing")]
    [DataRow("foreign")]
    public void BusinessMethodsKeepExactMembershipAndCounts(string mode)
    {
        string[] names = ["PreparationHandsUsableCatalogToApplicationBeforeItCreatesServices", "FailedPreparationNeverInvokesApplicationFactory", "PreCanceledPreparationNeverInvokesApplicationFactory", "ConcurrentConsumersReusePreparedCatalogWhileApplicationOwnsObjectLifetime", "RunningApplicationKeepsSnapshotWhileNextStartupValidatesChangedInventory", "FailedStartupCanBeRepairedWithIndependentPreparation"];
        int[] counts = [2, 4, 1, 1, 1, 1];
        var rows = new JsonArray();
        for (int i = 0; i < names.Length; i++)
        { for (int j = 0; j < counts[i]; j++) { rows.Add(new JsonObject { ["method"] = "CIS.Tests.BusinessCases.TypeManagerStartupStories." + names[i] }); } }
        var native = new JsonObject { ["total"] = 10, ["passed"] = 10, ["failed"] = 0, ["skipped"] = 0, ["results"] = rows };
        if (mode == "missing") { rows.RemoveAt(0); }
        if (mode == "foreign") { rows[0]!["method"] = "Other.Class.Method"; }
        if (mode == "valid") { TypeManagerFrozenTests.ValidateNative(native, true); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => TypeManagerFrozenTests.ValidateNative(native, true)); }
    }

    [TestMethod]
    [DataRow("valid")]
    [DataRow("pin")]
    [DataRow("producer")]
    [DataRow("source")]
    [DataRow("extra")]
    [DataRow("removed")]
    [DataRow("incomplete")]
    public void CandidateProducerKeepsTheIndependentCoreAndExactModuleSourcePins(string mode)
    {
        using var fixture = new TemporaryDirectory();
        string module = Path.Combine(fixture.Path, "src/CIS.Core/TypeManagement");
        Directory.CreateDirectory(module);
        File.WriteAllText(Path.Combine(module, "Example.cs"), "example source");
        string core = Path.Combine(fixture.Path, "Core.dll");
        File.WriteAllText(core, "opaque policy-only candidate");
        string pin = TypeManagerVerificationInputs.Hash(core);
        var sources = new JsonObject { ["TypeManagement/Example.cs"] = TypeManagerVerificationInputs.Hash(Path.Combine(module, "Example.cs")) };
        var producer = new JsonObject { ["complete"] = true, ["coreSha256"] = pin, ["coreSourceInputs"] = sources };
        if (mode == "pin") { pin = "wrong"; }
        if (mode == "producer") { producer["coreSha256"] = "wrong"; }
        if (mode == "source") { sources["TypeManagement/Example.cs"] = "wrong"; }
        if (mode == "extra") { sources["TypeManagement/Other.cs"] = "wrong"; }
        if (mode == "removed") { sources.Clear(); }
        if (mode == "incomplete") { producer["complete"] = false; }
        if (mode == "valid") { Assert.AreEqual(1, TypeManagerFrozenTests.ValidateProducer(fixture.Path, core, producer, pin).Count); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => TypeManagerFrozenTests.ValidateProducer(fixture.Path, core, producer, pin)); }
    }

    [TestMethod]
    [DataRow("valid")]
    [DataRow("runtime")]
    [DataRow("os")]
    [DataRow("architecture")]
    [DataRow("core")]
    [DataRow("duplicate")]
    [DataRow("missing")]
    public void EachBusinessCaseMustObserveThePinnedRuntimeAndCore(string mode)
    {
        using var fixture = new TemporaryDirectory();
        var identity = new JsonObject { ["runtime"] = ".NET 10.0.12", ["os"] = OperatingSystem.IsWindows() ? "windows" : "linux", ["architecture"] = "X64", ["coreSha256"] = "candidate-pin" };
        if (mode is "runtime" or "os" or "architecture") { identity[mode] = "wrong"; }
        if (mode == "core") { identity["coreSha256"] = "wrong"; }
        string line = identity.ToJsonString();
        var results = new XElement("Results");
        for (int i = 0; i < 10; i++)
        { results.Add(new XElement("UnitTestResult", new XAttribute("testId", i), new XElement("Output", new XElement("StdOut", mode == "missing" ? "" : mode == "duplicate" ? line + "\n" + line : line)))); }
        string path = Path.Combine(fixture.Path, "results.trx");
        new XDocument(new XElement("TestRun", results)).Save(path);
        if (mode == "valid") { Assert.AreEqual(10, TypeManagerFrozenTests.ObserveRuntime(path, "10.0.12", "candidate-pin").Count); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => TypeManagerFrozenTests.ObserveRuntime(path, "10.0.12", "candidate-pin")); }
    }
}
