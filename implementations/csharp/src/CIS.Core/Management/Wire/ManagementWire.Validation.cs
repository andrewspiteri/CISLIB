using System.Text.Json;
using CIS.Operations;

namespace CIS.Management.Wire;

public static partial class ManagementWire
{
    private static void ValidateValue(Type type, JsonElement value, bool request, ManagementWireLimits limits)
    {
        if (type == typeof(ManagementRegistrationRequest))
        {
            Shape(value, request, ["protocolMajor", "reportedInstanceId", "processStartedAtUtc", "applicationVersion"]);
            Version(value, "protocolMajor");
            Scalar(value, "reportedInstanceId", 1024);
            Time(value, "processStartedAtUtc");
            Scalar(value, "applicationVersion", 512);
        }
        else if (type == typeof(ManagementObservation))
        {
            Observation(value, request);
        }
        else if (type == typeof(ManagementInventorySnapshot))
        {
            Inventory(value, request, limits);
        }
        else if (type == typeof(ManagementMetadataCommand))
        {
            Shape(value, request, ["commandVersion", "displayName"], "description");
            Version(value, "commandVersion");
            Scalar(value, "displayName", 256);
            OptionalScalar(value, "description", 2048, true);
        }
        else if (type == typeof(ManagementApplication))
        {
            Application(value);
        }
        else if (type == typeof(ManagementServiceInfo))
        {
            Shape(value, false, ["serviceVersion", "protocolMajors", "features"]);
            Scalar(value, "serviceVersion", 512);
            JsonElement majors = Items(value, "protocolMajors", 16, 1);
            var unique = new HashSet<int>();
            foreach (JsonElement major in majors.EnumerateArray())
            {
                Require(major.ValueKind == JsonValueKind.Number && major.TryGetInt32(out int number)
                    && number > 0 && unique.Add(number));
            }
            Features(Items(value, "features", 64));
        }
        else if (type == typeof(ManagementCapabilities))
        {
            Shape(value, false, ["applicationId", "implementedApis", "callerHints"]);
            Id(value, "applicationId");
            Features(Items(value, "implementedApis", 64));
            var aliases = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement hint in Items(value, "callerHints", 64).EnumerateArray())
            {
                Shape(hint, false, ["operationId", "allowed"]);
                Require(aliases.Add(Alias(hint, "operationId", 128)));
                Boolean(hint, "allowed");
            }
        }
        else if (type == typeof(ManagementRegistrationReceipt))
        {
            Shape(value, false, ["registrationId", "applicationId", "registeredAtUtc"]);
            Id(value, "registrationId");
            Id(value, "applicationId");
            Time(value, "registeredAtUtc");
        }
        else if (type == typeof(ManagementReportReceipt))
        {
            Shape(value, false, ["registrationId", "stream", "submittedSequence", "acceptedSequence", "disposition", "receivedAtUtc"]);
            Id(value, "registrationId");
            Scalar(value, "stream", 64);
            IntegerText(value, "submittedSequence");
            IntegerText(value, "acceptedSequence");
            Scalar(value, "disposition", 64);
            Time(value, "receivedAtUtc");
        }
        else if (type == typeof(ManagementCloseReceipt))
        {
            Shape(value, false, ["registrationId", "closedAtUtc"]);
            Id(value, "registrationId");
            Time(value, "closedAtUtc");
        }
        else if (type == typeof(ManagementInstance))
        {
            Instance(value);
        }
        else if (type == typeof(ManagementInventoryEntry))
        {
            InventoryEntry(value, limits);
        }
        else if (type == typeof(ManagementMetadataDecision))
        {
            MetadataDecision(value);
        }
        else if (type == typeof(ManagementMetadataResult))
        {
            MetadataResult(value);
        }
        else if (type == typeof(ManagementOperationRejection))
        {
            Rejection(value);
        }
        else if (type == typeof(ManagementProblem))
        {
            Problem(value);
        }
        else if (type == typeof(ManagementPage<ManagementApplication>))
        {
            Page(value, typeof(ManagementApplication), limits);
        }
        else if (type == typeof(ManagementPage<ManagementInstance>))
        {
            Page(value, typeof(ManagementInstance), limits);
        }
        else if (type == typeof(ManagementPage<ManagementInventoryEntry>))
        {
            Page(value, typeof(ManagementInventoryEntry), limits);
        }
        else
        {
            throw new NotSupportedException("Select a declared management v1 DTO.");
        }
    }

    private static void Application(JsonElement value)
    {
        Shape(value, false, ["applicationId", "application", "environment", "metadata", "metadataRevision", "metadataETag"]);
        Id(value, "applicationId");
        Scalar(value, "application", 255);
        Scalar(value, "environment", 255);
        Metadata(value.GetProperty("metadata"));
        IntegerText(value, "metadataRevision");
        ETag(value, "metadataETag");
    }

    private static void Metadata(JsonElement value)
    {
        Shape(value, false, ["displayName"], "description");
        Scalar(value, "displayName", 256);
        OptionalScalar(value, "description", 2048, true);
    }

    private static void Observation(JsonElement value, bool request)
    {
        Shape(value, request, ["sequence", "observedAtUtc", "lifecycle", "health"]);
        IntegerText(value, "sequence");
        Time(value, "observedAtUtc");
        Scalar(value, "lifecycle", 64);
        Scalar(value, "health", 64);
        string lifecycle = String(value, "lifecycle");
        string health = String(value, "health");
        if (request)
        {
            Require(lifecycle is "starting" or "ready" or "stopping" or "stopped" or "unknown");
            Require(health is "healthy" or "degraded" or "unhealthy" or "unknown");
        }
    }

    private static void Inventory(JsonElement value, bool request, ManagementWireLimits limits)
    {
        Shape(value, request, ["sequence", "observedAtUtc", "modules", "categories", "jobs"]);
        IntegerText(value, "sequence");
        Time(value, "observedAtUtc");
        var modules = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement module in Items(value, "modules", limits.MaximumModules).EnumerateArray())
        {
            Shape(module, request, ["kind", "version"], "details");
            Require(modules.Add(Alias(module, "kind", 128)));
            Scalar(module, "version", 128);
            if (module.TryGetProperty("details", out JsonElement details))
            {
                Require(details.ValueKind == JsonValueKind.Object);
            }
        }

        var categories = new HashSet<(string Scope, string Id)>();
        foreach (JsonElement category in Items(value, "categories", limits.MaximumCategories).EnumerateArray())
        {
            Shape(category, request, ["executionScope", "categoryId", "displayName"], "description");
            string scope = Bytes(category, "executionScope", 256);
            string id = Bytes(category, "categoryId", 128);
            Require(categories.Add((scope, id)));
            Bytes(category, "displayName", 256);
            OptionalBytes(category, "description", 2048, true);
        }

        var jobs = new HashSet<(string Scope, string Id)>();
        int observations = 0;
        foreach (JsonElement job in Items(value, "jobs", limits.MaximumJobs).EnumerateArray())
        {
            Shape(job, request, ["executionScope", "jobId", "categoryId", "displayName", "supportedModes"],
                "description", "effectiveConfiguration", "observations", "resourceScopeDescription");
            string scope = Bytes(job, "executionScope", 256);
            string id = Bytes(job, "jobId", 128);
            string categoryId = Bytes(job, "categoryId", 128);
            Require(jobs.Add((scope, id)) && categories.Contains((scope, categoryId)));
            Bytes(job, "displayName", 256);
            OptionalBytes(job, "description", 2048, true);
            OptionalBytes(job, "resourceScopeDescription", 1024);
            var modes = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement mode in Items(job, "supportedModes", 2, 1).EnumerateArray())
            {
                string text = String(mode);
                Require(IsScalarText(text, 64) && modes.Add(text));
                if (request)
                {
                    Require(text is "continuous" or "scheduled");
                }
            }
            if (job.TryGetProperty("effectiveConfiguration", out JsonElement configuration))
            {
                Shape(configuration, request, ["revision", "enabled"]);
                IntegerText(configuration, "revision", true);
                Boolean(configuration, "enabled");
            }
            if (job.TryGetProperty("observations", out JsonElement entries))
            {
                Require(entries.ValueKind == JsonValueKind.Array);
                Require(entries.GetArrayLength() <= limits.MaximumJobObservations - observations);
                observations += entries.GetArrayLength();
                var resources = new HashSet<(string Resource, string Mode)>();
                foreach (JsonElement entry in entries.EnumerateArray())
                {
                    JobObservation(entry, request);
                    string mode = String(entry, "mode");
                    Require(resources.Add((String(entry, "resourceId"), mode)) && modes.Contains(mode));
                }
            }
        }
    }

    private static void JobObservation(JsonElement value, bool request)
    {
        Shape(value, request, ["resourceId", "mode", "state"], "lastOutcome", "observedOwner", "lastOccurrenceUtc", "nextOccurrenceUtc");
        Bytes(value, "resourceId", 512);
        Scalar(value, "mode", 64);
        Scalar(value, "state", 64);
        string mode = String(value, "mode");
        string state = String(value, "state");
        if (request)
        {
            Require(mode is "continuous" or "scheduled");
            Require(state is "disabled" or "standby" or "acquiring" or "running" or "idle" or "waiting"
                or "draining" or "lost" or "released" or "faulted");
        }
        OptionalBytes(value, "lastOutcome", 128);
        OptionalTime(value, "lastOccurrenceUtc");
        OptionalTime(value, "nextOccurrenceUtc");
        if (value.TryGetProperty("observedOwner", out JsonElement owner))
        {
            Shape(owner, request, ["ownerApplication", "instanceId", "generation", "expiresAtUtc", "observedAtUtc"]);
            Bytes(owner, "ownerApplication", 1024);
            Bytes(owner, "instanceId", 128);
            IntegerText(owner, "generation");
            Time(owner, "expiresAtUtc");
            Time(owner, "observedAtUtc");
        }
    }

    private static void Instance(JsonElement value)
    {
        Shape(value, false, ["registrationId", "applicationId", "reportedInstanceId", "instanceEvidence", "processStartedAtUtc",
            "applicationVersion", "protocolMajor", "registeredAtUtc", "presence"], "lastSeenAtUtc", "closedAtUtc", "observation",
            "observationReceivedAtUtc", "inventorySequence", "inventoryReceivedAtUtc");
        Id(value, "registrationId");
        Id(value, "applicationId");
        Scalar(value, "reportedInstanceId", 1024);
        Scalar(value, "applicationVersion", 512);
        PositiveInt(value, "protocolMajor");
        Time(value, "processStartedAtUtc");
        Time(value, "registeredAtUtc");
        Scalar(value, "presence", 64);
        OptionalTime(value, "lastSeenAtUtc");
        OptionalTime(value, "closedAtUtc");
        OptionalTime(value, "observationReceivedAtUtc");
        OptionalTime(value, "inventoryReceivedAtUtc");
        if (value.TryGetProperty("inventorySequence", out _))
        {
            IntegerText(value, "inventorySequence");
        }
        if (value.TryGetProperty("observation", out JsonElement observation))
        {
            Observation(observation, false);
        }
        JsonElement evidence = value.GetProperty("instanceEvidence");
        Shape(evidence, false, ["kind"], "reportedIdentifier", "verifiedIdentifier");
        Scalar(evidence, "kind", 64);
        string kind = String(evidence, "kind");
        OptionalScalar(evidence, "reportedIdentifier", 1024);
        OptionalScalar(evidence, "verifiedIdentifier", 1024);
        bool reported = evidence.TryGetProperty("reportedIdentifier", out _);
        bool verified = evidence.TryGetProperty("verifiedIdentifier", out _);
        Require(kind switch
        {
            "notProvided" => !reported && !verified,
            "reported" => reported && !verified,
            "verified" => verified,
            _ => true
        });
    }

    private static void InventoryEntry(JsonElement value, ManagementWireLimits limits)
    {
        Shape(value, false, ["registrationId", "applicationVersion", "receivedAtUtc", "inventory"]);
        Id(value, "registrationId");
        Scalar(value, "applicationVersion", 512);
        Time(value, "receivedAtUtc");
        Inventory(value.GetProperty("inventory"), false, limits);
    }

    private static void MetadataDecision(JsonElement value)
    {
        Shape(value, false, ["commandVersion", "applicationId", "decision", "code", "metadata", "metadataRevision", "metadataETag", "decidedAtUtc"]);
        Version(value, "commandVersion");
        Id(value, "applicationId");
        Scalar(value, "decision", 64);
        Alias(value, "code", 128);
        Metadata(value.GetProperty("metadata"));
        IntegerText(value, "metadataRevision");
        ETag(value, "metadataETag");
        Time(value, "decidedAtUtc");
    }

    private static void MetadataResult(JsonElement value)
    {
        // The two union fields are checked explicitly, allowing the opposite branch
        // to be omitted or null without relaxing null rules on other known fields.
        Shape(value, false, ["operation", "resultSource", "originalCompletion"], "attemptCompletion");
        Reference(value.GetProperty("operation"));
        Scalar(value, "resultSource", 64);
        Scalar(value, "originalCompletion", 64);
        string source = String(value, "resultSource");
        string original = String(value, "originalCompletion");
        bool hasResult = value.TryGetProperty("result", out JsonElement result) && result.ValueKind != JsonValueKind.Null;
        bool hasRejection = value.TryGetProperty("rejection", out JsonElement rejection) && rejection.ValueKind != JsonValueKind.Null;
        Require(hasResult != hasRejection);
        if (hasResult)
        {
            MetadataDecision(result);
        }
        else
        {
            Rejection(rejection);
        }
        if (source is "replayed" or "found")
        {
            Require(original is not ("notCommitted" or "unknown"));
        }
        if (value.TryGetProperty("attemptCompletion", out JsonElement attempt))
        {
            Shape(attempt, false, ["outcome", "cleanupSucceeded"], "cleanupCodes");
            Scalar(attempt, "outcome", 64);
            Boolean(attempt, "cleanupSucceeded");
            if (attempt.TryGetProperty("cleanupCodes", out JsonElement codes))
            {
                Require(codes.ValueKind == JsonValueKind.Array && codes.GetArrayLength() <= 16);
                foreach (JsonElement code in codes.EnumerateArray())
                {
                    Require(IsAlias(String(code), 128));
                }
            }
        }
    }

    private static void Rejection(JsonElement value)
    {
        Shape(value, false, ["code", "message"]);
        string code = String(value, "code");
        string message = String(value, "message");
        try
        {
            // Preserve the existing native application-error rules exactly. They
            // intentionally differ from problem codes and human display metadata.
            OperationText.Code(code, nameof(code));
            OperationText.Utf8(message, 1024, nameof(message), allowControls: true);
        }
        catch (ArgumentException)
        {
            Require(false);
        }
    }

    private static void Problem(JsonElement value)
    {
        Shape(value, false, ["type", "title", "status", "code"], "detail", "correlationId", "operation", "result");
        Scalar(value, "type", 2048);
        Require(Uri.TryCreate(String(value, "type"), UriKind.Absolute, out _));
        Scalar(value, "title", 256);
        JsonElement statusValue = value.GetProperty("status");
        Require(statusValue.ValueKind == JsonValueKind.Number && statusValue.TryGetInt32(out int status) && status is >= 400 and <= 599);
        Alias(value, "code", 128);
        OptionalScalar(value, "detail", 2048, true);
        OptionalScalar(value, "correlationId", 128);
        if (value.TryGetProperty("operation", out JsonElement reference))
        {
            Reference(reference);
        }
        if (value.TryGetProperty("result", out JsonElement result))
        {
            MetadataResult(result);
            Require(value.TryGetProperty("operation", out JsonElement operation));
            JsonElement nested = result.GetProperty("operation");
            Require(String(operation, "name") == String(nested, "name") && String(operation, "scope") == String(nested, "scope")
                && String(operation, "key") == String(nested, "key"));
        }
    }

    private static void Reference(JsonElement value)
    {
        Shape(value, false, ["name", "scope", "key"]);
        Alias(value, "name", 64);
        Bytes(value, "scope", 256);
        Require(IsOperationKey(String(value, "key")));
    }

    private static void Features(JsonElement features)
    {
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement feature in features.EnumerateArray())
        {
            Shape(feature, false, ["operationId", "version", "method", "path", "requiredCapabilities"]);
            Require(aliases.Add(Alias(feature, "operationId", 128)));
            PositiveInt(feature, "version");
            string method = String(feature, "method");
            Require(method.Length is >= 1 and <= 64 && method.All(character => char.IsAsciiLetter(character)));
            string path = String(feature, "path");
            Require(IsUtf8Text(path, 512) && path.StartsWith('/') && !path.StartsWith("//", StringComparison.Ordinal)
                && !path.Contains('?') && !path.Contains('#') && !path.Contains('\\'));
            var capabilities = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement capability in Items(feature, "requiredCapabilities", 8).EnumerateArray())
            {
                string name = String(capability);
                Require(IsAlias(name, 128) && capabilities.Add(name));
            }
        }
    }

    private static void Page(JsonElement value, Type itemType, ManagementWireLimits limits)
    {
        Shape(value, false, ["items"], "nextCursor");
        foreach (JsonElement item in Items(value, "items", 500).EnumerateArray())
        {
            ValidateValue(itemType, item, false, limits);
        }
        if (value.TryGetProperty("nextCursor", out JsonElement cursor))
        {
            Require(IsOpaqueCursor(String(cursor)));
        }
    }

    private static void Shape(JsonElement value, bool closed, string[] required, params string[] optional)
    {
        Require(value.ValueKind == JsonValueKind.Object);
        foreach (string name in required)
        {
            Require(value.TryGetProperty(name, out JsonElement property) && property.ValueKind != JsonValueKind.Null);
        }
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

    private static string String(JsonElement value, string name) => String(value.GetProperty(name));

    private static string String(JsonElement value)
    {
        Require(value.ValueKind == JsonValueKind.String);
        return value.GetString()!;
    }

    private static void Scalar(JsonElement value, string name, int maximum, bool empty = false)
        => Require(IsScalarText(String(value, name), maximum, empty));

    private static void OptionalScalar(JsonElement value, string name, int maximum, bool empty = false)
    {
        if (value.TryGetProperty(name, out JsonElement property))
        {
            Require(IsScalarText(String(property), maximum, empty));
        }
    }

    private static string Bytes(JsonElement value, string name, int maximum, bool empty = false)
    {
        string text = String(value, name);
        Require(IsUtf8Text(text, maximum, empty));
        return text;
    }

    private static void OptionalBytes(JsonElement value, string name, int maximum, bool empty = false)
    {
        if (value.TryGetProperty(name, out JsonElement property))
        {
            Require(IsUtf8Text(String(property), maximum, empty));
        }
    }

    private static string Alias(JsonElement value, string name, int maximum)
    {
        string text = String(value, name);
        Require(IsAlias(text, maximum));
        return text;
    }

    private static bool IsAlias(string value, int maximum)
        => value.Length >= 1 && value.Length <= maximum && value.All(character => char.IsAsciiLetterOrDigit(character)
            || character is '.' or '-' or '_' or ':');

    private static void Id(JsonElement value, string name) => Require(IsUuid(String(value, name)));

    private static void IntegerText(JsonElement value, string name, bool zero = false)
        => Require(IsOrderedInteger(String(value, name), zero));

    private static void Time(JsonElement value, string name) => Require(IsTimestamp(String(value, name)));

    private static void OptionalTime(JsonElement value, string name)
    {
        if (value.TryGetProperty(name, out _))
        {
            Time(value, name);
        }
    }

    private static void ETag(JsonElement value, string name) => Require(IsStrongETag(String(value, name)));

    private static void Version(JsonElement value, string name)
    {
        JsonElement selected = value.GetProperty(name);
        Require(selected.ValueKind == JsonValueKind.Number);
        Require(selected.TryGetInt32(out int version));
        Require(version == 1, ManagementWireFailure.UnsupportedVersion);
    }

    private static void PositiveInt(JsonElement value, string name)
    {
        JsonElement selected = value.GetProperty(name);
        Require(selected.ValueKind == JsonValueKind.Number && selected.TryGetInt32(out int number) && number > 0);
    }

    private static void Boolean(JsonElement value, string name)
        => Require(value.GetProperty(name).ValueKind is JsonValueKind.True or JsonValueKind.False);

    private static JsonElement Items(JsonElement value, string name, int maximum, int minimum = 0)
    {
        JsonElement items = value.GetProperty(name);
        Require(items.ValueKind == JsonValueKind.Array && items.GetArrayLength() >= minimum && items.GetArrayLength() <= maximum);
        return items;
    }
}
