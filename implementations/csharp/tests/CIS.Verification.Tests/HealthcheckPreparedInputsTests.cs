using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Tests.Verification;

[TestClass]
public sealed class HealthcheckPreparedInputsTests
{
    [TestMethod]
    [DataRow("Preparation")]
    [DataRow("Developer")]
    public async Task VerifiedPreparationProjectsExistingPinsWithoutExecutingAnotherBuild(string profile)
    {
        using var fixture = new Fixture(profile);
        Assert.AreEqual(0, await fixture.Project());
        JsonObject producer = Read(fixture.Producer);
        Assert.AreEqual(Hash(fixture.Package), (string?)producer["packageSha256"]);
        Assert.AreEqual(Hash(fixture.Core), (string?)producer["coreSha256"]);
        Assert.AreEqual(false, (bool?)producer["testsExecuted"]);
        Assert.AreEqual(false, (bool?)producer["releaseQualified"]);
        CollectionAssert.AreEqual(new[] { "Directory.Build.props", "Directory.Packages.props", "global.json", "src/CIS.Core/CIS.Core.csproj", "src/CIS.Core/Healthcheck/Fixture.cs" },
            producer["sourceInputs"]!.AsArray().Select(pin => (string)pin!["path"]!).ToArray());
        Assert.AreEqual(Hash(fixture.Summary), (string?)producer["preparedSummary"]!["sha256"]);
        Assert.AreEqual(Hash(fixture.Manifest), (string?)producer["sourceManifest"]!["sha256"]);
        Assert.AreEqual(Hash(fixture.Inspection), (string?)producer["architectureInspection"]!["sha256"]);
        CollectionAssert.AreEqual(new[] { "producer.json" }, Directory.EnumerateFiles(fixture.Output).Select(Path.GetFileName).ToArray());
    }

    [TestMethod]
    [DataRow("failed-stage")]
    [DataRow("wrong-scope")]
    [DataRow("manifest-pin")]
    [DataRow("source-change")]
    [DataRow("source-added")]
    [DataRow("source-deleted")]
    [DataRow("omitted-maintained-input")]
    [DataRow("inspection-pin")]
    [DataRow("inspection-package")]
    [DataRow("candidate-package-pin")]
    [DataRow("candidate-package-escape")]
    [DataRow("current-core")]
    [DataRow("manifest-escape")]
    public async Task ChangedOrIncompletePreparationCannotBecomeConsumerProvenance(string change)
    {
        using var fixture = new Fixture("Preparation");
        JsonObject summary = Read(fixture.Summary);
        switch (change)
        {
            case "failed-stage": summary["stages"]![0]!["status"] = "Failed"; break;
            case "wrong-scope": summary["source"]!["inputScope"] = "unscoped-history"; break;
            case "manifest-pin": File.AppendAllText(fixture.Manifest, "\n"); break;
            case "source-change": File.AppendAllText(fixture.Source, " dirty"); break;
            case "source-added": Write(Path.Combine(Path.GetDirectoryName(fixture.Source)!, "Added.cs"), "new source"); break;
            case "source-deleted": File.Delete(fixture.Source); break;
            case "omitted-maintained-input":
                JsonArray sources = JsonNode.Parse(File.ReadAllText(fixture.Manifest))!.AsArray();
                sources.RemoveAt(0); Write(fixture.Manifest, sources.ToJsonString());
                summary["source"]!["inputInventory"]!["sha256"] = Hash(fixture.Manifest);
                break;
            case "inspection-pin": File.AppendAllText(fixture.Inspection, "\n"); break;
            case "inspection-package":
                JsonObject inspection = Read(fixture.Inspection); inspection["package"]!["sha256"] = new string('0', 64);
                WriteJson(fixture.Inspection, inspection); summary["architectureInspection"]!["sha256"] = Hash(fixture.Inspection);
                break;
            case "candidate-package-pin": File.AppendAllText(fixture.Package, " changed package"); break;
            case "candidate-package-escape": summary["candidatePackage"]!["path"] = "../outside.nupkg"; break;
            case "current-core": File.AppendAllText(fixture.Core, " changed output"); break;
            case "manifest-escape": summary["source"]!["inputInventory"]!["path"] = "../outside.json"; break;
            default: throw new ArgumentException("Unknown fixture mutation.", nameof(change));
        }
        WriteJson(fixture.Summary, summary);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () => await fixture.Project());
        Assert.IsFalse(File.Exists(fixture.Producer), "Rejected preparation must not emit a projected producer.");
    }

    [TestMethod]
    public async Task ProjectionCannotWriteIntoMaintainedSourceOrOverwriteEvidence()
    {
        using var fixture = new Fixture("Preparation");
        string forbidden = Path.Combine(fixture.Language, "new-evidence");
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () => await fixture.Project(forbidden));
        Assert.IsFalse(Directory.Exists(forbidden));
        Assert.AreEqual(0, await fixture.Project());
        string before = Hash(fixture.Producer);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () => await fixture.Project());
        Assert.AreEqual(before, Hash(fixture.Producer));
    }

    [TestMethod]
    [DataRow("consumer-source")]
    [DataRow("test-source")]
    [DataRow("manifest-tamper")]
    [DataRow("summary-and-manifest-tamper")]
    [DataRow("bad-schema")]
    public async Task CurrentProducerChainIsRejectedByActualConsumersBeforeAnyBuildOrCollection(string change)
    {
        using var fixture = new Fixture("Preparation");
        Assert.AreEqual(0, await fixture.Project());
        if (change == "consumer-source") { File.AppendAllText(Path.Combine(fixture.Language, "tools/CisHealthProbe/Program.cs"), " changed consumer"); }
        if (change is "test-source" or "summary-and-manifest-tamper") { File.AppendAllText(Path.Combine(fixture.Language, "tests/ConsumerInput.cs"), " changed test"); }
        if (change == "manifest-tamper") { File.AppendAllText(fixture.Manifest, "\n"); }
        if (change == "summary-and-manifest-tamper")
        {
            Write(fixture.Manifest, System.Text.Json.JsonSerializer.SerializeToNode(SourceInventory.CaptureVerificationInputs(fixture.Root))!.ToJsonString());
            JsonObject summary = Read(fixture.Summary); summary["source"]!["inputInventory"]!["sha256"] = Hash(fixture.Manifest); WriteJson(fixture.Summary, summary);
        }
        if (change == "bad-schema") { JsonObject producer = Read(fixture.Producer); producer["schema"] = "cis.healthcheck.prepared-inputs/v999"; WriteJson(fixture.Producer, producer); }
        string packageOutput = Path.Combine(fixture.Root, ".artifacts/package-admission");
        int packageExit = await HealthcheckPackageVerification.RunAsync(["--language-root", fixture.Language, "--package", fixture.Package,
            "--provenance", fixture.Producer, "--dependency-cache", Path.Combine(fixture.Root, "unused-cache"), "--output", packageOutput]);
        Assert.AreEqual(1, packageExit);
        JsonObject package = Read(Path.Combine(packageOutput, "results.json"));
        Assert.AreEqual(false, (bool?)package["complete"]);
        Assert.HasCount(0, package["processes"]!.AsArray(), "The actual package consumer must reject the stale chain before SDK/build child admission.");
        string coverageOutput = Path.Combine(fixture.Root, ".artifacts/coverage-admission");
        int coverageExit = await HealthcheckCoverageVerification.RunAsync(["--language-root", fixture.Language, "--package", fixture.Package,
            "--provenance", fixture.Producer, "--reports", Path.Combine(fixture.Root, "absent-native-reports"), "--output", coverageOutput]);
        Assert.AreEqual(1, coverageExit);
        JsonObject coverage = Read(Path.Combine(coverageOutput, "results.json"));
        Assert.AreEqual(false, (bool?)coverage["complete"]);
        Assert.AreEqual(false, (bool?)coverage["qualified"]);
        Assert.IsFalse(coverage["inputPins"]!.AsObject().Any(pin => pin.Key.EndsWith("collection.json", StringComparison.Ordinal)), "Stale preparation is rejected before native collection admission.");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TemporaryDirectory temporary = new();
        internal string Root => temporary.Path;
        internal string Language { get; }
        internal string Prepared { get; }
        internal string Package { get; }
        internal string Core { get; }
        internal string Source { get; }
        internal string Summary => Path.Combine(Prepared, "summary.json");
        internal string Manifest => Path.Combine(Prepared, "source-inputs.json");
        internal string Inspection => Path.Combine(Prepared, "inspection.json");
        internal string Output => Path.Combine(Root, ".artifacts/projected");
        internal string Producer => Path.Combine(Output, "producer.json");

        internal Fixture(string profile)
        {
            Language = Path.Combine(Root, "implementations/csharp");
            Prepared = Path.Combine(Root, ".artifacts/prepared");
            Package = Path.Combine(Prepared, "packages/CIS.Core.fixture.nupkg");
            Core = Path.Combine(Language, "src/CIS.Core/bin/Release/net10.0/CIS.Core.dll");
            Source = Path.Combine(Language, "src/CIS.Core/Healthcheck/Fixture.cs");
            Write(Source, "namespace Fixture; internal sealed class Healthcheck {}");
            foreach (string relative in new[] { "src/CIS.Core/CIS.Core.csproj", "global.json", "Directory.Build.props", "Directory.Packages.props" })
            { Write(Path.Combine(Language, relative), relative); }
            Write(Path.Combine(Root, "docs/current.md"), "A maintained input.");
            Write(Path.Combine(Language, "tools/CisHealthProbe/Program.cs"), "Actual consumer parser fixture input.");
            Write(Path.Combine(Language, "tests/ConsumerInput.cs"), "Maintained test parser fixture input.");
            // Opaque fixture bytes validate pin/projection mechanics. They are
            // deliberately not represented as a built candidate or native pass.
            Write(Core, "opaque Core parser fixture");
            Directory.CreateDirectory(Path.GetDirectoryName(Package)!);
            using (ZipArchive archive = ZipFile.Open(Package, ZipArchiveMode.Create))
            {
                using (Stream stream = archive.CreateEntry("lib/net10.0/CIS.Core.dll").Open()) { stream.Write(File.ReadAllBytes(Core)); }
                using var writer = new StreamWriter(archive.CreateEntry("CIS.Core.nuspec").Open());
                writer.Write("<package><metadata><id>CIS.Core</id><version>0.1.0-fixture</version></metadata></package>");
            }
            Write(Manifest, JsonSerializer.SerializeToNode(SourceInventory.CaptureVerificationInputs(Root))!.ToJsonString());
            WriteJson(Inspection, new JsonObject
            {
                ["schema"] = "cis.architecture-inspection/v1",
                ["allowlists"] = new JsonObject { ["violations"] = 0 },
                ["ownership"] = new JsonObject { ["violations"] = 0 },
                ["package"] = new JsonObject { ["path"] = Package, ["sha256"] = Hash(Package) },
                ["assemblies"] = new JsonArray(new JsonObject { ["project"] = "src/CIS.Core/CIS.Core.csproj", ["facts"] = new JsonObject { ["sha256"] = Hash(Core) } })
            });
            WriteJson(Summary, new JsonObject
            {
                ["profile"] = profile,
                ["scope"] = "prepared-inputs",
                ["exitCode"] = 0,
                ["issues"] = new JsonArray(),
                ["stages"] = new JsonArray(new[] { "build", "pack", "architecture-inspection" }.Select(id => (JsonNode)new JsonObject { ["id"] = id, ["status"] = "Passed" }).ToArray()),
                ["source"] = new JsonObject { ["inputScope"] = SourceInventory.VerificationScope, ["inputInventory"] = Reference(Manifest) },
                ["candidatePackage"] = Reference(Package),
                ["architectureInspection"] = Reference(Inspection)
            });
        }

        private JsonObject Reference(string path) => new() { ["path"] = Path.GetRelativePath(Prepared, path).Replace('\\', '/'), ["sha256"] = Hash(path) };
        internal Task<int> Project(string? output = null) => HealthcheckPackageVerification.RunAsync(
            ["prepared-inputs", "--language-root", Language, "--verification-root", Prepared, "--package", Package, "--output", output ?? Output]);
        public void Dispose() => temporary.Dispose();
    }
}
