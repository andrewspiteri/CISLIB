using System.Diagnostics;
using System.IO.Compression;
using System.IO.Pipes;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.Logging;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests;

// One fresh local feed/cache/build per test-class run; the consumer has no ProjectReference or friend access.
internal sealed class CandidatePackageFixture
{
    internal static readonly TimeSpan Guard = TimeSpan.FromSeconds(30);
    internal string Root { get; }
    internal string Host => Path.Combine(Root, "app", "bin", "Release", "net10.0", "CIS.Logging.CandidateConsumer.dll");
    internal readonly Dictionary<string, string> AssemblyHashes = new(StringComparer.Ordinal);
    private int nextChild;
    private CandidatePackageFixture(string repository) => Root = Path.Combine(repository, ".artifacts", "csharp", "candidate-consumers", Guid.NewGuid().ToString("N"));
    internal static string Repository()
    {
        for (DirectoryInfo? at = new(AppContext.BaseDirectory); at is not null; at = at.Parent)
        { if (File.Exists(Path.Combine(at.FullName, "implementations/csharp/CisLib.slnx"))) { return at.FullName; } }
        throw new DirectoryNotFoundException("Repository root is required.");
    }
    internal static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    internal static async Task<CandidatePackageFixture> Prepare()
    {
        string repository = Repository(), language = Path.Combine(repository, "implementations/csharp");
        var fixture = new CandidatePackageFixture(repository);
        Directory.CreateDirectory(Path.Combine(fixture.Root, "feed")); Directory.CreateDirectory(Path.Combine(fixture.Root, "app"));
        string core = typeof(CoreLogRuntime).Assembly.Location;
        string version = FileVersionInfo.GetVersionInfo(core).ProductVersion!.Split('+')[0];
        string package = Path.Combine(repository, ".artifacts/csharp/packages", "CIS.Core." + version + ".nupkg");
        using (ZipArchive zip = ZipFile.OpenRead(package))
        {
            using Stream dll = zip.GetEntry("lib/net10.0/CIS.Core.dll")!.Open();
            fixture.AssemblyHashes["core"] = Convert.ToHexStringLower(SHA256.HashData(dll));
            Assert.AreEqual(Hash(core), fixture.AssemblyHashes["core"], "Repack current Core before candidate-consumer tests; stale package bytes are rejected.");
            using Stream nuspec = zip.GetEntry("CIS.Core.nuspec")!.Open();
            Assert.AreEqual(version, XDocument.Load(nuspec).Descendants().Single(e => e.Name.LocalName == "version").Value);
        }
        File.Copy(package, Path.Combine(fixture.Root, "feed", Path.GetFileName(package)));
        JsonObject assets = JsonNode.Parse(File.ReadAllBytes(Path.Combine(language, "src/CIS.Core/obj/project.assets.json")))!.AsObject();
        JsonObject policy = JsonNode.Parse(File.ReadAllBytes(Path.Combine(language, "eng/runtime-dependencies.json")))!.AsObject();
        var packageHashes = new JsonObject { [Path.GetFileName(package)] = Hash(package) };
        foreach (JsonNode? dependency in policy["packages"]!.AsArray())
        {
            string id = dependency!["id"]!.GetValue<string>(), v = dependency["version"]!.GetValue<string>(), lower = id.ToLowerInvariant();
            string source = assets["packageFolders"]!.AsObject().Select(folder => Path.Combine(folder.Key, lower, v, lower + "." + v + ".nupkg")).Single(File.Exists);
            string destination = Path.Combine(fixture.Root, "feed", Path.GetFileName(source)); File.Copy(source, destination); packageHashes[Path.GetFileName(source)] = Hash(source);
            string role = id.Contains("Logging", StringComparison.Ordinal) ? "logging" : "di";
            using ZipArchive zip = ZipFile.OpenRead(source);
            using Stream dll = zip.GetEntry("lib/net10.0/" + id + ".dll")!.Open(); fixture.AssemblyHashes[role] = Convert.ToHexStringLower(SHA256.HashData(dll));
        }
        foreach (string name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" }) { File.WriteAllText(Path.Combine(fixture.Root, name), "<Project />"); }
        File.Copy(Path.Combine(language, "global.json"), Path.Combine(fixture.Root, "global.json"));
        string config = "<configuration><packageSources><clear/><add key=\"local\" value=\"" + SecurityElement.Escape(Path.Combine(fixture.Root, "feed")) + "\"/></packageSources><fallbackPackageFolders><clear/></fallbackPackageFolders><auditSources><clear/></auditSources></configuration>";
        File.WriteAllText(Path.Combine(fixture.Root, "NuGet.Config"), config);
        string sourceRoot = Path.Combine(language, "verification/consumers/logging-candidate");
        File.Copy(Path.Combine(sourceRoot, "Program.cs"), Path.Combine(fixture.Root, "app/Program.cs"));
        File.Copy(Path.Combine(sourceRoot, "Consumer.csproj.template"), Path.Combine(fixture.Root, "app/Consumer.csproj"));
        var environment = Variables(fixture.Root);
        environment["NUGET_PACKAGES"] = Path.Combine(fixture.Root, "packages"); environment["NUGET_HTTP_CACHE_PATH"] = Path.Combine(fixture.Root, "http-cache");
        foreach ((string name, string[] arguments) in new[]
        {
            ("restore", new[] { "restore", "app/Consumer.csproj", "--configfile", "NuGet.Config", "--no-cache", "-p:CandidateVersion=" + version }),
            ("build", new[] { "build", "app/Consumer.csproj", "-c", "Release", "--no-restore", "-p:CandidateVersion=" + version })
        })
        {
            ProcessResult result = await ProcessRunner.RunAsync("dotnet", arguments, fixture.Root, TimeSpan.FromSeconds(90), environment, new UTF8Encoding(false, true));
            File.WriteAllText(Path.Combine(fixture.Root, name + ".log"), result.StandardOutput + result.StandardError); Assert.AreEqual(0, result.ExitCode, result.StandardOutput + result.StandardError);
        }
        JsonObject restored = JsonNode.Parse(File.ReadAllBytes(Path.Combine(fixture.Root, "app/obj/project.assets.json")))!.AsObject();
        string[] expected = ["CIS.Core/" + version, "Microsoft.Extensions.Logging.Abstractions/10.0.12", "Microsoft.Extensions.DependencyInjection.Abstractions/10.0.12"];
        CollectionAssert.AreEquivalent(expected, restored["targets"]!["net10.0"]!.AsObject().Select(item => item.Key).ToArray());
        Assert.IsTrue(restored["targets"]!["net10.0"]!.AsObject().All(item => (string?)item.Value!["type"] == "package"));
        Assert.AreEqual(1, restored["packageFolders"]!.AsObject().Count);
        Assert.AreEqual(Path.Combine(fixture.Root, "packages"), Path.TrimEndingDirectorySeparator(restored["packageFolders"]!.AsObject().Single().Key));
        Assert.AreEqual(Hash(package), Hash(Path.Combine(fixture.Root, "packages", "cis.core", version, "cis.core." + version + ".nupkg")));
        File.WriteAllText(Path.Combine(fixture.Root, "candidate.json"), new JsonObject
        {
            ["packageVersion"] = version,
            ["packageSha256"] = Hash(package),
            ["packages"] = packageHashes,
            ["programSha256"] = Hash(Path.Combine(fixture.Root, "app/Program.cs")),
            ["projectSha256"] = Hash(Path.Combine(fixture.Root, "app/Consumer.csproj")),
            ["restoredAssetsSha256"] = Hash(Path.Combine(fixture.Root, "app/obj/project.assets.json")),
            ["lockSha256"] = Hash(Path.Combine(fixture.Root, "app/packages.lock.json")),
            ["assemblyHashes"] = new JsonObject(fixture.AssemblyHashes.Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, JsonValue.Create(pair.Value))))
        }.ToJsonString());
        return fixture;
    }
    internal static Dictionary<string, string?> Variables(string cwd, IReadOnlyDictionary<string, string?>? overrides = null)
    {
        var env = Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(k => k.StartsWith("CORE_", StringComparison.OrdinalIgnoreCase) || k is "DD_SERVICE" or "DD_ENV" or "DD_VERSION")
            .ToDictionary(k => k, _ => (string?)null, StringComparer.OrdinalIgnoreCase);
        foreach (var item in new Dictionary<string, string?>
        {
            ["CORE_SERVICE_NAME"] = "Candidate.App",
            ["CORE_ENVIRONMENT"] = "acceptance",
            ["CORE_SERVICE_VERSION"] = "2.3.4",
            ["CORE_LOG_CONSOLE_LEVEL"] = "None",
            ["CORE_LOG_FILE_LEVEL"] = "Debug",
            ["CORE_LOG_FILE_PATH"] = Path.Combine(cwd, "Ω-🙂.log"),
            ["CORE_LOG_CONSOLE_FORMAT"] = "jsonl",
            ["CORE_LOG_CONSOLE_STREAM"] = "stderr",
            ["CORE_LOG_FILE_FORMAT"] = "jsonl",
            ["CORE_LOG_MAX_ENTRY_BYTES"] = "16384",
            ["CORE_LOG_FILE_MAX_BYTES"] = "65536",
            ["CORE_LOG_FILE_RETAINED_FILES"] = "2",
            ["CORE_LOG_QUEUE_CAPACITY"] = "64",
            ["CORE_LOG_SHUTDOWN_TIMEOUT_MS"] = "5000",
            ["DOTNET_ROLL_FORWARD"] = "Disable"
        }) { env[item.Key] = item.Value; }
        if (overrides is not null) { foreach (var item in overrides) { env[item.Key] = item.Value; } }
        return env;
    }
    internal void Origin(JsonObject report)
    {
        Assert.AreEqual(".NET 10.0.12", (string?)report["runtime"]);
        Assert.AreEqual(OperatingSystem.IsWindows() ? "windows" : "linux", (string?)report["os"]);
        Assert.AreEqual(System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(), (string?)report["architecture"]);
        foreach (var item in AssemblyHashes)
        {
            Assert.AreEqual(item.Value, (string?)report[item.Key]!["sha256"]);
            string name = item.Key switch { "core" => "CIS.Core.dll", "logging" => "Microsoft.Extensions.Logging.Abstractions.dll", _ => "Microsoft.Extensions.DependencyInjection.Abstractions.dll" };
            Assert.AreEqual(Path.Combine(Path.GetDirectoryName(Host)!, name), (string?)report[item.Key]!["path"]);
        }
        Assert.AreEqual(Host, (string?)report["consumer"]!["path"]); Assert.AreEqual(Hash(Host), (string?)report["consumer"]!["sha256"]);
    }
    internal async Task<(JsonObject Report, string Stdout, string Stderr)> Run(string mode, string cwd, IReadOnlyDictionary<string, string?>? changes = null, int count = 1)
    {
        string observation = Path.Combine(Root, "child-" + Interlocked.Increment(ref nextChild)); Directory.CreateDirectory(observation);
        string reportPath = Path.Combine(observation, "report.json");
        ProcessResult result = await ProcessRunner.RunAsync("dotnet", ["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", Host, mode, reportPath, count.ToString(System.Globalization.CultureInfo.InvariantCulture)], cwd, Guard, Variables(cwd, changes), new UTF8Encoding(false, true));
        File.WriteAllText(Path.Combine(observation, "stdout.txt"), result.StandardOutput); File.WriteAllText(Path.Combine(observation, "stderr.txt"), result.StandardError);
        Assert.AreEqual(0, result.ExitCode, result.StandardError); JsonObject report = JsonNode.Parse(File.ReadAllBytes(reportPath))!.AsObject(); Origin(report);
        return (report, result.StandardOutput, result.StandardError);
    }
    internal Child Start(string mode, string cwd, IReadOnlyDictionary<string, string?>? changes = null) => new(this, mode, cwd, changes, Interlocked.Increment(ref nextChild));
    internal sealed class Child : IAsyncDisposable
    {
        private readonly CandidatePackageFixture owner;
        private readonly string observation;
        internal Process Process { get; }
        private StreamReader? control;
        private StreamWriter? commandPipe;
        private readonly NamedPipeServerStream? controlPipe;
        private readonly Task? connected;
        private readonly Task<string>? errors;
        private Task<string>? data;
        private Task<byte[]>? rawData;
        internal byte[]? RawStdout { get; private set; }
        private readonly bool blocked;
        private readonly List<JsonObject> replies = [];
        internal Child(CandidatePackageFixture owner, string mode, string cwd, IReadOnlyDictionary<string, string?>? changes, int id)
        {
            this.owner = owner; blocked = mode != "interactive";
            observation = Path.Combine(owner.Root, "child-" + id); Directory.CreateDirectory(observation);
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = cwd, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, StandardOutputEncoding = new UTF8Encoding(false, true), StandardErrorEncoding = new UTF8Encoding(false, true) };
            foreach (string arg in new[] { "exec", "--fx-version", "10.0.12", "--roll-forward", "Disable", owner.Host, mode, Path.Combine(observation, "report.json") }) { start.ArgumentList.Add(arg); }
            foreach (var item in Variables(cwd, changes)) { if (item.Value is null) { start.Environment.Remove(item.Key); } else { start.Environment[item.Key] = item.Value; } }
            if (blocked)
            {
                string name = "cislib-candidate-" + Guid.NewGuid().ToString("N");
                controlPipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                start.Environment["CIS_CANDIDATE_CONTROL_PIPE"] = name;
                connected = controlPipe.WaitForConnectionAsync();
            }
            Process = Process.Start(start)!;
            if (blocked) { errors = Process.StandardError.ReadToEndAsync(); }
            else { control = Process.StandardOutput; }
            if (!blocked) { data = Process.StandardError.ReadToEndAsync(); }
        }
        internal async Task<JsonObject> Read()
        {
            if (blocked && control is null)
            {
                await connected!.WaitAsync(Guard); control = new StreamReader(controlPipe!); commandPipe = new StreamWriter(controlPipe!) { AutoFlush = true };
            }
            string? line = await control!.ReadLineAsync().WaitAsync(Guard); Assert.IsNotNull(line, "Candidate ended before its control reply.");
            JsonObject report = JsonNode.Parse(line)!.AsObject(); owner.Origin(report); Assert.AreEqual(Process.Id, report["pid"]!.GetValue<int>()); replies.Add(report);
            File.WriteAllText(Path.Combine(observation, "replies.json"), new JsonArray(replies.Select(r => r.DeepClone()).ToArray()).ToJsonString()); return report;
        }
        internal async Task<JsonObject> Command(string command)
        {
            if (blocked) { await commandPipe!.WriteLineAsync(command); await commandPipe.FlushAsync(); }
            else { await Process.StandardInput.WriteLineAsync(command); await Process.StandardInput.FlushAsync(); }
            return await Read();
        }
        private async Task<byte[]> DrainRaw()
        {
            using var buffer = new MemoryStream(); await Process.StandardOutput.BaseStream.CopyToAsync(buffer); return buffer.ToArray();
        }
        internal Task BeginDrain() => rawData ??= DrainRaw();
        internal async Task<string> Finish()
        {
            await Process.WaitForExitAsync().WaitAsync(Guard); Assert.AreEqual(0, Process.ExitCode);
            Assert.AreEqual("", await control!.ReadToEndAsync().WaitAsync(Guard));
            if (errors is not null) { Assert.AreEqual("", await errors.WaitAsync(Guard)); }
            if (blocked)
            {
                RawStdout = await (rawData ?? throw new InvalidOperationException("Data stream must be drained before finish.")).WaitAsync(Guard);
                File.WriteAllBytes(Path.Combine(observation, "stdout.bin"), RawStdout); return "";
            }
            string text = await data!.WaitAsync(Guard);
            File.WriteAllText(Path.Combine(observation, "stderr.txt"), text); return text;
        }
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!Process.HasExited) { Process.Kill(entireProcessTree: true); }
                await Process.WaitForExitAsync().WaitAsync(Guard);
                if (data is not null) { File.WriteAllText(Path.Combine(observation, "terminal-stream.txt"), await data.WaitAsync(Guard)); }
                if (errors is not null) { File.WriteAllText(Path.Combine(observation, "terminal-stderr.txt"), await errors.WaitAsync(Guard)); }
                if (rawData is not null) { File.WriteAllBytes(Path.Combine(observation, "terminal-stdout.bin"), await rawData.WaitAsync(Guard)); }
            }
            finally
            {
                controlPipe?.Dispose(); Process.Dispose();
                if (connected is not null)
                {
                    try { await connected.WaitAsync(Guard); }
                    catch (Exception error) when (error is IOException or ObjectDisposedException or OperationCanceledException) { }
                }
            }
        }
    }
}
