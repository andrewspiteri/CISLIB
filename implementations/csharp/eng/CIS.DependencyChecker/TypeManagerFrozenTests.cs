using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

internal static class TypeManagerFrozenTests
{
    private static readonly string[] Dependencies = ["CIS.Core.dll", "Microsoft.Extensions.Logging.Abstractions.dll", "Microsoft.Extensions.DependencyInjection.Abstractions.dll"];
    private static readonly string[] RuntimeSources = ["UnitTests/TypeManagerManifestTests", "UnitTests/TypeManagerInputTests", "UnitTests/TypeManagerCatalogTests", "UnitTests/TypeManagerOrderingTests", "UnitTests/TypeManagerFailureContractTests", "UnitTests/TypeManagerFreshAssemblyTests", "ArchitectureTests/TypeManagerRuntimeArchitectureTests"];
    private static readonly Dictionary<string, int> BusinessMethods = new(StringComparer.Ordinal)
    {
        ["PreparationHandsUsableCatalogToApplicationBeforeItCreatesServices"] = 2,
        ["FailedPreparationNeverInvokesApplicationFactory"] = 4,
        ["PreCanceledPreparationNeverInvokesApplicationFactory"] = 1,
        ["ConcurrentConsumersReusePreparedCatalogWhileApplicationOwnsObjectLifetime"] = 1,
        ["RunningApplicationKeepsSnapshotWhileNextStartupValidatesChangedInventory"] = 1,
        ["FailedStartupCanBeRepairedWithIndependentPreparation"] = 1
    };

    internal static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0] is not ("runtime" or "business")) { throw new ArgumentException("Use runtime or business frozen tests."); }
        bool business = args[0] == "business";
        var input = new TypeManagerVerificationInputs(args[1..], business ? ["repo-root", "candidate-core", "candidate-report", "expect-core-sha256", "output-root"] : ["repo-root", "candidate-core", "output-root"], []);
        input.EmptyOutput();
        string root = input.Output, language = input.Language, core = input.PathOf("candidate-core"), pin = Hash(core);
        var report = new JsonObject
        {
            ["complete"] = false,
            ["scope"] = business ? "Development business stories against a frozen Core; not fresh-process discovery or final package qualification" : "81 Core unit cases plus 9 runtime architecture cases against frozen candidate",
            ["verifierSha256"] = Hash(typeof(TypeManagerFrozenTests).Assembly.Location),
            ["coreSha256"] = pin,
            ["inputs"] = new JsonArray()
        };
        var copies = new Dictionary<string, string>(StringComparer.Ordinal);
        void Retain(string source, string relative, string logical, string? expected = null)
        {
            string target = Path.Combine(root, relative);
            Copy(source, target, expected);
            copies.Add(relative, Hash(target));
            report["inputs"]!.AsArray().Add(new JsonObject { ["path"] = logical, ["copiedPath"] = relative, ["sha256"] = Hash(target) });
        }
        try
        {
            JsonObject? producer = null;
            if (business)
            {
                string producerPath = input.PathOf("candidate-report");
                producer = Read(producerPath, out string producerHash);
                report["candidateReportSha256"] = producerHash;
                report["moduleSourceInputs"] = ValidateProducer(language, core, producer, input.Get("expect-core-sha256"));
                Retain(producerPath, "candidate-report.json", "candidate-report.json", producerHash);
            }
            foreach (string name in new[] { "global.json", "Directory.Build.props", "Directory.Packages.props", "eng/toolchain.json" })
            { Retain(Path.Combine(language, name), Path.GetFileName(name), name); }
            if (!business) { Retain(Path.Combine(language, "NuGet.config"), "NuGet.config", "NuGet.config"); }
            else { Write(Path.Combine(root, "NuGet.config"), "<configuration><packageSources><clear/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders><auditSources><clear/></auditSources></configuration>"); }
            string[] sources = business ? ["tests/TestAssembly.cs", "tests/CIS.Core.BusinessCases/TypeManagerStartupStories.cs"]
                : RuntimeSources.Select(name => "tests/CIS.Core." + name + ".cs").ToArray();
            foreach (string source in sources) { Retain(Path.Combine(language, source), Path.GetFileName(source), source); }
            string toolDirectory = Path.GetDirectoryName(typeof(TypeManagerFrozenTests).Assembly.Location)!;
            foreach (string file in Directory.GetFiles(toolDirectory).Where(path => path.EndsWith(".dll", StringComparison.Ordinal) || path.EndsWith(".deps.json", StringComparison.Ordinal) || path.EndsWith(".runtimeconfig.json", StringComparison.Ordinal)))
            { Retain(file, ".evidence-tool/" + Path.GetFileName(file), ".evidence-tool/" + Path.GetFileName(file)); }
            foreach (string name in Dependencies)
            {
                string? expected = producer?["fixtureFiles"]?["Host/bin/Release/net10.0/" + name]?.GetValue<string>();
                Require(!business || expected is not null, "Candidate dependency is missing from producer: " + name);
                Retain(name == "CIS.Core.dll" ? core : Path.Combine(Path.GetDirectoryName(core)!, name), "candidate/" + name, "candidate/" + name, expected);
            }
            if (!business) { Write(Path.Combine(root, "inputs.json"), report["inputs"]!.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true })); }
            JsonObject policy = Read(Path.Combine(root, "toolchain.json"));
            string runtime = (string)policy["targetRuntime"]!;
            Write(Path.Combine(root, "Directory.Build.targets"), "<Project />");
            string projectName = business ? "BusinessTests" : "RuntimeTests", assemblyName = business ? "CIS.Core.BusinessCases" : "CIS.Core.UnitTests";
            var project = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
                new XElement("PropertyGroup", new XElement("AssemblyName", assemblyName), new XElement("IsTestProject", true), new XElement("EnableMSTestRunner", false),
                    new XElement("UseVSTest", true), new XElement("AllowUnsafeBlocks", true), new XElement("RuntimeFrameworkVersion", runtime), new XElement("RollForward", "Disable"), new XElement("NuGetAudit", false)),
                new XElement("ItemGroup", Dependencies.Select(name => new XElement("Reference", new XAttribute("Include", Path.GetFileNameWithoutExtension(name)), new XAttribute("HintPath", "candidate/" + name))),
                    new[] { "Microsoft.NET.Test.Sdk", "MSTest.TestFramework", "MSTest.TestAdapter" }.Select(name => new XElement("PackageReference", new XAttribute("Include", name)))));
            Write(Path.Combine(root, projectName + ".csproj"), project.ToString());
            if (!business) { Write(Path.Combine(root, "AssemblyInfo.cs"), "[assembly: Microsoft.VisualStudio.TestTools.UnitTesting.DoNotParallelize]\n"); }
            string sdk = (await input.Run("sdk", ["--version"])).StandardOutput.Trim();
            Require(sdk == (string?)policy["sdk"], "Runtime verifier SDK differs from its pin.");
            string trx = Path.Combine(root, "results", business ? "business.trx" : "runtime-tests.trx");
            await input.Run("execution", ["test", projectName + ".csproj", "-c", "Release", "-m:1", "--results-directory", "results", "--logger", "trx;LogFileName=" + Path.GetFileName(trx)], timeout: TimeSpan.FromMinutes(5));
            JsonObject native = VerificationEvidence.ReadTrx(trx);
            ValidateNative(native, business);
            if (business)
            {
                report["observedRuntime"] = ObserveRuntime(trx, runtime, pin);
                report["binaryFiles"] = TreeHashes(Path.Combine(root, "bin/Release/net10.0"));
                foreach (string name in Dependencies)
                { Require((string?)report["binaryFiles"]![name] == copies["candidate/" + name], "Executed dependency differs from candidate: " + name); }
            }
            foreach ((string relative, string hash) in copies) { Require(Hash(Path.Combine(root, relative)) == hash, "Frozen input changed: " + relative); }
            Require(Hash(core) == pin, "Candidate Core changed during qualification.");
            report["native"] = native;
            foreach (string name in new[] { "total", "passed", "failed", "skipped" }) { report[name] = native[name]!.DeepClone(); }
            report["sdk"] = sdk; report["runtimePin"] = runtime; report["pinnedRuntime"] = runtime; report["trxSha256"] = Hash(trx); report["complete"] = true;
            return 0;
        }
        catch (Exception error) { report["error"] = error.Message; throw; }
        finally { WriteJson(Path.Combine(root, "results.json"), report); }
    }

    internal static JsonObject ValidateProducer(string language, string core, JsonObject producer, string expected)
    {
        Require(Hash(core) == expected, "Candidate Core differs from the independent pin.");
        Require((bool?)producer["complete"] == true && (string?)producer["coreSha256"] == expected, "Candidate producer does not match Core.");
        JsonObject module = TreeHashes(Path.Combine(language, "src/CIS.Core/TypeManagement"), path => path.EndsWith(".cs", StringComparison.Ordinal));
        var actual = new JsonObject(module.Select(item => new KeyValuePair<string, JsonNode?>("TypeManagement/" + item.Key, item.Value!.DeepClone())));
        var selected = new JsonObject(producer["coreSourceInputs"]!.AsObject().Where(item => item.Key.StartsWith("TypeManagement/", StringComparison.Ordinal)).Select(item => new KeyValuePair<string, JsonNode?>(item.Key, item.Value!.DeepClone())));
        Require(JsonNode.DeepEquals(actual, selected), "Candidate module source set/hashes differ.");
        return actual;
    }

    internal static void ValidateNative(JsonObject native, bool business)
    {
        int expected = business ? 10 : 90;
        Require((int?)native["total"] == expected && (int?)native["passed"] == expected && (int?)native["failed"] == 0 && (int?)native["skipped"] == 0, "Unexpected Type Manager test count or outcome.");
        if (!business) { return; }
        var actual = native["results"]!.AsArray().GroupBy(item => (string?)item!["method"], StringComparer.Ordinal).ToDictionary(group => group.Key!, group => group.Count(), StringComparer.Ordinal);
        Require(actual.Count == BusinessMethods.Count && BusinessMethods.All(item => actual.TryGetValue("CIS.Tests.BusinessCases.TypeManagerStartupStories." + item.Key, out int count) && count == item.Value), "Unexpected business method/count.");
    }

    internal static JsonArray ObserveRuntime(string trx, string runtime, string pin)
    {
        string os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "unsupported";
        Require(os != "unsupported", "Unsupported runtime qualification OS.");
        var observed = new JsonArray();
        foreach (XElement result in XDocument.Load(trx).Descendants().Where(item => item.Name.LocalName == "UnitTestResult"))
        {
            string stdout = string.Join("\n", result.Descendants().Where(item => item.Name.LocalName == "StdOut").Select(item => item.Value));
            string[] lines = stdout.Split('\n').Select(line => line.Trim()).Where(line => line.StartsWith("{\"runtime\":", StringComparison.Ordinal)).ToArray();
            Require(lines.Length == 1, "Missing or ambiguous runtime identity in native case output.");
            JsonObject identity = JsonNode.Parse(lines[0])!.AsObject();
            Require((string?)identity["runtime"] == ".NET " + runtime && (string?)identity["os"] == os && (string?)identity["architecture"] == "X64" && (string?)identity["coreSha256"] == pin, "Executed runtime/OS/architecture/Core differs from its qualification pin.");
            observed.Add(new JsonObject { ["testId"] = (string?)result.Attribute("testId"), ["identity"] = identity });
        }
        Require(observed.Count == 10, "Missing native business runtime observations.");
        return observed;
    }
}
