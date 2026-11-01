using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

internal static partial class HealthcheckGroupedMutation
{
    internal sealed record Toolchain(string Schema, string Platform, string Host, string SubjectDotnet, string ToolDotnet, string Sdk,
        string SubjectRuntime, string ToolRuntime, Input[] Files, Input[] Adapters);

    internal static string SdkMsBuildPath(Toolchain tools)
    {
        Require(tools.Sdk == "10.0.401", "Native project analysis requires the pinned SDK10.0.401.");
        Input[] matches = tools.Files.Where(pin => pin.Path.Replace('\\', '/').Split('/')[^1] == "MSBuild.dll").ToArray();
        Require(matches.Length == 1, "Native project analysis requires one unambiguous pinned SDK MSBuild.dll.");
        Input selected = matches[0];
        string path = selected.Path.Replace('\\', '/');
        bool absolute = path.StartsWith('/') || (path.Length > 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '/');
        Require(absolute && path.EndsWith("/" + tools.Sdk + "/MSBuild.dll", StringComparison.Ordinal)
            && !path.Split('/').Any(part => part is "." or "..")
            && selected.Sha256.Length == 64 && selected.Sha256.All(char.IsAsciiHexDigit),
            "Native project analysis must select the absolute, hash-pinned SDK10.0.401 MSBuild.dll.");
        return selected.Path;
    }

    internal static void ValidateSdkMsBuildEnvironment(JsonObject run, Toolchain tools)
    {
        string expected = SdkMsBuildPath(tools);
        JsonObject? environment = run["environment"] as JsonObject;
        string? Value(string name) => environment?[name] is JsonValue value && value.TryGetValue<string>(out string? text) ? text : null;
        string? actual = Value("MSBUILD_EXE_PATH");
        bool windowsPath = WindowsToolPath(expected);
        Require(actual is not null && (windowsPath
            ? string.Equals(actual.Replace('\\', '/'), expected.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)
            : string.Equals(actual, expected, StringComparison.Ordinal)),
            "Actual native project analysis environment differs from the frozen SDK MSBuild pin.");

        string subject = tools.SubjectDotnet.Replace('\\', '/');
        bool windowsHost = WindowsToolPath(tools.SubjectDotnet);
        StringComparison comparison = windowsHost ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        int separator = subject.LastIndexOf('/');
        Require(separator >= 0 && (subject.StartsWith('/') || (windowsHost && subject.Length > 3 && char.IsAsciiLetter(subject[0]) && subject[2] == '/'))
            && tools.Files.Any(pin => string.Equals(pin.Path.Replace('\\', '/'), subject, comparison)
                && pin.Sha256.Length == 64 && pin.Sha256.All(char.IsAsciiHexDigit)),
            "Native project analysis requires the already pinned absolute subject dotnet host.");
        Require(string.Equals(subject[(separator + 1)..], windowsHost ? "dotnet.exe" : "dotnet", comparison),
            "Native project analysis requires the canonical dotnet host filename used by its PATH lookup.");
        string directory = separator == 0 ? "/" : subject[..(separator == 2 && windowsHost ? 3 : separator)];
        string? path = Value("PATH");
        string? first = path?.Split(windowsHost ? ';' : ':')[0];
        Require(first is not null && string.Equals(windowsHost ? first.Replace('\\', '/') : first, directory, comparison),
            "Native project analysis PATH must begin with the frozen subject dotnet directory.");
    }

    private static bool WindowsToolPath(string path) => (path.Length > 2 && char.IsAsciiLetter(path[0]) && path[1] == ':') || path.StartsWith("\\\\", StringComparison.Ordinal)
        || path.StartsWith("//", StringComparison.Ordinal);

    private static async Task BindToolchainAsync(string root, Candidate candidate, Group group, string language, string subjectDotnet, string toolDotnet, string cli)
    {
        string platform = RuntimeInformation.OSDescription + "; " + RuntimeInformation.ProcessArchitecture + "; " + RuntimeInformation.FrameworkDescription;
        Require(platform == candidate.Platform && Environment.MachineName == candidate.Host, "Actual native platform differs from the prepared candidate.");
        string checker = typeof(HealthcheckGroupedMutation).Assembly.Location;
        var files = new List<string> { subjectDotnet, toolDotnet, typeof(object).Assembly.Location,
            checker, Path.ChangeExtension(checker, ".deps.json"), Path.ChangeExtension(checker, ".runtimeconfig.json"), typeof(ProcessRunner).Assembly.Location };
        // Bind the actual native deployment rather than trusting package/version labels alone.
        files.AddRange(Directory.EnumerateFiles(Path.GetDirectoryName(cli)!).Where(path => Path.GetExtension(path) is ".dll" or ".json" or ".exe" or ".so"));
        ProcessResult sdks = await ProcessRunner.RunAsync(subjectDotnet, ["--list-sdks"], language, TimeSpan.FromSeconds(20));
        Require(sdks.ExitCode == 0, "Cannot observe actual SDK installation.");
        string sdkRoot = Installation(sdks.StandardOutput, "10.0.401");
        foreach (string name in new[] { "MSBuild.dll", "Microsoft.Build.dll", "Microsoft.Build.Framework.dll", "Roslyn/bincore/csc.dll", "Roslyn/bincore/Microsoft.CodeAnalysis.CSharp.dll" })
        { files.Add(Path.Combine(sdkRoot, "10.0.401", name)); }
        foreach ((string dotnet, string version) in new[] { (subjectDotnet, "10.0.12"), (toolDotnet, "8.0.31") })
        {
            ProcessResult runtimes = await ProcessRunner.RunAsync(dotnet, ["--list-runtimes"], language, TimeSpan.FromSeconds(20));
            Require(runtimes.ExitCode == 0, "Cannot observe actual native runtime installation.");
            string runtimeRoot = Installation(runtimes.StandardOutput, "Microsoft.NETCore.App " + version);
            files.Add(Path.Combine(runtimeRoot, version, "System.Private.CoreLib.dll"));
            files.Add(Path.Combine(runtimeRoot, version, "Microsoft.NETCore.App.deps.json"));
        }
        Input[] adapters = group.Projects.SelectMany(project =>
        {
            string directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Path.Combine(language, "src/CIS.Core", project)))!, "bin/Release/net10.0");
            return Directory.EnumerateFiles(directory).Where(path =>
                (Path.GetExtension(path) == ".dll" && (Path.GetFileName(path).Contains("MSTest", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(path).Contains("TestAdapter", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path).Contains("TestPlatform", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(path).StartsWith("testhost", StringComparison.OrdinalIgnoreCase))) || Path.GetFileName(path) is "compatibility-manifest.json" or "testhost.deps.json" or "testhost.runtimeconfig.json")
                .Select(path => new Input(Path.GetFileName(path), Hash(path)));
        }).Distinct().OrderBy(pin => pin.Path, StringComparer.Ordinal).ToArray();
        Require(adapters.Length > 0 && adapters.Select(pin => pin.Path).Distinct(StringComparer.Ordinal).Count() == adapters.Length,
            "Selected projects do not share one pinned native MSTest adapter payload.");
        Input[] pins = files.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).Select(path => new Input(Path.GetFullPath(path), Hash(path))).ToArray();
        var actual = new Toolchain("cis.healthcheck.grouped-toolchain/v1", platform, Environment.MachineName,
            subjectDotnet, toolDotnet, "10.0.401", "10.0.12", "8.0.31", pins, adapters);
        string path = Path.Combine(root, "toolchain.json");
        if (File.Exists(path))
        {
            Toolchain expected = ReadToolchain(root, candidate);
            Require(actual.Platform == expected.Platform && actual.Host == expected.Host && actual.SubjectDotnet == expected.SubjectDotnet
                && actual.ToolDotnet == expected.ToolDotnet && actual.Files.SequenceEqual(expected.Files) && actual.Adapters.SequenceEqual(expected.Adapters),
                "Native tool/SDK/runtime/adapter bytes changed after the correctness pilot; freeze a new candidate and requalify.");
        }
        else
        {
            Require(group.Name == "pilot", "The correctness pilot must freeze native tool/adapter bytes before expansion.");
            Write(path, JsonSerializer.Serialize(actual, Json));
        }
    }

    private static Toolchain ReadToolchain(string root, Candidate candidate)
    {
        Toolchain tools = JsonSerializer.Deserialize<Toolchain>(File.ReadAllText(Path.Combine(root, "toolchain.json")), Json)
            ?? throw new InvalidDataException("Missing frozen native toolchain.");
        Require(tools.Schema == "cis.healthcheck.grouped-toolchain/v1" && tools.Platform == candidate.Platform && tools.Host == candidate.Host
            && tools.Sdk == "10.0.401" && tools.SubjectRuntime == "10.0.12" && tools.ToolRuntime == "8.0.31", "Frozen native toolchain identity differs from the candidate.");
        Require(tools.Files.Length > 0 && tools.Adapters.Length > 0 && tools.Files.All(pin => File.Exists(pin.Path) && Hash(pin.Path) == pin.Sha256),
            "An actual frozen native tool/SDK/runtime file changed or disappeared.");
        return tools;
    }

    private static string Installation(string output, string prefix)
    {
        string line = output.Split('\n').Single(value => value.StartsWith(prefix + " [", StringComparison.Ordinal)).Trim();
        int start = line.IndexOf('[', StringComparison.Ordinal);
        Require(start > 0 && line.EndsWith(']'), "Cannot resolve pinned native installation: " + prefix);
        return line[(start + 1)..^1];
    }

    private static string Executable(string selected)
    {
        if (Path.IsPathFullyQualified(selected)) { Require(File.Exists(selected), "Selected dotnet host does not exist."); return Path.GetFullPath(selected); }
        Require(selected is "dotnet" or "dotnet.exe", "Select an absolute dotnet host path or the ordinary dotnet PATH command.");
        string name = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        string? path = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).Select(directory => Path.Combine(directory.Trim('"'), name)).FirstOrDefault(File.Exists);
        Require(path is not null, "dotnet was not found on PATH.");
        return Path.GetFullPath(path!);
    }
}
