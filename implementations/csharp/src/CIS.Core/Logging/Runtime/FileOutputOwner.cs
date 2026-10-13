namespace CIS.Logging.Runtime;

// Acquisition owns physical handles only. Inventory validation must precede any append.
internal sealed class FileOutputOwner : IDisposable
{
    private readonly FileStream lease;
    internal string AbsolutePath { get; }
    internal FileStream ActiveFile { get; }
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
