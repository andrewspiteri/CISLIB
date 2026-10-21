using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using CIS.Healthcheck;

namespace CIS.Tests.CompatibilityTests.Healthcheck;

internal static class HealthcheckCompatibilityInputs
{
    internal const string Hc00Hash = "a4e3daaddc0a6cf5d242f2e1acd2f7148644e7b8dd3b65646f7c364a7df9c151";
    internal const string FixtureHash = "56a2baf3c69744e7f1e79fae21480647583dc6033c9000bf5f99afb4d6336b00";
    internal static readonly Assembly Core = typeof(HealthcheckService).Assembly;
    private static readonly NullabilityInfoContext Nullability = new();
    internal static string Repository()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")) &&
                File.Exists(Path.Combine(directory.FullName, "planning/healthcheck/baseline.json"))) { return directory.FullName; }
        }
        throw new DirectoryNotFoundException("Locate this native project inside the CisLib checkout.");
    }

    internal static string Fixture(string name) => Path.Combine(Repository(), "compatibility/csharp/healthcheck/initial-candidate", name);
    internal static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
    internal static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    internal static JsonObject Api() => Read(Fixture("api.json"));
    internal static JsonObject Configuration() => Read(Fixture("configuration.json"));
    internal static byte[] Wire(string directory, string name) => File.ReadAllBytes(Path.Combine(Repository(), "conformance/healthcheck/common", directory, name));
    internal static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidDataException(message); }
    }

    internal static void FrozenInputs()
    {
        string baselinePath = Path.Combine(Repository(), "planning/healthcheck/baseline.json");
        Require(Hash(baselinePath) == Hc00Hash, "The selected HC00 receipt changed.");
        Require(Api()["hc00ReceiptSha256"]!.GetValue<string>() == Hc00Hash, "The API fixture selected a different HC00 contract.");
        foreach (JsonNode? item in Read(baselinePath)["frozenInputs"]!["wireAndFixtures"]!.AsArray())
        {
            string relative = item!["path"]!.GetValue<string>();
            string actual = Hash(Path.Combine(Repository(), relative));
            Require(actual == item["sha256"]!.GetValue<string>(), "An original HC00 schema/wire fixture changed: " + relative);
        }
        Require(Hash(Fixture("baseline-hashes.json")) == FixtureHash, "The initial-candidate fixture manifest changed.");
        JsonArray entries = Read(Fixture("baseline-hashes.json"))["files"]!.AsArray();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonNode? item in entries)
        {
            string name = item!["path"]!.GetValue<string>();
            Require(name == Path.GetFileName(name) && names.Add(name), "Invalid or duplicate local fixture path.");
            Require(Hash(Fixture(name)) == item["sha256"]!.GetValue<string>(), "A selected initial-candidate fixture changed: " + name);
        }
        Require(names.SetEquals(["api.json", "configuration.json"]), "The fixture inventory differs.");
    }

    // A backward-compatible addition is allowed. Every selected member must retain its
    // CLR type, nullability, required/init modifier, parameter name and optional default.
    internal static void RequireMembers(IEnumerable<string> required, IEnumerable<string> observed)
    {
        var actual = observed.ToHashSet(StringComparer.Ordinal);
        string[] missing = required.Where(member => !actual.Contains(member)).ToArray();
        Require(missing.Length == 0, "Missing or changed contract members: " + string.Join("; ", missing));
    }

    internal static string Kind(Type type) => type.IsEnum ? "enum" : type.IsInterface ? "interface" :
        type.IsAbstract && type.IsSealed ? "static" : type.IsSealed ? "sealed" : type.IsAbstract ? "abstract" : "class";

    internal static IEnumerable<string> Members(Type type)
    {
        if (type.IsEnum)
        {
            Require(Enum.GetUnderlyingType(type) == typeof(int), "Enum underlying type changed: " + type.FullName);
            return type.GetFields(BindingFlags.Public | BindingFlags.Static).Select(field => field.Name + "=" + Convert.ToString(field.GetRawConstantValue(), CultureInfo.InvariantCulture));
        }
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        IEnumerable<string> constructors = type.GetConstructors(flags).Select(constructor => "C (" + Parameters(constructor) + ")");
        IEnumerable<string> properties = type.GetProperties(flags).Select(property =>
        {
            string access = property.GetMethod?.IsPublic == true ? " get" : "";
            if (property.SetMethod?.IsPublic == true)
            {
                access += property.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)) ? " init" : " set";
            }
            if (property.IsDefined(typeof(RequiredMemberAttribute))) { access += " required"; }
            return "P " + TypeName(property.PropertyType, Nullability.Create(property)) + " " + property.Name + access;
        });
        IEnumerable<string> methods = type.GetMethods(flags).Where(method => !method.IsSpecialName).Select(method =>
            "M " + (method.IsStatic ? "static " : "") + TypeName(method.ReturnType, Nullability.Create(method.ReturnParameter)) +
            " " + method.Name + "(" + Parameters(method) + ")");
        return constructors.Concat(properties).Concat(methods);
    }

    private static string Parameters(MethodBase method) => string.Join(",", method.GetParameters().Select(parameter =>
    {
        string modifier = parameter.IsOut ? "out " : parameter.ParameterType.IsByRef ? "ref " : "";
        string value = parameter.HasDefaultValue ? "=" + Default(parameter) : "";
        return modifier + TypeName(parameter.ParameterType, Nullability.Create(parameter)) + " " + parameter.Name + value;
    }));

    private static string Default(ParameterInfo parameter) => parameter.ParameterType == typeof(CancellationToken) ? "default" :
        parameter.DefaultValue switch
        {
            null => "null",
            bool boolean => boolean ? "true" : "false",
            string text => System.Text.Json.JsonSerializer.Serialize(text),
            IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
            _ => throw new InvalidDataException("Unsupported optional argument: " + parameter.Name)
        };

    internal static string TypeName(Type type, NullabilityInfo? nullable = null)
    {
        if (type.IsByRef) { return TypeName(type.GetElementType()!, nullable); }
        if (Nullable.GetUnderlyingType(type) is Type underlying) { return TypeName(underlying) + "?"; }
        bool commonSystemType = type.Namespace is "System" or "System.Threading" or "System.Threading.Tasks" or "System.Collections.Generic" or "System.Numerics";
        string name = type == typeof(void) ? "void" : type == typeof(bool) ? "bool" : type == typeof(byte) ? "byte" :
            type == typeof(int) ? "int" : type == typeof(long) ? "long" : type == typeof(double) ? "double" : type == typeof(string) ? "string" :
            type.Assembly == Core && type.Namespace == "CIS.Healthcheck" || commonSystemType ? type.Name.Split('`')[0] : type.FullName!;
        if (type.IsGenericType)
        {
            Type[] arguments = type.GetGenericArguments();
            name += "<" + string.Join(",", arguments.Select((argument, index) =>
                TypeName(argument, nullable?.GenericTypeArguments.ElementAtOrDefault(index)))) + ">";
        }
        if (!type.IsValueType && nullable?.ReadState == NullabilityState.Nullable) { name += "?"; }
        return name;
    }

    internal static object InvokeProtocol(string name, params object?[] arguments)
    {
        Type protocol = Core.GetType("CIS.Healthcheck.Reporting.HealthReportProtocol", throwOnError: true)!;
        MethodInfo method = protocol.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;
        try { return method.Invoke(null, arguments)!; }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    internal static T Property<T>(object value, string name) => (T)value.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value)!;
    internal static byte[] Envelope(byte[] snapshot, string view) => Encoding.UTF8.GetBytes(
        "{\"protocolVersion\":1,\"schemaVersion\":1,\"view\":\"" + view + "\",\"kind\":\"snapshot\",\"snapshot\":" + Encoding.UTF8.GetString(snapshot) + "}");

    // Only property ordering is insignificant. Number spelling/kind, list order,
    // omission, string precision and scalar types are compatibility observations.
    internal static bool Same(JsonNode? left, JsonNode? right)
    {
        if (left is null || right is null) { return left is null && right is null; }
        if (left.GetValueKind() != right.GetValueKind()) { return false; }
        if (left is JsonObject a && right is JsonObject b)
        { return a.Count == b.Count && a.All(pair => b.ContainsKey(pair.Key) && Same(pair.Value, b[pair.Key])); }
        if (left is JsonArray x && right is JsonArray y)
        { return x.Count == y.Count && x.Zip(y).All(pair => Same(pair.First, pair.Second)); }
        return left.ToJsonString() == right.ToJsonString();
    }
}
