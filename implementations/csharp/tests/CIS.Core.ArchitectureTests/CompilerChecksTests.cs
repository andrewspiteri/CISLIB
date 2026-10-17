using System.Security.Cryptography;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class CompilerChecksTests
{
    [TestMethod]
    [DataRow(0, "", null, true)]
    [DataRow(1, "error MSB1009: missing project", null, false)]
    [DataRow(1, "Consumer.cs(3,4): error CS1503: wrong argument [Consumer.csproj]", "CS1503", true)]
    [DataRow(1, "error CS1503: wrong argument", "CS1503", true)]
    [DataRow(1, "build output\nConsumer.cs(3,4): error CS1503: wrong argument\n", "CS1503", true)]
    [DataRow(0, "error CS1503: stale text", "CS1503", false)]
    [DataRow(1, "Consumer.cs: warning CS1503: warning only", "CS1503", false)]
    [DataRow(1, "error MSB1009: missing CS1503.cs", "CS1503", false)]
    [DataRow(1, "Consumer.cs: error CS15030: wrong code", "CS1503", false)]
    [DataRow(1, "CS1503.cs(1,1): error CS1002: syntax", "CS1503", false)]
    [DataRow(1, "Consumer.cs: error CS1503Suffix: wrong code", "CS1503", false)]
    [DataRow(1, "Consumer.cs: error CS1002: syntax", "CS1503", false)]
    [DataRow(1, "Consumer.cs: error CS1503", "CS1503", false)]
    public void ExpectedOutcomeRequiresTheActualCompilerError(int exitCode, string output, string? diagnostic, bool expected) =>
        Assert.AreEqual(expected, CompilerChecks.Matches(exitCode, output, diagnostic));

    [TestMethod]
    [DataRow("before\r\nanchor\r\nafter", "anchor", "changed", "before\nchanged\nafter")]
    [DataRow("before\nanchor\nafter", "anchor", "", "before\n\nafter")]
    public void ExactMutationWorksAcrossCheckoutLineEndings(string original, string find, string replace, string expected) =>
        Assert.AreEqual(expected, CompilerChecks.Mutate(original, find, replace));

    [TestMethod]
    [DataRow("no match", "anchor")]
    [DataRow("anchor then anchor", "anchor")]
    [DataRow("anything", "")]
    public void MissingOrAmbiguousMutationFails(string original, string find) =>
        Assert.ThrowsExactly<InvalidDataException>(() => CompilerChecks.Mutate(original, find, "changed"));

    [TestMethod]
    public void StartupArtifactMustStayInsideItsRootAndMatchItsHash()
    {
        using var fixture = new TemporaryDirectory();
        string root = Path.Combine(fixture.Path, "startup");
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "reference.dll");
        File.WriteAllText(path, "retained reference");
        var reference = new JsonObject { ["path"] = "reference.dll", ["sha256"] = Hash(path) };
        Assert.AreEqual(path, CompilerChecks.VerifiedReference(root, reference));
        File.AppendAllText(path, "changed");
        Assert.ThrowsExactly<InvalidDataException>(() => CompilerChecks.VerifiedReference(root, reference));
        File.WriteAllText(Path.Combine(fixture.Path, "outside.dll"), "outside");
        reference["path"] = "../outside.dll";
        reference["sha256"] = Hash(Path.Combine(fixture.Path, "outside.dll"));
        Assert.ThrowsExactly<InvalidDataException>(() => CompilerChecks.VerifiedReference(root, reference));
    }

    [TestMethod]
    public async Task FailedPrerequisiteLeavesAFailedReport()
    {
        using var fixture = new TemporaryDirectory();
        Directory.CreateDirectory(Path.Combine(fixture.Path, "eng"));
        File.WriteAllText(Path.Combine(fixture.Path, "eng/toolchain.json"), """{"sdk":"unavailable-test-sdk"}""");
        string output = Path.Combine(fixture.Path, "output");
        Assert.AreEqual(1, await CompilerChecks.RunAsync(["log-values", fixture.Path, output]));
        JsonNode report = JsonNode.Parse(File.ReadAllText(Path.Combine(output, "summary.json")))!;
        Assert.AreEqual("Failed", report["status"]!.GetValue<string>());
        StringAssert.Contains(report["detail"]!.GetValue<string>(), "Pinned SDK mismatch");
        Assert.AreEqual(0, report["controls"]!.AsArray().Count);
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
