using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.TestSupport;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

internal static class TypeManagerRuntimeVerification
{
    internal static readonly string[] Modes = ["outside-catalog-late-load", "manifest-version-conflict", "manifest-version-conflict-reversed", "partial-types", "validation-before-load", "dynamic-name-collision", "foreign-resolver-result", "file-identity-conflict", "loaded-identity-conflict", "tpa-selected-newer-version", "reference-reuse", "self-defined-reference-reuse", "invalid-executable", "locationless-host", "forwarding-and-collisions"];
    internal static async Task<int> RunAsync(string[] arguments)
    {
        var input = new TypeManagerVerificationInputs(arguments, ["repo-root", "output-root"], ["replay-root", "expect-core-sha256"]);
        input.EmptyOutput(); string root = input.Output, language = input.Language;
        string verifier = Hash(typeof(TypeManagerRuntimeVerification).Assembly.Location);
        var report = new JsonObject
        {
            ["scope"] = "Actual Core development source; fresh processes, not package qualification",
            ["complete"] = false,
            ["verifierSha256"] = verifier,
            ["sourceProvenanceScope"] = "Core C# sources and five named project/config files; not all transitive MSBuild inputs",
            ["results"] = new JsonArray()
        };
        void Save() => WriteJson(Path.Combine(root, "results.json"), report);
        Save();
        try
        {
            report["sdk"] = (await input.Run("sdk", ["--version"], language)).StandardOutput.Trim();
            Require((string?)report["sdk"] == (string?)Read(Path.Combine(language, "global.json"))["sdk"]!["version"], "Runtime producer SDK differs from the pin.");
            bool replay = input.Optional("replay-root") is not null; report["mode"] = replay ? "frozen-managed-binary-replay" : "build-and-run";
            JsonObject? original = null;
            if (replay)
            {
                string prior = input.PathOf("replay-root"); original = Read(Path.Combine(prior, "results.json"));
                Require((bool?)original["complete"] == true && (string?)original["verifierSha256"] == verifier, "Replay source is incomplete or its verifier changed.");
                foreach (string key in new[] { "coreSourceInputs", "coreProjectInputs", "sourceGitHead" }) { report[key] = original[key]?.DeepClone(); }
                foreach (string name in new[] { "Broken", "Forwarder", "Forwarded", "Host", "versions", "helper" }) { CopyTree(Path.Combine(prior, name), Path.Combine(root, name)); }
            }
            else
            {
                report["coreSourceInputs"] = input.CoreSources(); report["coreProjectInputs"] = input.CoreProjects();
                ProcessResult git = await ProcessRunner.RunAsync("git", ["rev-parse", "HEAD"], language, TimeSpan.FromSeconds(30));
                report["sourceGitHead"] = git.ExitCode == 0 ? git.StandardOutput.Trim() : null;
                string fixtures = Path.Combine(language, "tests", "fixtures", "TypeManager", "Runtime");
                foreach (string name in new[] { "Broken", "Forwarder", "Host" })
                {
                    await input.Run("build-" + name, ["build", Path.Combine(fixtures, name, name + ".csproj"), "-c", "Release", "-m:1", "--no-incremental", "--nologo", "-o", Path.Combine(root, name, "bin", "Release", "net10.0")], language);
                }
                Copy(Path.Combine(root, "Forwarder", "bin", "Release", "net10.0", "CIS.TypeManager.ForwardedFixture.dll"), Path.Combine(root, "Forwarded", "bin", "Release", "net10.0", "CIS.TypeManager.ForwardedFixture.dll"));
                string generated = Path.Combine(root, "generated"); Write(Path.Combine(generated, "Directory.Build.props"), "<Project />\n");
                foreach (string variant in new[] { "v1", "v2", "ref", "self-reference" })
                {
                    string directory = Path.Combine(generated, variant);
                    var properties = new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"), new XElement("ImplicitUsings", "enable"), new XElement("AssemblyName", "CIS.TypeManager.ConflictFixture"),
                        new XElement("AssemblyVersion", variant == "v2" ? "2.0.0.0" : "1.0.0.0"), new XElement("TreatWarningsAsErrors", "true"), new XElement("IsPackable", "false"));
                    if (variant == "ref") { properties.Add(new XElement("ProduceOnlyReferenceAssembly", "true")); }
                    Write(Path.Combine(directory, "Fixture.csproj"), new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"), properties).ToString());
                    Copy(Path.Combine(fixtures, "Conflict", "Collision.cs"), Path.Combine(directory, "Collision.cs"));
                    if (variant == "self-reference")
                    {
                        Write(Path.Combine(directory, "ReferenceAttribute.cs"), """
                            #pragma warning disable CS0436 // Intentional local attribute constructor metadata fixture.
                            [assembly: System.Runtime.CompilerServices.ReferenceAssembly]
                            namespace System.Runtime.CompilerServices;
                            [AttributeUsage(AttributeTargets.Assembly)]
                            internal sealed class ReferenceAssemblyAttribute : Attribute;
                            """);
                    }
                    await input.Run("build-" + variant, ["build", Path.Combine(directory, "Fixture.csproj"), "-c", "Release", "-m:1", "--nologo", "-o", Path.Combine(root, "versions", variant)], language);
                }
                Require(JsonNode.DeepEquals(report["coreSourceInputs"], input.CoreSources()) && JsonNode.DeepEquals(report["coreProjectInputs"], input.CoreProjects()), "Core inputs changed during the producer build.");
            }

            string host = Path.Combine(root, "Host", "bin", "Release", "net10.0", "CIS.TypeManager.RuntimeHost.dll"), core = Path.Combine(Path.GetDirectoryName(host)!, "CIS.Core.dll");
            report["coreSha256"] = Hash(core);
            var fixtureFiles = new JsonObject();
            foreach (string name in new[] { "Broken", "Forwarder", "Forwarded", "Host", "versions" })
            { foreach ((string relative, JsonNode? hash) in TreeHashes(Path.Combine(root, name))) { fixtureFiles.Add(name + "/" + relative, hash!.DeepClone()); } }
            report["fixtureFiles"] = fixtureFiles;
            if (input.Optional("expect-core-sha256") is string expected) { Require(Hash(core) == expected.ToLowerInvariant(), "Candidate Core SHA-256 differs from the required frozen bytes."); }
            if (replay) { Require((string?)report["coreSha256"] == (string?)original!["coreSha256"] && JsonNode.DeepEquals(fixtureFiles, original["fixtureFiles"]), "Replay Core/fixtures differ from the completed producer."); }
            string runtime = (string)Read(Path.Combine(language, "eng", "toolchain.json"))["targetRuntime"]!;
            foreach (string mode in Modes)
            {
                ProcessResult child = await input.Run(mode, ["exec", "--fx-version", runtime, "--roll-forward", "Disable", host, mode, root, Path.Combine(root, "deployments", mode)], language, timeout: TimeSpan.FromSeconds(60));
                Require(child.StandardError.Length == 0, "Runtime fixture wrote stderr: " + mode);
                JsonObject observed = JsonNode.Parse(child.StandardOutput)!.AsObject();
                Require((bool?)observed["success"] == true && (string?)observed["mode"] == mode, "Invalid runtime fixture result: " + mode);
                Require((bool?)observed["containerConstructed"] == (mode is "forwarding-and-collisions" or "dynamic-name-collision" or "outside-catalog-late-load"), "Incorrect container boundary: " + mode);
                report["results"]!.AsArray().Add(observed); Save();
            }
            if (!replay)
            {
                await input.Run("build-helper", ["build", Path.Combine(language, "eng", "CIS.TypeManager.Build", "CIS.TypeManager.Build.csproj"), "-c", "Release", "-m:1", "--nologo"], language);
                CopyTree(Path.Combine(language, "eng", "CIS.TypeManager.Build", "bin", "Release", "net10.0"), Path.Combine(root, "helper"));
            }
            string helper = Path.Combine(root, "helper", "CIS.TypeManager.Build.dll"); report["helperSha256"] = Hash(helper);
            if (replay) { Require((string?)report["helperSha256"] == (string?)original!["helperSha256"], "Replay build helper changed."); }
            string project = Path.Combine(root, "CheckReference.proj");
            Write(project, new XElement("Project", new XElement("UsingTask", new XAttribute("TaskName", "CIS.TypeManagement.Build.WriteTypeInventory"), new XAttribute("TaskFactory", "TaskHostFactory"), new XAttribute("Runtime", "NET"), new XAttribute("AssemblyFile", helper)),
                new XElement("Target", new XAttribute("Name", "Check"), new XElement("WriteTypeInventory", new XAttribute("OutputDirectory", "$(FixtureDirectory)"), new XAttribute("HostFileName", "CIS.TypeManager.ConflictFixture.dll"), new XAttribute("Assets", "@(Empty)")))).ToString());
            foreach (string variant in new[] { "ref", "self-reference" })
            {
                await input.Run("generator-rejects-" + variant, ["msbuild", project, "-t:Check", "-m:2", "-nologo", "-p:FixtureDirectory=" + Path.Combine(root, "versions", variant)], language, expectedError: "is a reference assembly");
                report["results"]!.AsArray().Add(new JsonObject { ["mode"] = "generator-rejects-" + variant, ["success"] = true });
            }
            Require(JsonNode.DeepEquals(fixtureFiles, Snapshot()), "Producer fixture bytes changed during execution."); report["complete"] = true; Save(); return 0;

            JsonObject Snapshot()
            {
                var result = new JsonObject();
                foreach (string name in new[] { "Broken", "Forwarder", "Forwarded", "Host", "versions" })
                { foreach ((string relative, JsonNode? hash) in TreeHashes(Path.Combine(root, name))) { result.Add(name + "/" + relative, hash!.DeepClone()); } }
                return result;
            }
        }
        catch (Exception error) { report["error"] = error.Message; throw; }
        finally { Save(); }
    }
}
