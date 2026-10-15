using CIS.Logging.Events;
using CIS.Logging.Rendering;

namespace CIS.Logging.Runtime;

// Startup adapters for built-in and application writers use the same worker and lifecycle path.
// Preparation is trusted Core rendering; application writer callbacks execute only on its worker.
internal sealed record OutputWriterBinding(
    string Name,
    CoreLogLevel Level,
    Func<AdmittedEvent, CancellationToken, ValueTask> Write,
    Func<CancellationToken, ValueTask>? Flush = null,
    Func<EncodedEvent, RenderedEvent>? Prepare = null,
    Action? Initialize = null,
    Func<ValueTask>? Cleanup = null,
    TimeSpan? FlushInterval = null);
