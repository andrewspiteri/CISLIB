using System.Text.Json;

namespace CIS.Management.Wire;

public static partial class ManagementConfigurationWire
{
    private static void CandidateCommand(JsonElement value, ManagementConfigurationWireLimits limits)
    {
        Shape(value, true, ["commandVersion", "applicationId", "configurationId", "schemaVersion", "selectedSource", "sourceId",
            "sourceETag", "assignmentETag", "commonSource", "applicationSource", "payloadJson"]);
        Version(value);
        CandidateIdentity(value, true);
        _ = SourceBytes(Text(value, "payloadJson"), limits);
    }

    private static void CandidateIdentity(JsonElement value, bool closed)
    {
        ApplicationDefinition(value);
        Require(Text(value, "selectedSource") is "common" or "application");
        Id(value, "sourceId");
        ETag(value, "sourceETag");
        ETag(value, "assignmentETag");
        Reference(value.GetProperty("commonSource"), closed);
        Reference(value.GetProperty("applicationSource"), closed);
    }

    private static void CandidatePreview(JsonElement value, ManagementConfigurationWireLimits limits)
    {
        Shape(value, false, ["bindingId", "applicationId", "environment", "configurationId", "schemaVersion", "hostApplicationName",
            "selectedSource", "sourceId", "sourceETag", "assignmentETag", "commonSource", "applicationSource", "draftSha256",
            "structurallyValid", "redacted", "changes", "issues", "currentAssignment", "recipients"]);
        CandidateIdentity(value, false);
        Id(value, "bindingId");
        Scalar(value, "environment", 255);
        Scalar(value, "hostApplicationName", 255);
        Require(Text(value, "hostApplicationName") != "CommonConfiguration");
        Digest(value, "draftSha256");
        Boolean(value, "structurallyValid");
        Boolean(value, "redacted");
        foreach (JsonElement change in Items(value, "changes", 256).EnumerateArray())
        {
            Shape(change, false, ["path", "kind"]);
            Bytes(change, "path", 1024, true);
            Scalar(change, "kind", 64);
        }
        Issues(value, false);
        Require(!value.GetProperty("structurallyValid").GetBoolean() || value.GetProperty("issues").GetArrayLength() == 0);
        Assignment(value.GetProperty("currentAssignment"), limits);
        JsonElement map = value.GetProperty("currentAssignment");
        Require(Text(map, "bindingId") == Text(value, "bindingId") && Text(map, "applicationId") == Text(value, "applicationId")
            && Text(map, "environment") == Text(value, "environment") && Text(map, "configurationId") == Text(value, "configurationId")
            && Text(map, "schemaVersion") == Text(value, "schemaVersion") && Text(map, "eTag") == Text(value, "assignmentETag"));
        var registrations = new HashSet<string>(StringComparer.Ordinal);
        Page(value.GetProperty("recipients"), recipient =>
        {
            Shape(recipient, false, ["registrationId", "compatibility"], "cohortId");
            Id(recipient, "registrationId");
            Require(registrations.Add(Text(recipient, "registrationId")));
            Scalar(recipient, "compatibility", 64);
            Optional(recipient, "cohortId", item => Require(ManagementWire.IsOperationKey(Text(item))));
        });
        if (value.GetProperty("redacted").GetBoolean())
        {
            Require(value.GetProperty("changes").GetArrayLength() == 0);
            Require(value.GetProperty("issues").EnumerateArray().All(issue => !issue.TryGetProperty("path", out _)));
        }
    }
}

