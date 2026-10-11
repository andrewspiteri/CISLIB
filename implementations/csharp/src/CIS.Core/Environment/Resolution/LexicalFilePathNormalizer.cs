namespace CIS.Configuration;

internal static class LexicalFilePathNormalizer
{
    public static string NormalizeAbsolute(string path) => Normalize(path, null);
    public static string Resolve(string path, string basePath) => Normalize(path, basePath);

    private static string Normalize(string path, string? basePath)
    {
        bool fullyQualified = Path.IsPathFullyQualified(path);
        if (!OperatingSystem.IsWindows() || IsExtended(path) ||
            (!fullyQualified && basePath is not null && IsDevice(basePath)) ||
            (!path.Contains('~') && (fullyQualified || basePath is null || !basePath.Contains('~'))))
        {
            return FullPath(path, basePath);
        }

        // Windows PathHelper expands '~' through GetLongPathName filesystem queries.
        // Use a same-width, nonstructural character absent from the relevant inputs,
        // then restore the literal text after native root/dot/separator normalization.
        char marker = FindUnusedCharacter(path, fullyQualified ? null : basePath);
        string maskedPath = path.Replace('~', marker);
        string? maskedBase = fullyQualified ? basePath : basePath?.Replace('~', marker);
        return FullPath(maskedPath, maskedBase).Replace(marker, '~');
    }

    private static string FullPath(string path, string? basePath) =>
        basePath is null ? Path.GetFullPath(path) : Path.GetFullPath(path, basePath);

    private static bool IsExtended(string path) =>
        path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\??\", StringComparison.Ordinal);

    private static bool IsDevice(string path) => IsExtended(path) || path.StartsWith(@"\\.\", StringComparison.Ordinal);

    private static char FindUnusedCharacter(string path, string? basePath)
    {
        var used = new bool[char.MaxValue + 1];
        foreach (char character in path)
        {
            used[character] = true;
        }
        if (basePath is not null)
        {
            foreach (char character in basePath)
            {
                used[character] = true;
            }
        }
        for (int candidate = 0xe000; candidate <= 0xfffd; candidate++)
        {
            if (!used[candidate])
            {
                return (char)candidate;
            }
        }
        for (int candidate = 0x0100; candidate <= 0xd7ff; candidate++)
        {
            if (!used[candidate])
            {
                return (char)candidate;
            }
        }
        // Exhausting these 63,230 distinct BMP characters exceeds Windows' 32,767 limit.
        throw new PathTooLongException("The path exceeds the native Windows syntax limit.");
    }
}
