using CIS.Logging.Scopes;
using Microsoft.Extensions.Logging;

namespace CIS.Logging.MicrosoftLogger;
// Host scopes are borrowed; only bounded immutable Core payloads leave this callback.
internal static class ProviderScopeCapture
{
    internal static CapturedOperationContext Copy(object? state) => state switch
    {
        OperationContextData typed => CapturedOperationContext.Capture(typed),
        IEnumerable<KeyValuePair<string, object?>> fields => ProviderStateCapture.CaptureScope(fields),
        _ => throw new ArgumentException("Use OperationContextData or structured string/object fields for scopes.", nameof(state))
    };
    internal static CapturedScopeContext Capture(IExternalScopeProvider scopes, int maximumDepth)
    {
        OperationScopeFrame? head = null;
        int depth = 0;
        scopes.ForEachScope((state, _) =>
        {
            if (++depth > maximumDepth)
            {
                throw new InvalidOperationException("The maximum combined scope depth is 32.");
            }

            CapturedOperationContext context = Copy(state);
            head = new OperationScopeFrame(context, head);
        }, 0);
        return CapturedScopeContext.FromHead(head);
    }
}
