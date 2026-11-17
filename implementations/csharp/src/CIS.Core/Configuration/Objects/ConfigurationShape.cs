using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CIS.Configuration;

internal enum ConfigurationValueKind { Scalar, Object, Sequence, Dictionary }

internal sealed class ConfigurationProperty(PropertyInfo property, string name, ConfigurationShape shape, NullabilityInfo nullability)
{
    internal PropertyInfo Property { get; } = property;
    internal string Name { get; } = name;
    internal ConfigurationShape Shape { get; } = shape;
    internal NullabilityInfo Nullability { get; } = nullability;
    internal bool AllowsNull { get; } = ConfigurationShape.AllowsNullValue(property.PropertyType, nullability.WriteState);
}

internal sealed class ConfigurationShape
{
    private readonly Func<object>? factory;
    private ConfigurationShape(Type type, ConfigurationValueKind kind, Func<object>? factory = null)
    { Type = type; Kind = kind; this.factory = factory; }

    internal Type Type { get; }
    internal ConfigurationValueKind Kind { get; }
    internal bool AllowsNull => !Type.IsValueType || Nullable.GetUnderlyingType(Type) is not null;
    internal IReadOnlyList<ConfigurationProperty> Properties { get; private set; } = Array.Empty<ConfigurationProperty>();
    internal ConfigurationShape? ElementShape { get; private set; }
    internal JsonElement DeclaredDefaults { get; private set; }

    internal object CreateInstance()
    {
        if (factory is null)
        { throw ConfigurationDiagnostics.Error(Type, "Configuration.Construction", "Only passive object shapes have an object construction path."); }
        try { return factory(); }
        catch (Exception)
        { throw ConfigurationDiagnostics.Error(Type, "Configuration.Construction", "Constructing a detached passive object failed."); }
    }

    internal static ConfigurationShape Create(Type type, Func<IConfigurationObject> factory, IConfigurationObject instance)
    {
        try { return new Builder().Build(type, () => factory(), instance); }
        catch (CoreConfigurationException) { throw; }
        catch (Exception)
        { throw Invalid(type, "Inspect the passive property shape; unsupported reflection or serialization metadata prevents mapping."); }
    }

    internal static bool AllowsNullValue(Type type, NullabilityState state)
        => Nullable.GetUnderlyingType(type) is not null || !type.IsValueType && state != NullabilityState.NotNull;

    private sealed class Builder
    {
        private readonly Dictionary<Type, ConfigurationShape> complete = [];
        private readonly HashSet<Type> active = [];
        private readonly NullabilityInfoContext nullability = new();

        internal ConfigurationShape Build(Type type, Func<object>? rootFactory = null, object? root = null)
        {
            if (active.Contains(type)) { throw Invalid(type, "Recursive configuration data shapes are unsupported."); }
            if (complete.TryGetValue(type, out ConfigurationShape? previous)) { return previous; }
            if (HasSerializationOverride(type)) { throw Invalid(type, "Custom converters and polymorphic configuration data are unsupported."); }
            Type scalar = Nullable.GetUnderlyingType(type) ?? type;
            if (scalar != type && HasSerializationOverride(scalar))
            { throw Invalid(type, "Custom converters and polymorphic nullable values are unsupported."); }
            if (IsScalar(scalar))
            {
                var shape = new ConfigurationShape(type, ConfigurationValueKind.Scalar);
                complete.Add(type, shape);
                return shape;
            }
            active.Add(type);
            ConfigurationShape result;
            try
            {
                if (type.IsArray)
                {
                    if (type.GetArrayRank() != 1) { throw Invalid(type, "Only single-dimensional configuration arrays are supported."); }
                    result = new(type, ConfigurationValueKind.Sequence) { ElementShape = Build(type.GetElementType()!) };
                }
                else if (TryCollection(type, out ConfigurationValueKind kind, out Type? element))
                { result = new(type, kind) { ElementShape = Build(element!) }; }
                else
                {
                    if (!type.IsClass || type.IsAbstract || type.ContainsGenericParameters || type == typeof(object)
                        || typeof(Delegate).IsAssignableFrom(type) || typeof(IEnumerable).IsAssignableFrom(type))
                    { throw Invalid(type, "Use supported scalar values, passive classes, arrays/lists or string-keyed dictionaries."); }
                    Func<object> construct = rootFactory ?? Constructor(type);
                    result = new(type, ConfigurationValueKind.Object, construct);
                    var names = new HashSet<string>(StringComparer.Ordinal);
                    var properties = new List<ConfigurationProperty>();
                    foreach (PropertyInfo property in PublicProperties(type))
                    {
                        if (property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition == JsonIgnoreCondition.Always) { continue; }
                        MethodInfo? setter = property.SetMethod;
                        if (property.GetIndexParameters().Length != 0 || property.GetMethod?.IsPublic != true
                            || setter is null || !setter.IsPublic
                            || setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)))
                        { throw Invalid(type, "Mapped properties must have public non-init read/write accessors and cannot be indexers."); }
                        if (HasSerializationOverride(property) || property.IsDefined(typeof(JsonExtensionDataAttribute), inherit: true))
                        { throw Invalid(type, "Property converters, polymorphic values and extension-data mapping are unsupported."); }
                        string name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name;
                        if (string.IsNullOrEmpty(name) || !names.Add(name))
                        { throw Invalid(type, "Mapped property names must be nonempty and unique under ordinal comparison."); }
                        properties.Add(new(property, name, Build(property.PropertyType), nullability.Create(property)));
                    }
                    result.Properties = Array.AsReadOnly(properties.OrderBy(property => property.Name, StringComparer.Ordinal).ToArray());
                    object defaults;
                    try { defaults = root ?? construct(); }
                    catch (Exception)
                    { throw ConfigurationDiagnostics.Error(type, "Configuration.Construction", "Constructing declared child defaults failed; constructors must be passive."); }
                    result.DeclaredDefaults = ConfigurationDefaults.Capture(result, defaults);
                }
                complete.Add(type, result);
            }
            finally { active.Remove(type); }
            return result;
        }

        private static Func<object> Constructor(Type type)
        {
            ConstructorInfo? constructor = type.GetConstructor(System.Type.EmptyTypes);
            if (constructor is null)
            { throw ConfigurationDiagnostics.Error(type, "Configuration.Construction", "A nested passive class requires a public parameterless constructor."); }
            return () => constructor.Invoke(null);
        }

        private static IEnumerable<PropertyInfo> PublicProperties(Type type)
        {
            var overrides = new HashSet<MethodInfo>();
            for (Type? current = type; current is not null; current = current.BaseType)
            {
                foreach (PropertyInfo property in current.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    MethodInfo? accessor = property.GetMethod ?? property.SetMethod;
                    if (accessor?.IsVirtual == true && !overrides.Add(accessor.GetBaseDefinition())) { continue; }
                    yield return property;
                }
            }
        }

        private static bool TryCollection(Type type, out ConfigurationValueKind kind, out Type? element)
        {
            kind = ConfigurationValueKind.Sequence;
            element = null;
            if (!type.IsGenericType) { return false; }
            Type definition = type.GetGenericTypeDefinition();
            Type[] arguments = type.GetGenericArguments();
            if (definition == typeof(Dictionary<,>) || definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>))
            {
                if (arguments[0] != typeof(string)) { throw Invalid(type, "Configuration dictionaries require string keys."); }
                kind = ConfigurationValueKind.Dictionary;
                element = arguments[1];
                return true;
            }
            if (definition == typeof(List<>) || definition == typeof(IList<>) || definition == typeof(IReadOnlyList<>)
                || definition == typeof(ICollection<>) || definition == typeof(IReadOnlyCollection<>) || definition == typeof(IEnumerable<>))
            { element = arguments[0]; return true; }
            return false;
        }
    }

    private static bool HasSerializationOverride(MemberInfo member)
        => member.IsDefined(typeof(JsonConverterAttribute), inherit: true)
            || member.IsDefined(typeof(JsonPolymorphicAttribute), inherit: true)
            || member.IsDefined(typeof(JsonDerivedTypeAttribute), inherit: true);

    private static bool IsScalar(Type type)
        => type.IsEnum || type == typeof(bool) || type == typeof(byte) || type == typeof(sbyte)
            || type == typeof(short) || type == typeof(ushort) || type == typeof(int) || type == typeof(uint)
            || type == typeof(long) || type == typeof(ulong) || type == typeof(float) || type == typeof(double)
            || type == typeof(decimal) || type == typeof(string) || type == typeof(char) || type == typeof(Guid)
            || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(TimeSpan);

    private static CoreConfigurationException Invalid(Type type, string explanation)
        => ConfigurationDiagnostics.Error(type, "Configuration.Shape", explanation);
}

internal static class ConfigurationDefaults
{
    internal static JsonElement Capture(ConfigurationShape shape, object? value)
    {
        try
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream)) { Write(writer, shape, value); }
            using JsonDocument document = JsonDocument.Parse(stream.ToArray());
            return document.RootElement.Clone();
        }
        catch (Exception)
        { throw ConfigurationDiagnostics.Error(shape.Type, "Configuration.Shape", "Capturing mapped defaults failed; properties and collections must be passive supported data."); }
    }

    private static void Write(Utf8JsonWriter writer, ConfigurationShape shape, object? value)
    {
        if (value is null) { writer.WriteNullValue(); return; }
        switch (shape.Kind)
        {
            case ConfigurationValueKind.Scalar:
                JsonSerializer.Serialize(writer, value, shape.Type);
                return;
            case ConfigurationValueKind.Object:
                if (value.GetType() != shape.Type)
                { throw ConfigurationDiagnostics.Error(shape.Type, "Configuration.Shape", "Polymorphic default objects are unsupported."); }
                writer.WriteStartObject();
                foreach (ConfigurationProperty property in shape.Properties)
                {
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Shape, property.Property.GetValue(value));
                }
                writer.WriteEndObject();
                return;
            case ConfigurationValueKind.Sequence:
                Type actual = value.GetType();
                if (!(actual.IsArray && actual.GetArrayRank() == 1)
                    && !(actual.IsGenericType && actual.GetGenericTypeDefinition() == typeof(List<>)))
                { throw ConfigurationDiagnostics.Error(shape.Type, "Configuration.Shape", "Sequence defaults must use a one-dimensional array or List."); }
                writer.WriteStartArray();
                foreach (object? item in (IEnumerable)value) { Write(writer, shape.ElementShape!, item); }
                writer.WriteEndArray();
                return;
            case ConfigurationValueKind.Dictionary:
                Type dictionary = value.GetType();
                if (!dictionary.IsGenericType || dictionary.GetGenericTypeDefinition() != typeof(Dictionary<,>))
                { throw ConfigurationDiagnostics.Error(shape.Type, "Configuration.Shape", "Dictionary defaults must use Dictionary with ordinary ordinal string-key behavior."); }
                object? comparer = dictionary.GetProperty("Comparer")!.GetValue(value);
                if (!ReferenceEquals(comparer, EqualityComparer<string>.Default) && !ReferenceEquals(comparer, StringComparer.Ordinal))
                { throw ConfigurationDiagnostics.Error(shape.Type, "Configuration.Shape", "Custom or case-insensitive dictionary comparers are unsupported; use the default or StringComparer.Ordinal."); }
                var pairs = new SortedDictionary<string, object?>(StringComparer.Ordinal);
                foreach (object item in (IEnumerable)value)
                {
                    Type pair = item.GetType();
                    string key = (string)pair.GetProperty("Key")!.GetValue(item)!;
                    pairs.Add(key, pair.GetProperty("Value")!.GetValue(item));
                }
                writer.WriteStartObject();
                foreach ((string key, object? item) in pairs)
                { writer.WritePropertyName(key); Write(writer, shape.ElementShape!, item); }
                writer.WriteEndObject();
                return;
            default: throw new InvalidOperationException("Unknown configuration shape.");
        }
    }
}
