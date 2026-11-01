using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using CIS.TestSupport;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

// Adapter observations are separate from the complete shipping release gates.
internal static class MicrosoftLoggingQualification
{
    private const string Prefix = "implementations/csharp/";
    private const string Adapter = "src/CIS.Core/Logging/MicrosoftLogger/";
    internal static async Task<int> RunAsync(string[] args)
    {
        var inputs = new TypeManagerVerificationInputs(args, ["repo-root", "output-root", "mode"], []);
        string mode = inputs.Get("mode");
        Require(mode is "Coverage" or "Mutation", "Expected Coverage or Mutation mode.");
        RequireNewOutput(inputs.Output);
        inputs.EmptyOutput();
        string repo = inputs.PathOf("repo-root"), language = inputs.Language, output = inputs.Output;
        JsonObject matrix = Read(Path.Combine(language, "verification/shipping-projects.json"), out string matrixHash);
        string[] paths = AdapterPaths(language, matrix);
        SourceInventoryEntry[] sources = Pin(repo, paths);
        SourceInventoryEntry[] verification = Pin(repo, new[]
        {
            Prefix + "eng/verify-microsoft-logging.ps1", Prefix + "eng/toolchain.json",
            Prefix + "verification/shipping-projects.json", Prefix + "verification/mutation/microsoft-logging-config.json",
            Prefix + "verification/coverage/microsoft-logging.runsettings",
            Prefix + "eng/CIS.DependencyChecker/bin/Release/net10.0/CIS.DependencyChecker.dll"
        }.Concat(Directory.GetFiles(Path.Combine(language, "eng/CIS.DependencyChecker"))
            .Where(path => new[] { ".cs", ".csproj", ".json" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Select(path => Slashes(Path.GetRelativePath(repo, path)))));
        Require(verification.Single(pin => pin.Path == Prefix + "verification/shipping-projects.json").Sha256 == matrixHash, "Adapter matrix changed while selecting sources.");
        Array(output, "adapter-source-inputs.json", sources);
        Array(output, "verification-inputs.json", verification);
        JsonObject policy = ReadPinned(Path.Combine(language, "eng/toolchain.json"),
            verification.Single(pin => pin.Path == Prefix + "eng/toolchain.json").Sha256, "Toolchain policy changed.");
        var commands = new JsonArray();
        var environment = new Dictionary<string, string?> { ["MSBUILDDISABLENODEREUSE"] = "1" };
        async Task<ProcessResult> Run(string name, string[] arguments, string cwd, bool success = true)
        {
            Console.WriteLine("Microsoft logging: " + name);
            ProcessResult result;
            if (name == "mutation")
            {
                // Legacy Windows VSTest can mix encodings in Stryker's human console output.
                result = await ProcessRunner.RunAsync(inputs.Dotnet, arguments, cwd, TimeSpan.FromMinutes(30), environment, Encoding.UTF8);
                RecordMutationProcess(output, result);
            }
            else
            {
                result = await inputs.Run(name, arguments, cwd, environment: environment,
                    timeout: TimeSpan.FromMinutes(30), requireSuccess: false);
            }
            commands.Add(new JsonObject
            {
                ["name"] = name,
                ["executable"] = inputs.Dotnet,
                ["argv"] = JsonSerializer.SerializeToNode(arguments),
                ["workingDirectory"] = cwd,
                ["exitCode"] = result.ExitCode,
                ["log"] = "logs/" + name + ".log"
            });
            Write(Path.Combine(output, "commands.json"), commands.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
            Require(!success || result.ExitCode == 0, name + " failed; native outputs retained.");
            return result;
        }
        try
        {
            ProcessResult sdk = await Run("sdk", ["--version"], language);
            Require(sdk.StandardOutput.Trim() == (string?)policy["sdk"], "Selected SDK differs from toolchain policy.");
            int exitCode = 0;
            if (mode == "Coverage")
            {
                foreach (string family in new[] { "Unit", "Integration" })
                {
                    string project = Project(family), directory = Path.Combine(output, family.ToLowerInvariant());
                    await Run("build-" + family, ["build", project, "-c", "Release", "--no-restore"], language);
                    await Run("coverage-" + family, CollectorArguments(family, directory), language);
                    WriteJson(Path.Combine(directory, "adapter-coverage.json"), Coverage(directory, repo, paths));
                }
            }
            else
            {
                environment["CisMutationTesting"] = "true";
                string snapshot = Path.Combine(output, "source"), isolated = Path.Combine(snapshot, Prefix);
                SourceInventoryEntry[] inventory = SourceInventory.CaptureVerificationInputs(repo);
                Require(inventory.Any(item => item.Path == "README.md"), "Mutation snapshot requires the root README.");
                foreach (SourceInventoryEntry pin in inventory) { Copy(Path.Combine(repo, pin.Path), Path.Combine(snapshot, pin.Path), pin.Sha256); }
                Array(output, "snapshot-inputs.json", inventory);
                await Run("tool-restore", ["tool", "restore", "--configfile", "eng/Tools.NuGet.config"], isolated);
                await Run("restore-unit", ["restore", Project("Unit"), "--locked-mode", "--configfile", "NuGet.config"], isolated);
                await Run("build-unit", ["build", Project("Unit"), "-c", "Release", "--no-restore"], isolated);
                await Run("baseline-unit", ["test", Project("Unit"), "-c", "Release", "--no-build", "--no-restore",
                    "--filter", Filter("Unit"), "--logger", "trx;LogFileName=baseline.trx", "--results-directory", Path.Combine(output, "baseline")], isolated);
                RequirePassingTrx(ReadXml(Path.Combine(output, "baseline/baseline.trx"), out _));
                ProcessResult packages = await Run("global-packages", ["nuget", "locals", "global-packages", "--list", "--force-english-output"], isolated);
                string packageLine = packages.StandardOutput.Trim();
                int separator = packageLine.IndexOf(": ", StringComparison.Ordinal);
                Require(separator >= 0, "Cannot resolve the global package directory.");
                string version = policy["tools"]!.AsArray().Single(tool => (string?)tool!["id"] == "dotnet-stryker")!["version"]!.GetValue<string>();
                string tool = Path.Combine(packageLine[(separator + 2)..].Trim(), "dotnet-stryker", version, "tools/net8.0/any/Stryker.CLI.dll");
                Require(File.Exists(tool), "Pinned Stryker entry point was not materialized.");
                try
                {
                    ProcessResult mutation = await Run("mutation", ["exec", "--fx-version", policy["toolHostRuntime"]!.GetValue<string>(),
                        "--roll-forward", "Disable", tool, "--config-file", "../../verification/mutation/microsoft-logging-config.json",
                        "--output", Path.Combine(output, "mutation")], Path.Combine(isolated, "src/CIS.Core"), success: false);
                    exitCode = mutation.ExitCode;
                }
                finally
                {
                    try { WriteJson(Path.Combine(output, "adapter-mutation.json"), Mutation(Path.Combine(output, "mutation/reports/mutation-report.json"), paths)); }
                    finally
                    {
                        try { RequireUnchanged(snapshot, inventory); }
                        finally { await Run("unmutated-rebuild", ["build", Project("Unit"), "-c", "Release", "--no-restore", "--no-incremental"], isolated); }
                    }
                }
            }
            Console.WriteLine($"Microsoft logging {mode} exit {exitCode}. Native reports: {output}");
            return exitCode;
        }
        finally
        {
            RequireUnchanged(repo, sources.Concat(verification));
            Require(AdapterPaths(language, Read(Path.Combine(language, "verification/shipping-projects.json"))).SequenceEqual(paths, StringComparer.Ordinal),
                "Canonical adapter source membership changed during verification.");
        }
    }

    internal static void RecordMutationProcess(string output, ProcessResult result)
    {
        string logs = Path.Combine(output, "logs");
        Directory.CreateDirectory(logs);
        File.WriteAllBytes(Path.Combine(logs, "mutation.stdout.bin"), result.StandardOutputBytes.ToArray());
        File.WriteAllBytes(Path.Combine(logs, "mutation.stderr.bin"), result.StandardErrorBytes.ToArray());
        Write(Path.Combine(logs, "mutation.log"), result.StandardOutput + result.StandardError);
        WriteJson(Path.Combine(logs, "mutation.process.json"), new JsonObject
        {
            ["pid"] = result.ProcessId,
            ["exitCode"] = result.ExitCode,
            ["stdoutSha256"] = Digest(result.StandardOutputBytes.Span),
            ["stderrSha256"] = Digest(result.StandardErrorBytes.Span),
            ["stdout"] = "logs/mutation.stdout.bin",
            ["stderr"] = "logs/mutation.stderr.bin",
            ["diagnosticDecoding"] = "UTF-8 with replacement; original streams retained and hashed separately."
        });
    }
    internal static void RequireNewOutput(string output) =>
        Require(!Directory.Exists(output) && !File.Exists(output), "Use a new output directory; retained reports are immutable.");

    internal static string[] AdapterPaths(string language, JsonObject matrix)
    {
        JsonNode?[] slices = matrix["shippingProjects"]!.AsArray().SelectMany(project => project!["moduleSlices"]!.AsArray())
            .Where(slice => (string?)slice!["id"] == "logging-microsoft").ToArray();
        Require(slices.Length == 1, "Expected one Microsoft logging matrix slice.");
        string[] expected = slices[0]!["sources"]!.AsArray().Select(item => Prefix + item!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray();
        string[] actual = Directory.GetFiles(Path.Combine(language, Adapter), "*.cs", SearchOption.TopDirectoryOnly)
            .Select(path => Prefix + Slashes(Path.GetRelativePath(language, path))).Order(StringComparer.Ordinal).ToArray();
        Require(expected.Length > 0 && expected.SequenceEqual(actual, StringComparer.Ordinal), "Adapter source files differ from the matrix.");
        return expected;
    }

    internal static void RequireUnchanged(string root, IEnumerable<SourceInventoryEntry> pins)
    {
        foreach (SourceInventoryEntry pin in pins)
        { Require(File.Exists(Path.Combine(root, pin.Path)) && Hash(Path.Combine(root, pin.Path)) == pin.Sha256, "Verification input changed: " + pin.Path); }
    }

    internal static string[] CollectorArguments(string family, string output) =>
        ["test", Project(family), "-c", "Release", "--no-build", "--no-restore", "--filter", Filter(family),
            "--settings", "verification/coverage/microsoft-logging.runsettings", "--collect", "Code Coverage;Format=cobertura",
            "--results-directory", output, "--logger", "trx;LogFileName=report.trx"];

    internal static JsonObject Coverage(string directory, string repo, string[] paths)
    {
        XDocument trx = ReadXml(Path.Combine(directory, "report.trx"), out string trxHash);
        RequirePassingTrx(trx);
        string[] attachments = trx.Descendants().Where(item => item.Name.LocalName == "UriAttachment")
            .Elements().Where(item => item.Name.LocalName == "A").Select(item => (string?)item.Attribute("href") ?? "")
            .Where(path => path.EndsWith(".cobertura.xml", StringComparison.OrdinalIgnoreCase)).ToArray();
        Require(attachments.Length == 1, "Expected one TRX Cobertura attachment per lane.");
        string name = Slashes(attachments[0]).Split('/')[^1];
        string[] reports = Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Where(path => Path.GetFileName(path) == name).ToArray();
        Require(reports.Length > 0, "Cannot resolve the TRX Cobertura attachment.");
        byte[] bytes = File.ReadAllBytes(reports[0]);
        string reportHash = Digest(bytes);
        Require(reports.All(path => Hash(path) == reportHash), "Conflicting copies of the TRX Cobertura attachment.");
        XDocument xml = Xml(bytes);
        var files = new JsonArray();
        foreach (string path in paths)
        {
            string full = Slashes(Path.GetFullPath(Path.Combine(repo, path)));
            XElement[] classes = xml.Descendants("class").Where(item => string.Equals(Slashes((string?)item.Attribute("filename") ?? ""), full,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)).ToArray();
            Require(classes.Length > 0, "Adapter source absent from coverage: " + path);
            var lines = new Dictionary<int, (int Hits, int Covered, int Total)>();
            foreach (XElement line in classes.Elements("lines").Elements("line"))
            {
                int number = Number((string?)line.Attribute("number"), "line number");
                Require(number > 0, "Invalid source line number.");
                int hits = Number((string?)line.Attribute("hits"), "line hits"), covered = 0, total = 0;
                string branch = (string?)line.Attribute("branch") ?? "false";
                Require(branch.Equals("true", StringComparison.OrdinalIgnoreCase) || branch.Equals("false", StringComparison.OrdinalIgnoreCase), "Invalid branch flag.");
                if (branch.Equals("true", StringComparison.OrdinalIgnoreCase))
                {
                    Match match = Regex.Match((string?)line.Attribute("condition-coverage") ?? "", @"\((\d+)/(\d+)\)", RegexOptions.CultureInvariant);
                    Require(match.Success, "Unrecognized native branch observation.");
                    covered = Number(match.Groups[1].Value, "covered branches"); total = Number(match.Groups[2].Value, "total branches");
                    Require(total > 0 && covered <= total, "Invalid native branch counts.");
                }
                if (lines.TryGetValue(number, out var previous))
                {
                    Require(previous.Total == 0 || total == 0 || previous.Total == total, "Conflicting branch denominators for a duplicate source line.");
                    hits = Math.Max(hits, previous.Hits); covered = Math.Max(covered, previous.Covered); total = Math.Max(total, previous.Total);
                }
                lines[number] = (hits, covered, total);
            }
            Require(lines.Count > 0 && lines.Values.Any(line => line.Hits > 0), "Adapter source has no measured execution: " + path);
            files.Add(new JsonObject
            {
                ["path"] = path,
                ["linesCovered"] = lines.Values.Count(line => line.Hits > 0),
                ["linesTotal"] = lines.Count,
                ["branchesCovered"] = lines.Values.Sum(line => (long)line.Covered),
                ["branchesTotal"] = lines.Values.Sum(line => (long)line.Total)
            });
        }
        return new JsonObject
        {
            ["files"] = files,
            ["trxSha256"] = trxHash,
            ["coberturaSha256"] = reportHash,
            ["thresholdEvaluation"] = "Deferred to WP07; these are separate lane observations, not release qualification."
        };
    }

    internal static JsonObject Mutation(string path, string[] expected)
    {
        JsonObject report = Read(path, out string hash);
        KeyValuePair<string, JsonNode?>[] files = report["files"]!.AsObject()
            .Where(item => ("/" + Slashes(item.Key)).Contains("/Logging/MicrosoftLogger/", StringComparison.Ordinal)).ToArray();
        string[] actual = files.Select(item => Prefix + Adapter + Slashes(item.Key).Split('/')[^1]).Order(StringComparer.Ordinal).ToArray();
        Require(actual.SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal), "Native mutant files differ from the adapter matrix.");
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach ((string name, JsonNode? file) in files)
        {
            JsonArray mutants = file!["mutants"]!.AsArray();
            Require(mutants.Count > 0, "Adapter source has zero mutants: " + name);
            foreach (JsonNode? mutant in mutants)
            {
                string? status = (string?)mutant?["status"];
                Require(!string.IsNullOrWhiteSpace(status), "Missing native mutant status.");
                counts[status!] = counts.GetValueOrDefault(status!) + 1;
            }
        }
        long denominator = new[] { "Killed", "Timeout", "Survived", "NoCoverage" }.Sum(status => (long)counts.GetValueOrDefault(status));
        Require(denominator > 0, "Adapter mutation denominator is zero.");
        return new JsonObject
        {
            ["nativeReportSha256"] = hash,
            ["counts"] = JsonSerializer.SerializeToNode(counts),
            ["testableDenominator"] = denominator,
            ["scope"] = "Matrix adapter files only; ignored/noncompiling mutants receive no kill credit."
        };
    }

    private static void RequirePassingTrx(XDocument trx)
    {
        XElement[] summaries = trx.Descendants().Where(item => item.Name.LocalName == "ResultSummary").ToArray();
        Require(summaries.Length == 1 && (string?)summaries[0].Attribute("outcome") == "Completed", "Native tests did not complete successfully.");
        XElement[] counters = summaries[0].Elements().Where(item => item.Name.LocalName == "Counters").ToArray();
        Require(counters.Length == 1, "Expected native test counters.");
        int total = Number((string?)counters[0].Attribute("total"), "test total");
        Require(total > 0 && Number((string?)counters[0].Attribute("executed"), "executed tests") == total
            && Number((string?)counters[0].Attribute("passed"), "passed tests") == total
            && Number((string?)counters[0].Attribute("failed"), "failed tests") == 0, "Native tests are empty, failed or skipped.");
    }
    private static int Number(string? value, string label)
    {
        Require(int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int result), "Invalid " + label + ".");
        return result;
    }
    private static XDocument ReadXml(string path, out string hash) { byte[] bytes = File.ReadAllBytes(path); hash = Digest(bytes); return Xml(bytes); }
    private static XDocument Xml(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }
    private static SourceInventoryEntry[] Pin(string repo, IEnumerable<string> paths) =>
        paths.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(path => new SourceInventoryEntry(path, Hash(Path.Combine(repo, path)))).ToArray();
    private static void Array<T>(string output, string name, T value) =>
        Write(Path.Combine(output, name), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    private static string Project(string family) => $"tests/CIS.Core.{family}Tests/CIS.Core.{family}Tests.csproj";
    private static string Filter(string family) => $"FullyQualifiedName~CIS.Tests.{family}Tests.Provider";
    private static string Slashes(string path) => path.Replace('\\', '/');
}
