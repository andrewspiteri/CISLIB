using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CIS.Verification;

// Invocation glue only: the ordinary CIS job owns timeout/process/storage guards.
// Native outcomes remain the unchanged Stryker report, never a child-audit flag.
internal static class HealthcheckMutationNativeRun
{
    private const string SessionVariable = "CIS_HEALTHCHECK_MUTATION_SESSION";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static StringComparison Comparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    internal sealed record Pin(string Path, string Sha256, long Bytes);
    internal sealed record Request(string LanguageRoot, string ProfileId, string Dotnet, string Cli, string Core,
        string ChildRoot, string AttemptId, string Output, string Configuration, string Source, string[] Arguments, long MaximumChildBytes);

    internal static async Task<int> RunAsync(string[] arguments, Func<string[], Task<int>> prepare)
    {
        Request request = Read(arguments);
        Need(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(SessionVariable)), "The outer invocation must not inherit a child mutation session.");
        string attempt = Path.Combine(request.ChildRoot, request.AttemptId);
        Need(!Directory.Exists(attempt) && !File.Exists(attempt), "A child attempt already exists; use a fresh CIS output attempt.");
        Directory.CreateDirectory(request.ChildRoot);
        NoLinks(request.ChildRoot);
        CheckOwnedAttempts(request.ChildRoot, request.ProfileId);
        Directory.CreateDirectory(attempt);
        NoLinks(attempt);
        string configuration = Path.Combine(attempt, "native-config.json");
        File.Copy(request.Configuration, configuration, overwrite: false);
        Need(Hash(configuration) == Hash(request.Configuration), "Rendered native configuration changed while copying.");
        string sessionRoot = Path.Combine(attempt, "session");
        int prepared = await prepare(["prepare", "--language-root", request.LanguageRoot, "--config", configuration,
            "--stryker-core", request.Core, "--dotnet", request.Dotnet, "--output", sessionRoot, "--phase", "automatic"]);
        Need(prepared == 0, "The maintained child session producer failed; native Stryker was not started.");
        string sessionPath = Path.Combine(sessionRoot, "session.json");
        NoLinks(sessionPath);
        Pin configPin = Pinned(configuration), sessionPin = Pinned(sessionPath), sourcePin = Pinned(request.Source);
        var tools = new
        {
            dotnet = Pinned(request.Dotnet),
            strykerCli = Pinned(request.Cli),
            strykerCore = Pinned(request.Core),
            wrapper = Pinned(typeof(HealthcheckMutationNativeRun).Assembly.Location),
            helper = Pinned(typeof(CIS.TestSupport.ProcessRunner).Assembly.Location),
            wrapperDeps = Pinned(Path.ChangeExtension(typeof(HealthcheckMutationNativeRun).Assembly.Location, ".deps.json")),
            wrapperRuntimeConfig = Pinned(Path.ChangeExtension(typeof(HealthcheckMutationNativeRun).Assembly.Location, ".runtimeconfig.json")),
        };
        string[] nativeArguments = ReplaceConfiguration(request.Arguments, request.Configuration, configuration);
        string[] command = [request.Dotnet, request.Cli, .. nativeArguments];
        string preparedPath = Path.Combine(attempt, "prepared.json");
        WriteNew(preparedPath, new
        {
            schema = "cis.healthcheck.shared-child-attempt-prepared/v1",
            request.ProfileId,
            request.AttemptId,
            phase = "mutation",
            request.LanguageRoot,
            outputRoot = request.Output,
            workingDirectory = Path.Combine(request.LanguageRoot, "src", "CIS.Core"),
            config = configPin,
            session = sessionPin,
            source = sourcePin,
            tools,
            command,
            createdUtc = DateTimeOffset.UtcNow,
        });
        CheckBytes(request.ChildRoot, request.MaximumChildBytes);
        var start = new ProcessStartInfo(request.Dotnet)
        {
            WorkingDirectory = Path.Combine(request.LanguageRoot, "src", "CIS.Core"),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(request.Cli);
        foreach (string argument in nativeArguments) { start.ArgumentList.Add(argument); }
        start.Environment[SessionVariable] = sessionPath;
        using var process = new Process { StartInfo = start };
        Need(process.Start(), "Native Stryker did not start.");
        DateTimeOffset startedUtc = process.StartTime.ToUniversalTime();
        string startedPath = Path.Combine(attempt, "started.json");
        WriteNew(startedPath, new
        {
            schema = "cis.healthcheck.shared-child-attempt-started/v1",
            prepared = Pinned(preparedPath),
            processId = process.Id,
            startedUtc,
            command,
        });
        Task stdout = process.StandardOutput.BaseStream.CopyToAsync(Console.OpenStandardOutput());
        Task stderr = process.StandardError.BaseStream.CopyToAsync(Console.OpenStandardError());
        await process.WaitForExitAsync();
        await Task.WhenAll(stdout, stderr);
        DateTimeOffset finishedUtc = process.ExitTime.ToUniversalTime();
        int exitCode = process.ExitCode;
        Need(Pinned(configuration) == configPin && Pinned(sessionPath) == sessionPin && Pinned(request.Source) == sourcePin,
            "The copied configuration, session or source changed during native mutation.");
        Need(Pinned(request.Dotnet) == tools.dotnet && Pinned(request.Cli) == tools.strykerCli && Pinned(request.Core) == tools.strykerCore
            && Pinned(typeof(HealthcheckMutationNativeRun).Assembly.Location) == tools.wrapper
            && Pinned(typeof(CIS.TestSupport.ProcessRunner).Assembly.Location) == tools.helper
            && Pinned(tools.wrapperDeps.Path) == tools.wrapperDeps && Pinned(tools.wrapperRuntimeConfig.Path) == tools.wrapperRuntimeConfig,
            "A pinned wrapper/native producer changed during mutation.");
        long bytes = CheckBytes(request.ChildRoot, request.MaximumChildBytes);
        string report = Path.Combine(request.Output, "reports", "mutation-report.json");
        Pin? reportPin = File.Exists(report) ? Pinned(report) : null;
        string binding = Path.Combine(attempt, "binding.json");
        WriteNew(binding, new
        {
            schema = "cis.healthcheck.shared-child-attempt/v1",
            request.ProfileId,
            request.AttemptId,
            phase = "mutation",
            request.LanguageRoot,
            outputRoot = request.Output,
            workingDirectory = start.WorkingDirectory,
            config = configPin,
            session = sessionPin,
            source = sourcePin,
            tools,
            phases = new { prepared = Pinned(preparedPath), started = Pinned(startedPath) },
            native = new
            {
                command,
                processId = process.Id,
                exitCode,
                startedUtc,
                finishedUtc,
                reportSha256 = reportPin?.Sha256,
                reportBytes = reportPin?.Bytes,
            },
            capture = new { root = request.ChildRoot, sessionRoot, bytesBeforeBinding = bytes, maximumBytes = request.MaximumChildBytes },
        });
        CheckBytes(request.ChildRoot, request.MaximumChildBytes);
        Console.Error.WriteLine("Child attempt binding: " + binding);
        return exitCode;
    }

    internal static Request Read(string[] arguments)
    {
        int separator = Array.IndexOf(arguments, "--");
        Need(separator > 0 && separator % 2 == 0 && arguments.Length - separator - 1 is > 0 and <= 256,
            "Expected explicit wrapper options followed by -- and unchanged native arguments.");
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 0; index < separator; index += 2)
        {
            Need(options.TryAdd(arguments[index], arguments[index + 1]), "Duplicate child wrapper option.");
        }
        string[] required = ["--language-root", "--profile-id", "--dotnet", "--stryker-cli", "--stryker-core", "--child-root"];
        Need(required.All(options.ContainsKey) && options.Keys.All(key => required.Contains(key, StringComparer.Ordinal) || key == "--maximum-child-bytes"),
            "Unknown or missing child wrapper options.");
        string language = Full(options["--language-root"]);
        string repository = Path.GetFullPath(Path.Combine(language, "..", ".."));
        Need(Same(language, Path.Combine(repository, "implementations", "csharp")), "Use the maintained implementations/csharp language root.");
        string id = options["--profile-id"];
        Need(Regex.IsMatch(id, @"\A[A-Za-z0-9][A-Za-z0-9._-]{0,63}\z", RegexOptions.CultureInvariant), "Unsafe profile ID.");
        string profileRoot = Path.Combine(repository, ".cis", "local", "testing", "incremental", id);
        string childRoot = Full(options["--child-root"]);
        Need(Same(childRoot, Path.Combine(profileRoot, "child-evidence")), "Child evidence must use the selected profile's owned retained root.");
        string[] native = arguments[(separator + 1)..];
        Need(native.All(value => value.Length <= 32768 && !value.Contains('\0')), "Native arguments are invalid or excessive.");
        string config = Full(Option(native, "--config-file")), output = Full(Option(native, "--output"));
        string attempt = Path.GetFileName(Path.TrimEndingDirectorySeparator(output));
        Need(Regex.IsMatch(attempt, @"\A[a-f0-9]{12}\z", RegexOptions.CultureInvariant)
            && Same(Path.GetDirectoryName(output)!, Path.Combine(profileRoot, "work")) && Same(config, Path.Combine(output, "stryker-config.json")),
            "Use the exact CIS-owned attempt output and rendered configuration.");
        foreach (string path in new[] { language, childRoot, output, config }) { NoLinks(path); }
        Need(Directory.Exists(language) && Directory.Exists(output) && File.Exists(config), "The language root or rendered CIS attempt is missing.");
        Need(new FileInfo(config).Length is > 0 and <= 262144, "Rendered native configuration exceeds256KiB.");
        string dotnet = Full(options["--dotnet"]), cli = Full(options["--stryker-cli"]), core = Full(options["--stryker-core"]);
        foreach (string path in new[] { dotnet, cli, core }) { NoLinks(path); Need(File.Exists(path), "An explicit native tool is missing."); }
        Need(Path.GetFileName(core) == "Stryker.Core.dll" && Same(Path.GetDirectoryName(cli)!, Path.GetDirectoryName(core)!),
            "Use one installed native Stryker CLI/Core deployment.");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(config));
        Need(document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("stryker-config", out JsonElement settings)
            && settings.ValueKind == JsonValueKind.Object && settings.TryGetProperty("mutate", out JsonElement mutate) && mutate.ValueKind == JsonValueKind.Array,
            "A rendered native source inventory is required.");
        JsonElement sourceArray = document.RootElement.GetProperty("stryker-config").GetProperty("mutate");
        Need(sourceArray.GetArrayLength() == 1 && sourceArray[0].ValueKind == JsonValueKind.String, "One rendered native source is required.");
        string[] sources = sourceArray.EnumerateArray().Select(item => item.GetString() ?? "").ToArray();
        Need(sources.Length == 1 && sources[0].Split('/').All(part => part.Length > 0 && part is not ("." or ".."))
            && !sources[0].Any(character => character is '\\' or ':' or '*' or '?' or '{' or '}'), "The rendered configuration must select one complete portable source file.");
        string source = Path.GetFullPath(Path.Combine(language, "src", "CIS.Core", sources[0]));
        NoLinks(source);
        Need(File.Exists(source) && new FileInfo(source).Length is > 0 and <= 16777216, "The rendered native source is missing or exceeds16MiB.");
        long maximum = options.TryGetValue("--maximum-child-bytes", out string? text)
            && long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed) ? parsed : 268435456;
        Need(maximum is > 0 and <= 1073741824 && (!options.ContainsKey("--maximum-child-bytes") || text == maximum.ToString(CultureInfo.InvariantCulture)),
            "An explicit child byte cap must be a positive integer through 1 GiB.");
        Need(!Directory.Exists(Path.Combine(childRoot, attempt)) && !File.Exists(Path.Combine(childRoot, attempt)),
            "A child attempt already exists; use a fresh CIS output attempt.");
        if (Directory.Exists(childRoot)) { CheckOwnedAttempts(childRoot, id); }
        return new(language, id, dotnet, cli, core, childRoot, attempt, output, config, source, native, maximum);
    }

    internal static string[] ReplaceConfiguration(string[] arguments, string original, string retained)
    {
        string[] result = [.. arguments];
        int index = Array.IndexOf(result, "--config-file");
        Need(index >= 0 && index + 1 < result.Length && Same(result[index + 1], original), "The original native configuration selector changed.");
        result[index + 1] = retained;
        return result;
    }
    private static string Option(string[] arguments, string key)
    {
        int[] indices = arguments.Select((value, index) => (value, index)).Where(item => item.value == key).Select(item => item.index).ToArray();
        Need(indices.Length == 1 && indices[0] + 1 < arguments.Length, "Use one explicit native option: " + key);
        return arguments[indices[0] + 1];
    }
    private static long CheckBytes(string root, long maximum)
    {
        long bytes = 0;
        foreach (string path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
        {
            NoLinks(path);
            if (File.Exists(path)) { bytes = checked(bytes + new FileInfo(path).Length); Need(bytes <= maximum, "Child retained storage exceeded its explicit byte cap."); }
        }
        return bytes;
    }
    private static void CheckOwnedAttempts(string root, string profileId)
    {
        foreach (string path in Directory.EnumerateFileSystemEntries(root))
        {
            NoLinks(path);
            string id = Path.GetFileName(path);
            Need(Directory.Exists(path) && Regex.IsMatch(id, @"\A[a-f0-9]{12}\z", RegexOptions.CultureInvariant),
                "The selected child root contains a foreign entry.");
            string prepared = Path.Combine(path, "prepared.json");
            NoLinks(prepared);
            Need(File.Exists(prepared) && new FileInfo(prepared).Length is > 0 and <= 1048576,
                "An existing child attempt lacks its bounded producer ownership record.");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(prepared));
            JsonElement record = document.RootElement;
            Need(record.ValueKind == JsonValueKind.Object && record.TryGetProperty("schema", out JsonElement schema) && schema.ValueKind == JsonValueKind.String
                && schema.GetString() == "cis.healthcheck.shared-child-attempt-prepared/v1" && record.TryGetProperty("profileId", out JsonElement profile)
                && profile.ValueKind == JsonValueKind.String && profile.GetString() == profileId && record.TryGetProperty("attemptId", out JsonElement attempt)
                && attempt.ValueKind == JsonValueKind.String && attempt.GetString() == id,
                "An existing child attempt belongs to another producer/profile.");
        }
    }
    private static Pin Pinned(string path) { NoLinks(path); return new(Path.GetFullPath(path), Hash(path), new FileInfo(path).Length); }
    private static string Hash(string path)
    { using FileStream stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
    private static void WriteNew(string path, object value)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        Need(bytes.Length <= 1048576, "A child attempt binding exceeds 1 MiB.");
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        file.Write(bytes); file.Flush(flushToDisk: true);
    }
    private static string Full(string path)
    { Need(Path.IsPathFullyQualified(path), "Use explicit absolute child wrapper paths."); return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
    private static bool Same(string left, string right) => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), Comparison);
    private static void NoLinks(string path)
    {
        for (string? entry = Path.GetFullPath(path); entry is not null; entry = Path.GetDirectoryName(entry))
        {
            FileAttributes attributes;
            try { attributes = File.GetAttributes(entry); }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { continue; }
            if ((attributes & FileAttributes.ReparsePoint) != 0) { throw new InvalidDataException("Linked child wrapper paths are forbidden."); }
        }
    }
    private static void Need(bool condition, string message) { if (!condition) { throw new InvalidDataException(message); } }
}
