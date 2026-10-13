using CIS.Logging.Events;
using CIS.Logging.Rendering;

namespace CIS.Logging.Runtime;

// Only the sequential file worker writes. Its terminal path releases this owned resource.
internal sealed class FileOutputWriter(string absolutePath, LogOutputFormat format, HumanOutputDetail detail, int maximumEntryBytes, long maximumFileBytes, int maximumRetainedFiles) : IDisposable
{
    private FileOutputOwner? owner;
    private long activeBytes;
    internal void Initialize()
    {
        if (maximumRetainedFiles is < LoggingOptions.MinimumFileRetainedFiles or > LoggingOptions.MaximumFileRetainedFiles)
        { throw new ArgumentOutOfRangeException(nameof(maximumRetainedFiles)); }
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
            // Failed startup preserves every pre-existing retained segment. A lower
            // selected bound needs explicit archival or a new group before retry.
            if (acquired.Inventory!.Segments.Count - 1 > maximumRetainedFiles)
            { throw new CoreLogInitializationException("file", "FileRetentionLimitExceeded"); }
            acquired.ActiveFile.Position = length;
            activeBytes = length;
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
        // The prepared record includes every encoded byte and its LF. Never split it.
        if (line.Length > maximumFileBytes)
        {
            if (format == LogOutputFormat.Human) { throw FileOutputWriteException.RenderTooLarge(); }
            throw new IOException("File record exceeds the segment byte limit.");
        }
        if (line.Length > maximumFileBytes - activeBytes)
        {
            active.Rotate(maximumRetainedFiles);
            activeBytes = 0;
        }
        await active.ActiveFile.WriteAsync(line).ConfigureAwait(false);
        activeBytes += line.Length;
    }
    public void Dispose() => Interlocked.Exchange(ref owner, null)?.Dispose();
}
