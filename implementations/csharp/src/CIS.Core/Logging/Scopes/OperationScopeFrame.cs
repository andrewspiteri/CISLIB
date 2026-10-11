namespace CIS.Logging.Scopes;

// Parent links and payloads are immutable because execution contexts may share them.
internal sealed class OperationScopeFrame
{
    internal OperationScopeFrame(CapturedOperationContext context, OperationScopeFrame? parent)
    {
        Context = context;
        Parent = parent;
        Depth = (parent?.Depth ?? 0) + 1;
    }

    internal CapturedOperationContext Context { get; }
    internal OperationScopeFrame? Parent { get; }
    internal int Depth { get; }
}
