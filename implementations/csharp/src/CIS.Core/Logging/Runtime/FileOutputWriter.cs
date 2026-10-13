using CIS.Logging.Events;
using CIS.Logging.Rendering;

namespace CIS.Logging.Runtime;

// Only the sequential file worker writes. Its terminal path releases this owned resource.
internal sealed class FileOutputWriter(string absolutePath, LogOutputFormat format, HumanOutputDetail detail, int maximumEntryBytes, long maximumFileBytes) : IDisposable
{
    private FileOutputOwner? owner;
    internal void Initialize()
    {
        FileOutputOwner acquired = FileOutputOwner.Initialize(absolutePath, format);
        try
        {
            long length = acquired.ActiveFile.Length;
            if (length > maximumFileBytes) { throw new CoreLogInitializationException("file", "FileSizeLimitExceeded"); }
            if (length != 0)
            {
                acquired.ActiveFile.Position = length - 1;
                if (acquired.ActiveFile.ReadByte() != (byte)'\n') { throw new CoreLogInitializationException("file", "FileRecordBoundaryInvalid"); }
            }
            acquired.ActiveFile.Position = length;
            owner = acquired;
        }
        catch (CoreLogInitializationException) { acquired.Dispose(); throw; }
        catch { acquired.Dispose(); throw new CoreLogInitializationException("file", "FileInitializationFailed"); }
    }
    internal RenderedEvent Prepare(EncodedEvent captured) => EventRenderer.Render(captured, format, detail, maximumEntryBytes);
    internal async ValueTask WriteAsync(AdmittedEvent admitted)
    {
        FileOutputOwner active = owner ?? throw new InvalidOperationException("File output is not initialized.");
        ReadOnlyMemory<byte> line = admitted.PreparedLine ?? throw new InvalidOperationException("File records require prepared output bytes.");
        // S06 replaces this terminal growth guard with complete-record rotation/retention.
        if (line.Length > maximumFileBytes - active.ActiveFile.Length) { throw new IOException("File segment rotation is required."); }
        await active.ActiveFile.WriteAsync(line).ConfigureAwait(false);
    }
    public void Dispose() => Interlocked.Exchange(ref owner, null)?.Dispose();
}
