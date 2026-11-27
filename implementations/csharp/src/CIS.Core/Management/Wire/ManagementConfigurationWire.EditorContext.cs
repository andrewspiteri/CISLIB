using System.Text.Json;

namespace CIS.Management.Wire;

public static partial class ManagementConfigurationWire
{
    private static void EditorContext(JsonElement value, ManagementConfigurationWireLimits limits)
    {
        Shape(value, false, ["bindingId", "applicationId", "application", "environment", "configurationId", "schemaVersion",
            "hostApplicationName", "shape", "selectedSource", "commonSource", "applicationSource", "defaults"], "description");
        Id(value, "bindingId");
        ApplicationDefinition(value);
        Scalar(value, "application", 255);
        Scalar(value, "environment", 255);
        Scalar(value, "hostApplicationName", 255);
        Require(Text(value, "hostApplicationName") != "CommonConfiguration");
        Declaration(value.GetProperty("shape"), 1, limits);
        OptionalScalar(value, "description", 2048, true);
        string selected = Text(value, "selectedSource");
        Require(selected is "common" or "application");
        JsonElement common = value.GetProperty("commonSource");
        JsonElement application = value.GetProperty("applicationSource");
        EditorSource(common);
        EditorSource(application);
        Require(!common.TryGetProperty("sourceId", out JsonElement commonId)
            || !application.TryGetProperty("sourceId", out JsonElement applicationId) || Text(commonId) != Text(applicationId));
        JsonElement defaults = value.GetProperty("defaults");
        Shape(defaults, false, ["state"], "defaultsJson");
        string state = Text(defaults, "state");
        Require(state is "available" or "redacted" or "unavailable");
        if (state == "available")
        {
            Require(Text(selected == "common" ? common : application, "state") == "available");
            _ = SourceBytes(Text(defaults, "defaultsJson"), limits);
        }
        else
        {
            Require(!defaults.TryGetProperty("defaultsJson", out _));
        }
        string sourceState = Text(selected == "common" ? common : application, "state");
        Require(sourceState != "redacted" || state == "redacted");
        Require(sourceState is not ("absent" or "unavailable") || state == "unavailable");
    }

    private static void EditorSource(JsonElement value)
    {
        Shape(value, false, ["state"], "sourceId");
        string state = Text(value, "state");
        Require(state is "available" or "absent" or "redacted" or "unavailable");
        if (state == "available")
        {
            Id(value, "sourceId");
        }
        else if (state == "unavailable")
        {
            Optional(value, "sourceId", item => Require(ManagementWire.IsUuid(Text(item))));
        }
        else
        {
            Require(!value.TryGetProperty("sourceId", out _));
        }
    }
}

