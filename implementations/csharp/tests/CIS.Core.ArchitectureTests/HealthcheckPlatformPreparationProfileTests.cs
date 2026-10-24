using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class HealthcheckPlatformPreparationProfileTests
{
    private const string Profile = "omit-archived-healthcheck-evidence-v1";
    private const string Archive = "planning/healthcheck/evidence/HC12/retained/archive.tar.gz";

    [TestMethod]
    public async Task DefaultPreparationRetainsEverySelectedArchive()
    {
        Fixture fixture = CreateFixture();
        Assert.AreEqual(0, await HealthcheckPlatformPreparation.RunAsync(["--repo-root", fixture.Source, "--output-root", fixture.Output]));
        JsonObject manifest = Read(fixture.Output, "source-inputs.json"), report = Read(fixture.Output, "preparation.json");
        CollectionAssert.AreEquivalent(fixture.Inputs.Keys.ToArray(), Paths(manifest));
        Assert.IsTrue(File.Exists(Path.Combine(fixture.Output, "source", Archive)));
        Assert.AreEqual("default", (string?)report["selectionPolicy"]?["profile"]);
        Assert.AreEqual(0, Read(fixture.Output, "archive-omissions.json")["files"]!.AsArray().Count);
        AssertInputsAndCopies(fixture, fixture.Inputs.Keys);
    }

    [TestMethod]
    public async Task ExplicitProfileOmitsOnlyExactEvidenceArchiveFilesAndPinsTheirOriginalBytes()
    {
        Fixture fixture = CreateFixture();
        Assert.AreEqual(0, await HealthcheckPlatformPreparation.RunAsync(["--repo-root", fixture.Source, "--output-root", fixture.Output, "--source-profile", Profile]));
        JsonObject manifest = Read(fixture.Output, "source-inputs.json"), report = Read(fixture.Output, "preparation.json"), omissions = Read(fixture.Output, "archive-omissions.json");
        string[] kept = fixture.Inputs.Keys.Where(path => path != Archive).ToArray();
        CollectionAssert.AreEquivalent(kept, Paths(manifest));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Output, "source", Archive)));
        JsonObject omitted = omissions["files"]!.AsArray().OfType<JsonObject>().Single();
        Assert.AreEqual(Archive, (string?)omitted["path"]);
        Assert.AreEqual(Hash(Path.Combine(fixture.Source, Archive)), (string?)omitted["sha256"]);
        Assert.AreEqual(new FileInfo(Path.Combine(fixture.Source, Archive)).Length, (long?)omitted["bytes"]);
        Assert.AreEqual(Profile, (string?)report["selectionPolicy"]?["profile"]);
        Assert.AreEqual((string?)report["selectionPolicySha256"], (string?)omissions["selectionPolicySha256"]);
        Assert.AreEqual(Hash(Path.Combine(fixture.Output, "archive-omissions.json")), (string?)report["archiveOmissionsManifestSha256"]);
        Assert.IsTrue((bool?)report["omittedArchivesVerifiedAgainstSourceBeforeAndAfterCopy"]);
        Assert.IsTrue((bool?)report["sourceVerifiedAfterCopy"]);
        Assert.IsTrue((bool?)report["destinationVerifiedAgainstSourceManifest"]);
        AssertInputsAndCopies(fixture, kept);
    }

    [TestMethod]
    public async Task UnsupportedSourceProfileCannotCreateAPreparationOutput()
    {
        Fixture fixture = CreateFixture();
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => HealthcheckPlatformPreparation.RunAsync(
            ["--repo-root", fixture.Source, "--output-root", fixture.Output, "--source-profile", "arbitrary-trim"]));
        Assert.IsFalse(Directory.Exists(fixture.Output));
        foreach ((string path, string hash) in fixture.Inputs) { Assert.AreEqual(hash, Hash(Path.Combine(fixture.Source, path))); }
    }

    private static Fixture CreateFixture()
    {
        // Every attempt gets its own retained scratch source/output. These
        // synthetic bytes test selection and are never a native Core baseline.
        string root = Path.Combine(Path.GetTempPath(), "cis-preparation-profile-controls", Guid.NewGuid().ToString("N"));
        string source = Path.Combine(root, "source"), output = Path.Combine(root, "output");
        string[] trees = ["implementations/csharp", "contracts/healthcheck", "conformance/healthcheck", "contracts/logging", "conformance/logging", "compatibility/csharp/healthcheck", "planning/healthcheck"];
        string[] names =
        [
            "README.md", "AGENTS.md", ".editorconfig", ".gitattributes", ".gitignore", "eng/test.ps1",
            "compatibility/csharp/publications/ledger.json", "implementations/csharp/CisLib.slnx", Archive,
            "planning/healthcheck/evidence/HC12/retained/archive.tar.gz.txt",
            "planning/healthcheck/evidence/HC12/retained/upper.TAR.GZ",
            "planning/healthcheck/evidence/HC12/retained/directory.tar.gz/keep.cs",
            "planning/healthcheck/evidence-extra/archive.tar.gz",
            "planning/healthcheck/qualification/archive.tar.gz",
            "implementations/csharp/tests/fixtures/archive.tar.gz",
            "implementations/csharp/src/CIS.Core/kept.cs", "implementations/csharp/build/kept.targets",
            "contracts/healthcheck/kept.json", "conformance/healthcheck/kept.json",
            "contracts/logging/kept.json", "conformance/logging/kept.json"
        ];
        foreach (string tree in trees) { Directory.CreateDirectory(Path.Combine(source, tree)); }
        foreach (string relative in names)
        {
            string path = Path.Combine(source, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "synthetic preparation selection control " + relative + "\n", new UTF8Encoding(false));
        }
        return new(source, output, names.ToDictionary(path => path, path => Hash(Path.Combine(source, path)), StringComparer.Ordinal));
    }

    private static void AssertInputsAndCopies(Fixture fixture, IEnumerable<string> copied)
    {
        foreach ((string relative, string sha) in fixture.Inputs) { Assert.AreEqual(sha, Hash(Path.Combine(fixture.Source, relative))); }
        foreach (string relative in copied) { Assert.AreEqual(fixture.Inputs[relative], Hash(Path.Combine(fixture.Output, "source", relative))); }
    }

    private static JsonObject Read(string root, string relative) => JsonNode.Parse(File.ReadAllText(Path.Combine(root, relative)))!.AsObject();
    private static string[] Paths(JsonObject manifest) => manifest["files"]!.AsArray().OfType<JsonObject>().Select(row => (string)row["path"]!).ToArray();
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
    private sealed record Fixture(string Source, string Output, Dictionary<string, string> Inputs);
}
