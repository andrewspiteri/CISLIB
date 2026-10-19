using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml;
using CIS.TestSupport;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class MicrosoftLoggingBundleTests
{
    [TestMethod]
    [DataRow("valid")]
    [DataRow("forged-release-claim")]
    [DataRow("wrong-schema")]
    [DataRow("wrong-module")]
    [DataRow("missing-area")]
    [DataRow("duplicate-area")]
    [DataRow("missing-doc")]
    [DataRow("escaped-doc")]
    [DataRow("empty-source")]
    [DataRow("empty-prior")]
    [DataRow("missing-prior")]
    [DataRow("wrong-mutation")]
    [DataRow("missing-native-json")]
    [DataRow("missing-native-html")]
    [DataRow("failed-developer")]
    [DataRow("unfinished-developer")]
    [DataRow("bad-reference")]
    [DataRow("wrong-package-id")]
    [DataRow("missing-core")]
    [DataRow("missing-api-xml")]
    [DataRow("missing-api-member")]
    [DataRow("changed-core")]
    [DataRow("duplicate-zip-entry")]
    [DataRow("case-duplicate-zip-entry")]
    [DataRow("dtd")]
    [DataRow("empty-version")]
    [DataRow("empty-pending")]
    public async Task DraftRequiresOwnedPackageRegistrationAndEvidence(string mode)
    {
        using Fixture fixture = await Fixture.CreateAsync();
        if (mode == "forged-release-claim") { fixture.Registration["state"] = "ReleaseQualified"; fixture.Registration["releaseQualified"] = true; }
        if (mode == "wrong-schema") { fixture.Registration["schema"] = "wrong"; }
        if (mode == "wrong-module") { fixture.Registration["module"]!["id"] = "other"; }
        if (mode == "missing-area") { fixture.Registration["requiredReleaseAreas"]!.AsArray().RemoveAt(0); }
        if (mode == "duplicate-area") { fixture.Registration["requiredReleaseAreas"]!.AsArray()[0] = "Source"; }
        if (mode == "missing-doc") { File.Delete(Path.Combine(fixture.Repo, "docs/provider.md")); }
        if (mode == "escaped-doc") { fixture.Registration["module"]!["documentation"]!.AsArray()[0] = "../outside.md"; }
        if (mode == "empty-source") { File.Delete(Path.Combine(fixture.Repo, "source/case α.cs")); }
        if (mode == "empty-prior") { File.Delete(Path.Combine(fixture.Repo, "history/prior.json")); }
        if (mode == "missing-prior") { Directory.Delete(Path.Combine(fixture.Repo, "history"), recursive: true); }
        if (mode == "wrong-mutation") { Write(Path.Combine(fixture.Repo, "native/mutation.json"), "changed"); }
        if (mode == "missing-native-json") { File.Delete(Path.Combine(fixture.Repo, "native/mutation.json")); }
        if (mode == "missing-native-html") { File.Delete(Path.Combine(fixture.Repo, "native/mutation.html")); }
        if (mode == "failed-developer") { fixture.Summary["exitCode"] = 1; }
        if (mode == "unfinished-developer") { fixture.Summary["finishedUtc"] = null; }
        if (mode == "bad-reference") { fixture.Summary["families"]![0]!["report"]!["sha256"] = new string('0', 64); }
        if (mode == "wrong-package-id") { fixture.Nuspec = fixture.Nuspec.Replace("<id>CIS.Core</id>", "<id>Different</id>", StringComparison.Ordinal); }
        if (mode == "missing-core") { fixture.Remove = "lib/net10.0/CIS.Core.dll"; }
        if (mode == "missing-api-xml") { fixture.Remove = "lib/net10.0/CIS.Core.xml"; }
        if (mode == "missing-api-member") { fixture.ApiXml = "<doc><members/></doc>"; }
        if (mode == "changed-core") { Write(Path.Combine(fixture.Repo, "implementations/csharp/src/CIS.Core/bin/Release/net10.0/CIS.Core.dll"), "changed"); }
        if (mode == "duplicate-zip-entry") { fixture.Duplicate = "CIS.Core.nuspec"; }
        if (mode == "case-duplicate-zip-entry") { fixture.Duplicate = "cis.core.nuspec"; }
        if (mode == "dtd") { fixture.ApiXml = "<!DOCTYPE doc [<!ENTITY e SYSTEM 'file:///not-read'>]><doc>&e;</doc>"; }
        if (mode == "empty-version") { fixture.Nuspec = fixture.Nuspec.Replace("<version>1.2.3</version>", "<version/>", StringComparison.Ordinal); }
        if (mode == "empty-pending") { fixture.Registration["pendingReleaseRequirements"]!.AsArray().Clear(); }
        fixture.Save();
        if (mode is "valid" or "forged-release-claim")
        {
            JsonObject manifest = await fixture.WriteDraft();
            Assert.AreEqual("IncompleteReleaseDraft", (string?)manifest["state"]);
            Assert.IsFalse(manifest["releaseQualified"]!.GetValue<bool>());
            Assert.HasCount(9, manifest["releaseAreas"]!.AsArray());
            Assert.IsTrue(manifest["releaseAreas"]!.AsArray().All(item => (string?)item!["fullReleaseState"] == "Pending"));
            Assert.HasCount(1, manifest["pendingReleaseRequirements"]!.AsArray());
            Assert.AreEqual(Hash(fixture.Package), (string?)manifest["product"]!["packageSha256"]);
            Assert.AreEqual(Hash(Path.Combine(fixture.Output, "products/candidate.nupkg")), (string?)manifest["product"]!["packageSha256"]);
            Assert.AreEqual(Hash(Path.Combine(fixture.Output, "products/CIS.Core.dll")), (string?)manifest["product"]!["assemblySha256"]);
            Assert.AreEqual(Hash(Path.Combine(fixture.Output, "products/CIS.Core.xml")), (string?)manifest["product"]!["apiDocumentationSha256"]);
            Assert.AreEqual(Hash(Path.Combine(fixture.Output, "manifest.json")), File.ReadAllText(Path.Combine(fixture.Output, "manifest.sha256")).Trim());
            Assert.AreEqual("package-source-identity", (string?)manifest["product"]!["packageSourceCommit"]);
            Assert.AreNotEqual((string?)manifest["source"]!["headCommit"], (string?)manifest["product"]!["packageSourceCommit"]);
            Assert.AreEqual("developer-source-identity", (string?)manifest["developer"]!["sourceCommit"]);
            StringAssert.Contains(manifest["product"]!["comparedBuildOutput"]!["qualification"]!.GetValue<string>(), "not cryptographically tied");
            Assert.IsTrue(manifest["files"]!.AsArray().Any(file => (string?)file!["role"] == "PriorDevelopmentEvidenceOnly"));
            Assert.IsTrue(manifest["files"]!.AsArray().Any(file => (string?)file!["role"] == "LocalNativeMutationReportNotReleaseRetained"));
            foreach (JsonNode? entry in manifest["files"]!.AsArray())
            { Assert.AreEqual((string?)entry!["sha256"], Hash(Path.Combine(fixture.Repo, entry["path"]!.GetValue<string>()))); }
        }
        else
        {
            Exception error = await Assert.ThrowsAsync<Exception>(fixture.WriteDraft);
            Assert.IsTrue(error is InvalidDataException or IOException or InvalidOperationException or XmlException, error.ToString());
            Assert.IsFalse(Directory.Exists(fixture.Output), "Refusal must not materialize products or a manifest.");
        }
    }

    [TestMethod]
    public async Task MaintainedSourceInventoryKeepsUnicodeAndExcludesIgnoredAndDeletedFiles()
    {
        using Fixture fixture = await Fixture.CreateAsync();
        Write(Path.Combine(fixture.Repo, ".gitignore"), "source/bin/\n");
        Write(Path.Combine(fixture.Repo, "source/bin/ignored.cs"), "ignored");
        Write(Path.Combine(fixture.Repo, "source/deleted.cs"), "tracked then deleted");
        await fixture.Git("add", "--", "source/deleted.cs");
        File.Delete(Path.Combine(fixture.Repo, "source/deleted.cs"));
        JsonObject manifest = await fixture.WriteDraft();
        string[] paths = manifest["files"]!.AsArray().Select(item => item!["path"]!.GetValue<string>()).ToArray();
        CollectionAssert.Contains(paths, "source/case α.cs");
        CollectionAssert.DoesNotContain(paths, "source/bin/ignored.cs");
        CollectionAssert.DoesNotContain(paths, "source/deleted.cs");
        Assert.IsTrue(paths.Contains("implementations/csharp/eng/CIS.DependencyChecker/MicrosoftLoggingBundle.cs", StringComparer.Ordinal));
    }

    [TestMethod]
    [DataRow("directory")]
    [DataRow("file")]
    public async Task ExistingDraftOutputsArePreserved(string mode)
    {
        using Fixture fixture = await Fixture.CreateAsync();
        if (mode == "directory") { Directory.CreateDirectory(fixture.Output); }
        else { Write(fixture.Output, "preserve"); }
        await Assert.ThrowsExactlyAsync<InvalidDataException>(fixture.WriteDraft);
        if (mode == "directory") { Assert.IsTrue(Directory.Exists(fixture.Output)); }
        else { Assert.AreEqual("preserve", File.ReadAllText(fixture.Output)); }
    }

    [TestMethod]
    public async Task MissingGitIdentityDoesNotCreateReleaseDraft()
    {
        using Fixture fixture = await Fixture.CreateAsync(git: false);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(fixture.WriteDraft);
        Assert.IsFalse(Directory.Exists(fixture.Output));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TemporaryDirectory directory = new();
        internal string Repo => Path.Combine(directory.Path, "repo");
        internal string Output => Path.Combine(directory.Path, "output");
        internal string Package => Path.Combine(directory.Path, "candidate.nupkg");
        private string RegistrationPath => Path.Combine(Repo, "registration.json");
        private string Developer => Path.Combine(Repo, ".artifacts/csharp/verification/Developer");
        internal JsonObject Registration { get; } = JsonNode.Parse("""{"schema":"cis.csharp.verification-bundle-registration/v1","requiredReleaseAreas":["Product","Source","Tests","Contracts","History","Examples","Tooling","Evidence","Integrity"],"pendingReleaseRequirements":["Full candidate release qualification remains required"],"module":{"id":"logging-microsoft","apiMembers":["T:Fixture.Api"],"documentation":["docs/provider.md"],"requiredFiles":["required.json"],"sourceRoots":[{"path":"source","role":"SharedValidationSource"}],"priorEvidenceRoots":["history"],"nativeMutation":{"audit":"mutation-audit.json","run":"prior","json":"native/mutation.json","html":"native/mutation.html"}}}""")!.AsObject();
        internal JsonObject Summary { get; } = JsonNode.Parse("""{"schema":"cis.verification/v1","profile":"Developer","lane":"fixture-data-only","runId":"policy-fixture","exitCode":0,"finishedUtc":"2026-10-19T00:00:00Z","issues":[],"source":{"commit":"developer-source-identity"},"families":[{"id":"unit","report":{"path":"native-report.data","sha256":""}}]}""")!.AsObject();
        internal string Nuspec { get; set; } = """<package><metadata><id>CIS.Core</id><version>1.2.3</version><repository commit="package-source-identity"/></metadata></package>""";
        internal string ApiXml { get; set; } = """<doc><members><member name="T:Fixture.Api"/></members></doc>""";
        internal string? Remove { get; set; }
        internal string? Duplicate { get; set; }
        private Fixture()
        {
            Write(Path.Combine(Repo, "docs/provider.md"), "Policy fixture documentation.");
            Write(Path.Combine(Repo, "required.json"), "{}");
            Write(Path.Combine(Repo, "source/case α.cs"), "Owned source input; not compiled or credited as a runtime.");
            Write(Path.Combine(Repo, "history/prior.json"), "Historical fixture data only.");
            Write(Path.Combine(Repo, "native/mutation.json"), "Native-report policy fixture; no executed mutants are claimed.");
            Write(Path.Combine(Repo, "native/mutation.html"), "Historical report fixture.");
            WriteJson(Path.Combine(Repo, "mutation-audit.json"), new JsonObject { ["prior"] = new JsonObject { ["nativeReportSha256"] = Hash(Path.Combine(Repo, "native/mutation.json")) } });
            foreach (string file in new[] { "write-microsoft-logging-bundle.ps1", "CIS.DependencyChecker/MicrosoftLoggingBundle.cs", "CIS.DependencyChecker/SourceReplay.cs", "CIS.DependencyChecker/TypeManagerVerificationInputs.cs", "CIS.DependencyChecker/ToolchainInputs.cs", "CIS.DependencyChecker/Program.cs" })
            { Write(Path.Combine(Repo, "implementations/csharp/eng", file), "Owned producer provenance fixture input."); }
            Write(Path.Combine(Repo, "implementations/csharp/src/CIS.Core/bin/Release/net10.0/CIS.Core.dll"), "assembly fixture bytes");
            Write(Path.Combine(Developer, "source-inputs.json"), "[]");
            Write(Path.Combine(Developer, "validation.json"), "{}");
            Write(Path.Combine(Developer, "native-report.data"), "Native reference fixture; not executed test evidence.");
            Summary["families"]![0]!["report"]!["sha256"] = Hash(Path.Combine(Developer, "native-report.data"));
            Save();
        }
        internal static async Task<Fixture> CreateAsync(bool git = true)
        {
            var fixture = new Fixture();
            if (git)
            {
                await fixture.Git("init", "--quiet");
                Directory.CreateDirectory(Path.Combine(fixture.Repo, "empty-hooks"));
                await fixture.Git("-c", "user.name=CISLIB Fixture", "-c", "user.email=fixture@cislib.invalid", "-c", "commit.gpgsign=false",
                    "-c", "core.hooksPath=" + Path.Combine(fixture.Repo, "empty-hooks"), "commit", "--quiet", "--allow-empty", "-m", "Policy fixture");
            }
            return fixture;
        }
        internal void Save()
        {
            WriteJson(RegistrationPath, Registration);
            WriteJson(Path.Combine(Developer, "summary.json"), Summary);
            using var stream = File.Create(Package); using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
            foreach ((string path, string text) in new Dictionary<string, string>
            {
                ["CIS.Core.nuspec"] = Nuspec,
                ["lib/net10.0/CIS.Core.dll"] = "assembly fixture bytes",
                ["lib/net10.0/CIS.Core.xml"] = ApiXml
            })
            {
                if (path == Remove) { continue; }
                using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false)); writer.Write(text);
            }
            if (Duplicate is not null) { using var writer = new StreamWriter(zip.CreateEntry(Duplicate).Open()); writer.Write(Nuspec); }
        }
        internal Task<JsonObject> WriteDraft() => MicrosoftLoggingBundle.WriteAsync(Repo, Package, Output, RegistrationPath);
        internal async Task Git(params string[] args)
        {
            ProcessResult result = await ProcessRunner.RunAsync("git", ["-C", Repo, .. args], Repo, TimeSpan.FromSeconds(20));
            Assert.AreEqual(0, result.ExitCode, result.StandardError);
        }
        public void Dispose()
        {
            string objects = Path.Combine(Repo, ".git/objects");
            if (Directory.Exists(objects))
            { foreach (string file in Directory.GetFiles(objects, "*", SearchOption.AllDirectories)) { File.SetAttributes(file, FileAttributes.Normal); } }
            directory.Dispose();
        }
    }
}

