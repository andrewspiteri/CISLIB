using System.Diagnostics;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace CIS.Verification.HealthcheckPackageProducer;

// Local nonshipping producer metadata, not publication or an independent build attestation.
internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private sealed record SourcePin([property: JsonPropertyName("path")] string Path, [property: JsonPropertyName("sha256")] string Sha256);

    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Expected absolute language-root, new empty output directory, and absolute existing dotnet executable.");
            return 2;
        }
        string language = Absolute(args[0]), output = Absolute(args[1]), dotnet = Absolute(args[2]);
        Require(!Within(language, output) && !Within(output, language) && language != output, "Keep output separate from source inputs.");
        Require(!Directory.Exists(output) || !Directory.EnumerateFileSystemEntries(output).Any(), "Use a new or empty output; failed attempts are retained.");
        Directory.CreateDirectory(Path.Combine(output, "logs"));
        var report = new JsonObject
        {
            ["schema"] = "cis.healthcheck.local-package-production/v1-development", ["complete"] = false,
            ["scope"] = "Fresh local uninstrumented Core build and package with verified selected inputs; unpublished, not independent toolchain attestation or release qualification",
            ["languageRoot"] = language, ["outputRoot"] = output, ["baseCommit"] = null,
            ["producerSha256"] = Hash(typeof(Program).Assembly.Location), ["runtime"] = RuntimeInformation.FrameworkDescription,
            ["architecture"] = RuntimeInformation.ProcessArchitecture.ToString(), ["processes"] = new JsonArray()
        };
        var failures = new List<string>();
        SourcePin[]? before = null;
        string readme = Path.GetFullPath(Path.Combine(language, "..", "..", "README.md"));
        string? readmeHash = null, dotnetHash = null, coreHash = null, packageHash = null, package = null, coreMvid = null;
        string core = Path.Combine(language, "src", "CIS.Core", "bin", "Release", "net10.0", "CIS.Core.dll");
        WriteJson(Path.Combine(output, "results.json"), report);
        try
        {
            Require(RuntimeInformation.FrameworkDescription == ".NET 10.0.12" && RuntimeInformation.ProcessArchitecture == Architecture.X64
                && (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()), "Execute on the pinned .NET 10.0.12 Windows/Linux x64 runtime.");
            Require(File.Exists(dotnet), "The supplied existing dotnet executable is missing.");
            Require(Environment.GetEnvironmentVariable("CisMutationTesting") != "true"
                && new[] { "CIS_HEALTHCHECK_MUTATION_SESSION", "STRYKER_MUTANT_FILE", "ACTIVE_MUTANT" }
                    .All(name => string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name))), "Do not produce a candidate inside an active mutation session.");
            before = Capture(language); readmeHash = Hash(readme); dotnetHash = Hash(dotnet);
            WriteJson(Path.Combine(output, "source-inputs.before.json"), before);
            report["readme"] = new JsonObject { ["path"] = readme, ["sha256"] = readmeHash };
            report["dotnet"] = new JsonObject { ["path"] = dotnet, ["sha256"] = dotnetHash };
            await Run("sdk", ["--version"], 30);
            Require(File.ReadAllText(Path.Combine(output, "logs", "sdk.stdout.log")).Trim() == "10.0.401", "Actual SDK selection is not 10.0.401.");
            await Run("build", ["build", "src/CIS.Core/CIS.Core.csproj", "-c", "Release", "--no-restore", "--no-incremental",
                "-maxcpucount:1", "-p:UseSharedCompilation=false", "-nologo"], 300);
            coreHash = Hash(core); coreMvid = VerifyUninstrumentedCore(core);
            report["builtCore"] = new JsonObject { ["path"] = core, ["sha256"] = coreHash, ["mvid"] = coreMvid };
            string packages = Path.Combine(output, "packages");
            await Run("pack", ["pack", "src/CIS.Core/CIS.Core.csproj", "-c", "Release", "--no-build", "--no-restore", "-o", packages, "-nologo"], 120);
            string[] candidates = Directory.GetFiles(packages, "*.nupkg", SearchOption.TopDirectoryOnly);
            Require(candidates.Length == 1, "Expected exactly one newly created nupkg.");
            package = candidates[0]; packageHash = Hash(package);
            using ZipArchive archive = ZipFile.OpenRead(package);
            ZipArchiveEntry spec = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
            using Stream specData = spec.Open();
            XElement metadata = XDocument.Load(specData).Descendants().Single(element => element.Name.LocalName == "metadata");
            Require(metadata.Elements().Single(element => element.Name.LocalName == "id").Value == "CIS.Core", "Unexpected produced package identity.");
            ZipArchiveEntry asset = archive.Entries.Single(entry => entry.FullName == "lib/net10.0/CIS.Core.dll");
            using Stream assetData = asset.Open();
            Require(Convert.ToHexStringLower(SHA256.HashData(assetData)) == coreHash, "Package Core bytes differ from the fresh built Core.");
            report["packageVersion"] = metadata.Elements().Single(element => element.Name.LocalName == "version").Value;
        }
        catch (Exception error) { failures.Add(error.ToString()); }
        finally
        {
            try
            {
                if (before is not null)
                {
                    SourcePin[] after = Capture(language);
                    WriteJson(Path.Combine(output, "source-inputs.after.json"), after);
                    Require(before.SequenceEqual(after), "Selected source/build input set or bytes changed during production.");
                    report["sourceInputsVerifiedAfter"] = true;
                }
                if (readmeHash is not null) { Require(Hash(readme) == readmeHash, "Repository README changed during production."); }
                if (dotnetHash is not null) { Require(Hash(dotnet) == dotnetHash, "The supplied dotnet executable changed during production."); }
                if (coreHash is not null) { Require(Hash(core) == coreHash && VerifyUninstrumentedCore(core) == coreMvid, "Fresh built Core changed during packing."); }
                if (packageHash is not null) { Require(Hash(package!) == packageHash, "Produced package changed during verification."); }
            }
            catch (Exception error) { failures.Add(error.ToString()); }
        }
        if (failures.Count == 0)
        {
            try
            {
                string provenance = Path.Combine(output, "package-provenance.json");
                WriteJson(provenance, new
                {
                    schema = "cis.healthcheck.package-producer/v1-development", scope = "Unpublished local candidate; verified selected inputs, not independent build attestation",
                    baseCommit = (string?)null, recordedUtc = DateTimeOffset.UtcNow, sdk = "10.0.401", runtime = "10.0.12",
                    packageSha256 = packageHash, coreSha256 = coreHash, sourceInputs = before,
                    repositoryReadme = new { path = readme, sha256 = readmeHash }
                });
                report["provenance"] = new JsonObject { ["path"] = provenance, ["sha256"] = Hash(provenance) };
                report["complete"] = true;
            }
            catch (Exception error) { failures.Add(error.ToString()); }
        }
        report["packagePath"] = package; report["packageSha256"] = packageHash; report["coreSha256"] = coreHash;
        report["status"] = failures.Count == 0 ? "LocalPackageProduced" : "LocalPackageProductionFailed";
        report["failures"] = JsonSerializer.SerializeToNode(failures); report["exitCode"] = failures.Count == 0 ? 0 : 1;
        WriteJson(Path.Combine(output, "results.json"), report);
        Console.WriteLine(JsonSerializer.Serialize(new { status = report["status"]!.GetValue<string>(), results = Path.Combine(output, "results.json") }));
        return failures.Count == 0 ? 0 : 1;

        async Task Run(string name, string[] arguments, int seconds)
        {
            string stdout = Path.Combine(output, "logs", name + ".stdout.log"), stderr = Path.Combine(output, "logs", name + ".stderr.log");
            var row = new JsonObject { ["name"] = name, ["executable"] = dotnet, ["arguments"] = JsonSerializer.SerializeToNode(arguments),
                ["workingDirectory"] = language, ["timeoutSeconds"] = seconds, ["timedOut"] = false, ["forcedStop"] = false };
            report["processes"]!.AsArray().Add(row);
            using var child = new Process { StartInfo = new ProcessStartInfo(dotnet)
            { WorkingDirectory = language, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true } };
            foreach (string argument in arguments) { child.StartInfo.ArgumentList.Add(argument); }
            child.StartInfo.Environment["DOTNET_ROLL_FORWARD"] = "Disable";
            child.StartInfo.Environment["UseSharedCompilation"] = "false";
            bool started = false;
            Task? copying = null;
            try
            {
                await using var outputFile = File.Create(stdout);
                await using var errorFile = File.Create(stderr);
                try
                {
                    row["startedUtc"] = DateTimeOffset.UtcNow;
                    Require(child.Start(), "Native producer command did not start."); started = true;
                    row["pid"] = child.Id; WriteJson(Path.Combine(output, "results.json"), report);
                    copying = Task.WhenAll(child.StandardOutput.BaseStream.CopyToAsync(outputFile), child.StandardError.BaseStream.CopyToAsync(errorFile));
                    try { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(seconds)); }
                    catch (TimeoutException) { row["timedOut"] = true; throw; }
                    Require(child.ExitCode == 0, "Actual native " + name + " command failed; retain its logs.");
                }
                finally
                {
                    if (started && !child.HasExited) { row["forcedStop"] = true; child.Kill(entireProcessTree: true); }
                    if (started) { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); row["exitCode"] = child.ExitCode; }
                    if (copying is not null) { await copying.WaitAsync(TimeSpan.FromSeconds(10)); }
                }
            }
            finally
            {
                // Both log writers have been disposed before reopening for hash.
                row["finishedUtc"] = DateTimeOffset.UtcNow;
                row["stdoutPath"] = stdout; row["stderrPath"] = stderr;
                row["stdoutSha256"] = File.Exists(stdout) ? Hash(stdout) : null;
                row["stderrSha256"] = File.Exists(stderr) ? Hash(stderr) : null;
                WriteJson(Path.Combine(output, "results.json"), report);
            }
        }
    }

    private static SourcePin[] Capture(string language)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (string relative in new[] { "src/CIS.Core/CIS.Core.csproj", "src/CIS.Core/packages.lock.json", "global.json",
            "Directory.Build.props", "Directory.Packages.props", "NuGet.config", "eng/CIS.TypeManager.Build/CIS.TypeManager.Build.csproj" })
        { Require(File.Exists(Path.Combine(language, relative)), "Missing selected build input: " + relative); paths.Add(relative); }
        foreach (string relative in new[] { "Directory.Build.targets", "Directory.Build.rsp", "Directory.Packages.targets", "Directory.Solution.props", "Directory.Solution.targets" })
        { if (File.Exists(Path.Combine(language, relative))) { paths.Add(relative); } }
        foreach (string tree in new[] { "src/CIS.Core", "eng/CIS.TypeManager.Build", "build/type-manager" })
        {
            string directory = Path.Combine(language, tree);
            Require(Directory.Exists(directory), "Missing selected source/build tree: " + tree);
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(language, file);
                if (relative.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")) { continue; }
                if (tree == "src/CIS.Core" && Path.GetExtension(file) != ".cs") { continue; }
                Require((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0, "Linked source/build input is unsupported.");
                paths.Add(relative.Replace('\\', '/'));
            }
        }
        Require(paths.Any(path => path.StartsWith("src/CIS.Core/", StringComparison.Ordinal) && path.EndsWith(".cs", StringComparison.Ordinal)), "Core source is empty.");
        Require(paths.Any(path => path.StartsWith("eng/CIS.TypeManager.Build/", StringComparison.Ordinal) && path.EndsWith(".cs", StringComparison.Ordinal)), "TypeManager helper source is empty.");
        return paths.Order(StringComparer.Ordinal).Select(path => new SourcePin(path, Hash(Path.Combine(language, path)))).ToArray();
    }

    private static string VerifyUninstrumentedCore(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        Require(metadata.GetString(metadata.GetAssemblyDefinition().Name) == "CIS.Core", "Fresh output is not the Core assembly.");
        foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
        {
            TypeDefinition type = metadata.GetTypeDefinition(handle);
            Require(!metadata.GetString(type.Namespace).StartsWith("Stryker", StringComparison.Ordinal), "Mutation-instrumented Core cannot be packaged.");
        }
        return metadata.GetGuid(metadata.GetModuleDefinition().Mvid).ToString("D");
    }

    private static string Absolute(string value) { Require(Path.IsPathFullyQualified(value), "Every supplied path must be absolute."); return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value)); }
    private static bool Within(string root, string path) => path.StartsWith(root + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static void WriteJson(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, Json) + "\n");
    private static void Require(bool condition, string message) { if (!condition) { throw new InvalidDataException(message); } }
}
