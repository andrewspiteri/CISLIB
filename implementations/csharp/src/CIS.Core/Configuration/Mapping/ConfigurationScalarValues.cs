using System.Globalization;
using System.Text.Json;

namespace CIS.Configuration;

internal static class ConfigurationScalarValues
{
    internal static object Read(Type declaredType, JsonElement json, Type configurationType)
    {
        try { return ReadValue(Nullable.GetUnderlyingType(declaredType) ?? declaredType, json); }
        catch (Exception)
        { throw ConfigurationDiagnostics.Error(configurationType, "Configuration.Mapping", "A known scalar value has an unsupported JSON kind, format or range."); }
    }

    private static object ReadValue(Type type, JsonElement json)
    {
        if (type.IsEnum)
        {
            if (json.ValueKind == JsonValueKind.String)
            {
                string name = json.GetString()!;
                int index = Array.FindIndex(Enum.GetNames(type), item => string.Equals(item, name, StringComparison.Ordinal));
                if (index < 0) { throw Invalid(); }
                return Enum.GetValues(type).GetValue(index)!;
            }
            if (json.ValueKind != JsonValueKind.Number) { throw Invalid(); }
            object number = JsonSerializer.Deserialize(json, Enum.GetUnderlyingType(type)) ?? throw Invalid();
            return Enum.ToObject(type, number);
        }
        if (type == typeof(string))
        {
            if (json.ValueKind != JsonValueKind.String) { throw Invalid(); }
            return json.GetString()!;
        }
        if (type == typeof(char))
        {
            if (json.ValueKind != JsonValueKind.String) { throw Invalid(); }
            string text = json.GetString()!;
            if (text.Length != 1 || char.IsSurrogate(text[0])) { throw Invalid(); }
            return text[0];
        }
        if (type == typeof(bool))
        {
            if (json.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) { throw Invalid(); }
            return json.GetBoolean();
        }
        if (type == typeof(Guid))
        {
            if (json.ValueKind != JsonValueKind.String || !Guid.TryParseExact(json.GetString(), "D", out Guid value)) { throw Invalid(); }
            return value;
        }
        if (type == typeof(TimeSpan))
        {
            if (json.ValueKind != JsonValueKind.String || !TimeSpan.TryParseExact(json.GetString(), "c", CultureInfo.InvariantCulture, out TimeSpan value)) { throw Invalid(); }
            return value;
        }
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
        {
            if (json.ValueKind != JsonValueKind.String) { throw Invalid(); }
            string text = json.GetString()!;
            bool offset = HasOffset(text);
            if (type == typeof(DateTimeOffset))
            {
                if (!offset || !json.TryGetDateTimeOffset(out DateTimeOffset value)) { throw Invalid(); }
                return value.ToUniversalTime();
            }
            if (offset)
            {
                if (!json.TryGetDateTimeOffset(out DateTimeOffset value)) { throw Invalid(); }
                return value.UtcDateTime;
            }
            if (!json.TryGetDateTime(out DateTime wallClock)) { throw Invalid(); }
            return DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        }
        if (json.ValueKind != JsonValueKind.Number) { throw Invalid(); }
        object result = JsonSerializer.Deserialize(json, type) ?? throw Invalid();
        if (result is float single)
        {
            if (!float.IsFinite(single)) { throw Invalid(); }
            return single == 0 ? 0f : single;
        }
        if (result is double numberValue)
        {
            if (!double.IsFinite(numberValue)) { throw Invalid(); }
            return numberValue == 0 ? 0d : numberValue;
        }
        return result;
    }

    private static bool HasOffset(string text)
        => text.EndsWith('Z') || text.LastIndexOf('+') > 9 || text.LastIndexOf('-') > 9;

    private static FormatException Invalid() => new("Unsupported configuration scalar.");
}
