using CIS.Text;

namespace CIS.Logging.Scopes;

// An owned immutable payload; it contains no caller DTO or dictionary.
internal sealed class CapturedOperationContext
{
    private static readonly LogValue EmptyAttributes = LogValue.Map(new Dictionary<string, LogValue>());

    private CapturedOperationContext(string? operationId, LogValue attributes)
    {
        OperationId = operationId;
        Attributes = attributes;
    }

    internal string? OperationId { get; }
    internal LogValue Attributes { get; }
    internal bool IsTruncated => Attributes.IsTruncated;

    internal static CapturedOperationContext Capture(OperationContextData context)
    {
        ArgumentNullException.ThrowIfNull(context);
        string? operationId = context.OperationId?.Trim();
        IReadOnlyDictionary<string, LogValue>? attributes = context.Attributes;
        if (string.IsNullOrEmpty(operationId))
        {
            operationId = null;
        }
        else if (!UnicodeScalarText.IsValid(operationId, 255, allowControls: false))
        {
            throw new ArgumentException("Use an operation identifier of at most 255 valid Unicode scalars without controls.", nameof(context));
        }
        return new(operationId, attributes is null ? EmptyAttributes : LogValue.Map(attributes));
    }
}
