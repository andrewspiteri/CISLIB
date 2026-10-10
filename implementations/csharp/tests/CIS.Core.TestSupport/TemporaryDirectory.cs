namespace CIS.TestSupport;

/// <summary>Owns one uniquely created test directory and its cleanup.</summary>
public sealed class TemporaryDirectory : IDisposable
{
    private readonly string ownerRoot;
    private bool disposed;

    public TemporaryDirectory()
    {
        ownerRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cislib-tests"));
        Path = System.IO.Path.Combine(ownerRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        // Cleanup is restricted to the exact child created by this instance.
        if (!string.Equals(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path)), ownerRoot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Test cleanup path is outside its owned directory.");
        }

        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }

        disposed = true;
    }
}
