using CIS.Logging.Events;
using CIS.Logging.Rendering;

namespace CIS.Logging.Runtime;

// The selected stream is borrowed: runtime closure or failure never closes/disposes it.
// Rendering precedes admission; the single awaited output worker serializes record writes.
internal sealed class ConsoleOutputWriter(Stream stream, LogOutputFormat format, HumanOutputDetail detail, int maximumBytes)
{
    // Initialization runs before runtime publication, while the newly owned workers are idle.
    internal void Initialize()
    {
        bool writable;
        try { writable = stream.CanWrite; }
        catch { throw new CoreLogInitializationException("console", "ConsoleInitializationFailed"); }
        if (!writable) { throw new CoreLogInitializationException("console", "ConsoleStreamNotWritable"); }
    }
    internal RenderedEvent Prepare(EncodedEvent captured) => EventRenderer.Render(captured, format, detail, maximumBytes);
    internal ValueTask FlushAsync() => FlushAsync(CancellationToken.None);
    internal ValueTask FlushAsync(CancellationToken cancellationToken) => new(stream.FlushAsync(cancellationToken));
    internal ValueTask WriteAsync(AdmittedEvent admitted) => WriteAsync(admitted, CancellationToken.None);
    internal ValueTask WriteAsync(AdmittedEvent admitted, CancellationToken cancellationToken)
    {
        ReadOnlyMemory<byte> line = admitted.PreparedLine ?? throw new InvalidOperationException("Console records require prepared output bytes.");
        return stream.WriteAsync(line, cancellationToken);
    }
}
