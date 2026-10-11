namespace CIS.Logging.Scopes;

// One instance belongs to one future CoreLogRuntime; there is no process-wide scope slot.
// Runtime admission/closure and event capture are separate later integrations.
internal sealed class OperationScopeStore
{
    internal const int MaximumDepth = 32;
    private readonly AsyncLocal<OperationScopeFrame?> current = new();

    internal OperationScopeFrame? CurrentFrame => current.Value;

    internal CapturedScopeContext Capture() => CapturedScopeContext.FromHead(current.Value);

    internal IDisposable BeginScope(OperationContextData context)
    {
        ArgumentNullException.ThrowIfNull(context);
        OperationScopeFrame? parent = current.Value;
        if (parent?.Depth >= MaximumDepth)
        {
            throw new InvalidOperationException("The maximum operation scope depth is 32.");
        }
        CapturedOperationContext captured = CapturedOperationContext.Capture(context);
        // A cooperative map enumerator may re-enter scope creation. Never overwrite its newer head.
        if (!ReferenceEquals(current.Value, parent))
        {
            throw new InvalidOperationException("Scope state changed during input capture.");
        }
        var frame = new OperationScopeFrame(captured, parent);
        var handle = new Handle(this, frame);
        current.Value = frame;
        return handle;
    }

    private sealed class Handle(OperationScopeStore owner, OperationScopeFrame frame) : IDisposable
    {
        // Idempotence state only: an inherited frame or this flag cannot identify a flow owner.
        private int disposed;

        public void Dispose()
        {
            OperationScopeFrame? active = owner.current.Value;
            if (ReferenceEquals(active, frame))
            {
                owner.current.Value = frame.Parent;
                Volatile.Write(ref disposed, 1);
                return;
            }
            for (OperationScopeFrame? ancestor = active; ancestor is not null; ancestor = ancestor.Parent)
            {
                if (ReferenceEquals(ancestor, frame))
                {
                    throw new InvalidOperationException("Dispose operation scopes in LIFO order.");
                }
            }
            if (Volatile.Read(ref disposed) == 0)
            {
                throw new InvalidOperationException("The operation scope is not active in the current asynchronous context.");
            }
        }
    }
}
