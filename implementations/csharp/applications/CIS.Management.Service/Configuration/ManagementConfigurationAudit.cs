using System.Text.Json;
using CIS.Audit;
using CIS.Management.Wire;

namespace CIS.Management.Service.Configuration;

internal sealed record ManagementConfigurationAuditDetails(string ResourceId, string Family, string Code,
    string Reason, string? PublicationId, string? Digest, string? ResultingETag, string? ReleaseId = null,
    string? PreviousETag = null, string? Generation = null, ManagementConfigurationAssignmentMap? Before = null,
    ManagementConfigurationAssignmentMap? After = null);

internal static class ManagementConfigurationAudit
{
    internal static readonly AuditEventDefinition<ManagementConfigurationAuditDetails> Definition = new(
        "cis.management.configuration.decision", 1, AuditCategory.Administration, new DetailsCodec(),
        requireTarget: true, requireOperationReference: true);

    private sealed class DetailsCodec : IAuditDetailsCodec<ManagementConfigurationAuditDetails>
    {
        public void Validate(ManagementConfigurationAuditDetails details)
        {
            if (!ManagementWire.IsUuid(details.ResourceId) || details.Family is not ("catalog" or "publication" or "release" or "assignment" or "creation")
                || !ManagementWire.IsUtf8Text(details.Reason, 1024)
                || details.PublicationId is not null && !ManagementWire.IsUuid(details.PublicationId)
                || details.Digest is not null && !ManagementConfigurationWire.IsSha256(details.Digest)
                || details.ResultingETag is not null && !ManagementWire.IsStrongETag(details.ResultingETag)
                || details.ReleaseId is not null && !ManagementWire.IsUuid(details.ReleaseId)
                || details.PreviousETag is not null && !ManagementWire.IsStrongETag(details.PreviousETag)
                || details.Generation is not null && !ManagementWire.IsOrderedInteger(details.Generation))
            {
                throw new ArgumentException("Capture only the bounded actual configuration decision metadata.", nameof(details));
            }
            foreach (ManagementConfigurationAssignmentMap map in new[] { details.Before, details.After }.OfType<ManagementConfigurationAssignmentMap>())
            {
                _ = ManagementConfigurationWire.EncodeResponse(map);
            }
        }

        public void Write(Utf8JsonWriter writer, ManagementConfigurationAuditDetails details)
        {
            writer.WriteStartObject();
            writer.WriteString("resourceId", details.ResourceId);
            writer.WriteString("family", details.Family);
            writer.WriteString("code", details.Code);
            writer.WriteString("reason", details.Reason);
            if (details.PublicationId is not null)
            {
                writer.WriteString("publicationId", details.PublicationId);
            }
            if (details.Digest is not null)
            {
                writer.WriteString("digest", details.Digest);
            }
            if (details.ResultingETag is not null)
            {
                writer.WriteString("resultingETag", details.ResultingETag);
            }
            if (details.ReleaseId is not null)
            {
                writer.WriteString("releaseId", details.ReleaseId);
            }
            if (details.PreviousETag is not null)
            {
                writer.WriteString("previousETag", details.PreviousETag);
            }
            if (details.Generation is not null)
            {
                writer.WriteString("generation", details.Generation);
            }
            if (details.Before is not null)
            {
                writer.WritePropertyName("before");
                writer.WriteRawValue(ManagementConfigurationWire.EncodeResponse(details.Before));
            }
            if (details.After is not null)
            {
                writer.WritePropertyName("after");
                writer.WriteRawValue(ManagementConfigurationWire.EncodeResponse(details.After));
            }
            writer.WriteEndObject();
        }
    }
}
