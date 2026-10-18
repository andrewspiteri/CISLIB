using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using CIS.TestSupport;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class ToolchainChecksTests
{
    [TestMethod]
    [DataRow("valid")]
    [DataRow("framework-download")]
    [DataRow("expression-license")]
    [DataRow("mixed-nuspec-case")]
    [DataRow("missing-package")]
    [DataRow("unknown-package")]
    [DataRow("wrong-version")]
    [DataRow("duplicate-policy")]
    [DataRow("missing-archive")]
    [DataRow("wrong-id")]
    [DataRow("wrong-metadata-version")]
    [DataRow("changed-nuspec")]
    [DataRow("changed-license")]
    [DataRow("escaping-license")]
    [DataRow("changed-deps")]
    [DataRow("duplicate-entry")]
    [DataRow("missing-license")]
    [DataRow("dtd")]
    public void ProvenanceBindsApprovedCacheAndRetainedBytes(string mode)
    {
        using var fixture = new Fixture();
        string package = fixture.Package;
        if (mode == "framework-download") { fixture.AddDownload(); }
        if (mode == "expression-license") { fixture.Nuspec = fixture.Nuspec.Replace("<license type=\"file\">LICENSE.txt</license>", "<license type=\"expression\">MIT</license>", StringComparison.Ordinal); fixture.SavePackage(); }
        if (mode == "mixed-nuspec-case") { File.Move(Path.Combine(package, "example.nuspec"), Path.Combine(package, "Example.NuSpec")); }
        if (mode == "missing-package") { Directory.Delete(package, recursive: true); }
        if (mode == "unknown-package") { Directory.CreateDirectory(Path.Combine(fixture.Cache, "unknown", "1.0.0")); }
        if (mode == "wrong-version") { Directory.Move(package, Path.Combine(fixture.Cache, "example", "2.0.0")); }
        if (mode == "duplicate-policy") { fixture.Runtime["packages"]!.AsArray().Add(fixture.Runtime["packages"]![0]!.DeepClone()); fixture.SavePolicies(); }
        if (mode == "missing-archive") { File.Delete(Path.Combine(package, "example.1.0.0.nupkg")); }
        if (mode == "wrong-id") { fixture.Nuspec = fixture.Nuspec.Replace("<id>Example</id>", "<id>Different</id>", StringComparison.Ordinal); fixture.SavePackage(); }
        if (mode == "wrong-metadata-version") { fixture.Nuspec = fixture.Nuspec.Replace("<version>1.0.0</version>", "<version>2.0.0</version>", StringComparison.Ordinal); fixture.SavePackage(); }
        if (mode == "changed-nuspec") { Write(Path.Combine(package, "example.nuspec"), fixture.Nuspec + " "); }
        if (mode == "changed-license") { Write(Path.Combine(package, "LICENSE.txt"), "different"); }
        if (mode == "escaping-license") { fixture.Nuspec = fixture.Nuspec.Replace("LICENSE.txt", "../outside.txt", StringComparison.Ordinal); fixture.SavePackage(); }
        if (mode == "changed-deps") { Write(Path.Combine(fixture.Tool, "tools/net8.0/any/Tool.deps.json"), "{}"); }
        if (mode == "duplicate-entry") { fixture.Duplicate = true; fixture.SavePackage(); }
        if (mode == "missing-license") { fixture.Nuspec = fixture.Nuspec.Replace("<license type=\"file\">LICENSE.txt</license>", "", StringComparison.Ordinal); fixture.SavePackage(); }
        if (mode == "dtd") { fixture.Nuspec = "<!DOCTYPE package [<!ENTITY x SYSTEM 'file:///not-read'>]>" + fixture.Nuspec; fixture.SavePackage(); }
        if (mode is "valid" or "framework-download" or "expression-license" or "mixed-nuspec-case")
        {
            fixture.Record();
            JsonObject result = Read(Path.Combine(fixture.Output, "prerequisites.json"));
            Assert.AreEqual("observed-shell", (string?)result["powershellVersion"]);
            JsonArray packages = result["packages"]!.AsArray();
            Assert.HasCount(mode == "framework-download" ? 3 : 2, packages);
            foreach (JsonNode? item in packages)
            {
                Assert.AreEqual((string?)item!["sha256"], Hash(Path.Combine(fixture.Output, item["retainedArchive"]!.GetValue<string>())));
                if ((string?)item["licenseType"] == "file") { Assert.AreEqual((string?)item["licenseSha256"], Hash(Path.Combine(fixture.Output, item["retainedLicense"]!.GetValue<string>()))); }
            }
            JsonNode tool = packages.Single(item => (string?)item!["id"] == "Tool")!;
            Assert.AreEqual((string?)tool["embeddedDependencyManifestSha256"], Hash(Path.Combine(fixture.Output, tool["retainedDependencyManifest"]!.GetValue<string>())));
            Assert.AreEqual("Dependency/1.2.3", (string?)tool["embeddedDependencies"]![0]!["identity"]);
            Assert.AreEqual("development-only CLI", (string?)tool["role"]);
        }
        else
        {
            Exception error = Assert.Throws<Exception>(fixture.Record);
            Assert.IsTrue(error is InvalidDataException or IOException or InvalidOperationException or XmlException);
            Assert.IsFalse(File.Exists(Path.Combine(fixture.Output, "prerequisites.json")));
        }
    }

    [TestMethod]
    public void RecordedPrerequisitesCannotBeOverwritten()
    {
        using var fixture = new Fixture();
        fixture.Record();
        string hash = Hash(Path.Combine(fixture.Output, "prerequisites.json"));
        Assert.ThrowsExactly<InvalidDataException>(fixture.Record);
        Assert.AreEqual(hash, Hash(Path.Combine(fixture.Output, "prerequisites.json")));
    }

    [TestMethod]
    [DataRow("valid")]
    [DataRow("same-copy")]
    [DataRow("empty-tests")]
    [DataRow("skipped-tests")]
    [DataRow("failed-tests")]
    [DataRow("incomplete")]
    [DataRow("wrong-collector")]
    [DataRow("duplicate-collector")]
    [DataRow("missing-attachment")]
    [DataRow("conflicting-copy")]
    [DataRow("zero-lines")]
    [DataRow("zero-branches")]
    [DataRow("bad-count")]
    [DataRow("bad-rate")]
    [DataRow("inconsistent-rate")]
    [DataRow("below-threshold")]
    public void CoveragePreservesNativeCountersCollectorAndGates(string mode)
    {
        using var fixture = new Fixture();
        XElement counters = fixture.Trx.Descendants("Counters").Single(), collector = fixture.Trx.Descendants("Collector").Single();
        if (mode == "empty-tests") { counters.SetAttributeValue("total", "0"); }
        if (mode == "skipped-tests") { counters.SetAttributeValue("executed", "1"); counters.SetAttributeValue("passed", "1"); }
        if (mode == "failed-tests") { counters.SetAttributeValue("failed", "1"); }
        if (mode == "incomplete") { fixture.Trx.Descendants("ResultSummary").Single().SetAttributeValue("outcome", "Aborted"); }
        if (mode == "wrong-collector") { collector.SetAttributeValue("uri", "wrong"); }
        if (mode == "duplicate-collector") { fixture.Trx.Root!.Add(new XElement(collector)); }
        if (mode == "missing-attachment") { collector.Descendants("A").Single().Remove(); }
        if (mode == "zero-lines") { fixture.Coverage.Root!.SetAttributeValue("lines-valid", "0"); }
        if (mode == "zero-branches") { fixture.Coverage.Root!.SetAttributeValue("branches-valid", "0"); }
        if (mode == "bad-count") { fixture.Coverage.Root!.SetAttributeValue("lines-valid", "-1"); }
        if (mode == "bad-rate") { fixture.Coverage.Root!.SetAttributeValue("line-rate", "NaN"); }
        if (mode == "inconsistent-rate") { fixture.Coverage.Root!.SetAttributeValue("line-rate", ".5"); }
        if (mode == "below-threshold") { fixture.Coverage.Root!.SetAttributeValue("line-rate", ".9"); fixture.Coverage.Root!.SetAttributeValue("lines-covered", "9"); }
        fixture.SaveCoverage();
        if (mode is "same-copy" or "conflicting-copy") { Write(Path.Combine(fixture.Output, "copy/report.cobertura.xml"), mode == "same-copy" ? fixture.Coverage.ToString() : "different"); }
        if (mode is "valid" or "same-copy" or "below-threshold")
        {
            JsonObject result = ToolchainReports.Coverage(fixture.Output, fixture.Policy["coverage"]!.AsObject());
            Assert.AreEqual(mode != "below-threshold", result["passed"]!.GetValue<bool>());
            Assert.AreEqual(2, result["parserCases"]!.GetValue<int>());
            Assert.AreEqual(Hash(Path.Combine(fixture.Output, "native/report.cobertura.xml")), (string?)result["coberturaSha256"]);
        }
        else { Assert.ThrowsExactly<InvalidDataException>(() => ToolchainReports.Coverage(fixture.Output, fixture.Policy["coverage"]!.AsObject())); }
    }

    [TestMethod]
    [DataRow("valid")]
    [DataRow("stale-source")]
    [DataRow("wrong-tool")]
    [DataRow("foreign-file")]
    [DataRow("extra-file")]
    [DataRow("unknown-status")]
    [DataRow("duplicate-id")]
    [DataRow("zero-denominator")]
    [DataRow("timeout-no-credit")]
    [DataRow("survivor-no-credit")]
    [DataRow("unreviewed")]
    [DataRow("duplicate-review")]
    [DataRow("wrong-status")]
    [DataRow("wrong-reason")]
    [DataRow("wrong-mutator")]
    [DataRow("wrong-replacement")]
    public void MutationPreservesSourceReviewAndConservativeDenominator(string mode)
    {
        using var fixture = new Fixture();
        JsonObject native = fixture.Mutation["files"]!["Logging/Configuration/Parser.cs"]!.AsObject();
        JsonArray mutants = native["mutants"]!.AsArray();
        if (mode == "stale-source") { Write(Path.Combine(fixture.Language, "src/CIS.Core/Logging/Configuration/Parser.cs"), "changed"); }
        if (mode == "wrong-tool") { fixture.Review["strykerVersion"] = "2.0.0"; }
        if (mode == "foreign-file") { fixture.Mutation["files"] = new JsonObject { ["foreign.cs"] = native.DeepClone() }; }
        if (mode == "extra-file") { fixture.Mutation["files"]!.AsObject()["other.cs"] = native.DeepClone(); }
        if (mode == "unknown-status") { mutants[0]!["status"] = "AssumedKilled"; }
        if (mode == "duplicate-id") { mutants.Add(mutants[0]!.DeepClone()); }
        if (mode == "zero-denominator") { mutants[0]!["status"] = "Ignored"; }
        if (mode == "timeout-no-credit") { mutants[0]!["status"] = "Timeout"; }
        if (mode == "survivor-no-credit") { mutants[0]!["status"] = "Survived"; }
        if (mode == "unreviewed") { fixture.Review["mutants"]!.AsArray().Clear(); }
        if (mode == "duplicate-review") { fixture.Review["mutants"]!.AsArray().Add(fixture.Review["mutants"]![0]!.DeepClone()); }
        if (mode == "wrong-status") { fixture.Review["mutants"]![0]!["status"] = "CompileError"; }
        if (mode == "wrong-reason") { fixture.Review["mutants"]![0]!["statusReason"] = "Other"; }
        if (mode == "wrong-mutator") { fixture.Review["mutants"]![0]!["mutatorName"] = "Other"; }
        if (mode == "wrong-replacement") { fixture.Review["mutants"]![0]!["replacement"] = "{ }"; }
        WriteJson(Path.Combine(fixture.Output, "mutation.json"), fixture.Mutation);
        WriteJson(Path.Combine(fixture.Output, "review.json"), fixture.Review);
        if (mode == "valid")
        {
            JsonObject result = ToolchainReports.Mutation(Path.Combine(fixture.Output, "mutation.json"), Path.Combine(fixture.Output, "review.json"), fixture.Language, "1.0.0");
            Assert.AreEqual(1, result["numerator"]!.GetValue<int>());
            Assert.AreEqual(1, result["denominator"]!.GetValue<int>());
            Assert.AreEqual(2, result["total"]!.GetValue<int>());
            Assert.HasCount(0, result["reclassifiedMutants"]!.AsArray());
            Assert.AreEqual(Hash(Path.Combine(fixture.Output, "mutation.json")), (string?)result["nativeReportSha256"]);
        }
        else { Assert.ThrowsExactly<InvalidDataException>(() => ToolchainReports.Mutation(Path.Combine(fixture.Output, "mutation.json"), Path.Combine(fixture.Output, "review.json"), fixture.Language, "1.0.0")); }
    }

    [TestMethod]
    [DataRow("../escape")]
    [DataRow("/absolute")]
    [DataRow("C:/absolute")]
    [DataRow("..\\escape")]
    [DataRow("empty//segment")]
    [DataRow("")]
    public void PackagePathsStayInsideTheirArchive(string relative) => Assert.ThrowsExactly<InvalidDataException>(() => ToolchainInputs.Relative(relative));

    [TestMethod]
    public void NativeXmlRejectsDtd() => Assert.ThrowsExactly<XmlException>(() => ToolchainInputs.Xml(Encoding.UTF8.GetBytes("<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///not-read'>]><x>&e;</x>")));

    [TestMethod]
    public void ToolHostAndRawProcessBytesRemainPinned()
    {
        using var fixture = new Fixture();
        Write(Path.Combine(fixture.Tool, "tools/net8.0/any/Tool.dll"), "tool");
        string[] args = ToolchainQualification.ToolArguments(fixture.Policy, fixture.Cache, "Tool");
        CollectionAssert.AreEqual(new[] { "exec", "--fx-version", "8.0.31", "--roll-forward", "Disable", Path.GetFullPath(Path.Combine(fixture.Tool, "tools/net8.0/any/Tool.dll")) }, args);
        File.Delete(args[^1]);
        Assert.ThrowsExactly<InvalidDataException>(() => ToolchainQualification.ToolArguments(fixture.Policy, fixture.Cache, "Tool"));
        byte[] stdout = [0xff, 0x41], stderr = [0x80];
        var result = new ProcessResult(2, "display", "diagnostic") { ProcessId = 42, StandardOutputBytes = stdout, StandardErrorBytes = stderr };
        ToolchainQualification.RecordProcess(fixture.Output, "native", result);
        CollectionAssert.AreEqual(stdout, File.ReadAllBytes(Path.Combine(fixture.Output, "logs/native.stdout.bin")));
        JsonObject process = Read(Path.Combine(fixture.Output, "logs/native.process.json"));
        Assert.AreEqual(Digest(stdout), (string?)process["stdoutSha256"]);
        Assert.AreEqual(Digest(stderr), (string?)process["stderrSha256"]);
        Assert.AreEqual(2, process["exitCode"]!.GetValue<int>());
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TemporaryDirectory directory = new();
        internal string Language => Path.Combine(directory.Path, "language");
        internal string Cache => Path.Combine(directory.Path, "cache");
        internal string Output => Path.Combine(directory.Path, "output");
        internal string Package => Path.Combine(Cache, "example/1.0.0");
        internal string Tool => Path.Combine(Cache, "tool/1.0.0");
        internal JsonObject Policy { get; } = JsonNode.Parse("""{"sdk":"10.0.401","targetRuntime":"10.0.12","toolHostRuntime":"8.0.31","tools":[{"id":"Tool","version":"1.0.0","entryPoint":"Tool.dll","provenance":"https://example.invalid/tool"}],"coverage":{"collector":"datacollector://Microsoft/CodeCoverage/2.0","minimumLineRate":0.95,"minimumBranchRate":0.9},"frameworkDownloads":{"packages":[{"id":"Framework.Download","version":"10.0.12","provenance":"SDK implicit"}]}}""")!.AsObject();
        internal JsonObject Runtime { get; } = JsonNode.Parse("""{"packages":[{"id":"Example","version":"1.0.0","provenance":"https://example.invalid/package"}]}""")!.AsObject();
        internal string Nuspec { get; set; } = """<package><metadata><id>Example</id><version>1.0.0</version><authors>Example author</authors><license type="file">LICENSE.txt</license></metadata></package>""";
        internal bool Duplicate { get; set; }
        internal XDocument Trx { get; } = XDocument.Parse("""<TestRun><ResultSummary outcome="Completed"><Counters total="2" executed="2" passed="2" failed="0"/></ResultSummary><Collector collectorDisplayName="Code Coverage" uri="datacollector://Microsoft/CodeCoverage/2.0"><UriAttachment><A href="native\report.cobertura.xml"/></UriAttachment></Collector></TestRun>""");
        internal XDocument Coverage { get; } = XDocument.Parse("""<coverage lines-valid="10" lines-covered="10" branches-valid="10" branches-covered="9" line-rate="1" branch-rate=".9"/>""");
        internal JsonObject Mutation { get; } = JsonNode.Parse("""{"files":{"Logging/Configuration/Parser.cs":{"mutants":[{"id":"1","status":"Killed"},{"id":"2","status":"Ignored","statusReason":"Removed","mutatorName":"Block removal","replacement":"{}"}]}}}""")!.AsObject();
        internal JsonObject Review { get; }
        internal Fixture()
        {
            SavePolicies(); SavePackage();
            Archive(Tool, "tool.1.0.0.nupkg", new Dictionary<string, string>
            {
                ["tool.nuspec"] = Nuspec.Replace("<id>Example</id>", "<id>Tool</id>", StringComparison.Ordinal),
                ["LICENSE.txt"] = "Example licence",
                ["tools/net8.0/any/Tool.deps.json"] = """{"libraries":{"Dependency/1.2.3":{"type":"package"}}}"""
            });
            string parser = "src/CIS.Core/Logging/Configuration/Parser.cs";
            Write(Path.Combine(Language, parser), "source");
            Review = new JsonObject { ["source"] = parser, ["sourceSha256"] = Hash(Path.Combine(Language, parser)), ["strykerVersion"] = "1.0.0", ["mutants"] = new JsonArray(Mutation["files"]!["Logging/Configuration/Parser.cs"]!["mutants"]![1]!.DeepClone()) };
            SaveCoverage();
        }
        internal void SavePolicies()
        {
            WriteJson(Path.Combine(Language, "eng/toolchain.json"), Policy);
            WriteJson(Path.Combine(Language, "eng/runtime-dependencies.json"), Runtime);
            WriteJson(Path.Combine(Language, "eng/development-dependencies.json"), new JsonObject { ["packages"] = new JsonArray() });
        }
        internal void SavePackage() => Archive(Package, "example.1.0.0.nupkg", new Dictionary<string, string> { ["example.nuspec"] = Nuspec, ["LICENSE.txt"] = "Example licence" }, Duplicate);
        internal void AddDownload() => Archive(Path.Combine(Cache, "framework.download/10.0.12"), "framework.download.10.0.12.nupkg", new Dictionary<string, string>
        { ["framework.download.nuspec"] = Nuspec.Replace("<id>Example</id>", "<id>Framework.Download</id>", StringComparison.Ordinal).Replace("<version>1.0.0</version>", "<version>10.0.12</version>", StringComparison.Ordinal), ["LICENSE.txt"] = "Example licence" });
        internal void SaveCoverage()
        {
            Write(Path.Combine(Output, "parser.trx"), Trx.ToString());
            Write(Path.Combine(Output, "native/report.cobertura.xml"), Coverage.ToString());
        }
        internal void Record() => ToolchainInputs.Record(Output, Language, Cache, "observed-shell", "observed-git");
        private static void Archive(string root, string name, Dictionary<string, string> contents, bool duplicate = false)
        {
            Directory.CreateDirectory(root);
            using var stream = File.Create(Path.Combine(root, name)); using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
            foreach ((string path, string content) in contents)
            {
                Write(Path.Combine(root, path), content);
                using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false)); writer.Write(content);
            }
            if (duplicate) { using var writer = new StreamWriter(zip.CreateEntry("example.nuspec").Open()); writer.Write(contents["example.nuspec"]); }
        }
        public void Dispose() => directory.Dispose();
    }
}
