using System.Text.Json;
using CIS.Security;

namespace CIS.Audit;

/// <summary>The two explicitly selected v1 schemas for the existing awaited Security observations.</summary>
public static class SecurityAuditEvents
{
    /// <summary>Gets the final composite authorization observation, including a missing caller or target.</summary>
    public static AuditEventDefinition<SecurityAuthorizationAudit> AuthorizationDecision
    {
        get;
    } = new(
        "cis.security.authorization.decision", 1, AuditCategory.Security, new AuthorizationCodec(), requireReason: true);

    /// <summary>Gets the separately reported operation outcome with its actual resolved target.</summary>
    public static AuditEventDefinition<SecurityOperationAudit> OperationReported
    {
        get;
    } = new(
        "cis.security.operation.reported", 1, AuditCategory.Security, new OperationCodec(), requireTarget: true);

    private static void WriteCapabilities(Utf8JsonWriter writer, IReadOnlyList<string> capabilities)
    {
        writer.WriteStartArray("capabilities");
        foreach (string capability in capabilities)
        {
            writer.WriteStringValue(capability);
        }
        writer.WriteEndArray();
    }

    private sealed class AuthorizationCodec : IAuditDetailsCodec<SecurityAuthorizationAudit>
    {
        public void Validate(SecurityAuthorizationAudit details) => ArgumentNullException.ThrowIfNull(details);

        public void Write(Utf8JsonWriter writer, SecurityAuthorizationAudit details)
        {
            writer.WriteStartObject();
            WriteCapabilities(writer, details.Capabilities);
            writer.WriteString("decisionCode", details.Decision.Code);
            if (details.Decision.Failure is { } failure)
            {
                writer.WriteString("failureSource", failure.Source switch
                {
                    SecurityFailureSource.Local => "local",
                    SecurityFailureSource.Remote => "remote",
                    _ => "unknown"
                });
                if (failure.HttpStatusCode is { } status)
                {
                    writer.WriteNumber("httpStatusCode", status);
                }
                if (failure.ProviderCode is { } provider)
                {
                    writer.WriteString("providerCode", provider);
                }
                if (failure.TransportCode is { } transport)
                {
                    writer.WriteString("transportCode", transport);
                }
            }
            writer.WriteEndObject();
        }
    }

    private sealed class OperationCodec : IAuditDetailsCodec<SecurityOperationAudit>
    {
        public void Validate(SecurityOperationAudit details) => ArgumentNullException.ThrowIfNull(details);

        public void Write(Utf8JsonWriter writer, SecurityOperationAudit details)
        {
            writer.WriteStartObject();
            WriteCapabilities(writer, details.Capabilities);
            writer.WriteString("reportedOutcome", details.Outcome switch
            {
                SecurityOperationOutcome.Completed => "completed",
                SecurityOperationOutcome.Failed => "failed",
                SecurityOperationOutcome.Canceled => "canceled",
                _ => throw new ArgumentOutOfRangeException(nameof(details))
            });
            if (details.OutcomeCode is { } code)
            {
                writer.WriteString("outcomeCode", code);
            }
            writer.WriteEndObject();
        }
    }
}
