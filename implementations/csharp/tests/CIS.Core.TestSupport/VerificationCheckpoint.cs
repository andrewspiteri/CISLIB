namespace CIS.TestSupport;

/// <summary>Locates the checkpoint selected for this test host while keeping isolated fixture repositories independent.</summary>
public static class VerificationCheckpoint
{
    /// <summary>Uses runner-owned selection for the matching repository, otherwise the normal Developer checkpoint.</summary>
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
        return Path.Combine(fullRepository, ".artifacts/csharp/verification/Developer");
    }
}
