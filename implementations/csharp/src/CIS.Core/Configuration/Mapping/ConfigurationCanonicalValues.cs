using System.Globalization;
using System.Text.Json;

namespace CIS.Configuration;

internal static class ConfigurationCanonicalValues
{
    internal static byte[] Write(ConfigurationMappedValue value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) { Write(writer, value); }
        return stream.ToArray();
    }

    private static void Write(Utf8JsonWriter writer, ConfigurationMappedValue value)
    {
        writer.WriteStartArray();
        if (value.IsNull) { writer.WriteStringValue("null"); }
        else
        {
            switch (value.Shape.Kind)
            {
                case ConfigurationValueKind.Object:
                case ConfigurationValueKind.Dictionary:
                    writer.WriteStringValue(value.Shape.Kind == ConfigurationValueKind.Object ? "object" : "dictionary");
                    writer.WriteStartObject();
                    foreach ((string name, ConfigurationMappedValue member) in value.Members!.OrderBy(item => item.Key, StringComparer.Ordinal))
                    { writer.WritePropertyName(name); Write(writer, member); }
                    writer.WriteEndObject();
                    break;
                case ConfigurationValueKind.Sequence:
                    writer.WriteStringValue("sequence");
                    writer.WriteStartArray();
                    foreach (ConfigurationMappedValue item in value.Items!) { Write(writer, item); }
                    writer.WriteEndArray();
                    break;
                case ConfigurationValueKind.Scalar:
                    (string tag, string text) = Scalar(value.Shape.Type, value.Scalar!);
                    writer.WriteStringValue(tag);
                    writer.WriteStringValue(text);
                    break;
                default: throw new InvalidOperationException("Unsupported canonical configuration shape.");
            }
        }
        writer.WriteEndArray();
    }

    private static (string Tag, string Text) Scalar(Type declaredType, object value)
    {
        Type type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;
        if (type.IsEnum)
        {
            Type underlying = Enum.GetUnderlyingType(type);
            (string tag, string text) = Scalar(underlying, Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture)!);
            return ("enum:" + tag, text);
        }
        if (value is DateTime date)
        { return (date.Kind == DateTimeKind.Unspecified ? "datetime:unspecified" : "datetime:utc", date.Ticks.ToString(CultureInfo.InvariantCulture)); }
        if (value is DateTimeOffset offset)
        { return ("datetimeoffset:utc", offset.UtcTicks.ToString(CultureInfo.InvariantCulture)); }
        if (value is TimeSpan duration) { return ("timespan", duration.Ticks.ToString(CultureInfo.InvariantCulture)); }
        if (value is Guid guid) { return ("guid", guid.ToString("D", CultureInfo.InvariantCulture)); }
        if (value is string textValue) { return ("string", textValue); }
        if (value is char character) { return ("char", character.ToString()); }
        if (value is bool boolean) { return ("bool", boolean ? "true" : "false"); }
        if (value is decimal decimalValue) { return ("decimal", decimalValue == 0 ? "0" : decimalValue.ToString("G29", CultureInfo.InvariantCulture)); }
        if (value is float single) { return ("f32", single == 0 ? "0" : single.ToString("R", CultureInfo.InvariantCulture)); }
        if (value is double number) { return ("f64", number == 0 ? "0" : number.ToString("R", CultureInfo.InvariantCulture)); }
        string tagValue = Type.GetTypeCode(type) switch
        {
            TypeCode.SByte => "i8", TypeCode.Byte => "u8", TypeCode.Int16 => "i16", TypeCode.UInt16 => "u16",
            TypeCode.Int32 => "i32", TypeCode.UInt32 => "u32", TypeCode.Int64 => "i64", TypeCode.UInt64 => "u64",
            _ => throw new InvalidOperationException("Unsupported canonical scalar.")
        };
        return (tagValue, ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture)!);
    }
}
