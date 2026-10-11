namespace CIS.Logging.Scopes;

// One instance belongs to one future CoreLogRuntime; there is no process-wide scope slot.
// Runtime admission/closure and merged event capture are separate later integrations.
internal sealed class OperationScopeStore
{
    internal const int MaximumDepth = 32;
    private readonly AsyncLocal<OperationScopeFrame?> current = new();

    internal OperationScopeFrame? CurrentFrame => current.Value;

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
                throw new InvalidOperationException("Dispose an operation scope in its creating logical flow.");
            }
        }
    }
}
