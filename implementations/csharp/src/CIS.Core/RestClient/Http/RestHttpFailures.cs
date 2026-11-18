using System.Runtime.ExceptionServices;

namespace CIS.RestClient.Http;

// Kept intact through native content wrappers. The logical owner rethrows the
// original only after owned cleanup, before any cancellation/result mapping.
internal sealed class RestEncodingFaultException(Exception original) : Exception("Request encoding failed unexpectedly.", original)
{
    internal ExceptionDispatchInfo Original { get; } = ExceptionDispatchInfo.Capture(original);
}

// These identify failures from known native I/O/default codec boundaries only.
// Custom codec/mapper/callback exceptions are not classified by their CLR type.
internal abstract class RestHttpExpectedException(string code, string explanation, Exception? inner = null) : Exception(explanation, inner)
{
    internal string Code { get; } = code;
}
internal sealed class RestDecodingException(Exception? inner = null)
    : RestHttpExpectedException("client.invalid_response", "The response does not match the required representation.", inner);
internal sealed class RestResponseLimitException()
    : RestHttpExpectedException("client.response_too_large", "The consumed response exceeds the configured limit.");
internal sealed class RestResponseReadException(Exception inner, int? status = null)
    : RestHttpExpectedException("client.transport_error", "The response body could not be read.", inner)
{ internal int? HttpStatusCode { get; } = status; }
internal sealed class RestRequestWriteException(Exception inner)
    : RestHttpExpectedException("client.transport_error", "The request payload could not be transferred.", inner);
internal sealed class RestContentPreparationException(Exception? inner = null)
    : RestHttpExpectedException("client.preparation_failed", "The request content could not be prepared within the selected capability.", inner);
internal sealed class RestTransportException(Exception inner, RestTransportFailureKind kind)
    : RestHttpExpectedException("client.transport_error", "The HTTP exchange could not be completed.", inner)
{ internal RestTransportFailureKind Kind { get; } = kind; }
