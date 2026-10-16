using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CIS.TestSupport;

namespace CIS.Verification;

internal sealed record SourceInventoryEntry(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("sha256")] string Sha256);

internal static class SourceInventory
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    { ".git", ".artifacts", "bin", "obj", ".vs" };

    internal static async Task<int> RunAsync(string[] args)
    {
        if (args.Length != 1) { Console.Error.WriteLine("Usage: source-inventory <root>"); return 2; }
        try
        {
            Console.WriteLine(JsonSerializer.Serialize(await CaptureAsync(args[0])));
            return 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException or System.ComponentModel.Win32Exception or TimeoutException)
        {
            Console.Error.WriteLine("Source inventory failed: " + error.Message);
            return 1;
        }
    }

    internal static async Task<SourceInventoryEntry[]> CaptureAsync(string root)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!Directory.Exists(root)) { throw new DirectoryNotFoundException("Source root does not exist: " + root); }
        ProcessResult repository = await GitAsync(root, "rev-parse", "--show-prefix");
        IEnumerable<string> files;
        if (repository.ExitCode == 0 && repository.StandardOutput.TrimEnd('\r', '\n').Length == 0)
        {
            string[] inputs = await GitPathsAsync(root, "--cached", "--others", "--exclude-standard");
            var deleted = new HashSet<string>(await GitPathsAsync(root, "--deleted"), StringComparer.Ordinal);
            files = inputs.Where(path => !deleted.Contains(path));
        }
        else
        {
            if (repository.ExitCode != 0 && (Directory.Exists(Path.Combine(root, ".git")) || File.Exists(Path.Combine(root, ".git"))))
            {
                throw new InvalidOperationException("Unable to inspect Git source root: " + repository.StandardError);
            }
            files = EnumerateCopy(root).Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'));
        }

        var result = new List<SourceInventoryEntry>();
        foreach (string path in files.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            using FileStream stream = File.OpenRead(Path.Combine(root, path));
            result.Add(new(path, Convert.ToHexStringLower(SHA256.HashData(stream))));
        }
        return result.ToArray();
    }

    private static IEnumerable<string> EnumerateCopy(string directory)
    {
        foreach (string path in Directory.EnumerateFiles(directory)) { yield return path; }
        foreach (string child in Directory.EnumerateDirectories(directory))
        {
            if (ExcludedDirectories.Contains(Path.GetFileName(child)) || (File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) { continue; }
            foreach (string path in EnumerateCopy(child)) { yield return path; }
        }
    }

    private static async Task<string[]> GitPathsAsync(string root, params string[] arguments)
    {
        ProcessResult result = await GitAsync(root, ["ls-files", "-z", .. arguments]);
        if (result.ExitCode != 0) { throw new InvalidOperationException("Unable to enumerate Git source inputs: " + result.StandardError); }
        return result.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static Task<ProcessResult> GitAsync(string root, params string[] arguments) =>
        ProcessRunner.RunAsync("git", ["-C", root, .. arguments], root, TimeSpan.FromSeconds(30), outputEncoding: new UTF8Encoding(false, true));
}
