using CIS.Text;

namespace CIS.Logging.Exceptions;

// Each capture has independent budgets. Exception accessors are caller code and must cooperate.
internal sealed class ExceptionProjection
{
    internal const int MaximumNodes = 16;
    internal const int MaximumDepth = 4;
    internal const int MaximumMessageScalars = 4096;
    internal const int MaximumStackScalars = 16384;
    private int remainingNodes = MaximumNodes;

    private ExceptionProjection()
    {
    }

    internal static CapturedException? Capture(Exception? exception)
    {
        return exception is null ? null : new ExceptionProjection().CaptureNode(exception, 1);
    }

    private CapturedException CaptureNode(Exception exception, int depth)
    {
        remainingNodes--;
        Type runtimeType = exception.GetType();
        string type = runtimeType.FullName ?? runtimeType.Name;
        string message = UnicodeScalarText.Prefix(exception.Message ?? string.Empty,
            MaximumMessageScalars, out bool messageClipped);
        string? sourceStack = exception.StackTrace;
        string? stackTrace = null;
        bool stackClipped = false;
        if (sourceStack is not null)
        {
            stackTrace = UnicodeScalarText.Prefix(sourceStack, MaximumStackScalars, out stackClipped);
        }
        var children = new List<CapturedException>();
        bool childrenClipped = false;
        foreach (Exception child in Children(exception))
        {
            if (remainingNodes == 0 || depth == MaximumDepth)
            {
                childrenClipped = true;
                break;
            }
            children.Add(CaptureNode(child, depth + 1));
        }
        return new CapturedException(type, message, stackTrace, children,
            messageClipped || stackClipped || childrenClipped);
    }

    private static IEnumerable<Exception> Children(Exception exception)
    {
        if (exception is AggregateException aggregate)
        {
            return aggregate.InnerExceptions;
        }
        return exception.InnerException is { } child ? [child] : [];
    }
}
