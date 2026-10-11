using System.Collections.ObjectModel;

namespace CIS.Logging.Exceptions;

// Owned capture state for the future event pipeline; no Exception or caller collection is retained.
internal sealed class CapturedException
{
    private readonly ReadOnlyCollection<CapturedException> children;

    internal CapturedException(string type, string message, string? stackTrace,
        IEnumerable<CapturedException> innerExceptions, bool isTruncated)
    {
        Type = type;
        Message = message;
        StackTrace = stackTrace;
        children = Array.AsReadOnly(innerExceptions.ToArray());
        IsTruncated = isTruncated;
        NodeCount = 1;
        Depth = 1;
        HasTruncatedContent = isTruncated;
        foreach (CapturedException child in children)
        {
            NodeCount += child.NodeCount;
            Depth = Math.Max(Depth, child.Depth + 1);
            HasTruncatedContent |= child.HasTruncatedContent;
        }
    }

    internal string Type { get; }
    internal string Message { get; }
    internal string? StackTrace { get; }
    internal IReadOnlyList<CapturedException> InnerExceptions => children;
    internal bool IsTruncated { get; }
    internal bool HasTruncatedContent { get; }
    internal int NodeCount { get; }
    internal int Depth { get; }
}
