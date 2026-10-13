using CIS.Logging.Events;
using CIS.Logging.Rendering;

namespace CIS.Logging.Runtime;

// The selected stream is borrowed: runtime closure or failure never closes/disposes it.
// Rendering precedes admission; the single awaited output worker serializes record writes.
internal sealed class ConsoleOutputWriter(Stream stream, LogOutputFormat format, HumanOutputDetail detail, int maximumBytes)
{
    internal RenderedEvent Prepare(EncodedEvent captured) => EventRenderer.Render(captured, format, detail, maximumBytes);
    internal ValueTask WriteAsync(AdmittedEvent admitted)
    {
        ReadOnlyMemory<byte> line = admitted.PreparedLine ?? throw new InvalidOperationException("Console records require prepared output bytes.");
        return stream.WriteAsync(line);
    }
}
