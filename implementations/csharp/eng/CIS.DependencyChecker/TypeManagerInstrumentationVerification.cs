using System.Text.Json.Nodes;
using System.Xml.Linq;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

internal static class TypeManagerInstrumentationVerification
{
    internal static async Task<int> RunAsync(string[] arguments)
    {
        var input = new TypeManagerVerificationInputs(arguments, ["repo-root", "output-root", "candidate-core", "candidate-report"], ["replay-root"]);
        input.EmptyOutput(); string root = input.Output, language = input.Language, source = Path.Combine(language, "src", "CIS.Core"), templates = Path.Combine(language, "tests", "fixtures", "TypeManager", "Instrumentation");
        string factory = Path.Combine(source, "TypeManagement", "TypeManager.cs");
        var report = new JsonObject
        {
            ["complete"] = false,
            ["scope"] = "Test-only source instrumentation, not a shipping binary",
            ["candidateCoreSha256"] = Hash(input.PathOf("candidate-core")),
            ["factorySourceSha256"] = Hash(factory),
            ["candidateReportSha256"] = Hash(input.PathOf("candidate-report")),
            ["verifierSha256"] = Hash(typeof(TypeManagerInstrumentationVerification).Assembly.Location),
            ["templateInputs"] = TreeHashes(templates, p => p.EndsWith(".cs", StringComparison.Ordinal)),
            ["results"] = new JsonArray()
        };
        void Save() => WriteJson(Path.Combine(root, "results.json"), report); Save();
        var environment = new Dictionary<string, string?> { ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1", ["MSBUILDDISABLENODEREUSE"] = "1" };
        try
        {
            JsonObject producer = Read(input.PathOf("candidate-report")), sources = input.CoreSources(), projects = input.CoreProjects();
            Require((bool?)producer["complete"] == true && (string?)producer["coreSha256"] == (string?)report["candidateCoreSha256"], "Candidate producer report does not match the frozen Core bytes");
            Require((string?)producer["mode"] == "build-and-run" && (string?)producer["verifierSha256"] == Hash(typeof(TypeManagerRuntimeVerification).Assembly.Location), "Candidate report must come from the current build-and-run producer");
            Require(JsonNode.DeepEquals(sources, producer["coreSourceInputs"]) && JsonNode.DeepEquals(projects, producer["coreProjectInputs"]), "Candidate source provenance differs from the source selected for instrumentation");
            Require((string?)report["factorySourceSha256"] == (string?)sources["TypeManagement/TypeManager.cs"], "Factory source changed while capturing provenance");
            report["sourceProvenanceScope"] = "Core C# sources and five named project/config files; not all transitive MSBuild inputs; producer report is trusted";
            report["sourceGitHead"] = producer["sourceGitHead"]?.DeepClone();
            foreach (string name in new[] { "global.json", "Directory.Build.props", "Directory.Packages.props", "NuGet.config" }) { Copy(Path.Combine(language, name), Path.Combine(root, name), (string)projects[name]!); }
            report["sdk"] = (await input.Run("sdk", ["--version"], environment: environment)).StandardOutput.Trim();
            Require((string?)report["sdk"] == (string?)Read(Path.Combine(root, "global.json"))["sdk"]!["version"], "Instrumentation SDK differs from the pin");
            string[] variants = ["instrumented", "enumerate-twice", "query-enumeration", "skip-final-cancellation"];
            JsonObject? original = null;
            if (input.Optional("replay-root") is not null)
            {
                string prior = input.PathOf("replay-root"); original = Read(Path.Combine(prior, "results.json"));
                Require((bool?)original["complete"] == true && new[] { "candidateCoreSha256", "candidateReportSha256", "factorySourceSha256", "verifierSha256", "templateInputs" }.All(key => JsonNode.DeepEquals(report[key], original[key])), "Replay differs from the completed source/candidate/verifier");
                CopyTree(Path.Combine(prior, "binaries"), Path.Combine(root, "binaries"));
                report["sourceInputs"] = original["sourceInputs"]!.DeepClone(); report["instrumentedFactoryHashes"] = original["instrumentedFactoryHashes"]!.DeepClone();
            }
            else
            {
                report["sourceInputs"] = sources.DeepClone(); report["instrumentedFactoryHashes"] = new JsonObject();
                foreach (string variant in variants)
                {
                    string variantRoot = Path.Combine(root, "generated", variant), core = Path.Combine(variantRoot, "Core");
                    foreach ((string relative, JsonNode? hash) in sources) { Copy(Path.Combine(source, relative), Path.Combine(core, relative), (string)hash!); }
                    string coreProject = Path.Combine(source, "CIS.Core.csproj"); Require(Hash(coreProject) == (string?)projects["src/CIS.Core/CIS.Core.csproj"], "Core project changed while copying");
                    XElement project = XElement.Load(coreProject);
                    foreach (XElement element in project.Elements().SelectMany(group => group.Elements()).Where(e => e.Name.LocalName is "ProjectReference" or "None").ToArray()) { element.Remove(); }
                    foreach (XElement element in project.Elements().Where(e => e.Name.LocalName == "Import").ToArray()) { element.Remove(); }
                    XElement packable = project.Elements("PropertyGroup").Elements("IsPackable").Single(); packable.Value = "false";
                    project.Add(new XElement("ItemGroup", new XElement("InternalsVisibleTo", new XAttribute("Include", "CIS.TypeManager.InstrumentationHost"))));
                    Write(Path.Combine(core, "Core.csproj"), project.ToString());
                    string text = File.ReadAllText(Path.Combine(core, "TypeManagement", "TypeManager.cs")).Replace("\r\n", "\n", StringComparison.Ordinal);

                    const string enumeration = "var types = assembly.GetTypes();", wrapped = "var types = TypeManagerTestProbe.Enumerate(assembly);", final = "cancellationToken.ThrowIfCancellationRequested();\n        return manager;";
                    text = ReplaceOnce(text, enumeration, wrapped);
                    text = ReplaceOnce(text, final, "TypeManagerTestProbe.BeforePublication?.Invoke();\n        " + final);
                    if (variant == "enumerate-twice") { text = ReplaceOnce(text, wrapped, "_ = TypeManagerTestProbe.Enumerate(assembly);\n                " + wrapped); }
                    else if (variant == "query-enumeration") { const string anchor = "PreparationInputs.ValidateContract(contractType);"; text = ReplaceOnce(text, anchor, anchor + "\n        _ = TypeManagerTestProbe.Enumerate(Assemblies[0]);"); }
                    else if (variant == "skip-final-cancellation") { text = ReplaceOnce(text, final, "return manager;"); }
                    string changed = Path.Combine(core, "TypeManagement", "TypeManager.cs"); Write(changed, text); report["instrumentedFactoryHashes"]![variant] = Hash(changed);
                    Copy(Path.Combine(templates, "TypeManagerTestProbe.cs"), Path.Combine(core, "TypeManagerTestProbe.cs"), (string)report["templateInputs"]!["TypeManagerTestProbe.cs"]!);
                    string library = Path.Combine(variantRoot, "Library"), host = Path.Combine(variantRoot, "Host");
                    Write(Path.Combine(library, "Library.csproj"), """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><AssemblyName>CIS.TypeManager.ProbeLibrary</AssemblyName></PropertyGroup></Project>""");
                    Copy(Path.Combine(templates, "ProbeLibrary.cs"), Path.Combine(library, "ProbeLibrary.cs"), (string)report["templateInputs"]!["ProbeLibrary.cs"]!);
                    Write(Path.Combine(host, "Host.csproj"), """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><AssemblyName>CIS.TypeManager.InstrumentationHost</AssemblyName></PropertyGroup><ItemGroup><ProjectReference Include="../Core/Core.csproj"/><ProjectReference Include="../Library/Library.csproj"/></ItemGroup></Project>""");
                    Copy(Path.Combine(templates, "Program.cs"), Path.Combine(host, "Program.cs"), (string)report["templateInputs"]!["Program.cs"]!);
                    await input.Run("build-" + variant, ["build", Path.Combine(host, "Host.csproj"), "-c", "Release", "-m:1", "--nologo"], environment: environment);
                    CopyTree(Path.Combine(host, "bin", "Release", "net10.0"), Path.Combine(root, "binaries", variant));
                }
            }
            report["binaryFiles"] = TreeHashes(Path.Combine(root, "binaries"));
            if (original is not null) { Require(JsonNode.DeepEquals(report["binaryFiles"], original["binaryFiles"]), "Instrumented/control replay bytes differ from the completed run"); }
            string runtime = (string)Read(Path.Combine(language, "eng", "toolchain.json"))["targetRuntime"]!;
            foreach ((string variant, string mode, string? error) in new (string, string, string?)[] { ("instrumented", "enumeration", null), ("instrumented", "publication-cancellation", null), ("enumerate-twice", "enumeration", "ENUMERATION_COUNT:"), ("query-enumeration", "enumeration", "QUERY_ENUMERATION:"), ("skip-final-cancellation", "publication-cancellation", "PUBLICATION_CANCELLATION:") })
            {
                string name = variant + "-" + mode;
                var child = await input.Run(name, ["exec", "--fx-version", runtime, "--roll-forward", "Disable", Path.Combine(root, "binaries", variant, "CIS.TypeManager.InstrumentationHost.dll"), mode, Path.Combine(root, "deployments", name)], expectedError: error, environment: environment, timeout: TimeSpan.FromSeconds(60));
                var result = new JsonObject { ["case"] = name, ["passed"] = true };
                if (error is not null) { result["rejectedFor"] = error; }
                else
                {
                    Require(child.StandardError.Length == 0, "Instrumentation fixture wrote stderr");
                    JsonObject observed = JsonNode.Parse(child.StandardOutput)!.AsObject(); Require((bool?)observed["success"] == true && (string?)observed["mode"] == mode, "Invalid instrumentation report"); result["observed"] = observed;
                }
                report["results"]!.AsArray().Add(result); Save();
            }
            Require(JsonNode.DeepEquals(report["binaryFiles"], TreeHashes(Path.Combine(root, "binaries"))), "Instrumentation binaries changed during execution");
            report["complete"] = true; Save(); return 0;
        }
        catch (Exception error) { report["error"] = error.Message; throw; }
        finally { Save(); }
    }
    private static string ReplaceOnce(string source, string before, string after)
    {
        int index = source.IndexOf(before, StringComparison.Ordinal);
        Require(index >= 0 && source.IndexOf(before, index + before.Length, StringComparison.Ordinal) < 0, "Instrumentation anchor is absent or ambiguous: " + before);
        return source[..index] + after + source[(index + before.Length)..];
    }
}
