using System.Security.Cryptography;
using System.Text.RegularExpressions;
using static CIS.Verification.NativeDiagnosticExtentReader;

namespace CIS.Verification;

// Explicit physical evidence, never a guessed fallback for a malformed log.
// The caller externally pins this complete inventory to the closed campaign.
internal sealed class RetainedDiagnosticInputs
{
    private readonly string root;
    private readonly Extent[] physical;
    private readonly Dictionary<string, Extent[]> parents;
    private readonly string[] logical;
    private static readonly StringComparer Paths = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly string[] OwnershipMarkers = ["DesignModeClient.ProcessRequests: Processing Message:", "TestHostManagerCallbacks.ExitCallBack:", "DotnetTestHostManager: Starting process", "Executor.Execute: Exiting with exit code of "];
    private static readonly Regex PositiveProcessPrefix = new(@"^TpTrace \w+: 0 : [1-9]\d*,", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    internal RetainedDiagnosticInputs(string physicalLogRoot, IReadOnlyList<Extent> physicalInventory,
        IReadOnlyDictionary<string, IReadOnlyList<Extent>> originalParentExtents)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(physicalLogRoot));
        Need(Path.IsPathFullyQualified(physicalLogRoot) && Directory.Exists(root), "Existing absolute original physical log root required.");
        NoLinks(root);
        physical = physicalInventory.ToArray();
        Need(physical.Length > 0 && physical.Select(value => Path.GetFullPath(value.Path)).Distinct(Paths).Count() == physical.Length, "Complete unique physical diagnostic inventory required.");
        parents = new(StringComparer.Ordinal);
        var byPath = physical.ToDictionary(value => value.Path, StringComparer.Ordinal);
        foreach (Extent pin in physical)
        {
            Need(Path.IsPathFullyQualified(pin.Path) && Paths.Equals(Path.GetFullPath(pin.Path), pin.Path)
                && pin.Path.StartsWith(root + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal), "Every exact physical pin must stay inside the original root.");
        }

        var hiddenBackups = new HashSet<string>(StringComparer.Ordinal);
        var consumed = new HashSet<string>(Paths);
        foreach ((string active, IReadOnlyList<Extent> supplied) in originalParentExtents)
        {
            Extent[] chain = supplied.ToArray();
            Need(byPath.ContainsKey(active) && active.EndsWith("-log.txt", StringComparison.Ordinal)
                && chain.Length is 1 or 2 && chain[^1].Path == active, "An explicit parent chain must end at its original active diagnostic path.");
            if (chain.Length == 2)
            {
                string backup = Path.Combine(Path.GetDirectoryName(active)!, Path.GetFileNameWithoutExtension(active) + ".bak" + Path.GetExtension(active));
                Need(Paths.Equals(chain[0].Path, backup) && hiddenBackups.Add(chain[0].Path), "Only the exact retained sibling backup followed by active bytes is supported.");
            }
            foreach (Extent extent in chain)
            {
                Need(byPath.TryGetValue(extent.Path, out Extent? original) && original == extent && extent.Bytes > 0 && consumed.Add(extent.Path), "Each ordered extent must equal one unique complete physical inventory row.");
            }

            Need(parents.TryAdd(active, chain), "Original parent path is repeated.");
        }
        Need(parents.Count > 0, "The explicit retained-extent route requires at least one declared parent.");
        logical = physical.Select(value => value.Path).Where(path => !hiddenBackups.Contains(path)).Order(StringComparer.Ordinal).ToArray();
        string[] expanded = logical.SelectMany(path => parents.TryGetValue(path, out Extent[]? chain) ? chain.Select(value => value.Path) : [path]).Order(StringComparer.Ordinal).ToArray();
        Need(expanded.SequenceEqual(physical.Select(value => value.Path).Order(StringComparer.Ordinal)), "Logical paths and explicit extents must cover the entire original physical inventory exactly once.");
        Recheck();
        // Classification can stop after one line; framing of EVERY declared
        // parent is therefore validated completely before the audit starts.
        foreach (Extent[] chain in parents.Values)
        {
            foreach (Line line in NativeDiagnosticExtentReader.Read(chain))
            {
                if (OwnershipMarkers.Any(marker => line.Text.Contains(marker, StringComparison.Ordinal)))
                {
                    Need(PositiveProcessPrefix.IsMatch(line.Text), "Retained parent protocol and ownership records require their own complete positive PID prefix.");
                }
            }
        }

        Recheck();
    }

    internal string[] LogicalPaths => logical.ToArray();
    internal IEnumerable<string> ReadLines(string path)
    {
        Need(logical.Contains(path, StringComparer.Ordinal), "Undeclared logical diagnostic path.");
        return parents.TryGetValue(path, out Extent[]? chain)
            ? NativeDiagnosticExtentReader.Read(chain).Select(line => line.Text)
            : File.ReadLines(path);
    }

    internal void Recheck()
    {
        NoLinks(root);
        string[] actual = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
        Need(actual.SequenceEqual(physical.Select(value => value.Path).Order(StringComparer.Ordinal)), "The entire original physical diagnostic file set must remain unchanged.");
        foreach (Extent pin in physical)
        {
            NoLinks(pin.Path);
            Need(pin.Bytes >= 0 && pin.Sha256.Length == 64 && pin.Sha256.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f')
                && new FileInfo(pin.Path).Length == pin.Bytes, "Exact physical diagnostic pin required.");
            using var stream = File.OpenRead(pin.Path);
            Need(Convert.ToHexStringLower(SHA256.HashData(stream)) == pin.Sha256 && stream.Length == pin.Bytes, "Original physical diagnostic bytes changed.");
        }
    }

    private static void NoLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            Need((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0, "Linked physical diagnostic path unsupported.");
        }
    }
    private static void Need(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidDataException(message);
        }
    }
}
