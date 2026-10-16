using System.Text.Json.Nodes;
using System.Xml.Linq;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

internal static class TypeManagerMutationPreparation
{
    internal static int Run(string[] arguments)
    {
        var input = new TypeManagerVerificationInputs(arguments, ["repo-root", "output-root", "candidate-core", "candidate-report", "expected-candidate-report-sha256", "expected-core-sha256"], []);
        string root = input.Output, language = input.Language, candidate = input.PathOf("candidate-core"), producerPath = input.PathOf("candidate-report");
        if (Directory.Exists(root)) { Require(!Directory.EnumerateFileSystemEntries(root).Any(), "Use an empty output directory"); }
        string reportPin = input.Get("expected-candidate-report-sha256"), corePin = input.Get("expected-core-sha256");
        JsonObject producer = ReadPinned(producerPath, reportPin, "Candidate producer differs from the independent report pin");
        Require(Hash(candidate) == corePin && (bool?)producer["complete"] == true && (string?)producer["coreSha256"] == corePin, "Completed producer/Core differs from the independent pin");
        string coreRoot = Path.Combine(language, "src", "CIS.Core"), module = Path.Combine(coreRoot, "TypeManagement");
        JsonObject moduleInputs = new(Directory.GetFiles(module, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(p => new KeyValuePair<string, JsonNode?>(Path.GetRelativePath(coreRoot, p).Replace('\\', '/'), JsonValue.Create(Hash(p)))));
        JsonObject expected = new(producer["coreSourceInputs"]!.AsObject().Where(p => p.Key.StartsWith("TypeManagement/", StringComparison.Ordinal)).Select(p => new KeyValuePair<string, JsonNode?>(p.Key, p.Value!.DeepClone())));
        Require(moduleInputs.Count > 0 && JsonNode.DeepEquals(moduleInputs, expected), "Module source set/hashes differ from the candidate producer");
        Require(moduleInputs.All(p => p.Key.Split('/').Length == 2), "Expand the mutation glob before adding nested module sources");
        Directory.CreateDirectory(root); string source = Path.Combine(root, "source"); var inputs = new JsonObject();
        void CopyInput(string path, string relative, string? pin = null) { Copy(path, Path.Combine(source, relative), pin); inputs[relative] = Hash(Path.Combine(source, relative)); }
        foreach ((string relative, JsonNode? hash) in moduleInputs) { CopyInput(Path.Combine(coreRoot, relative), "Core/" + relative, (string)hash!); }
        foreach (string relative in new[] {
            "tests/CIS.Core.UnitTests/TypeManagerInputTests.cs", "tests/CIS.Core.UnitTests/TypeManagerManifestTests.cs", "tests/CIS.Core.UnitTests/TypeManagerCatalogTests.cs",
            "tests/CIS.Core.UnitTests/TypeManagerOrderingTests.cs", "tests/CIS.Core.UnitTests/TypeManagerFailureContractTests.cs", "tests/CIS.Core.UnitTests/TypeManagerFreshAssemblyTests.cs",
            "tests/CIS.Core.ArchitectureTests/TypeManagerRuntimeArchitectureTests.cs", "tests/CIS.Core.BusinessCases/TypeManagerStartupStories.cs" })
        { Require(File.Exists(Path.Combine(language, relative)), "Missing canonical test input: " + relative); CopyInput(Path.Combine(language, relative), "Tests/" + Path.GetFileName(relative)); }
        foreach (string name in new[] { "global.json", "Directory.Build.props", "Directory.Packages.props" }) { CopyInput(Path.Combine(language, name), name); }
        CopyInput(Path.Combine(language, "eng", "toolchain.json"), "toolchain.json");
        CopyInput(Path.Combine(language, "verification", "mutation", "type-manager-config.json"), "stryker-config.json");
        JsonObject policy = Read(Path.Combine(source, "toolchain.json")); Require((string?)policy["targetFramework"] == "net10.0", "This mutation project supports net10.0 only");
        var references = new List<XElement>();
        foreach (string name in new[] { "Microsoft.Extensions.Logging.Abstractions", "Microsoft.Extensions.DependencyInjection.Abstractions" })
        {
            string file = name + ".dll", path = Path.Combine(Path.GetDirectoryName(candidate)!, file);
            string pin = (string)producer["fixtureFiles"]!["Host/bin/Release/net10.0/" + file]!;
            CopyInput(path, "references/" + file, pin);
            references.Add(new XElement("Reference", new XAttribute("Include", name), new XAttribute("HintPath", "../references/" + file)));
        }
        Write(Path.Combine(source, "Directory.Build.targets"), "<Project />");
        Write(Path.Combine(source, "NuGet.config"), "<configuration><packageSources><clear/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders><auditSources><clear/></auditSources></configuration>");
        Write(Path.Combine(source, "Core", "Core.csproj"), """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><AssemblyName>CIS.Core</AssemblyName><AssemblyVersion>1.0.0.0</AssemblyVersion><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup><InternalsVisibleTo Include="CIS.Core.UnitTests" /></ItemGroup></Project>""");
        var project = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"), new XElement("PropertyGroup", new XElement("AssemblyName", "CIS.Core.UnitTests"), new XElement("IsTestProject", "true"),
            new XElement("AllowUnsafeBlocks", "true"), new XElement("RuntimeFrameworkVersion", (string)policy["targetRuntime"]!), new XElement("RollForward", "Disable"), new XElement("EnableMSTestRunner", "false"), new XElement("NuGetAudit", "false")),
            new XElement("ItemGroup", new XElement("ProjectReference", new XAttribute("Include", "../Core/Core.csproj")), references, new[] { "Microsoft.NET.Test.Sdk", "MSTest.TestFramework", "MSTest.TestAdapter" }.Select(n => new XElement("PackageReference", new XAttribute("Include", n)))));
        Write(Path.Combine(source, "Tests", "Tests.csproj"), project.ToString());
        Write(Path.Combine(source, "Tests", "AssemblyInfo.cs"), "[assembly: Microsoft.VisualStudio.TestTools.UnitTesting.DoNotParallelize]");
        var report = new JsonObject
        {
            ["prepared"] = true,
            ["qualified"] = false,
            ["scope"] = "Isolated source-module mutation; not shipping Core",
            ["candidateCoreSha256"] = corePin,
            ["candidateReportSha256"] = reportPin,
            ["preparerSha256"] = Hash(typeof(TypeManagerMutationPreparation).Assembly.Location),
            ["inputs"] = inputs,
            ["generatedFiles"] = TreeHashes(source),
            ["expectedBaselineCases"] = 100,
            ["baselineCommand"] = "dotnet test Tests/Tests.csproj -c Release -m:1 --logger trx",
            ["mutationCommand"] = "dotnet exec --fx-version " + (string)policy["toolHostRuntime"]! + " --roll-forward Disable <Stryker.CLI.dll> --config-file stryker-config.json --output <report-directory>",
            ["workingDirectory"] = source
        };
        WriteJson(Path.Combine(root, "preparation.json"), report); return 0;
    }

    internal static async Task<int> RunControlsAsync(string[] arguments)
    {
        var input = new TypeManagerVerificationInputs(arguments, ["repo-root", "candidate-core", "candidate-report", "output-root"], []);
        input.EmptyOutput(); string root = input.Output, candidate = input.PathOf("candidate-core"), producer = input.PathOf("candidate-report");
        string candidatePin = Hash(candidate), producerPin = Hash(producer), tool = typeof(TypeManagerMutationPreparation).Assembly.Location;
        string changedCore = Path.Combine(root, "changed-core.dll"), changedSource = Path.Combine(root, "changed-source.json"), changedDependency = Path.Combine(root, "dependency", "CIS.Core.dll");
        Copy(candidate, changedCore); using (FileStream stream = new(changedCore, FileMode.Append)) { stream.WriteByte(0); }
        JsonObject changed = ReadPinned(producer, producerPin, "Control producer report pin mismatch"); changed["coreSourceInputs"]!["TypeManagement/TypeManager.cs"] = new string('0', 64); WriteJson(changedSource, changed);
        Copy(candidate, changedDependency);
        foreach (string file in new[] { "Microsoft.Extensions.Logging.Abstractions.dll", "Microsoft.Extensions.DependencyInjection.Abstractions.dll" })
        { Copy(Path.Combine(Path.GetDirectoryName(candidate)!, file), Path.Combine(Path.GetDirectoryName(changedDependency)!, file)); }
        using (FileStream stream = new(Path.Combine(Path.GetDirectoryName(changedDependency)!, "Microsoft.Extensions.Logging.Abstractions.dll"), FileMode.Append)) { stream.WriteByte(0); }
        var report = new JsonObject { ["complete"] = false, ["qualified"] = false, ["verifierSha256"] = Hash(tool), ["results"] = new JsonArray() };
        string path = Path.Combine(root, "results.json"); WriteJson(path, report);
        try
        {
            string runtime = (string)Read(Path.Combine(input.Language, "eng", "toolchain.json"))["targetRuntime"]!;
            foreach ((string name, string core, string record, string pin, string reason) in new[] {
                ("changed-report", candidate, producer, new string('0', 64), "Candidate producer differs from the independent report pin"),
                ("changed-core", changedCore, producer, producerPin, "Completed producer/Core differs from the independent pin"),
                ("changed-source", candidate, changedSource, Hash(changedSource), "Module source set/hashes differ from the candidate producer"),
                ("changed-dependency", changedDependency, producer, producerPin, "Input differs from its pin:") })
            {
                string output = Path.Combine(root, name);
                await input.Run(name, ["exec", "--fx-version", runtime, "--roll-forward", "Disable", tool, "type-manager-mutation-prepare",
                    "--repo-root", input.PathOf("repo-root"), "--candidate-core", core, "--candidate-report", record, "--output-root", output,
                    "--expected-candidate-report-sha256", pin, "--expected-core-sha256", candidatePin], expectedError: reason);
                Require(!File.Exists(Path.Combine(output, "preparation.json")) && !Directory.Exists(Path.Combine(output, "source", "Core", "bin")), "Rejected preparation produced a success record or binaries");
                report["results"]!.AsArray().Add(new JsonObject { ["case"] = name, ["passed"] = true, ["rejectedFor"] = reason }); WriteJson(path, report);
            }
            Require(Hash(candidate) == candidatePin && Hash(producer) == producerPin, "Original inputs changed"); report["complete"] = true; return 0;
        }
        catch (Exception error) { report["error"] = error.Message; throw; }
        finally { WriteJson(path, report); }
    }
}
