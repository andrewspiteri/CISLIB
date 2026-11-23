using CIS.Security;

namespace CIS.Audit;

// Scoped ordinary capture reads the actual owning security projection at call time.
internal sealed class AuditCapture(AuditCaptureEngine engine, ICallerContextAccessor? accessor) : IAuditCapture
{
    public CapturedAuditRecord Capture<T>(AuditEventDefinition<T> definition, T details,
        AuditCaptureOptions? options = null, CancellationToken cancellationToken = default)
        => engine.Capture(definition, details, options, accessor?.Current, cancellationToken: cancellationToken);

    public CapturedAuditRecord CaptureLocalSystem<T>(AuditEventDefinition<T> definition, T details,
        AuditCaptureOptions? options = null, CancellationToken cancellationToken = default)
        => engine.Capture(definition, details, options, null, localSystem: true, cancellationToken: cancellationToken);
}
