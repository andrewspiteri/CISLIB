namespace CIS.TestSupport;

/// <summary>Locates the checkpoint selected for this test host while keeping isolated fixture repositories independent.</summary>
public static class VerificationCheckpoint
{
    /// <summary>Resolves the explicitly prepared package for this repository's qualification candidate.</summary>
    public static string Package(string repository)
    {
        string preparedRoot = Root(repository);
        string? selected = System.Environment.GetEnvironmentVariable("CIS_VERIFICATION_PACKAGE");
        if (string.IsNullOrWhiteSpace(selected) || !File.Exists(selected))
        {
            throw new InvalidOperationException(
                "Qualification requires CIS_VERIFICATION_PACKAGE naming the current prepared package. Run eng/verify.ps1.");
        }
        using var summary = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(Path.Combine(preparedRoot, "summary.json")));
        if (!summary.RootElement.TryGetProperty("candidatePackage", out var package) ||
            !package.TryGetProperty("sha256", out var digest) ||
            !string.Equals(digest.GetString(), Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(selected))), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The selected package differs from the current prepared candidate's package pin.");
        }
        return Path.GetFullPath(selected);
    }

    /// <summary>Requires explicit runner-owned prepared inputs matching the selected repository.</summary>
    public static string Root(string repository) => Resolve(repository,
        System.Environment.GetEnvironmentVariable("CIS_VERIFICATION_ROOT"),
        System.Environment.GetEnvironmentVariable("CIS_VERIFICATION_REPOSITORY"));

    /// <summary>Resolves an explicit repository/root pair without changing process environment.</summary>
    public static string Resolve(string repository, string? selectedRoot, string? selectedRepository)
    {
        string fullRepository = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repository));
        if (!string.IsNullOrWhiteSpace(selectedRoot) && !string.IsNullOrWhiteSpace(selectedRepository) &&
            fullRepository.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(selectedRepository)),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        { return Path.GetFullPath(selectedRoot); }
        throw new InvalidOperationException(
            "Qualification requires explicit matching prepared inputs. Run eng/verify.ps1, or set " +
            "CIS_VERIFICATION_ROOT and CIS_VERIFICATION_REPOSITORY to the current prepared candidate. " +
            "A previous Developer checkpoint is never selected automatically.");
    }
}
