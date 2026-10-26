namespace CIS.Healthcheck.Internal;

/// <summary>Opaque state-owned reporting identity; contains no caller-supplied delegate or binding/owner reference.</summary>
internal interface IHealthExecutionReportingHandle
{
}

/// <summary>Distinguishes an external monitor from one created atomically for a binding.</summary>
internal enum HealthBindingOwnership
{
    ExternalMonitor = 0,
    CreatedMonitor = 1
}

/// <summary>A state-issued binding target retained by its binding, never a singleton-owned binding graph.</summary>
internal sealed class BindingTarget
{
    internal BindingTarget(
        Guid registrationId,
        Guid monitorId,
        HealthBindingOwnership ownership,
        IHealthExecutionReportingHandle reportingHandle)
    {
        ArgumentNullException.ThrowIfNull(reportingHandle);
        RegistrationId = registrationId;
        MonitorId = monitorId;
        Ownership = ownership;
        ReportingHandle = reportingHandle;
    }

    internal Guid RegistrationId { get; }
    internal Guid MonitorId { get; }
    internal HealthBindingOwnership Ownership { get; }
    internal IHealthExecutionReportingHandle ReportingHandle { get; }
}
