namespace CIS.Audit.Hosting;

// Passive metadata retains only the exact owners constructed by this registration, including failed Build.
internal sealed class AuditHostingRegistration(AuditOptions options)
{
    private readonly object gate = new();
    private AuditCaptureEngine? capture;
    private AuditWriter? writer;
    private AuditRuntime? runtime;
    private bool closing;
    private bool settled;

    internal AuditOptions Options { get; } = options;

    internal bool IsDrained
    {
        get
        {
            lock (gate)
            {
                return runtime?.IsDrained ?? settled;
            }
        }
    }

    internal void BeginStopping() => _ = StopAsync();

    internal void Bind(AuditCaptureEngine value)
    {
        bool close;
        lock (gate)
        {
            if (capture is not null && !ReferenceEquals(capture, value))
            {
                throw new InvalidOperationException("One Audit capture owner belongs to this registration.");
            }
            capture = value;
            close = closing;
        }
        if (close)
        {
            value.BeginClose();
        }
    }

    internal void Bind(AuditWriter value)
    {
        bool close;
        lock (gate)
        {
            if (writer is not null && !ReferenceEquals(writer, value))
            {
                throw new InvalidOperationException("One Audit writer belongs to this registration.");
            }
            writer = value;
            close = closing;
        }
        if (close)
        {
            value.BeginClose();
        }
    }

    internal void Bind(AuditRuntime value)
    {
        bool close;
        lock (gate)
        {
            if (runtime is not null && !ReferenceEquals(runtime, value))
            {
                throw new InvalidOperationException("One Audit runtime belongs to this registration.");
            }
            runtime = value;
            close = closing;
        }
        if (close)
        {
            value.BeginStopping();
        }
    }

    internal async Task StopAsync()
    {
        AuditRuntime? selected;
        AuditCaptureEngine? captured;
        AuditWriter? published;
        lock (gate)
        {
            closing = true;
            selected = runtime;
            captured = capture;
            published = writer;
        }
        try
        {
            if (selected is not null)
            {
                await selected.StopAsync().ConfigureAwait(false);
                return;
            }

            // A provider can fail before constructing the runtime. Join the exact owners it did create.
            captured?.BeginClose();
            published?.BeginClose();
            await Task.WhenAll(captured?.DrainAsync() ?? Task.CompletedTask,
                published?.DrainAsync() ?? Task.CompletedTask).ConfigureAwait(false);
        }
        finally
        {
            lock (gate)
            {
                settled = true;
            }
        }
    }
}
