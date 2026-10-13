namespace CIS.Logging.Runtime;

// Acquisition owns physical handles only. Inventory validation must precede any append.
internal sealed class FileOutputOwner : IDisposable
{
    private readonly FileStream lease;
    internal string AbsolutePath { get; }
    internal FileStream ActiveFile { get; private set; }
    internal FileGroupInventory? Inventory { get; private set; }
    internal bool ActiveWasCreated { get; }

    private FileOutputOwner(string absolutePath, FileStream lease, FileStream activeFile, bool activeWasCreated)
    {
        AbsolutePath = absolutePath;
        this.lease = lease;
        ActiveFile = activeFile;
        ActiveWasCreated = activeWasCreated;
    }

    internal static FileOutputOwner Initialize(string capturedAbsolutePath) => InitializeCore(capturedAbsolutePath, rejectLostActive: false);

    private static FileOutputOwner InitializeCore(string capturedAbsolutePath, bool rejectLostActive)
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
            bool activeWasCreated = false;
            try { active = OpenActive(capturedAbsolutePath, FileMode.Open); }
            catch (FileNotFoundException)
            {
                if (rejectLostActive && HasExistingInventory(capturedAbsolutePath)) { throw new CoreLogInitializationException("file", "FileInventoryIncompatible"); }
                active = OpenActive(capturedAbsolutePath, FileMode.CreateNew); activeWasCreated = true;
            }
            var owner = new FileOutputOwner(capturedAbsolutePath, lease, active, activeWasCreated);
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

    private static bool HasExistingInventory(string path)
    {
        try
        {
            using var existing = new FileStream(path + ".cis.inventory.json", FileMode.Open, FileAccess.Read, FileShare.Read);
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            throw new CoreLogInitializationException("file", "FileInventoryInitializationFailed");
        }
    }

    private static FileStream OpenActive(string path, FileMode mode)
        => new(path, mode, FileAccess.ReadWrite, FileShare.Read, bufferSize: 1, FileOptions.Asynchronous | FileOptions.SequentialScan);

    internal static FileOutputOwner Initialize(string capturedAbsolutePath, LogOutputFormat format)
    {
        if (!Enum.IsDefined(format)) { throw new ArgumentOutOfRangeException(nameof(format)); }
        FileOutputOwner owner = InitializeCore(capturedAbsolutePath, rejectLostActive: true);
        try
        {
            owner.Inventory = FileGroupInventory.OpenOrCreate(owner, format);
            return owner;
        }
        catch { owner.Dispose(); throw; }
    }

    internal void Rotate(int maximumRetainedFiles)
    {
        RequireOwnership();
        FileGroupInventory current = Inventory ?? throw new InvalidOperationException("Rotation requires a managed inventory.");
        FileGroupInventory next = current.AfterRotation(AbsolutePath, maximumRetainedFiles);
        string directory = Path.GetDirectoryName(AbsolutePath)!;
        // Remove only exact oldest inventory members, while holding the group lease,
        // before the old active file becomes an additional retained segment.
        int removed = current.Segments.Count + 1 - next.Segments.Count;
        try
        {
            for (int index = 0; index < removed; index++) { File.Delete(Path.Combine(directory, current.Segments[index].FileName)); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // No new retained segment has been created. The sequential worker faults,
            // preserves other outputs and releases this owner's handles without retry.
            throw FileOutputWriteException.RetentionDeleteFailed();
        }
        string closedPath = Path.Combine(directory, next.Segments[^2].FileName);
        // The lease remains held throughout. No incoming record is written until the new
        // identity is published. Failure after rename leaves no active path to append to;
        // startup fails closed until S07 supplies interrupted-rotation reconciliation.
        // Any rotation exception makes this writer unusable; its worker faults terminally.
        ActiveFile.Dispose();
        File.Move(AbsolutePath, closedPath, overwrite: false);
        next.PublishReplacement(AbsolutePath);
        Inventory = next;
        ActiveFile = OpenActive(AbsolutePath, FileMode.CreateNew);
    }

    internal void RequireOwnership()
    {
        if (!lease.CanWrite || !ActiveFile.CanWrite) { throw new InvalidOperationException("File ownership is not active."); }
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
