using CIS.Logging.Events;
using CIS.Logging.Rendering;

namespace CIS.Logging.Runtime;

// Frozen presentation and a selected stream. Rendering precedes admission; only the worker performs I/O.
internal sealed class ConsoleOutputWriter(Stream stream, LogOutputFormat format, HumanOutputDetail detail, int maximumBytes)
{
    internal RenderedEvent Prepare(EncodedEvent captured) => EventRenderer.Render(captured, format, detail, maximumBytes);
    internal ValueTask WriteAsync(AdmittedEvent admitted)
    {
        ReadOnlyMemory<byte> line = admitted.PreparedLine ?? throw new InvalidOperationException("Console records require prepared output bytes.");
        return stream.WriteAsync(line);
    }
}
