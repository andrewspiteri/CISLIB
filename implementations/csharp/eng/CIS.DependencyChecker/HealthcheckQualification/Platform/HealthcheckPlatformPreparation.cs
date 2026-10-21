using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CIS.Verification;

// Copies selected real inputs. It does not restore, build, run tests, materialize
// toolchains, mutate source, or claim an exact-package/release source replay.
internal static class HealthcheckPlatformPreparation
{
    private sealed record InputFile(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("sha256")] string Sha256,
        [property: JsonPropertyName("bytes")] long Bytes);

    private sealed record Omission(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("reason")] string Reason);

    private static readonly string[] RequiredTrees =
    [
        "implementations/csharp", "contracts/healthcheck", "conformance/healthcheck",
        "contracts/logging", "conformance/logging",
        "compatibility/csharp/healthcheck", "planning/healthcheck"
    ];
    private static readonly string[] RequiredFiles =
    [
        "README.md", "AGENTS.md", ".editorconfig", ".gitattributes", ".gitignore",
        "eng/test.ps1", "compatibility/csharp/publications/ledger.json"
    ];
    private static readonly string[] OptionalTrees = [".github/workflows"];
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".cis", ".artifacts", "bin", "obj", ".vs", "TestResults", "StrykerOutput", ".stryker-tmp"
    };
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static async Task<int> RunAsync(string[] args)
    {
        Dictionary<string, string> options = Parse(args);
        string repo = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options["repo-root"]));
        string output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options["output-root"]));
        Require(Directory.Exists(repo), "The repository root does not exist.");
        Require(File.Exists(Path.Combine(repo, "implementations/csharp/CisLib.slnx")), "Use the CisLib repository root.");
        Require(!Within(output, repo), "The output must not contain the repository source root.");
        foreach (string tree in RequiredTrees.Concat(OptionalTrees))
        { Require(!Within(Path.Combine(repo, tree), output), "Keep output outside selected source trees: " + tree); }
        EnsureNoLinks(repo);
        EnsureNoLinks(output);
        Require(!File.Exists(output), "Use a directory output path.");
        Require(!Directory.Exists(output) || !Directory.EnumerateFileSystemEntries(output).Any(), "Use a new or empty output directory; failed attempts are retained.");

        Directory.CreateDirectory(output);
        string snapshot = Path.Combine(output, "source");
        string manifestPath = Path.Combine(output, "source-inputs.json");
        string reportPath = Path.Combine(output, "preparation.json");
        var omissions = new List<Omission>();
        var report = new Dictionary<string, object?>
        {
            ["schema"] = "cis.healthcheck.platform-preparation/v1-development",
            ["prepared"] = false,
            ["qualified"] = false,
            ["scope"] = "Selected Healthcheck native platform source snapshot; not the whole repository, package or release snapshot",
            ["sourceRoot"] = repo,
            ["outputRoot"] = output,
            ["snapshotRoot"] = snapshot,
            ["preparedOn"] = RuntimeInformation.OSDescription,
            ["preparerAssemblySha256"] = await HashAsync(typeof(HealthcheckPlatformPreparation).Assembly.Location),
            ["selection"] = new { requiredTrees = RequiredTrees, requiredFiles = RequiredFiles, optionalTrees = OptionalTrees },
            ["excludedDirectoryNames"] = ExcludedDirectories.Order(StringComparer.Ordinal).ToArray(),
            ["outsideSelection"] = new[]
            {
                ".git", ".cis/local", ".artifacts and all pre-existing build/test output",
                "other language implementations", "unrelated planning/review/evidence snapshots",
                "nonselected contracts/conformance/compatibility history except the actual publication ledger",
                "docs outside the root README; external tools, caches and SDK/runtime installations"
            },
            ["nativeExecution"] = "Pending; preparation does not run or qualify any native test family"
        };
        await WriteJsonAsync(reportPath, report, createNew: true);
        try
        {
            InputFile[] before = await CaptureAsync(repo, omissions);
            Require(before.Length > 0, "The selected snapshot is empty.");
            string selectedDigest = Digest(before);
            await WriteJsonAsync(manifestPath, new
            {
                schema = "cis.healthcheck.selected-source-inputs/v1-development",
                scope = "Exact selected input bytes, sorted by ordinal repository-relative path; no generated outputs",
                digestAlgorithm = "SHA-256 over UTF-8 path TAB lowercase-sha256 LF records, sorted ordinally, including final LF",
                inputsSha256 = selectedDigest,
                files = before
            }, createNew: true);
            string manifestPin = await HashAsync(manifestPath);
            report["sourceInputsManifestSha256"] = manifestPin;
            report["sourceInputsSha256"] = selectedDigest;
            report["sourceVerifiedBeforeCopy"] = true;
            report["inputCount"] = before.Length;
            report["inputBytes"] = before.Sum(item => item.Bytes);
            report["omissions"] = omissions.OrderBy(item => item.Path, StringComparer.Ordinal).ThenBy(item => item.Reason, StringComparer.Ordinal).ToArray();
            await WriteJsonAsync(reportPath, report);

            Directory.CreateDirectory(snapshot);
            foreach (InputFile file in before)
            {
                string source = Contained(repo, file.Path), destination = Contained(snapshot, file.Path);
                EnsureNoLinks(source);
                EnsureNoLinks(destination);
                Require(await HashAsync(source) == file.Sha256, "Source changed before copying: " + file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 81_920, useAsync: true))
                await using (var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81_920, useAsync: true))
                { await input.CopyToAsync(target); }
                Require(await HashAsync(source) == file.Sha256 && await HashAsync(destination) == file.Sha256,
                    "Input or destination changed while copying: " + file.Path);
            }

            InputFile[] after = await CaptureAsync(repo, []);
            Require(before.SequenceEqual(after), "The selected source file set or bytes changed during preparation.");
            InputFile[] copied = await CaptureDestinationAsync(snapshot);
            Require(before.SequenceEqual(copied), "The copied source file set or bytes differ from the immutable source manifest.");
            Require(await HashAsync(manifestPath) == manifestPin, "The immutable source-input manifest changed during preparation.");
            report["sourceVerifiedAfterCopy"] = true;
            report["destinationVerifiedAgainstSourceManifest"] = true;
            report["destinationInputsSha256"] = Digest(copied);
            report["prepared"] = true;
            await WriteJsonAsync(reportPath, report);
            Console.WriteLine(JsonSerializer.Serialize(new { prepared = true, source = snapshot, files = before.Length, sourceInputsSha256 = selectedDigest, sourceInputsManifestSha256 = manifestPin }));
            return 0;
        }
        catch (Exception error)
        {
            report["prepared"] = false;
            report["failure"] = error.GetType().FullName + ": " + error.Message;
            await WriteJsonAsync(reportPath, report);
            throw;
        }
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            RequireArgument(i + 1 < args.Length && (args[i] is "--repo-root" or "--output-root"), "Use --repo-root <path> --output-root <new-or-empty-path>.");
            RequireArgument(!string.IsNullOrWhiteSpace(args[i + 1]) && options.TryAdd(args[i][2..], args[i + 1]), "Use each named option once with a nonblank value.");
        }
        RequireArgument(options.Count == 2, "Both --repo-root and --output-root are required.");
        return options;
    }

    private static async Task<InputFile[]> CaptureAsync(string repo, List<Omission> omissions)
    {
        var paths = new List<string>();
        foreach (string file in RequiredFiles)
        {
            string full = Contained(repo, file);
            Require(File.Exists(full), "Missing selected source input: " + file);
            AddOrOmit(repo, full, paths, omissions);
        }
        foreach (string tree in RequiredTrees)
        {
            string full = Contained(repo, tree);
            Require(Directory.Exists(full), "Missing selected source tree: " + tree);
            EnumerateTree(repo, full, paths, omissions);
        }
        foreach (string tree in OptionalTrees)
        {
            string full = Contained(repo, tree);
            if (Directory.Exists(full)) { EnumerateTree(repo, full, paths, omissions); }
            else { omissions.Add(new(tree, "Optional tree is absent")); }
        }
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<InputFile>();
        foreach (string path in paths.Order(StringComparer.Ordinal))
        {
            Require(names.Add(path), "Duplicate or case-colliding snapshot path: " + path);
            string full = Contained(repo, path);
            EnsureNoLinks(full);
            result.Add(new(path, await HashAsync(full), new FileInfo(full).Length));
        }
        return result.ToArray();
    }

    private static void EnumerateTree(string repo, string directory, List<string> files, List<Omission> omissions)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        { omissions.Add(new(Relative(repo, directory), "Linked/reparse entry is never followed")); return; }
        foreach (string child in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
        { AddOrOmit(repo, child, files, omissions); }
    }

    private static void AddOrOmit(string repo, string path, List<string> files, List<Omission> omissions)
    {
        FileAttributes attributes = File.GetAttributes(path);
        string relative = Relative(repo, path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        { omissions.Add(new(relative, "Linked/reparse entry is never followed")); return; }
        if (Path.GetFileName(path).Equals(".git", StringComparison.OrdinalIgnoreCase))
        { omissions.Add(new(relative, "Git metadata file/directory is excluded")); return; }
        if ((attributes & FileAttributes.Directory) != 0)
        {
            if (ExcludedDirectories.Contains(Path.GetFileName(path)))
            { omissions.Add(new(relative, "Generated, repository metadata or output directory excluded by name")); return; }
            EnumerateTree(repo, path, files, omissions);
        }
        else { files.Add(relative); }
    }

    private static async Task<InputFile[]> CaptureDestinationAsync(string root)
    {
        var paths = new List<string>();
        void Enumerate(string directory)
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                Require((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0, "Unexpected linked destination entry: " + path);
                if (Directory.Exists(path)) { Enumerate(path); }
                else { paths.Add(Relative(root, path)); }
            }
        }
        Enumerate(root);
        var files = new List<InputFile>();
        foreach (string path in paths.Order(StringComparer.Ordinal))
        {
            string full = Contained(root, path);
            files.Add(new(path, await HashAsync(full), new FileInfo(full).Length));
        }
        return files.ToArray();
    }

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    private static bool Within(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
    private static string Contained(string root, string relative)
    {
        string full = Path.GetFullPath(Path.Combine(root, relative));
        Require(Within(root, full), "A source/destination path escapes its root: " + relative);
        return full;
    }
    private static void EnsureNoLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if (File.Exists(current) || Directory.Exists(current))
            { Require((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0, "Linked source/output path is not allowed: " + current); }
        }
    }
    private static async Task<string> HashAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81_920, useAsync: true);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
    }
    private static string Digest(IEnumerable<InputFile> files) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Concat(files.Select(file => file.Path + "\t" + file.Sha256 + "\n")))));
    private static async Task WriteJsonAsync(string path, object value, bool createNew = false)
    {
        string json = JsonSerializer.Serialize(value, JsonOptions).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
        await using var stream = new FileStream(path, createNew ? FileMode.CreateNew : FileMode.Create, FileAccess.Write, FileShare.None, 81_920, useAsync: true);
        await stream.WriteAsync(Encoding.UTF8.GetBytes(json));
    }
    private static void Require(bool condition, string reason)
    { if (!condition) { throw new InvalidDataException(reason); } }
    private static void RequireArgument(bool condition, string reason)
    { if (!condition) { throw new ArgumentException(reason); } }
}
