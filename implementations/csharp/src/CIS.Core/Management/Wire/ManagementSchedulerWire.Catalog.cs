using System.Text.Json;

namespace CIS.Management.Wire;

public static partial class ManagementSchedulerWire
{
    private static void CapabilityKey(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["ownerApplication", "kind", "executionScope", "definitionId", "definitionVersion"]);
        Scalar(value, "ownerApplication", 255);
        Token(value, "kind");
        Require(!closed || Text(value, "kind") is "job" or "triggerProvider" or "ruleEngine");
        Bytes(value, "executionScope", 256);
        Bytes(value, "definitionId", 128);
        Integer(value, "definitionVersion");
    }

    private static void ShapeDeclaration(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        int nodes = 0;
        Declaration(value, closed, limits, 1, ref nodes);
    }

    private static void Declaration(JsonElement value, bool closed, ManagementSchedulerWireLimits limits, int depth, ref int nodes)
    {
        Require(depth <= limits.MaximumJsonDepth && ++nodes <= ManagementCapabilityLimits.MaximumShapeNodes);
        Shape(value, closed, ["kind", "nullable"], "members", "element", "allowedValues");
        _ = Boolean(value, "nullable");
        string kind = Text(value, "kind");
        Require(kind is "object" or "array" or "dictionary" or "string" or "number" or "integer" or "boolean");
        if (kind == "object")
        {
            Require(!Has(value, "element") && !Has(value, "allowedValues"));
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement member in Items(value, "members", 256).EnumerateArray())
            {
                Shape(member, closed, ["name", "required", "shape"]);
                Scalar(member, "name", 256);
                Require(names.Add(Text(member, "name")));
                _ = Boolean(member, "required");
                Declaration(Field(member, "shape"), closed, limits, depth + 1, ref nodes);
            }
        }
        else if (kind is "array" or "dictionary")
        {
            Require(!Has(value, "members") && !Has(value, "allowedValues"));
            Declaration(Field(value, "element"), closed, limits, depth + 1, ref nodes);
        }
        else
        {
            Require(!Has(value, "members") && !Has(value, "element"));
            if (Has(value, "allowedValues"))
            {
                Require(kind == "string");
                var choices = new HashSet<string>(StringComparer.Ordinal);
                foreach (JsonElement choice in Items(value, "allowedValues", ManagementCapabilityLimits.MaximumEnumValues, 1).EnumerateArray())
                {
                    Require(ManagementWire.IsUtf8Text(Text(choice), 1024, allowEmpty: true) && choices.Add(Text(choice)));
                }
            }
        }
    }

    private static void ConfigurationContract(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["configurationId", "schemaVersion", "shape"]);
        Portable(value, "configurationId");
        Integer(value, "schemaVersion");
        ShapeDeclaration(Field(value, "shape"), closed, limits);
    }

    private static void Form(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["fields"], "groups", "lookups");
        var groups = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement group in FormItems(value, "groups", ManagementCapabilityLimits.MaximumGroups))
        {
            Shape(group, closed, ["id", "label"], "order");
            Token(group, "id");
            Require(groups.Add(Text(group, "id")));
            Bytes(group, "label", 256);
            Optional(group, "order", _ => Number(group, "order", -10000, 10000));
        }
        var lookups = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement lookup in FormItems(value, "lookups", ManagementCapabilityLimits.MaximumLookups))
        {
            Shape(lookup, closed, ["id", "ownerApplication", "executionScope", "lookupName", "contract"]);
            Token(lookup, "id");
            Require(lookups.Add(Text(lookup, "id")));
            Scalar(lookup, "ownerApplication", 255);
            Bytes(lookup, "executionScope", 256);
            Token(lookup, "lookupName");
            Contract(Field(lookup, "contract"), closed, limits);
        }
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement field in Items(value, "fields", ManagementCapabilityLimits.MaximumFields).EnumerateArray())
        {
            Shape(field, closed, ["path", "editor", "label"], "description", "groupId", "order", "unit", "readOnly", "lookupId");
            Pointer(field, "path");
            Require(paths.Add(Text(field, "path")));
            Token(field, "editor");
            Require(!closed || Text(field, "editor") is "text" or "number" or "boolean" or "enum" or "duration" or "group" or "calendar");
            Bytes(field, "label", 256);
            Optional(field, "description", _ => Bytes(field, "description", 2048, empty: true));
            Optional(field, "groupId", _ => Require(groups.Contains(Text(field, "groupId"))));
            Optional(field, "lookupId", _ => Require(lookups.Contains(Text(field, "lookupId"))));
            Optional(field, "order", _ => Number(field, "order", -10000, 10000));
            Optional(field, "readOnly", _ => Boolean(field, "readOnly"));
            Optional(field, "unit", _ => Require(Text(field, "unit") is "milliseconds" or "seconds" or "minutes" or "hours" or "days" or "bytes" or "count" or "percent"));
        }
        // The passive model validates all references and finite metadata independently of this JSON shape.
        _ = value.Deserialize<ManagementFormDescription>(Json) ?? throw new DataFailure(ManagementWireFailure.Invalid);
    }

    private static IEnumerable<JsonElement> FormItems(JsonElement value, string name, int maximum)
        => Has(value, name) ? Items(value, name, maximum).EnumerateArray() : [];

    private static void Capability(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Require(Utf8.GetByteCount(value.GetRawText()) <= ManagementCapabilityLimits.MaximumDefinitionBytes, ManagementWireFailure.TooLarge);
        Shape(value, closed, ["key", "displayName", "supportedOperations"], "description", "categoryId", "configurationContract",
            "inputContract", "inputShape", "eventContract", "eventShape", "resultSummaryContract", "resultSummaryShape", "form", "executionSupport");
        CapabilityKey(Field(value, "key"), closed, limits);
        Bytes(value, "displayName", 256);
        Optional(value, "description", _ => Bytes(value, "description", 2048, empty: true));
        Optional(value, "categoryId", _ => Bytes(value, "categoryId", 128));
        var operations = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement operation in Items(value, "supportedOperations", ManagementCapabilityLimits.MaximumOperations, 1).EnumerateArray())
        {
            string text = Text(operation);
            Require(text.Length is >= 1 and <= 128 && text.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'));
            Require(operations.Add(text));
            Require(!closed || text is "inspect" or "configure" or "createConfigured" or "manual" or "cancel");
        }
        Optional(value, "configurationContract", item => ConfigurationContract(item, closed, limits));
        foreach ((string contract, string declaration) in new[]
        {
            ("inputContract", "inputShape"), ("eventContract", "eventShape"), ("resultSummaryContract", "resultSummaryShape")
        })
        {
            Require(Has(value, contract) == Has(value, declaration));
            Optional(value, contract, item => Contract(item, closed, limits));
            Optional(value, declaration, item => ShapeDeclaration(item, closed, limits));
        }
        Optional(value, "form", item => Form(item, closed, limits));
        Optional(value, "executionSupport", item => ExecutionSupport(item, closed, limits));
        var captured = value.Deserialize<ManagementCapabilityDefinition>(Json) ?? throw new DataFailure(ManagementWireFailure.Invalid);
        Require(!closed || captured.IsSupported);
    }

    private static void Participation(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["key", "registrationId", "inventorySequence", "observedAtUtc", "receivedAtUtc", "availability"], "draining", "build");
        CapabilityKey(Field(value, "key"), closed, limits);
        Id(value, "registrationId");
        Integer(value, "inventorySequence");
        Time(value, "observedAtUtc");
        Time(value, "receivedAtUtc");
        Token(value, "availability");
        Optional(value, "draining", _ => Boolean(value, "draining"));
        Optional(value, "build", item => Build(item, closed, limits));
    }

    private static void CapabilityQuery(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, [], "kind", "executionScope", "definitionId", "definitionVersion");
        Optional(value, "kind", _ => Token(value, "kind"));
        Optional(value, "executionScope", _ => Bytes(value, "executionScope", 256));
        Optional(value, "definitionId", _ => Bytes(value, "definitionId", 128));
        Optional(value, "definitionVersion", _ => Integer(value, "definitionVersion"));
    }

    private static void AdministrativeAction(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["operationId", "supported", "allowed"], "reasonCode");
        Bytes(value, "operationId", 128);
        bool supported = Boolean(value, "supported");
        bool allowed = Boolean(value, "allowed");
        Require(!allowed || supported);
        Optional(value, "reasonCode", _ => Bytes(value, "reasonCode", 128));
    }

    private static void ConfigurationActions(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["resourceId", "actions"]);
        Scalar(value, "resourceId", 1024);
        var operations = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement item in Items(value, "actions", 32).EnumerateArray())
        {
            AdministrativeAction(item, closed, limits);
            Require(operations.Add(Text(item, "operationId")));
        }
    }

    private static void Cohort(JsonElement value, bool closed, ManagementSchedulerWireLimits limits)
    {
        Shape(value, closed, ["cohortId", "displayName"]);
        Portable(value, "cohortId");
        Scalar(value, "displayName", 256);
    }
}
