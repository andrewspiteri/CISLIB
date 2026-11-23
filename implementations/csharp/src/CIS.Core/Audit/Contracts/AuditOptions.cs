using CIS.Messaging;

namespace CIS.Audit;

/// <summary>Passive restart-required Audit route and complete-payload limits.</summary>
public sealed class AuditOptions
{
    /// <summary>Selects one existing Messaging event route without a database, broker or worker operation.</summary>
    public AuditOptions(string routeAlias, AuditLimits? limits = null)
    {
        RouteAlias = MessageText.Alias(routeAlias, nameof(routeAlias));
        Limits = limits ?? new();
    }

    /// <summary>Gets the single explicit event route alias.</summary>
    public string RouteAlias
    {
        get;
    }

    /// <summary>Gets complete-payload limits which must also fit the selected Messaging runtime.</summary>
    public AuditLimits Limits
    {
        get;
    }
}
