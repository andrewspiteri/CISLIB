using CIS.Audit;
using CIS.Audit.Hosting;

namespace CIS.Hosting;

/// <summary>Selects Audit in the existing single CISApplication composition.</summary>
public static class CISApplicationAuditBuilderExtensions
{
    /// <summary>Captures the Audit definition; the facade selects its bridge after existing Messaging registration.</summary>
    public static CISApplicationBuilder UseAudit(this CISApplicationBuilder builder, AuditOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.ConfigureCisAudit(options);
        builder.AuditEnabled = true;
        return builder;
    }
}
