using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.TestSupport;

namespace CIS.Verification;

// Shared file/process operations for the existing Type Manager development checks.
internal sealed class TypeManagerVerificationInputs
{
    private readonly Dictionary<string, string> options;
    internal TypeManagerVerificationInputs(string[] arguments, string[] required, string[] optional)
    {
        options = new(StringComparer.Ordinal); var allowed = required.Concat(optional).Append("dotnet").ToHashSet(StringComparer.Ordinal);
        for (int index = 0; index < arguments.Length; index += 2)
        {
            if (index + 1 >= arguments.Length || !arguments[index].StartsWith("--", StringComparison.Ordinal) || !allowed.Contains(arguments[index][2..]) || !options.TryAdd(arguments[index][2..], arguments[index + 1]))
            { throw new ArgumentException("Use each supported named option once, with its value."); }
        }
        if (required.Any(key => !options.ContainsKey(key))) { throw new ArgumentException("Missing required option: " + string.Join(", ", required.Where(key => !options.ContainsKey(key)))); }
    }
    internal string Get(string key) => options.TryGetValue(key, out string? value) ? value : throw new ArgumentException("Missing --" + key);
    internal string? Optional(string key) => options.GetValueOrDefault(key);
    internal string PathOf(string key) => Path.GetFullPath(Get(key));
    internal string Dotnet => Optional("dotnet") ?? "dotnet";
    internal string Language => Path.Combine(PathOf("repo-root"), "implementations", "csharp");
    internal string Output => PathOf("output-root");
    internal void EmptyOutput()
    {
        string root = Output;
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) { throw new InvalidDataException("Use an empty output directory."); }
        Directory.CreateDirectory(root); Directory.CreateDirectory(Path.Combine(root, "logs"));
    }
    internal async Task<ProcessResult> Run(string name, IEnumerable<string> arguments, string? cwd = null, string? expectedError = null, IReadOnlyDictionary<string, string?>? environment = null, TimeSpan? timeout = null, bool requireSuccess = true)
    {
        string[] command = arguments.ToArray();
        ProcessResult child = await ProcessRunner.RunAsync(Dotnet, command, cwd ?? Output, timeout ?? TimeSpan.FromMinutes(4), environment, new UTF8Encoding(false, true));
        string combined = child.StandardOutput + child.StandardError;
        Write(Path.Combine(Output, "logs", name + ".log"), combined);
        WriteJson(Path.Combine(Output, "logs", name + ".process.json"), new JsonObject
        {
            ["pid"] = child.ProcessId,
            ["executable"] = Dotnet,
            ["arguments"] = JsonSerializer.SerializeToNode(command),
            ["exitCode"] = child.ExitCode,
            ["stdoutSha256"] = Digest(Encoding.UTF8.GetBytes(child.StandardOutput)),
            ["stderrSha256"] = Digest(Encoding.UTF8.GetBytes(child.StandardError))
        });
        Require(expectedError is null ? !requireSuccess || child.ExitCode == 0 : child.ExitCode != 0 && combined.Contains(expectedError, StringComparison.Ordinal), name + (expectedError is null ? " failed; inspect its log." : " did not reject for " + expectedError));
        return child;
    }
    internal JsonObject CoreSources() => TreeHashes(Path.Combine(Language, "src", "CIS.Core"), path => path.EndsWith(".cs", StringComparison.Ordinal), sourceOnly: true);
    internal JsonObject CoreProjects() => new(new[] { "src/CIS.Core/CIS.Core.csproj", "global.json", "Directory.Build.props", "Directory.Packages.props", "NuGet.config" }
        .Select(name => new KeyValuePair<string, JsonNode?>(name, JsonValue.Create(Hash(Path.Combine(Language, name))))));
    internal static JsonObject TreeHashes(string root, Func<string, bool>? include = null, bool sourceOnly = false) =>
        new(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(path => (!sourceOnly || !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj")) && (include is null || include(path)))
            .Order(StringComparer.Ordinal).Select(path => new KeyValuePair<string, JsonNode?>(Path.GetRelativePath(root, path).Replace('\\', '/'), JsonValue.Create(Hash(path)))));
    internal static void Copy(string source, string target, string? expectedHash = null)
    {
        string before = Hash(source); Require(expectedHash is null || before == expectedHash, "Input differs from its pin: " + source);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target, overwrite: true);
        Require(Hash(source) == before && Hash(target) == before, "Input changed while copying: " + source);
    }
    internal static void CopyTree(string source, string target, bool sourceOnly = false)
    {
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file);
            if (sourceOnly && relative.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or "__pycache__")) { continue; }
            Copy(file, Path.Combine(target, relative));
        }
    }
    internal static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    internal static string Hash(string path) => Digest(File.ReadAllBytes(path));
    internal static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    internal static void Write(string path, string text) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text, new UTF8Encoding(false)); }
    internal static void WriteJson(string path, JsonObject value) => Write(path, value.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    internal static void Require(bool value, string reason) { if (!value) { throw new InvalidDataException(reason); } }
}
