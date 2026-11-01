using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

// Collect native reports only from the exact packaged binaries and successful Developer inputs.
internal static partial class ShippingCoverageCollection
{
    internal static async Task<int> RunAsync(string[] args)
    {
        var inputs = new TypeManagerVerificationInputs(args, ["repo-root", "output-root", "verification-root", "package"], ["budget-seconds"]);
        Require(int.TryParse(inputs.Optional("budget-seconds") ?? "1800", System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out int budget) && budget is > 0 and <= 3600, "Coverage budget-seconds must be between 1 and 3600.");
        var clock = Stopwatch.StartNew();
        DateTimeOffset collectionStarted = DateTimeOffset.UtcNow, deadline = collectionStarted.AddSeconds(budget);
        RequireNewOutput(inputs.Output, Environment.GetEnvironmentVariable("CIS_ARCHITECTURE_INSPECTION"));
        string language = inputs.Language, repo = inputs.PathOf("repo-root"), output = inputs.Output;
        string preparedRoot = inputs.PathOf("verification-root"), package = inputs.PathOf("package");
        Require(File.Exists(package) && File.Exists(Path.Combine(preparedRoot, "summary.json")),
            "Coverage needs the explicitly prepared package and verification summary before collection.");
        Directory.CreateDirectory(output);
        SourceInventoryEntry[] before = SourceInventory.CaptureVerificationInputs(repo);
        JsonObject preparation = Read(Path.Combine(preparedRoot, "summary.json"));
        Require((string?)preparation["source"]?["inputScope"] == SourceInventory.VerificationScope,
            "Coverage requires preparation with the declared maintained C# verification input scope.");
        string sourceManifest = Contained(preparedRoot, preparation["source"]!["inputInventory"]!["path"]!.GetValue<string>());
        Require(Hash(sourceManifest) == (string?)preparation["source"]!["inputInventory"]!["sha256"], "Prepared source manifest changed.");
        SourceInventoryEntry[] preparedInputs = JsonSerializer.Deserialize<SourceInventoryEntry[]>(File.ReadAllBytes(sourceManifest))
            ?? throw new InvalidDataException("Prepared source input manifest is empty.");
        RequireSameSources(preparedInputs, before);
        WriteArray(Path.Combine(output, "source-inputs.json"), before);
        JsonObject policy = Read(Path.Combine(language, "verification/coverage/shipping.json"));
        ValidatePolicy(language, policy);
        var paths = new List<string>
        {
            "Directory.Build.props", "Directory.Packages.props", "global.json", "NuGet.config",
            "eng/verify-shipping-coverage.ps1", "eng/toolchain.json", "verification/coverage/shipping.json",
            policy["settings"]!.GetValue<string>(), "eng/CIS.DependencyChecker/bin/Release/net10.0/CIS.DependencyChecker.dll"
        };
        paths.AddRange(Directory.GetFiles(Path.Combine(language, "eng/CIS.DependencyChecker"))
            .Where(path => new[] { ".cs", ".csproj", ".json" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)));
        foreach (JsonNode? assembly in policy["assemblies"]!.AsArray())
        {
            paths.Add(assembly!["project"]!.GetValue<string>());
            paths.AddRange(Sources(Contained(language, assembly["sourceRoot"]!.GetValue<string>()), [".cs"]));
            string binary = Binary(language, assembly);
            paths.Add(binary); paths.Add(Path.ChangeExtension(binary, ".pdb"));
        }
        paths.AddRange(Sources(Path.Combine(language, "tests"), [".cs", ".csproj", ".props", ".targets", ".json", ".py", ".ps1"]));
        foreach ((_, JsonNode? family) in policy["families"]!.AsObject())
        { paths.Add($"tests/CIS.Core.{family}/bin/Release/net10.0/CIS.Core.{family}.dll"); }
        paths.AddRange(before.Select(item => Path.Combine(repo, item.Path)));
        paths.AddRange(DeveloperInputs(repo, preparedRoot));
        paths.Add(sourceManifest);
        SourceInventoryEntry[] pins = PinInputs(language, paths);
        WriteArray(Path.Combine(output, "inputs.json"), pins);

        var environment = new Dictionary<string, string?>
        {
            ["MSBUILDDISABLENODEREUSE"] = "1",
            ["CIS_VERIFICATION_ROOT"] = preparedRoot,
            ["CIS_VERIFICATION_REPOSITORY"] = repo,
            ["CIS_VERIFICATION_PACKAGE"] = package,
            ["CIS_ARCHITECTURE_INSPECTION"] = null
        };
        ProcessResult sdk = await inputs.Run("sdk", ["--version"], language, environment: environment,
            timeout: TimeSpan.FromSeconds(Math.Min(30, FamilyTimeoutSeconds(budget, clock.Elapsed.TotalSeconds))));
        string expectedSdk = Read(Path.Combine(language, "eng/toolchain.json"))["sdk"]!.GetValue<string>();
        Require(sdk.StandardOutput.Trim() == expectedSdk, "Selected SDK differs from the pin.");
        ProcessResult git = await ProcessRunner.RunAsync("git", ["-C", language, "rev-parse", "HEAD"], language,
            TimeSpan.FromSeconds(Math.Min(30, FamilyTimeoutSeconds(budget, clock.Elapsed.TotalSeconds))));
        Require(git.ExitCode == 0 || !(Directory.Exists(Path.Combine(repo, ".git")) || File.Exists(Path.Combine(repo, ".git"))),
            "Unable to read repository HEAD.");
        WriteJson(Path.Combine(output, "environment.json"), new JsonObject
        {
            ["os"] = RuntimeInformation.OSDescription,
            ["processArchitecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["sdk"] = sdk.StandardOutput.Trim(),
            ["sourceCommit"] = git.ExitCode == 0 ? git.StandardOutput.Trim() : null,
            ["sourceInputIdentity"] = Digest(JsonSerializer.SerializeToUtf8Bytes(before)),
            ["sourceInputScope"] = SourceInventory.VerificationScope
        });
        string packageHash = Hash(package);
        string inspectedPackage = Read(Contained(preparedRoot, preparation["architectureInspection"]!["path"]!.GetValue<string>()))["package"]!["sha256"]!.GetValue<string>();
        Require(packageHash == inspectedPackage, "The explicit coverage package differs from the prepared inspection candidate.");
        string[] loadedCopies = ValidateCandidate(language, policy, package);
        ValidateReceiptPolicy(policy);
        string collectorPackage = FindCollectorPackage(language, policy["families"]!.AsObject().Select(family => family.Value!.GetValue<string>()));
        JsonObject collection = CreateReceipt(language, policy, package, collectorPackage);
        collection["budgetSeconds"] = budget; collection["startedUtc"] = collectionStarted.ToString("O");
        collection["deadlineUtc"] = deadline.ToString("O"); collection["elapsedSeconds"] = clock.Elapsed.TotalSeconds;
        string collectionPath = Path.Combine(output, "collection.json");
        WriteJson(collectionPath, collection);
        // Include all copies actually present in no-build test hosts in the immutable input pins.
        string[] coreCopies = collection["families"]!.AsArray().SelectMany(family => family!["coreCopies"]!.AsArray()).Select(pin => pin!["path"]!.GetValue<string>()).ToArray();
        pins = pins.Concat(PinInputs(language, loadedCopies.Concat(coreCopies).Append(package).Append(collectorPackage))).DistinctBy(item => item.Path, StringComparer.Ordinal).ToArray();
        WriteArray(Path.Combine(output, "inputs.json"), pins);
        WriteJson(Path.Combine(output, "candidate.json"), new JsonObject { ["path"] = Slashes(Path.GetRelativePath(repo, package)), ["sha256"] = packageHash });

        var commands = new JsonArray(); var results = new List<string>();
        int collectionExit = 1;
        try
        {
            foreach ((string family, JsonNode? suffix) in policy["families"]!.AsObject())
            {
                JsonObject row = collection["families"]!.AsArray().OfType<JsonObject>().Single(item => (string?)item["name"] == family);
                string directory = Path.Combine(output, family);
                string[] arguments = CollectorArguments(suffix!.GetValue<string>(), policy["settings"]!.GetValue<string>(), directory);
                Console.WriteLine("Collecting shipping coverage: " + family);
                DateTimeOffset started = DateTimeOffset.UtcNow;
                double remaining = FamilyTimeoutSeconds(budget, clock.Elapsed.TotalSeconds);
                row["timeoutSeconds"] = remaining;
                row["status"] = "Running"; row["startedUtc"] = started.ToString("O");
                WriteJson(collectionPath, collection);
                ProcessResult? child = null;
                try
                {
                    remaining = FamilyTimeoutSeconds(budget, clock.Elapsed.TotalSeconds);
                    row["timeoutSeconds"] = remaining;
                    child = await inputs.Run(family, arguments, language, environment: environment, timeout: TimeSpan.FromSeconds(remaining), requireSuccess: false);
                    row["exitCode"] = child.ExitCode; row["outputDrained"] = child.OutputDrained;
                    Require(child.ExitCode == 0 && child.OutputDrained, $"Coverage test family {family} failed ({child.ExitCode}); see its retained log.");
                    string trx = Path.Combine(directory, "report.trx");
                    FocusedTests.NativeCase[] nativeCases = FocusedTests.ReadResults(trx, started);
                    if (family == "unit")
                    { ValidateUnitSelection(nativeCases, Path.Combine(language, "tests/CIS.Core.UnitTests/bin/Release/net10.0/CIS.Core.UnitTests.dll")); }
                    row["trxSha256"] = Hash(trx); row["status"] = "Passed";
                    results.Add(directory);
                }
                catch (ProcessTimeoutException error)
                { child = error.Result; row["exitCode"] = child.ExitCode; row["timedOut"] = true; row["cleanupCompleted"] = error.CleanupCompleted; row["status"] = "Failed"; throw; }
                catch (ProcessOutputLimitException error)
                { child = error.Result; row["exitCode"] = child.ExitCode; row["status"] = "Failed"; throw; }
                catch (TimeoutException error)
                { row["status"] = "NotRun"; row["failure"] = error.Message; throw; }
                catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or System.ComponentModel.Win32Exception or System.Xml.XmlException)
                { row["status"] = "Failed"; row["failure"] = error.Message; throw; }
                finally
                {
                    row["finishedUtc"] = DateTimeOffset.UtcNow.ToString("O");
                    collection["elapsedSeconds"] = clock.Elapsed.TotalSeconds;
                    if (child is not null) { Write(Path.Combine(output, family + ".log"), child.StandardOutput + child.StandardError); }
                    commands.Add(new JsonObject
                    {
                        ["family"] = family,
                        ["executable"] = inputs.Dotnet,
                        ["argv"] = JsonSerializer.SerializeToNode(arguments),
                        ["workingDirectory"] = language,
                        ["startedUtc"] = started.ToString("O"),
                        ["finishedUtc"] = row["finishedUtc"]!.DeepClone(),
                        ["exitCode"] = child?.ExitCode
                    });
                    WriteArray(Path.Combine(output, "commands.json"), commands); WriteJson(collectionPath, collection);
                }
            }
            Require(Hash(package) == packageHash, "Candidate package changed during coverage.");
            RequireSameSources(before, SourceInventory.CaptureVerificationInputs(repo));
            RequireUnchangedInputs(language, pins);
            collection["commands"] = new JsonObject { ["path"] = "commands.json", ["sha256"] = Hash(Path.Combine(output, "commands.json")) };
            _ = FamilyTimeoutSeconds(budget, clock.Elapsed.TotalSeconds);
            foreach (JsonObject family in collection["families"]!.AsArray().OfType<JsonObject>())
            { Require(JsonNode.DeepEquals(family["coreCopies"], CoreCopies(language, family["name"]!.GetValue<string>(), collection["coreSha256"]!.GetValue<string>())), "Native Core copy membership changed during coverage."); }
            JsonObject analyzed = await ShippingCoverage.SummarizeAsync(language, results, async project =>
            {
                double left = Math.Min(60, FamilyTimeoutSeconds(budget, clock.Elapsed.TotalSeconds));
                ProcessResult evaluated = await inputs.Run("coverage-inventory-" + Path.GetFileNameWithoutExtension(project),
                    ["msbuild", project, "-getItem:Compile", "-p:Configuration=Release", "-p:TargetFramework=net10.0", "-nologo"],
                    language, environment: environment, timeout: TimeSpan.FromSeconds(left));
                return evaluated.StandardOutput;
            });
            WriteJson(Path.Combine(output, "coverage.json"), analyzed);
            int exit = analyzed["qualified"]!.GetValue<bool>() ? 0 : 2;
            collection["shippingAnalysisExitCode"] = exit;
            Require(clock.Elapsed.TotalSeconds <= budget, "The overall shipping coverage deadline expired during analysis.");
            if (exit is 0 or 2 && File.Exists(Path.Combine(output, "coverage.json")))
            {
                collection["shippingCoverage"] = new JsonObject { ["path"] = "coverage.json", ["sha256"] = Hash(Path.Combine(output, "coverage.json")) };
                collection["complete"] = true;
            }
            collectionExit = exit;
        }
        catch (Exception error) { collection["failure"] = error.Message; throw; }
        finally
        {
            double elapsed = clock.Elapsed.TotalSeconds;
            collection["elapsedSeconds"] = elapsed; collection["budgetMet"] = elapsed <= budget;
            if (elapsed > budget) { collection["complete"] = false; collection["failure"] ??= "The overall shipping coverage deadline expired."; }
            WriteJson(collectionPath, collection);
        }
        return (bool?)collection["complete"] == true && (bool?)collection["budgetMet"] == true ? collectionExit : 1;
    }

    internal static void RequireNewOutput(string output, string? focusedInspection)
    {
        Require(string.IsNullOrWhiteSpace(focusedInspection), "CIS_ARCHITECTURE_INSPECTION is only for focused tests. Clear it before running shipping coverage.");
        Require(!Directory.Exists(output) && !File.Exists(output), "Use a new output directory; native reports are immutable.");
    }

    internal static void ValidatePolicy(string language, JsonObject policy)
    {
        _ = Contained(language, policy["settings"]!.GetValue<string>());
        JsonArray assemblies = policy["assemblies"]!.AsArray();
        Require(assemblies.Count > 0, "Coverage must include shipping assemblies.");
        var names = new HashSet<string>(StringComparer.Ordinal); var entries = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonNode? assembly in assemblies)
        {
            string name = assembly!["name"]!.GetValue<string>();
            Require(SimpleName(name) && names.Add(name) && entries.Add(assembly["packageEntry"]!.GetValue<string>()), "Duplicate or invalid coverage assembly.");
            _ = Contained(language, assembly["project"]!.GetValue<string>());
            _ = Contained(language, assembly["sourceRoot"]!.GetValue<string>());
        }
        Require(names.Contains("CIS.Core"), "Coverage requires CIS.Core.");
        JsonObject families = policy["families"]!.AsObject();
        Require(families.Count > 0 && families.All(item => SimpleName(item.Key) && SimpleName(item.Value!.GetValue<string>())), "Invalid coverage family.");
    }

    internal static string[] DeveloperInputs(string repo, string preparedRoot)
    {
        string root = Path.GetFullPath(preparedRoot);
        string summaryPath = Path.Combine(root, "summary.json");
        JsonObject summary = Read(summaryPath);
        Require((int?)summary["exitCode"] == 0 && ((string?)summary["profile"] == "Developer" ||
            (string?)summary["profile"] == "Preparation" && (string?)summary["scope"] == "prepared-inputs"),
            "Coverage requires successful current preparation or a matching Developer checkpoint.");
        string inspectionPath = Contained(root, summary["architectureInspection"]!["path"]!.GetValue<string>());
        JsonObject inspection = ReadPinned(inspectionPath, summary["architectureInspection"]!["sha256"]!.GetValue<string>(), "Developer inspection differs from its recorded pin.");
        string startupRoot = Path.Combine(root, "contracts/startup"), startupPath = Path.Combine(startupRoot, "summary.json");
        JsonObject startup = Read(startupPath);
        string reference = Contained(startupRoot, startup["referenceAssembly"]!["path"]!.GetValue<string>());
        Require(Hash(reference) == (string?)startup["referenceAssembly"]!["sha256"], "Startup reference assembly differs from its recorded pin.");
        Require(summary["candidatePackage"] is JsonObject, "Preparation must explicitly identify its candidate package.");
        string package = CompilerChecks.VerifiedReference(root, summary["candidatePackage"]!);
        string inspectedPackage = Path.GetFullPath(inspection["package"]!["path"]!.GetValue<string>(), repo);
        Require(string.Equals(package, inspectedPackage, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            && Hash(package) == (string?)inspection["package"]!["sha256"], "Prepared package differs from its inspection or recorded pin.");
        return [summaryPath, inspectionPath, package, startupPath, reference];
    }

    internal static string[] ValidateCandidate(string language, JsonObject policy, string package)
    {
        using ZipArchive archive = ZipFile.OpenRead(package);
        string[] expected = policy["assemblies"]!.AsArray().Select(item => item!["packageEntry"]!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray();
        string[] actual = archive.Entries.Where(entry => entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).Select(entry => entry.FullName).Order(StringComparer.Ordinal).ToArray();
        Require(actual.SequenceEqual(expected, StringComparer.Ordinal), "Shipped DLL inventory differs from the coverage policy.");
        var copies = new List<string>();
        foreach (JsonNode? assembly in policy["assemblies"]!.AsArray())
        {
            string name = assembly!["name"]!.GetValue<string>();
            using Stream stream = archive.GetEntry(assembly["packageEntry"]!.GetValue<string>())!.Open();
            string packed = Convert.ToHexStringLower(SHA256.HashData(stream));
            Require(Hash(Binary(language, assembly)) == packed, "Repack the current candidate before coverage: " + name);
            foreach ((_, JsonNode? family) in policy["families"]!.AsObject())
            {
                string copy = Path.Combine(language, $"tests/CIS.Core.{family}/bin/Release/net10.0/{name}.dll");
                if (!File.Exists(copy)) { Require(name != "CIS.Core", "Missing copy-local shipping assembly: " + copy); continue; }
                Require(Hash(copy) == packed, "Copy-local shipping assembly differs from the package: " + copy);
                copies.Add(copy);
            }
        }
        return copies.ToArray();
    }

    internal static SourceInventoryEntry[] PinInputs(string language, IEnumerable<string> paths) =>
        paths.Select(path => Path.GetFullPath(path, language)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(path => new SourceInventoryEntry(Slashes(Path.GetRelativePath(language, path)), Hash(path))).ToArray();

    internal static void RequireUnchangedInputs(string language, IEnumerable<SourceInventoryEntry> pins)
    {
        foreach (SourceInventoryEntry pin in pins)
        { Require(Hash(Path.GetFullPath(pin.Path, language)) == pin.Sha256, "Coverage input changed: " + pin.Path); }
    }

    internal static void RequireSameSources(IEnumerable<SourceInventoryEntry> before, IEnumerable<SourceInventoryEntry> after) =>
        Require(before.OrderBy(item => item.Path, StringComparer.Ordinal).SequenceEqual(after.OrderBy(item => item.Path, StringComparer.Ordinal)), "Repository inputs changed during coverage.");

    internal static string[] CollectorArguments(string suffix, string settings, string directory)
    {
        string[] arguments = ["test", $"tests/CIS.Core.{suffix}/CIS.Core.{suffix}.csproj", "-c", "Release", "--no-build", "--no-restore", "--settings", settings,
            "--collect", "Code Coverage;Format=cobertura", "--results-directory", directory, "--logger", "trx;LogFileName=report.trx"];
        return suffix == "UnitTests" ? [.. arguments, "--filter", UnitFilter] : arguments;
    }

    internal static double FamilyTimeoutSeconds(int budget, double elapsedSeconds)
    {
        double remaining = budget - elapsedSeconds;
        if (remaining <= 0) { throw new TimeoutException("The overall shipping coverage deadline expired before child admission."); }
        return Math.Min(1800, remaining);
    }

    private static string Binary(string language, JsonNode assembly) =>
        Path.GetFullPath(Path.Combine(language, Path.GetDirectoryName(assembly["project"]!.GetValue<string>())!, "bin/Release/net10.0", assembly["name"] + ".dll"));

    private static IEnumerable<string> Sources(string root, string[] extensions) => Directory.GetFiles(root, "*", SearchOption.AllDirectories)
        .Where(path => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj") && extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
    private static string Slashes(string path) => path.Replace('\\', '/');
    private static bool SimpleName(string value) => value.Length > 0 && value is not ("." or "..") && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');
    private static string Contained(string root, string value)
    {
        string path = Path.GetFullPath(value, root), relative = Path.GetRelativePath(root, path);
        Require(!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal), "Coverage path escapes its root.");
        return path;
    }
    private static void WriteArray<T>(string path, T value) => Write(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }) + "\n");
}
