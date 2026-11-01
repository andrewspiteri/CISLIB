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

    [TestMethod]
    [DataRow("catalog")]
    [DataRow("compiler")]
    [DataRow("reference")]
    [DataRow("import")]
    [DataRow("generated")]
    [DataRow("runtime")]
    public void ProspectiveCompilerInputsRejectChangedActualDependencies(string changed)
    {
        using var fixture = new TemporaryDirectory();
        var prepared = CompilerFixture(fixture.Path);
        CompilerChecks.ValidateCompilerInputFiles(prepared.Language, prepared.Output, prepared.Inputs);
        File.AppendAllText(prepared.Paths[changed], "changed");
        Assert.ThrowsExactly<InvalidDataException>(() => CompilerChecks.ValidateCompilerInputFiles(prepared.Language, prepared.Output, prepared.Inputs));
    }

    [TestMethod]
    [DataRow("compiler")]
    [DataRow("runtime")]
    [DataRow("reference")]
    [DataRow("hostFxr")]
    public void ProspectiveCompilerInputsRejectNewToolOrReferenceMembers(string dependency)
    {
        using var fixture = new TemporaryDirectory();
        var prepared = CompilerFixture(fixture.Path);
        string directory = dependency == "hostFxr" ? prepared.Inputs["hostFxrDirectory"]!.GetValue<string>() : Path.GetDirectoryName(prepared.Paths[dependency])!;
        File.WriteAllText(Path.Combine(directory, "new.dll"), "new dependency");
        Assert.ThrowsExactly<InvalidDataException>(() => CompilerChecks.ValidateCompilerInputFiles(prepared.Language, prepared.Output, prepared.Inputs));
    }

    [TestMethod]
    public void ProspectiveCompilerInputsExcludeUnrelatedTestsButRejectEscapingOwnedPaths()
    {
        using var fixture = new TemporaryDirectory();
        var prepared = CompilerFixture(fixture.Path);
        Directory.CreateDirectory(Path.Combine(prepared.Language, "tests"));
        File.WriteAllText(Path.Combine(prepared.Language, "tests/Unrelated.cs"), "unrelated test change");
        CompilerChecks.ValidateCompilerInputFiles(prepared.Language, prepared.Output, prepared.Inputs);
        JsonObject first = prepared.Inputs["files"]!.AsArray()[0]!.AsObject();
        string outside = Path.Combine(fixture.Path, "outside.dll"); File.WriteAllText(outside, "outside");
        first["path"] = "../outside.dll"; first["external"] = false; first["sha256"] = Hash(outside);
        Assert.ThrowsExactly<InvalidDataException>(() => CompilerChecks.ValidateCompilerInputFiles(prepared.Language, prepared.Output, prepared.Inputs));
    }

    [TestMethod]
    public void CompilerReuseRequiresTheCurrentCommandAndGeneratedIdentity()
    {
        var recorded = new JsonObject
        {
            ["schema"] = "cis.compiler-control-inputs/v1",
            ["identity"] = new JsonObject { ["arguments"] = new JsonArray("/nullable:enable", "/reference:actual.dll"), ["generated"] = "original", ["environment"] = "original" }
        };
        JsonObject current = recorded.DeepClone().AsObject();
        Assert.IsTrue(CompilerChecks.CompilerReuseMatches(recorded, current));
        current["identity"]!["arguments"]!.AsArray()[0] = "/nullable:disable";
        Assert.IsFalse(CompilerChecks.CompilerReuseMatches(recorded, current));
        current = recorded.DeepClone().AsObject(); current["identity"]!["generated"] = "changed";
        Assert.IsFalse(CompilerChecks.CompilerReuseMatches(recorded, current));
        current = recorded.DeepClone().AsObject(); current["identity"]!["environment"] = "changed";
        Assert.IsFalse(CompilerChecks.CompilerReuseMatches(recorded, current));
        current = recorded.DeepClone().AsObject(); current.Remove("schema");
        Assert.IsFalse(CompilerChecks.CompilerReuseMatches(recorded, current), "Legacy summaries have no prospective reuse credit.");
    }

    [TestMethod]
    [DataRow("bytes")]
    [DataRow("file")]
    [DataRow("directory")]
    [DataRow("escape")]
    public void RetainedCompilerPayloadRejectsChangedOrMissingMembership(string changed)
    {
        using var fixture = new TemporaryDirectory();
        string output = Path.Combine(fixture.Path, "group"); Directory.CreateDirectory(output);
        string log = Path.Combine(output, "compiler.log"); File.WriteAllText(log, "actual compiler output");
        var payload = new JsonObject
        {
            ["schema"] = "cis.compiler-control-outputs/v1",
            ["files"] = new JsonArray(new JsonObject { ["path"] = "compiler.log", ["sha256"] = Hash(log) }),
            ["directories"] = new JsonArray()
        };
        CompilerChecks.ValidateCompilerPayload(output, payload);
        switch (changed)
        {
            case "bytes": File.AppendAllText(log, "changed"); break;
            case "file": File.WriteAllText(Path.Combine(output, "extra.log"), "unexpected"); break;
            case "directory": Directory.CreateDirectory(Path.Combine(output, "extra")); break;
            default:
                string outside = Path.Combine(fixture.Path, "outside.log"); File.WriteAllText(outside, "outside");
                payload["files"]!.AsArray()[0]!["path"] = "../outside.log";
                payload["files"]!.AsArray()[0]!["sha256"] = Hash(outside); break;
        }
        Assert.ThrowsExactly<InvalidDataException>(() => CompilerChecks.ValidateCompilerPayload(output, payload));
    }

    private static (string Language, string Output, JsonObject Inputs, Dictionary<string, string> Paths) CompilerFixture(string root)
    {
        string language = Path.Combine(root, "repository/implementations/csharp"), output = Path.Combine(root, "output");
        string source = Path.Combine(language, "verification/contracts/startup");
        string sdk = Path.Combine(root, "sdk"), runtime = Path.Combine(root, "runtime"), hostFxr = Path.Combine(root, "host/fxr"), references = Path.Combine(root, "references");
        foreach (string directory in new[] { source, output, sdk, runtime, hostFxr, references }) { Directory.CreateDirectory(directory); }
        var paths = new Dictionary<string, string>
        {
            ["catalog"] = Path.Combine(source, "surface.json"),
            ["compiler"] = Path.Combine(sdk, "csc.dll"),
            ["reference"] = Path.Combine(references, "reference.dll"),
            ["import"] = Path.Combine(output, "import.props"),
            ["generated"] = Path.Combine(output, "generated.editorconfig"),
            ["runtime"] = Path.Combine(runtime, "host.bin")
        };
        foreach ((string name, string path) in paths) { File.WriteAllText(path, name); }
        var files = new JsonArray(paths.Where(pair => pair.Key != "catalog").Select(pair => (JsonNode)new JsonObject
        {
            ["path"] = pair.Key is "compiler" or "runtime" or "reference" ? pair.Value : Path.GetRelativePath(output, pair.Value),
            ["external"] = pair.Key is "compiler" or "runtime" or "reference",
            ["sha256"] = Hash(pair.Value)
        }).ToArray());
        var inputs = new JsonObject
        {
            ["schema"] = "cis.compiler-control-inputs/v1",
            ["identity"] = new JsonObject
            {
                ["group"] = "startup",
                ["sources"] = new JsonArray(new JsonObject
                {
                    ["path"] = "implementations/csharp/verification/contracts/startup/surface.json",
                    ["sha256"] = Hash(paths["catalog"])
                }),
                ["externalDirectories"] = new JsonArray(new JsonObject { ["path"] = references, ["members"] = new JsonArray("reference.dll") })
            },
            ["files"] = files,
            ["sdkDirectory"] = sdk,
            ["sdkMembers"] = new JsonArray("csc.dll"),
            ["runtimeDirectory"] = runtime,
            ["runtimeMembers"] = new JsonArray("host.bin"),
            ["hostFxrDirectory"] = hostFxr,
            ["hostFxrMembers"] = new JsonArray()
        };
        return (language, output, inputs, paths);
    }

    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
