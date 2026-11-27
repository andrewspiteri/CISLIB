using System.Security.Cryptography;
using System.Text.Json;
using CIS.Operations;

namespace CIS.Management.Wire;

public static partial class ManagementConfigurationWire
{
    private static void Value(Type type, JsonElement value, bool request, ManagementConfigurationWireLimits limits)
    {
        if (type == typeof(ManagementConfigurationCreationCommand))
        {
            Shape(value, true, ["commandVersion", "configurationId", "intent", "reason"]);
            Version(value);
            Require(ManagementWire.IsOperationKey(Text(value, "configurationId")));
            Require(Text(value, "intent") is "createGeneric" or "reuseGeneric");
            Reason(value);
        }
        else if (type == typeof(ManagementConfigurationCreationContext))
        {
            Shape(value, false, ["applicationId", "application", "environment", "eTag", "schemaVersion", "hostApplicationName"]);
            Id(value, "applicationId");
            Scalar(value, "application", 255);
            Scalar(value, "environment", 255);
            ETag(value, "eTag");
            Require(Text(value, "schemaVersion") == "1");
            Scalar(value, "hostApplicationName", 255);
            Require(Text(value, "hostApplicationName") != "CommonConfiguration");
        }
        else if (type == typeof(ManagementConfigurationSummary))
        {
            Summary(value);
        }
        else if (type == typeof(ManagementPage<ManagementConfigurationSummary>))
        {
            var sources = new HashSet<string>(StringComparer.Ordinal);
            var bindings = new HashSet<string>(StringComparer.Ordinal);
            var identities = new HashSet<(string, string)>();
            Page(value, item =>
            {
                Summary(item);
                Require(sources.Add(Text(item, "sourceId")) && bindings.Add(Text(item, "bindingId"))
                    && identities.Add((Text(item, "configurationId"), Text(item, "schemaVersion"))));
            });
        }
        else if (type == typeof(ManagementConfigurationCandidatePreviewCommand))
        {
            CandidateCommand(value, limits);
        }
        else if (type == typeof(ManagementConfigurationCandidatePreview))
        {
            CandidatePreview(value, limits);
        }
        else if (type == typeof(ManagementConfigurationCatalogCommand))
        {
            Shape(value, true, ["commandVersion", "manifestDigest", "reason"]);
            Version(value);
            Digest(value, "manifestDigest");
            Reason(value);
        }
        else if (type == typeof(ManagementConfigurationPublicationCommand))
        {
            Shape(value, true, ["commandVersion", "payloadJson", "reason"]);
            Version(value);
            _ = SourceBytes(Text(value, "payloadJson"), limits);
            Reason(value);
        }
        else if (type == typeof(ManagementConfigurationReleaseCommand))
        {
            Shape(value, true, ["commandVersion", "commonSource", "applicationSource", "reason"]);
            Version(value);
            Reference(value.GetProperty("commonSource"), true);
            Reference(value.GetProperty("applicationSource"), true);
            Reason(value);
        }
        else if (type == typeof(ManagementConfigurationAssignmentCommand))
        {
            AssignmentCommand(value, true, limits);
        }
        else if (type == typeof(ManagementConfigurationValidationCommand))
        {
            Shape(value, true, ["commandVersion", "sourceId", "payloadJson"]);
            Version(value);
            Id(value, "sourceId");
            _ = SourceBytes(Text(value, "payloadJson"), limits);
        }
        else if (type == typeof(ManagementConfigurationPreviewCommand))
        {
            Shape(value, true, ["commandVersion", "applicationId", "configurationId", "schemaVersion"], "commonSource", "applicationSource", "assignment");
            Version(value);
            ApplicationDefinition(value);
            PreviewVariant(value, "assignment", true, limits);
        }
        else if (type == typeof(ManagementConfigurationEditorContext))
        {
            EditorContext(value, limits);
        }
        else if (type == typeof(ManagementConfigurationDefinition))
        {
            Definition(value, limits);
        }
        else if (type == typeof(ManagementConfigurationCatalog))
        {
            Catalog(value);
        }
        else if (type == typeof(ManagementConfigurationSource))
        {
            Source(value, limits);
        }
        else if (type == typeof(ManagementConfigurationPublicationMetadata) || type == typeof(ManagementConfigurationPublication))
        {
            Publication(value, type == typeof(ManagementConfigurationPublication), limits);
        }
        else if (type == typeof(ManagementConfigurationRelease))
        {
            Release(value);
        }
        else if (type == typeof(ManagementConfigurationAssignmentMap))
        {
            Assignment(value, limits);
        }
        else if (type == typeof(ManagementConfigurationSnapshot))
        {
            Shape(value, false, ["desired", "eTag", "commonSource", "applicationSource"]);
            Desired(value.GetProperty("desired"));
            ETag(value, "eTag");
            var desired = value.GetProperty("desired").Deserialize<ManagementConfigurationDesiredContext>(Json)!;
            SourceContent(value.GetProperty("commonSource"), limits, desired, "common");
            SourceContent(value.GetProperty("applicationSource"), limits, desired, "application");
        }
        else if (type == typeof(ManagementConfigurationValidationResult))
        {
            Shape(value, false, ["valid", "issues"]);
            Boolean(value, "valid");
            Issues(value, false);
            Require(!value.GetProperty("valid").GetBoolean() || value.GetProperty("issues").GetArrayLength() == 0);
        }
        else if (type == typeof(ManagementConfigurationPreview))
        {
            Preview(value, limits);
        }
        else if (type == typeof(ManagementConfigurationDecision))
        {
            Decision(value, limits);
        }
        else if (type == typeof(ManagementConfigurationOperationResult))
        {
            Result(value, limits);
        }
        else if (type == typeof(ManagementConfigurationProblem))
        {
            Problem(value, limits);
        }
        else if (type == typeof(ManagementConfigurationStatusReport))
        {
            Shape(value, request, ["sequence", "observedAtUtc", "entries"]);
            Integer(value, "sequence");
            Time(value, "observedAtUtc");
            var identities = new HashSet<(string, string)>();
            foreach (JsonElement entry in Items(value, "entries", limits.MaximumDefinitions).EnumerateArray())
            {
                Status(entry, request);
                Require(identities.Add((Text(entry, "configurationId"), Text(entry, "schemaVersion"))));
            }
        }
        else if (type == typeof(ManagementPage<ManagementConfigurationDefinition>))
        {
            Page(value, item => Definition(item, limits));
        }
        else if (type == typeof(ManagementPage<ManagementConfigurationPublicationMetadata>))
        {
            Page(value, item => Publication(item, false, limits));
        }
        else if (type == typeof(ManagementPage<ManagementConfigurationRelease>))
        {
            var releases = new HashSet<string>(StringComparer.Ordinal);
            Page(value, release =>
            {
                Release(release);
                Require(releases.Add(Text(release, "releaseId")));
            });
        }
        else if (type == typeof(ManagementPage<ManagementConfigurationAdoptionEntry>))
        {
            Page(value, Adoption);
        }
        else
        {
            throw new NotSupportedException("Select a declared configuration wire DTO.");
        }
    }

    private static void Definition(JsonElement value, ManagementConfigurationWireLimits limits)
    {
        Shape(value, false, ["configurationId", "schemaVersion", "sourceId", "environment", "kind", "shape"],
            "defaultsJson", "description", "applicationId", "bindingId", "hostApplicationName");
        SourceIdentity(value);
        if (Text(value, "kind") == "application")
        {
            Id(value, "bindingId");
            Scalar(value, "hostApplicationName", 255);
        }
        else
        {
            Require(!value.TryGetProperty("bindingId", out _) && !value.TryGetProperty("hostApplicationName", out _));
        }
        Declaration(value.GetProperty("shape"), 1, limits);
        Optional(value, "defaultsJson", defaults => _ = SourceBytes(Text(defaults), limits));
        OptionalScalar(value, "description", 2048, true);
    }

    private static void Summary(JsonElement value)
    {
        Shape(value, false, ["applicationId", "environment", "configurationId", "schemaVersion", "sourceId", "bindingId", "hostApplicationName", "origin", "hasCohortOverrides"], "description", "modifiedAtUtc");
        ApplicationDefinition(value);
        Scalar(value, "environment", 255);
        Id(value, "sourceId");
        Id(value, "bindingId");
        Scalar(value, "hostApplicationName", 255);
        Require(Text(value, "hostApplicationName") != "CommonConfiguration");
        Scalar(value, "origin", 64);
        Boolean(value, "hasCohortOverrides");
        OptionalScalar(value, "description", 2048, true);
        Optional(value, "modifiedAtUtc", item => Require(ManagementWire.IsTimestamp(Text(item))));
    }

    private static void Declaration(JsonElement value, int depth, ManagementConfigurationWireLimits limits)
    {
        Require(depth <= limits.MaximumJsonDepth);
        Shape(value, false, ["kind", "nullable"], "members", "element", "allowedValues");
        Boolean(value, "nullable");
        string kind = Text(value, "kind");
        Require(kind is "object" or "array" or "dictionary" or "string" or "number" or "integer" or "boolean");
        if (kind == "object")
        {
            Require(!value.TryGetProperty("element", out _) && !value.TryGetProperty("allowedValues", out _));
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement member in Items(value, "members", 256).EnumerateArray())
            {
                Shape(member, false, ["name", "required", "shape"]);
                Scalar(member, "name", 256);
                Require(names.Add(Text(member, "name")));
                Boolean(member, "required");
                Declaration(member.GetProperty("shape"), depth + 1, limits);
            }
        }
        else if (kind is "array" or "dictionary")
        {
            Require(!value.TryGetProperty("members", out _) && !value.TryGetProperty("allowedValues", out _));
            Require(value.TryGetProperty("element", out JsonElement element));
            Declaration(element, depth + 1, limits);
        }
        else
        {
            Require(!value.TryGetProperty("members", out _) && !value.TryGetProperty("element", out _));
            if (value.TryGetProperty("allowedValues", out _))
            {
                Require(kind == "string");
                var values = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonElement item in Items(value, "allowedValues", 4096, 1).EnumerateArray())
                {
                    Require(ManagementWire.IsUtf8Text(Text(item), 64 * 1024, true) && values.Add(Text(item)));
                }
            }
        }
    }

    private static void Catalog(JsonElement value)
    {
        Shape(value, false, ["catalogId", "eTag", "loadedManifestDigest", "ready", "definitionCount", "sourceCount",
            "bindingCount", "quotaBytes", "usedBytes"], "appliedManifestDigest");
        Id(value, "catalogId");
        ETag(value, "eTag");
        Digest(value, "loadedManifestDigest");
        Optional(value, "appliedManifestDigest", item => Require(IsSha256(Text(item))));
        Boolean(value, "ready");
        Count(value, "definitionCount");
        Count(value, "sourceCount");
        Count(value, "bindingCount");
        SafeBytes(value, "quotaBytes", false);
        SafeBytes(value, "usedBytes", true);
        Require(value.GetProperty("usedBytes").GetInt64() <= value.GetProperty("quotaBytes").GetInt64());
        Require(!value.GetProperty("ready").GetBoolean() || value.TryGetProperty("appliedManifestDigest", out _));
    }

    private static void Source(JsonElement value, ManagementConfigurationWireLimits limits)
    {
        Shape(value, false, ["sourceId", "configurationId", "schemaVersion", "environment", "kind", "eTag", "publications"], "applicationId");
        SourceIdentity(value);
        ETag(value, "eTag");
        Page(value.GetProperty("publications"), publication =>
        {
            Publication(publication, false, limits);
            foreach (string field in new[] { "sourceId", "configurationId", "schemaVersion", "environment", "kind" })
            {
                Require(Text(publication, field) == Text(value, field));
            }
            Require(OptionalText(publication, "applicationId") == OptionalText(value, "applicationId"));
        });
    }

    private static void SourceIdentity(JsonElement value)
    {
        Id(value, "sourceId");
        DefinitionIdentity(value);
        Scalar(value, "environment", 255);
        string kind = Text(value, "kind");
        Require(kind is "common" or "application", "configuration.content.scope");
        if (kind == "application")
        {
            Id(value, "applicationId");
        }
        else
        {
            Require(!value.TryGetProperty("applicationId", out _), "configuration.content.scope");
        }
    }

    private static void Publication(JsonElement value, bool content, ManagementConfigurationWireLimits limits)
    {
        string[] fields = ["publicationId", "sourceId", "configurationId", "schemaVersion", "environment", "kind",
            "sha256", "actor", "createdAtUtc", "reason"];
        Shape(value, false, content ? [.. fields, "payloadJson"] : fields, "applicationId");
        SourceIdentity(value);
        Id(value, "publicationId");
        Digest(value, "sha256");
        Scalar(value, "actor", 256);
        Time(value, "createdAtUtc");
        Reason(value);
        if (content)
        {
            Integrity(value, limits);
        }
    }

    private static void Reference(JsonElement value, bool closed)
    {
        Shape(value, closed, ["state"], "publicationId");
        string state = Text(value, "state");
        Require(state is "present" or "absent");
        if (state == "present")
        {
            Id(value, "publicationId");
        }
        else
        {
            Require(!value.TryGetProperty("publicationId", out _));
        }
    }

    private static void Release(JsonElement value)
    {
        Shape(value, false, ["releaseId", "applicationId", "configurationId", "schemaVersion", "environment", "commonSource",
            "applicationSource", "actor", "createdAtUtc", "reason", "eTag"]);
        Id(value, "releaseId");
        ApplicationDefinition(value);
        Scalar(value, "environment", 255);
        Reference(value.GetProperty("commonSource"), false);
        Reference(value.GetProperty("applicationSource"), false);
        Scalar(value, "actor", 256);
        Time(value, "createdAtUtc");
        Reason(value);
        ETag(value, "eTag");
    }

    private static void Assignment(JsonElement value, ManagementConfigurationWireLimits limits)
    {
        Shape(value, false, ["bindingId", "applicationId", "configurationId", "schemaVersion", "environment", "eTag", "overrides"],
            "defaultReleaseId", "generation");
        Id(value, "bindingId");
        ApplicationDefinition(value);
        Scalar(value, "environment", 255);
        ETag(value, "eTag");
        Overrides(value, false, limits);
        bool assigned = value.TryGetProperty("defaultReleaseId", out _);
        Require(assigned == value.TryGetProperty("generation", out _));
        if (assigned)
        {
            Id(value, "defaultReleaseId");
            Integer(value, "generation");
        }
        else
        {
            Require(value.GetProperty("overrides").GetArrayLength() == 0);
        }
    }

    private static void AssignmentCommand(JsonElement value, bool closed, ManagementConfigurationWireLimits limits)
    {
        Shape(value, closed, ["commandVersion", "intent", "defaultReleaseId", "overrides", "reason"]);
        Version(value);
        if (closed)
        {
            Intent(value);
        }
        else
        {
            Scalar(value, "intent", 64);
        }
        Id(value, "defaultReleaseId");
        Overrides(value, closed, limits);
        Reason(value);
    }

    private static void Overrides(JsonElement value, bool closed, ManagementConfigurationWireLimits limits)
    {
        var cohorts = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement entry in Items(value, "overrides", limits.MaximumOverrides).EnumerateArray())
        {
            Shape(entry, closed, ["cohortId", "releaseId"]);
            Portable(entry, "cohortId");
            Id(entry, "releaseId");
            Require(cohorts.Add(Text(entry, "cohortId")));
        }
    }

    private static void Desired(JsonElement value)
    {
        Shape(value, false, ["authorityId", "applicationId", "environment", "registrationId", "configurationId", "schemaVersion",
            "hostApplicationName", "releaseId", "generation"], "cohortId");
        Id(value, "authorityId");
        Id(value, "registrationId");
        ApplicationDefinition(value);
        Scalar(value, "environment", 255);
        Scalar(value, "hostApplicationName", 255);
        Id(value, "releaseId");
        Integer(value, "generation");
        Optional(value, "cohortId", item => Require(ManagementWire.IsOperationKey(Text(item))));
    }

    private static void SourceContent(JsonElement value, ManagementConfigurationWireLimits limits,
        ManagementConfigurationDesiredContext desired, string expectedKind)
    {
        string[] present = ["publicationId", "sha256", "payloadJson", "sourceId", "configurationId", "schemaVersion", "environment", "kind", "applicationId"];
        Shape(value, false, ["state"], present);
        string state = Text(value, "state");
        Require(state is "present" or "absent", "configuration.content.invalid");
        if (state == "absent")
        {
            Require(present.All(field => !value.TryGetProperty(field, out _)), "configuration.content.invalid");
            return;
        }
        Shape(value, false, ["state", "publicationId", "sha256", "payloadJson", "sourceId", "configurationId", "schemaVersion", "environment", "kind"], "applicationId");
        SourceIdentity(value);
        Id(value, "publicationId");
        Require(Text(value, "configurationId") == desired.ConfigurationId && Text(value, "schemaVersion") == desired.SchemaVersion,
            "configuration.content.schema");
        Require(Text(value, "environment") == desired.Environment && Text(value, "kind") == expectedKind
            && (expectedKind == "common" || Text(value, "applicationId") == desired.ApplicationId), "configuration.content.scope");
        Integrity(value, limits);
    }

    private static void Integrity(JsonElement value, ManagementConfigurationWireLimits limits)
    {
        Digest(value, "sha256");
        byte[] content = SourceBytes(Text(value, "payloadJson"), limits);
        Require(Convert.ToHexStringLower(SHA256.HashData(content)) == Text(value, "sha256"), "configuration.integrity");
    }

    private static void Preview(JsonElement value, ManagementConfigurationWireLimits limits)
    {
        Shape(value, false, ["applicationId", "configurationId", "schemaVersion", "structurallyValid", "redacted", "changes", "issues",
            "currentAssignment", "recipients"], "commonSource", "applicationSource", "proposedAssignment");
        ApplicationDefinition(value);
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
        JsonElement assignment = value.GetProperty("currentAssignment");
        Assignment(assignment, limits);
        foreach (string field in new[] { "applicationId", "configurationId", "schemaVersion" })
        {
            Require(Text(value, field) == Text(assignment, field));
        }
        var registrations = new HashSet<string>(StringComparer.Ordinal);
        Page(value.GetProperty("recipients"), recipient =>
        {
            Shape(recipient, false, ["registrationId", "compatibility"], "cohortId");
            Id(recipient, "registrationId");
            Require(registrations.Add(Text(recipient, "registrationId")));
            Scalar(recipient, "compatibility", 64);
            Optional(recipient, "cohortId", item => Require(ManagementWire.IsOperationKey(Text(item))));
        });
        PreviewVariant(value, "proposedAssignment", false, limits);
    }

    private static void PreviewVariant(JsonElement value, string assignmentName, bool closed,
        ManagementConfigurationWireLimits limits)
    {
        bool common = value.TryGetProperty("commonSource", out JsonElement commonSource);
        bool application = value.TryGetProperty("applicationSource", out JsonElement applicationSource);
        bool assignment = value.TryGetProperty(assignmentName, out JsonElement proposal);
        Require(common == application && assignment != common);
        if (assignment)
        {
            AssignmentCommand(proposal, closed, limits);
        }
        else
        {
            Reference(commonSource, closed);
            Reference(applicationSource, closed);
        }
    }

    private static void Issues(JsonElement value, bool closed)
    {
        foreach (JsonElement issue in Items(value, "issues", 64).EnumerateArray())
        {
            Shape(issue, closed, ["code"], "path");
            Code(issue, "code");
            Optional(issue, "path", item => Require(ManagementWire.IsUtf8Text(Text(item), 1024, true)));
        }
    }

    private static void Decision(JsonElement value, ManagementConfigurationWireLimits limits)
    {
        Shape(value, false, ["commandVersion", "kind", "decision", "code", "decidedAtUtc"], "catalogId", "manifestDigest", "sourceId",
            "publicationId", "sha256", "applicationId", "configurationId", "schemaVersion", "releaseId", "generation", "eTag", "intent", "assignment", "bindingId", "hostApplicationName", "sourceETag");
        Version(value);
        Scalar(value, "kind", 64);
        Scalar(value, "decision", 64);
        Code(value, "code");
        Time(value, "decidedAtUtc");
        foreach (string field in new[] { "catalogId", "sourceId", "publicationId", "applicationId", "releaseId", "bindingId" })
        {
            Optional(value, field, item => Require(ManagementWire.IsUuid(Text(item))));
        }
        foreach (string field in new[] { "manifestDigest", "sha256" })
        {
            Optional(value, field, item => Require(IsSha256(Text(item))));
        }
        Optional(value, "configurationId", item => Require(ManagementWire.IsOperationKey(Text(item))));
        foreach (string field in new[] { "schemaVersion", "generation" })
        {
            Optional(value, field, item => Require(ManagementWire.IsOrderedInteger(Text(item))));
        }
        Optional(value, "eTag", item => Require(ManagementWire.IsStrongETag(Text(item))));
        Optional(value, "sourceETag", item => Require(ManagementWire.IsStrongETag(Text(item))));
        OptionalScalar(value, "hostApplicationName", 255);
        OptionalScalar(value, "intent", 64);
        Optional(value, "assignment", item => Assignment(item, limits));
        switch (Text(value, "kind"))
        {
            case "configurationCreated":
                Required(value, "applicationId", "configurationId", "schemaVersion", "sourceId", "bindingId", "hostApplicationName", "sourceETag", "eTag");
                Require(Text(value, "schemaVersion") == "1" && Text(value, "hostApplicationName") != "CommonConfiguration");
                break;
            case "catalogApplied":
                Required(value, "catalogId", "manifestDigest", "eTag");
                break;
            case "sourcePublished":
                Required(value, "sourceId", "publicationId", "sha256", "eTag");
                break;
            case "releaseCreated":
                Required(value, "applicationId", "configurationId", "schemaVersion", "releaseId", "eTag");
                break;
            case "assignmentsReplaced":
                Required(value, "applicationId", "configurationId", "schemaVersion", "generation", "eTag", "intent", "assignment");
                Scalar(value, "intent", 64);
                JsonElement map = value.GetProperty("assignment");
                foreach (string field in new[] { "applicationId", "configurationId", "schemaVersion", "generation", "eTag" })
                {
                    Require(Text(value, field) == Text(map, field));
                }
                break;
        }
    }

    private static void Result(JsonElement value, ManagementConfigurationWireLimits limits)
    {
        Shape(value, false, ["operation", "resultSource", "originalCompletion"], "result", "rejection", "attemptCompletion");
        Operation(value.GetProperty("operation"));
        Scalar(value, "resultSource", 64);
        Scalar(value, "originalCompletion", 64);
        bool hasResult = value.TryGetProperty("result", out JsonElement result);
        bool hasRejection = value.TryGetProperty("rejection", out JsonElement rejection);
        Require(hasResult != hasRejection);
        if (hasResult)
        {
            Decision(result, limits);
        }
        else
        {
            Shape(rejection, false, ["code", "message"]);
            try
            {
                OperationText.Code(Text(rejection, "code"), "code");
                OperationText.Utf8(Text(rejection, "message"), 1024, "message", allowControls: true);
            }
            catch (ArgumentException)
            {
                Require(false);
            }
        }
        string source = Text(value, "resultSource");
        string original = Text(value, "originalCompletion");
        if (source is "prepared" or "replayed" or "found")
        {
            Require(original is not ("notCommitted" or "unknown"));
        }
        Optional(value, "attemptCompletion", attempt =>
        {
            Shape(attempt, false, ["outcome", "cleanupSucceeded"], "cleanupCodes");
            Scalar(attempt, "outcome", 64);
            Boolean(attempt, "cleanupSucceeded");
            if (attempt.TryGetProperty("cleanupCodes", out _))
            {
                foreach (JsonElement code in Items(attempt, "cleanupCodes", 16).EnumerateArray())
                {
                    Require(IsCode(Text(code)));
                }
            }
        });
    }

    private static void Operation(JsonElement value)
    {
        Shape(value, false, ["name", "scope", "key"]);
        Code(value, "name", 64);
        Bytes(value, "scope", 256);
        Require(ManagementWire.IsOperationKey(Text(value, "key")));
    }

    private static void Problem(JsonElement value, ManagementConfigurationWireLimits limits)
    {
        Shape(value, false, ["type", "title", "status", "code"], "detail", "correlationId", "operation", "result");
        Scalar(value, "type", 2048);
        Require(Uri.TryCreate(Text(value, "type"), UriKind.Absolute, out _));
        Scalar(value, "title", 256);
        Require(Field(value, "status").ValueKind == JsonValueKind.Number
            && Field(value, "status").TryGetInt32(out int status) && status is >= 400 and <= 599);
        Code(value, "code");
        OptionalScalar(value, "detail", 1024, true);
        OptionalScalar(value, "correlationId", 128);
        Optional(value, "operation", Operation);
        Optional(value, "result", result =>
        {
            Result(result, limits);
            Require(value.TryGetProperty("operation", out JsonElement operation));
            JsonElement nested = result.GetProperty("operation");
            foreach (string field in new[] { "name", "scope", "key" })
            {
                Require(Text(operation, field) == Text(nested, field));
            }
        });
    }

    private static void Status(JsonElement value, bool closed)
    {
        Shape(value, closed, ["configurationId", "schemaVersion", "updateMode", "state", "liveStateValid"],
            "desired", "applied", "lastSuccessful", "candidateFailureCode", "notificationFailureCode");
        DefinitionIdentity(value);
        Scalar(value, "updateMode", 64);
        Scalar(value, "state", 64);
        string state = Text(value, "state");
        if (closed)
        {
            Require(Text(value, "updateMode") is "HotReload" or "RestartRequired");
            Require(state is "Pending" or "Applied" or "AppliedUnchanged" or "Rejected" or "PendingRestart" or "Faulted");
        }
        Boolean(value, "liveStateValid");
        bool live = value.GetProperty("liveStateValid").GetBoolean();
        Optional(value, "desired", item => ConfigurationVersion(item, closed, false));
        Optional(value, "applied", item => ConfigurationVersion(item, closed, true));
        Optional(value, "lastSuccessful", item => ConfigurationVersion(item, closed, true));
        Optional(value, "candidateFailureCode", item => Require(IsCode(Text(item))));
        Optional(value, "notificationFailureCode", item => Require(IsCode(Text(item))));
        Require(live || !value.TryGetProperty("applied", out _));
        if (state is "Applied" or "AppliedUnchanged")
        {
            Require(live);
            Required(value, "desired", "applied");
            JsonElement desired = value.GetProperty("desired");
            JsonElement applied = value.GetProperty("applied");
            Require(Text(desired, "releaseId") == Text(applied, "releaseId")
                && Text(desired, "generation") == Text(applied, "generation"));
        }
        if (state is "Rejected" or "PendingRestart")
        {
            Require(live);
            Required(value, "desired", "applied");
        }
        if (state == "Faulted")
        {
            Require(!live);
        }
    }

    private static void ConfigurationVersion(JsonElement value, bool closed, bool applied)
    {
        Shape(value, closed, applied ? ["releaseId", "generation", "localRevision"] : ["releaseId", "generation"]);
        Id(value, "releaseId");
        Integer(value, "generation");
        if (applied)
        {
            Integer(value, "localRevision", true);
        }
    }

    private static void Adoption(JsonElement value)
    {
        Shape(value, false, ["applicationId", "environment", "configurationId", "schemaVersion", "registrationFreshness", "adoptionFreshness", "convergence"],
            "cohortId", "registrationId", "reportedInstanceId", "applicationVersion", "desired", "reported", "observedAtUtc", "receivedAtUtc");
        ApplicationDefinition(value);
        Scalar(value, "environment", 255);
        Scalar(value, "registrationFreshness", 64);
        Scalar(value, "adoptionFreshness", 64);
        Scalar(value, "convergence", 64);
        Optional(value, "cohortId", item => Require(ManagementWire.IsOperationKey(Text(item))));
        Optional(value, "registrationId", item => Require(ManagementWire.IsUuid(Text(item))));
        OptionalScalar(value, "reportedInstanceId", 1024);
        OptionalScalar(value, "applicationVersion", 512);
        Optional(value, "desired", item => ConfigurationVersion(item, false, false));
        Optional(value, "reported", item =>
        {
            Status(item, false);
            Require(Text(item, "configurationId") == Text(value, "configurationId")
                && Text(item, "schemaVersion") == Text(value, "schemaVersion"));
        });
        Optional(value, "observedAtUtc", item => Require(ManagementWire.IsTimestamp(Text(item))));
        Optional(value, "receivedAtUtc", item => Require(ManagementWire.IsTimestamp(Text(item))));
        bool report = value.TryGetProperty("reported", out _);
        Require(report == value.TryGetProperty("observedAtUtc", out _) && report == value.TryGetProperty("receivedAtUtc", out _));
        if (report)
        {
            Required(value, "registrationId");
        }
    }

    private static void Page(JsonElement value, Action<JsonElement> item)
    {
        Shape(value, false, ["items"], "nextCursor");
        foreach (JsonElement entry in Items(value, "items", 500).EnumerateArray())
        {
            item(entry);
        }
        Optional(value, "nextCursor", cursor => Require(Text(cursor) is { Length: >= 1 and <= 2048 } text
            && text.All(character => character is >= '!' and <= '~')));
    }

    private static bool Matches(ManagementConfigurationDesiredContext value, ManagementConfigurationSnapshotTarget expected)
        => value.AuthorityId == expected.AuthorityId && value.ApplicationId == expected.ApplicationId
            && value.Environment == expected.Environment && value.RegistrationId == expected.RegistrationId
            && value.ConfigurationId == expected.ConfigurationId && value.SchemaVersion == expected.SchemaVersion
            && value.HostApplicationName == expected.HostApplicationName && value.CohortId == expected.CohortId;

    private static void ValidateExpected(ManagementConfigurationSnapshotTarget expected)
    {
        if (!ManagementWire.IsUuid(expected.AuthorityId) || !ManagementWire.IsUuid(expected.ApplicationId)
            || !ManagementWire.IsUuid(expected.RegistrationId) || !ManagementWire.IsOperationKey(expected.ConfigurationId)
            || !ManagementWire.IsOrderedInteger(expected.SchemaVersion) || !ManagementWire.IsScalarText(expected.Environment, 255)
            || !ManagementWire.IsScalarText(expected.HostApplicationName, 255)
            || expected.CohortId is not null && !ManagementWire.IsOperationKey(expected.CohortId))
        {
            throw new ArgumentException("Supply a valid independently trusted configuration target.", nameof(expected));
        }
    }

    private static void Shape(JsonElement value, bool closed, string[] required, params string[] optional)
    {
        Require(value.ValueKind == JsonValueKind.Object);
        Required(value, required);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            bool known = required.Contains(property.Name, StringComparer.Ordinal) || optional.Contains(property.Name, StringComparer.Ordinal);
            Require(!closed || known);
            if (known)
            {
                Require(property.Value.ValueKind != JsonValueKind.Null);
            }
        }
    }

    private static void Required(JsonElement value, params string[] names)
    {
        foreach (string name in names)
        {
            Require(value.TryGetProperty(name, out JsonElement selected) && selected.ValueKind != JsonValueKind.Null);
        }
    }

    private static JsonElement Field(JsonElement value, string name)
    {
        Require(value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out _));
        return value.GetProperty(name);
    }

    private static string Text(JsonElement value, string name) => Text(Field(value, name));

    private static string Text(JsonElement value)
    {
        Require(value.ValueKind == JsonValueKind.String);
        return value.GetString()!;
    }

    private static string? OptionalText(JsonElement value, string name)
        => value.TryGetProperty(name, out JsonElement item) ? Text(item) : null;

    private static void Optional(JsonElement value, string name, Action<JsonElement> validate)
    {
        if (value.TryGetProperty(name, out JsonElement item))
        {
            validate(item);
        }
    }

    private static void Scalar(JsonElement value, string name, int maximum, bool empty = false)
        => Require(ManagementWire.IsScalarText(Text(value, name), maximum, empty));

    private static void OptionalScalar(JsonElement value, string name, int maximum, bool empty = false)
        => Optional(value, name, item => Require(ManagementWire.IsScalarText(Text(item), maximum, empty)));

    private static void Bytes(JsonElement value, string name, int maximum, bool empty = false)
        => Require(ManagementWire.IsUtf8Text(Text(value, name), maximum, empty));

    private static void Id(JsonElement value, string name) => Require(ManagementWire.IsUuid(Text(value, name)));

    private static void Portable(JsonElement value, string name) => Require(ManagementWire.IsOperationKey(Text(value, name)));

    private static void Integer(JsonElement value, string name, bool zero = false)
        => Require(ManagementWire.IsOrderedInteger(Text(value, name), zero));

    private static void Digest(JsonElement value, string name) => Require(IsSha256(Text(value, name)), "configuration.integrity");

    private static void ETag(JsonElement value, string name) => Require(ManagementWire.IsStrongETag(Text(value, name)));

    private static void Time(JsonElement value, string name) => Require(ManagementWire.IsTimestamp(Text(value, name)));

    private static void DefinitionIdentity(JsonElement value)
    {
        Portable(value, "configurationId");
        Integer(value, "schemaVersion");
    }

    private static void ApplicationDefinition(JsonElement value)
    {
        Id(value, "applicationId");
        DefinitionIdentity(value);
    }

    private static void Intent(JsonElement value) => Require(Text(value, "intent") is "replace" or "promote" or "rollback");

    private static void Version(JsonElement value)
    {
        JsonElement selected = Field(value, "commandVersion");
        Require(selected.ValueKind == JsonValueKind.Number && selected.TryGetInt32(out int version) && version == 1,
            "configuration.version.unsupported", ManagementWireFailure.UnsupportedVersion);
    }

    private static bool IsCode(string value, int maximum = 128)
        => value.Length is >= 1 && value.Length <= maximum && value.All(character => char.IsAsciiLetterOrDigit(character)
            || character is '.' or '-' or '_' or ':');

    private static void Code(JsonElement value, string name, int maximum = 128) => Require(IsCode(Text(value, name), maximum));

    private static void Reason(JsonElement value) => Bytes(value, "reason", 1024);

    private static void Boolean(JsonElement value, string name)
        => Require(Field(value, name).ValueKind is JsonValueKind.True or JsonValueKind.False);

    private static void Count(JsonElement value, string name)
        => Require(Field(value, name).ValueKind == JsonValueKind.Number && Field(value, name).TryGetInt32(out int count) && count >= 0);

    private static void SafeBytes(JsonElement value, string name, bool zero)
        => Require(Field(value, name).ValueKind == JsonValueKind.Number && Field(value, name).TryGetInt64(out long count)
            && count >= (zero ? 0 : 1) && count <= 9_007_199_254_740_991);

    private static JsonElement Items(JsonElement value, string name, int maximum, int minimum = 0)
    {
        JsonElement items = Field(value, name);
        Require(items.ValueKind == JsonValueKind.Array && items.GetArrayLength() >= minimum && items.GetArrayLength() <= maximum);
        return items;
    }
}
