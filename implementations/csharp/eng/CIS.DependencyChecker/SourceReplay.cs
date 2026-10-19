using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.TestSupport;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Verification;

// Directory/source replay only. Restoring historical products and complete offline Git provenance is separate.
internal static class SourceReplay
{
    internal static async Task<int> RunAsync(string[] args)
    {
        var options = new TypeManagerVerificationInputs(args, ["repo-root", "output-root", "profile", "lane"], ["input-root", "inventory-path", "powershell"]);
        string profile = options.Get("profile"), lane = options.Get("lane");
        Require(profile is "Developer" or "Release", "Expected Developer or Release profile.");
        Require(!string.IsNullOrWhiteSpace(lane), "Expected a recorded lane.");
        string output = options.Output, repo = options.PathOf("repo-root");
        Require(!File.Exists(output) && !Directory.Exists(output), "Use a new replay output directory.");
        Directory.CreateDirectory(output);
        string original = Path.GetFullPath(options.Optional("input-root") ?? repo);
        string immutable = Path.Combine(output, "input"), work = Path.Combine(output, "work");
        var result = new JsonObject
        {
            ["schema"] = "cis.replay/v1",
            ["profile"] = profile,
            ["lane"] = lane,
            ["runRoot"] = output,
            ["exitCode"] = 1,
            ["inputVerifiedBefore"] = false,
            ["inputVerifiedAfter"] = false,
            ["detail"] = null
        };
        SourceInventoryEntry[]? inventory = null;
        bool materialized = false;
        int exitCode = 1;
        try
        {
            if (options.Optional("inventory-path") is { } inventoryPath)
            {
                byte[] bytes = File.ReadAllBytes(Path.GetFullPath(inventoryPath));
                result["providedInventorySha256"] = Digest(bytes);
                inventory = JsonSerializer.Deserialize<SourceInventoryEntry[]>(WithoutBom(bytes)) ?? throw new InvalidDataException("Input inventory is empty.");
            }
            else { inventory = await SourceInventory.CaptureAsync(original); }
            ValidateInventory(inventory);
            AssertInputs(original, inventory);
            foreach (SourceInventoryEntry pin in inventory)
            {
                Copy(InputPath(original, pin.Path), InputPath(immutable, pin.Path), pin.Sha256);
                Copy(InputPath(original, pin.Path), InputPath(work, pin.Path), pin.Sha256);
            }
            AssertInputs(original, inventory); AssertInputs(immutable, inventory); AssertInputs(work, inventory);
            materialized = true;
            result["inputVerifiedBefore"] = true;
            result["sourceFileCount"] = inventory.Length;
            Write(Path.Combine(output, "input-inventory.json"), JsonSerializer.Serialize(inventory, new JsonSerializerOptions { WriteIndented = true }) + "\n");
            ProcessResult child = await ProcessRunner.RunAsync(options.Optional("powershell") ?? "pwsh", VerificationArguments(work, profile, lane),
                work, TimeSpan.FromMinutes(45), EnvironmentFor(output), Encoding.UTF8);
            // Keep the established human log, plus original streams and hashes for exact diagnostics.
            ToolchainQualification.RecordProcess(output, "verification", child);
            Write(Path.Combine(output, "verification.log"), child.StandardOutput + child.StandardError);
            result["verification"] = new JsonObject
            {
                ["executable"] = options.Optional("powershell") ?? "pwsh",
                ["arguments"] = JsonSerializer.SerializeToNode(VerificationArguments(work, profile, lane)),
                ["workingDirectory"] = work,
                ["process"] = "logs/verification.process.json",
                ["exitCode"] = child.ExitCode
            };
            exitCode = child.ExitCode;
            result["detail"] = "Copied source/input replay with isolated caches. This is not historical binary/package replay or a release bundle certification.";
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or JsonException or System.ComponentModel.Win32Exception or TimeoutException)
        { exitCode = 1; result["detail"] = error.Message; }
        finally
        {
            // Even a failed/timed-out child must not leave a false integrity success.
            if (materialized && inventory is not null)
            {
                try { AssertInputs(original, inventory); AssertInputs(immutable, inventory); AssertInputs(work, inventory); result["inputVerifiedAfter"] = true; }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
                { exitCode = 1; result["detail"] = error.Message; }
            }
            result["exitCode"] = exitCode;
            WriteJson(Path.Combine(output, "summary.json"), result);
            WriteJson(Path.Combine(repo, ".artifacts/csharp/replay/latest.json"), result);
        }
        Console.WriteLine(result.ToJsonString());
        return exitCode;
    }

    internal static void ValidateInventory(SourceInventoryEntry[] inventory)
    {
        Require(inventory.Length > 0, "Input inventory is empty.");
        var paths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (SourceInventoryEntry? pin in inventory)
        {
            Require(pin is not null, "Null input inventory entry.");
            string relative = Relative(pin!.Path);
            Require(paths.Add(relative), "Duplicate input path: " + relative);
            Require(pin.Sha256 is { Length: 64 } && pin.Sha256.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'), "Invalid input SHA-256.");
        }
    }
    internal static void AssertInputs(string root, SourceInventoryEntry[] inventory)
    {
        foreach (SourceInventoryEntry pin in inventory)
        {
            string path = InputPath(root, pin.Path);
            Require(File.Exists(path) && Hash(path) == pin.Sha256, "Input checksum mismatch: " + pin.Path);
        }
    }
    internal static string InputPath(string root, string relative)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string path = Path.GetFullPath(Path.Combine(root, Relative(relative)));
        Require(path.StartsWith(root + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal), "Input path outside root.");
        // A lexical child can still escape through a linked directory or file.
        string current = root;
        foreach (string part in Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            if (File.Exists(current) || Directory.Exists(current))
            { Require((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0, "Linked replay input is outside the owned file boundary."); }
        }
        return path;
    }
    private static string Relative(string? path)
    {
        Require(!string.IsNullOrWhiteSpace(path), "Empty input path.");
        string value = path!.Replace('\\', '/');
        Require(!Path.IsPathRooted(value) && !value.Contains(':') && value.Split('/').All(part => part.Length > 0 && part is not "." and not ".."), "Input path outside root.");
        return value;
    }
    internal static string[] VerificationArguments(string work, string profile, string lane) =>
        ["-NoProfile", "-File", Path.Combine(work, "eng/verify.ps1"), "-Profile", profile, "-Lane", lane];
    internal static IReadOnlyDictionary<string, string?> EnvironmentFor(string output) => new Dictionary<string, string?>
    {
        ["DOTNET_CLI_HOME"] = Path.Combine(output, "dotnet-home"),
        ["NUGET_PACKAGES"] = Path.Combine(output, "packages"),
        ["NUGET_HTTP_CACHE_PATH"] = Path.Combine(output, "http-cache"),
        ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
        ["DOTNET_NOLOGO"] = "1",
        ["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false",
        ["UseSharedCompilation"] = "false",
        ["MSBUILDDISABLENODEREUSE"] = "1",
        ["GIT_CEILING_DIRECTORIES"] = output
    };
    private static ReadOnlySpan<byte> WithoutBom(byte[] bytes) => bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? bytes.AsSpan(Encoding.UTF8.Preamble.Length) : bytes;
}
