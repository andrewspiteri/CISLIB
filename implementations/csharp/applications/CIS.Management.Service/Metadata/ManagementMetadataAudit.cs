using System.Text.Json;
using CIS.Audit;
using CIS.Management.Wire;

namespace CIS.Management.Service.Metadata;

internal sealed record ManagementMetadataAuditDetails(string ApplicationId, string PreviousRevision, string ResultingRevision,
    ManagementApplicationMetadata Before, ManagementApplicationMetadata After);

internal static class ManagementMetadataAudit
{
    internal static readonly AuditEventDefinition<ManagementMetadataAuditDetails> Definition = new(
        "cis.management.application.metadata.decision", 1, AuditCategory.Administration, new DetailsCodec(),
        requireTarget: true, requireOperationReference: true);

    private sealed class DetailsCodec : IAuditDetailsCodec<ManagementMetadataAuditDetails>
    {
        public void Validate(ManagementMetadataAuditDetails details)
        {
            ArgumentNullException.ThrowIfNull(details);
            if (!ManagementWire.IsUuid(details.ApplicationId) || !ManagementWire.IsOrderedInteger(details.PreviousRevision)
                || !ManagementWire.IsOrderedInteger(details.ResultingRevision))
            {
                throw new ArgumentException("Capture the actual application and metadata revision transition.", nameof(details));
            }
            _ = ManagementWire.EncodeRequest(new ManagementMetadataCommand(1, details.Before.DisplayName, details.Before.Description));
            _ = ManagementWire.EncodeRequest(new ManagementMetadataCommand(1, details.After.DisplayName, details.After.Description));
        }

        public void Write(Utf8JsonWriter writer, ManagementMetadataAuditDetails details)
        {
            writer.WriteStartObject();
            writer.WriteString("applicationId", details.ApplicationId);
            writer.WriteString("previousRevision", details.PreviousRevision);
            writer.WriteString("resultingRevision", details.ResultingRevision);
            WriteMetadata(writer, "before", details.Before);
            WriteMetadata(writer, "after", details.After);
            writer.WriteEndObject();
        }

        private static void WriteMetadata(Utf8JsonWriter writer, string name, ManagementApplicationMetadata value)
        {
            writer.WritePropertyName(name);
            writer.WriteStartObject();
            writer.WriteString("displayName", value.DisplayName);
            if (value.Description is not null)
            {
                writer.WriteString("description", value.Description);
            }
            writer.WriteEndObject();
        }
    }
}
