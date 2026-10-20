using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.TestSupport;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

// Isolated native Core mutation execution. Compiler/filter dispositions remain explicit evidence.
internal static class LoggingMutationQualification
{
    internal static async Task<int> RunAsync(string[] args, string scope = "frontend")
    {
        bool fullCore = scope == "core", helper = scope == "helper";
        Require(scope is "frontend" or "core" or "helper", "Unknown mutation scope.");
        var input = new TypeManagerVerificationInputs(args, ["repo-root", "output-root"], []);
        input.EmptyOutput();
        string repo = input.PathOf("repo-root"), output = input.Output;
        const string prefix = "implementations/csharp/";
        SourceInventoryEntry[] inventory = (await SourceInventory.CaptureAsync(repo)).Where(item =>
            item.Path.StartsWith(prefix, StringComparison.Ordinal) || item.Path.StartsWith("conformance/", StringComparison.Ordinal) || item.Path.StartsWith("contracts/", StringComparison.Ordinal) ||
            item.Path.StartsWith("compatibility/csharp/", StringComparison.Ordinal) || item.Path == "README.md").ToArray();
        string snapshot = Path.Combine(output, "source"), language = Path.Combine(snapshot, prefix);
        foreach (var pin in inventory) { Copy(Path.Combine(repo, pin.Path), Path.Combine(snapshot, pin.Path), pin.Sha256); }
        Write(Path.Combine(output, "snapshot-inputs.json"), JsonSerializer.Serialize(inventory, new JsonSerializerOptions { WriteIndented = true }));
        JsonObject policy = Read(Path.Combine(language, "eng/toolchain.json"));
        Require((string?)policy["sdk"] == "10.0.401" && (string?)policy["targetRuntime"] == "10.0.12" && (string?)policy["toolHostRuntime"] == "8.0.31", "Requalify mutation toolchain pins.");
        if (helper) { PrepareHelperTests(language); }
        string projectRoot = helper ? "eng/CIS.TypeManager.Build" : "src/CIS.Core";
        string configPath = Path.Combine(language, "verification/mutation", helper ? "build-helper-config.json" : fullCore ? "core-release-config.json" : "frontend-interpolation-config.json");
        JsonObject config = Read(configPath)["stryker-config"]!.AsObject();
        string filter = (string?)config["test-case-filter"] ?? "FullyQualifiedName~CIS.Tests";
        var environment = new Dictionary<string, string?> { ["CisMutationTesting"] = "true", ["MSBUILDDISABLENODEREUSE"] = "1", ["UseSharedCompilation"] = "false", ["RestoreLockedMode"] = "true" };
        async Task<ProcessResult> Run(string name, string[] command, bool success = true, string? cwd = null)
        {
            Console.WriteLine("Logging mutation: " + name);
            return await input.Run(name, command, cwd ?? language, environment: environment, timeout: TimeSpan.FromMinutes(30), requireSuccess: success);
        }
        ProcessResult sdk = await Run("sdk", ["--version"]);
        Require(sdk.StandardOutput.Trim() == "10.0.401", "Mutation SDK differs from its pin.");
        string project = helper ? "tests/CIS.TypeManager.Build.MutationTests/CIS.TypeManager.Build.MutationTests.csproj" : "tests/CIS.Core.UnitTests/CIS.Core.UnitTests.csproj";
        await Run("restore", ["restore", project, "--locked-mode", "--configfile", "NuGet.config"]);
        await Run("build", ["build", project, "-c", "Release", "--no-restore"]);
        if (helper)
        {
            Write(Path.Combine(language, "tests/CIS.TypeManager.Build.MutationTests/bin/Release/net10.0/reference-path.txt"),
                Path.Combine(language, "eng/CIS.TypeManager.Build/obj/Release/net10.0/ref/CIS.TypeManager.Build.dll"));
            Write(Path.Combine(language, "tests/CIS.TypeManager.Build.MutationTests/bin/Release/net10.0/satellite-path.txt"),
                Path.Combine(language, "tests/fixtures/BuildHelperSatellite/bin/Release/net10.0/BuildHelperSatellite.dll"));
        }
        await Run("baseline", ["test", project, "-c", "Release", "--no-build", "--no-restore", "--filter", filter,
            "--logger", "trx;LogFileName=baseline.trx", "--results-directory", output]);
        RequirePassingSerializedTrx(Path.Combine(output, "baseline.trx"));
        ProcessResult packages = await Run("global-packages", ["nuget", "locals", "global-packages", "--list", "--force-english-output"]);
        string packageLine = packages.StandardOutput.Trim();
        int separator = packageLine.IndexOf(": ", StringComparison.Ordinal);
        Require(separator >= 0, "Cannot resolve global package directory.");
        string version = policy["tools"]!.AsArray().Single(tool => (string?)tool!["id"] == "dotnet-stryker")!["version"]!.GetValue<string>();
        Require(version == "4.16.0", "Requalify the mutation tool version.");
        string tool = Path.Combine(packageLine[(separator + 2)..].Trim(), "dotnet-stryker", version, "tools/net8.0/any/Stryker.CLI.dll");
        Require(File.Exists(tool), "Restore pinned dotnet-stryker before mutation qualification.");
        string native = Path.Combine(output, "native");
        ProcessResult mutation;
        try
        {
            // UTF-8 replacement is only diagnostic decoding; preserve the native streams separately.
            Console.WriteLine("Logging mutation: native run");
            mutation = await ProcessRunner.RunAsync(input.Dotnet,
                ["exec", "--fx-version", "8.0.31", "--roll-forward", "Disable", tool, "--config-file", configPath,
                    "--output", native, "--skip-version-check", "--log-to-file"], Path.Combine(language, projectRoot),
                TimeSpan.FromHours(12), environment, Encoding.UTF8);
            MicrosoftLoggingQualification.RecordMutationProcess(output, mutation);
        }
        finally
        {
            foreach (var pin in inventory)
            {
                Require(Hash(Path.Combine(repo, pin.Path)) == pin.Sha256 && Hash(Path.Combine(snapshot, pin.Path)) == pin.Sha256,
                    "Mutation source/input changed: " + pin.Path);
            }
            await Run("restored-build", ["build", project, "-c", "Release", "--no-restore", "--no-incremental"]);
        }
        await Run("restored-tests", ["test", project, "-c", "Release", "--no-build", "--no-restore", "--filter", filter,
            "--logger", "trx;LogFileName=restored.trx", "--results-directory", output]);
        RequirePassingSerializedTrx(Path.Combine(output, "restored.trx"));
        string reportPath = Path.Combine(native, "reports/mutation-report.json");
        JsonObject report = Read(reportPath);
        var files = report["files"]!.AsObject().Where(file => file.Value!["mutants"]!.AsArray().Count > 0).ToArray();
        string[] expected = config["mutate"]!.AsArray().Select(path => path!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray();
        string Relative(string path) => Path.GetRelativePath(Path.Combine(language, projectRoot), Path.GetFullPath(path)).Replace('\\', '/');
        if (fullCore || helper)
        {
            Require(expected.SequenceEqual(["**/*.cs"], StringComparer.Ordinal) && config["ignore-mutations"] is null && config["ignore-methods"] is null,
                "Full assembly selection requires all source files without mutation or method exclusions.");
            string sourcePrefix = prefix + projectRoot + "/";
            string[] declaredSources = inventory.Where(item => item.Path.StartsWith(sourcePrefix, StringComparison.Ordinal) && item.Path.EndsWith(".cs", StringComparison.Ordinal))
                .Select(item => item.Path[sourcePrefix.Length..]).Order(StringComparer.Ordinal).ToArray();
            Require(declaredSources.Length > 0 && report["files"]!.AsObject().Select(file => Relative(file.Key)).Order(StringComparer.Ordinal)
                .SequenceEqual(declaredSources, StringComparer.Ordinal), "Native report omits or adds shipping source files.");
        }
        else
        {
            Require(files.Select(file => file.Key.Replace('\\', '/').Split("/src/CIS.Core/", 2)[1]).Order(StringComparer.Ordinal).SequenceEqual(expected, StringComparer.Ordinal), "Mutation scope differs from the six-file selection.");
        }
        var counts = files.SelectMany(file => file.Value!["mutants"]!.AsArray()).GroupBy(mutant => mutant!["status"]!.GetValue<string>())
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        foreach (var file in files)
        {
            string relative = Relative(file.Key);
            Require(!relative.StartsWith("../", StringComparison.Ordinal), "Native source escaped its shipping project.");
            Require(file.Value!["source"]!.GetValue<string>() == File.ReadAllText(Path.Combine(language, projectRoot, relative)), "Native report source mismatch: " + relative);
        }
        var controls = new JsonArray();
        foreach ((string name, string suffix, int line, string replacement) in helper ? new[] { ("helper-success", "/WriteTypeInventory.cs", 29, "false") } : new[]
        {
            ("caller-capture", "/Events/InterpolationCapture.cs", 85, "\"\""),
            ("consumer-render", "/Events/CapturedInterpolation.cs", 60, ";"),
            ("batch-sort", "/Runtime/IncomingBatchIndex.cs", 16, ";"),
            ("closed-admission", "/Runtime/IncomingRingBuffer.cs", 41, "{}")
        })
        {
            JsonObject file = files.Single(file => file.Key.Replace('\\', '/').EndsWith(suffix, StringComparison.Ordinal)).Value!.AsObject();
            JsonObject mutant = file["mutants"]!.AsArray().Single(mutant => (int?)mutant!["location"]!["start"]!["line"] == line && (string?)mutant["replacement"] == replacement)!.AsObject();
            controls.Add(new JsonObject { ["name"] = name, ["id"] = (string?)mutant["id"], ["status"] = (string?)mutant["status"] });
        }
        long denominator = new[] { "Killed", "Survived", "NoCoverage", "Timeout" }.Sum(status => (long)counts.GetValueOrDefault(status));
        Require(denominator > 0, "Mutation denominator is empty.");
        bool knownKills = controls.All(control => (string?)control!["status"] == "Killed");
        decimal score = 100m * counts.GetValueOrDefault("Killed") / denominator;
        WriteJson(Path.Combine(output, "results.json"), new JsonObject
        {
            ["scope"] = helper ? "All packaged build-helper source; child-process controls and release dispositions are separate" : fullCore ? "All Core source; helper, child-process controls and release dispositions are separate" : "Six-file frontend/interpolation diagnostic; no full shipping release credit",
            ["nativeReportSha256"] = Hash(reportPath),
            ["configSha256"] = Hash(configPath),
            ["strykerSha256"] = Hash(tool),
            ["verifierSha256"] = Hash(typeof(LoggingMutationQualification).Assembly.Location),
            ["mutationTestMode"] = "serialized",
            ["counts"] = JsonSerializer.SerializeToNode(counts),
            ["strictScore"] = score,
            ["threshold"] = 90,
            ["knownKillControls"] = controls,
            ["knownKillsPassed"] = knownKills,
            ["nativeExitCode"] = mutation.ExitCode,
            ["diagnosticGatePassed"] = knownKills && score >= 90 && mutation.ExitCode == 0,
            ["exclusionsReviewed"] = false
        });
        Console.WriteLine($"Strict mutation score: {score:F6}%; known kills: {knownKills}; native report: {native}");
        return knownKills && score >= 90 && mutation.ExitCode == 0 ? 0 : 1;
    }

    private static void PrepareHelperTests(string language)
    {
        string directory = Path.Combine(language, "tests/CIS.TypeManager.Build.MutationTests");
        string sdkReference = "$([System.IO.Path]::GetDirectoryName('$(NETCoreSdkBundledVersionsProps)'))/";
        var project = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("IsTestProject", "true"), new XElement("EnableDefaultCompileItems", "false")),
            new XElement("ItemGroup",
                new XElement("ProjectReference", new XAttribute("Include", "../../eng/CIS.TypeManager.Build/CIS.TypeManager.Build.csproj")),
                new XElement("ProjectReference", new XAttribute("Include", "../fixtures/BuildHelperSatellite/BuildHelperSatellite.csproj"), new XAttribute("ReferenceOutputAssembly", "false")),
                new XElement("Compile", new XAttribute("Include", "../../verification/mutation/build-helper/WriteTypeInventoryTests.cs")),
                new[] { "Microsoft.Build.Framework", "Microsoft.Build.Utilities.Core", "Microsoft.NET.StringTools" }.Select(name =>
                    new XElement("Reference", new XAttribute("Include", name), new XAttribute("HintPath", sdkReference + name + ".dll"), new XAttribute("Private", "true")))));
        Write(Path.Combine(directory, "CIS.TypeManager.Build.MutationTests.csproj"), project.ToString());
        string satellite = Path.Combine(language, "tests/fixtures/BuildHelperSatellite");
        Write(Path.Combine(satellite, "BuildHelperSatellite.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><GenerateAssemblyInfo>false</GenerateAssemblyInfo></PropertyGroup></Project>");
        Write(Path.Combine(satellite, "Satellite.cs"), "[assembly: System.Reflection.AssemblyCulture(\"fr\")]\npublic sealed class Satellite { }\n");
    }

    private static void RequirePassingSerializedTrx(string path)
    {
        XDocument trx = XDocument.Load(path);
        XElement counters = trx.Descendants().Single(element => element.Name.LocalName == "Counters");
        int total = (int?)counters.Attribute("total") ?? 0;
        Require(total > 0 && (int?)counters.Attribute("passed") == total && (int?)counters.Attribute("executed") == total,
            "Mutation tests are empty, failed or skipped.");
        Require(trx.Descendants().Any(element => element.Name.LocalName == "StdOut" && element.Value.Contains("CIS mutation scheduling: serialized", StringComparison.Ordinal)),
            "Missing native serialized scheduling observation.");
    }
}
