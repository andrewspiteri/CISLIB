using CIS.Text;

namespace CIS.Logging.Scopes;

// An owned immutable payload; it contains no caller DTO or dictionary.
internal sealed class CapturedOperationContext
{
    internal static readonly CapturedOperationContext Omitted = new(null, LogValue.OwnedMap(new Dictionary<string, LogValue>(), truncated: true));
    private static readonly LogValue EmptyAttributes = LogValue.Map(new Dictionary<string, LogValue>());

    private CapturedOperationContext(string? operationId, LogValue attributes)
    {
        OperationId = operationId;
        Attributes = attributes;
    }

    internal string? OperationId { get; }
    internal LogValue Attributes { get; }
    internal bool IsTruncated => Attributes.IsTruncated;

    internal static CapturedOperationContext FromAttributes(LogValue attributes, string? operationId = null) => new(operationId, attributes);

    internal static CapturedOperationContext Capture(OperationContextData context)
    {
        ArgumentNullException.ThrowIfNull(context);
        string? operationId = NormalizeOperationId(context.OperationId, nameof(context));
        IReadOnlyDictionary<string, LogValue>? attributes = context.Attributes;
        return new(operationId, attributes is null ? EmptyAttributes : LogValue.Map(attributes));
    }
    internal static string? NormalizeOperationId(string? value, string parameterName)
    {
        string? operationId = value?.Trim();
        if (string.IsNullOrEmpty(operationId))
        {
            operationId = null;
        }
        else if (!UnicodeScalarText.IsValid(operationId, 255, allowControls: false))
        {
            throw new ArgumentException("Use an operation identifier of at most 255 valid Unicode scalars without controls.", parameterName);
        }
        return operationId;
    }

}
