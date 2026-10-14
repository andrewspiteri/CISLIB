using CIS.Logging.Values;

namespace CIS.Logging.Scopes;

// An immutable merged view, independent of store/flow/frame lifetime.
internal sealed class CapturedScopeContext
{
    private static readonly CapturedScopeContext Empty = new(null,
        LogValue.Map(new Dictionary<string, LogValue>()), scopeDepth: 0);

    private CapturedScopeContext(string? operationId, LogValue attributes, int scopeDepth)
    {
        OperationId = operationId;
        Attributes = attributes;
        ScopeDepth = scopeDepth;
    }

    internal string? OperationId { get; }
    internal LogValue Attributes { get; }
    internal int ScopeDepth { get; }
    internal bool IsTruncated => Attributes.IsTruncated;

    internal CapturedScopeContext Overlay(CapturedScopeContext inner)
    {
        if (ScopeDepth + inner.ScopeDepth > OperationScopeStore.MaximumDepth) { throw new InvalidOperationException("The maximum combined scope depth is 32."); }
        return new(inner.OperationId ?? OperationId, Events.EventProjection.Merge(Attributes, inner.Attributes), ScopeDepth + inner.ScopeDepth);
    }

    internal static CapturedScopeContext FromHead(OperationScopeFrame? head)
    {
        if (head is null) { return Empty; }
        if (head.Parent is null) { return new(head.Context.OperationId, head.Context.Attributes, scopeDepth: 1); }

        // BeginScope bounds this immutable chain at 32; read it once, then merge oldest first.
        var frames = new OperationScopeFrame[head.Depth];
        int position = frames.Length;
        for (OperationScopeFrame? frame = head; frame is not null; frame = frame.Parent)
        {
            frames[--position] = frame;
        }
        var winners = new Dictionary<string, LogValue>(StringComparer.Ordinal);
        string? operationId = null;
        bool truncated = false;
        foreach (OperationScopeFrame frame in frames)
        {
            CapturedOperationContext context = frame.Context;
            operationId = context.OperationId ?? operationId;
            truncated |= context.IsTruncated;
            foreach ((string key, LogValue value) in context.Attributes.MapProperties)
            {
                // The root consumes one node; no bounded result can retain more distinct keys.
                // Still visit later scopes so a last-position duplicate replaces its earlier value.
                if (winners.ContainsKey(key) || winners.Count < LogValueProjection.MaximumNodes - 1)
                {
                    winners[key] = value;
                }
                else
                {
                    truncated = true;
                }
            }
        }
        LogValue attributes = new LogValueProjection().Map(winners, depth: 1, truncated);
        return new(operationId, attributes, head.Depth);
    }
}
