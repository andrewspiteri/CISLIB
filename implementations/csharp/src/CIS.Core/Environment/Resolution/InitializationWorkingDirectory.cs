namespace CIS.Configuration;

internal sealed class InitializationWorkingDirectory
{
    private InitializationWorkingDirectory(string value) => Value = value;
    public string Value { get; }

    public static InitializationWorkingDirectory Capture(Func<string>? readCurrentDirectory = null)
    {
        string value = (readCurrentDirectory ?? Directory.GetCurrentDirectory)();
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
        {
            throw new InvalidOperationException("The initialization working directory must be fully qualified.");
        }
        // A fully qualified capture needs no later ambient-directory lookup.
        return new InitializationWorkingDirectory(LexicalFilePathNormalizer.NormalizeAbsolute(value));
    }
}
