namespace CIS.Logging.Runtime;

// Acquisition owns physical handles only. Inventory validation must precede any append.
internal sealed class FileOutputOwner : IDisposable
{
    private readonly FileStream lease;
    internal string AbsolutePath { get; }
    internal FileStream ActiveFile { get; }

    private FileOutputOwner(string absolutePath, FileStream lease, FileStream activeFile)
    {
        AbsolutePath = absolutePath;
        this.lease = lease;
        ActiveFile = activeFile;
    }

    internal static FileOutputOwner Initialize(string capturedAbsolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capturedAbsolutePath);
        if (!Path.IsPathFullyQualified(capturedAbsolutePath))
        {
            throw new ArgumentException("File output requires a captured absolute path.", nameof(capturedAbsolutePath));
        }

        FileStream? lease = null, active = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(capturedAbsolutePath)!);
            string leasePath = capturedAbsolutePath + ".cis.lock";
            try
            {
                lease = new FileStream(leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                throw new CoreLogInitializationException("file", "FileOwnershipUnavailable");
            }

            // A reserved lease is empty and persistent. Never truncate an existing collision.
            if (lease.Length != 0 || !ExcludesSecondHandle(leasePath))
            {
                throw new CoreLogInitializationException("file", "FileOwnershipUnavailable");
            }
            active = new FileStream(capturedAbsolutePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read,
                bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var owner = new FileOutputOwner(capturedAbsolutePath, lease, active);
            lease = null;
            active = null;
            return owner;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            throw new CoreLogInitializationException("file", "FileInitializationFailed");
        }
        finally
        {
            try { active?.Dispose(); }
            finally { lease?.Dispose(); }
        }
    }

    private static bool ExcludesSecondHandle(string leasePath)
    {
        try
        {
            using var second = new FileStream(leasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException) { return true; }
    }

    public void Dispose()
    {
        try { ActiveFile.Dispose(); }
        finally { lease.Dispose(); }
        // Do not unlink the lease: a new inode could bypass an existing Unix owner's lock.
    }
}
