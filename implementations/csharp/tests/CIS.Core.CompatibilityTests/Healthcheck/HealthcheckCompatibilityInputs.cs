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
    internal const string HistoricalDirectory = "compatibility/csharp/healthcheck/hc00-originals";
    private const string HistoricalMapHash = "5d2451aad8ad296f54662d983b0f6da7e12225481e3af3e106536ade19b6a3db";
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
        string repository = Repository();
        string baselinePath = Path.Combine(repository, "planning/healthcheck/baseline.json");
        Require(Hash(baselinePath) == Hc00Hash, "The selected HC00 receipt changed.");
        Require(Api()["hc00ReceiptSha256"]!.GetValue<string>() == Hc00Hash, "The API fixture selected a different HC00 contract.");
        IReadOnlyDictionary<string, string> historical = HistoricalInputs(repository);
        var observedHistorical = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonNode? item in Read(baselinePath)["frozenInputs"]!["wireAndFixtures"]!.AsArray())
        {
            string relative = item!["path"]!.GetValue<string>();
            string selected = Path.Combine(repository, relative);
            if (historical.TryGetValue(relative, out string? original))
            {
                selected = original;
                observedHistorical.Add(relative);
            }
            string actual = Hash(selected);
            Require(actual == item["sha256"]!.GetValue<string>(), "An original HC00 schema/wire fixture changed: " + relative);
        }
        Require(observedHistorical.SetEquals(historical.Keys), "A historical mapping is not selected by the HC00 receipt.");
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

    // Only the four explicitly amended canonical files use historical mirrors. Missing
    // mirrors, a changed map or a changed byte fail; current files are never a fallback.
    internal static IReadOnlyDictionary<string, string> HistoricalInputs(string repository)
    {
        string directory = Path.Combine(repository, HistoricalDirectory);
        string manifestPath = Path.Combine(directory, "path-map.json");
        Require(File.Exists(manifestPath) && Hash(manifestPath) == HistoricalMapHash, "The explicit HC00 historical path map changed or is missing.");
        JsonObject manifest = Read(manifestPath);
        Require(manifest["schemaVersion"]!.GetValue<int>() == 1 && manifest["hc00ReceiptSha256"]!.GetValue<string>() == Hc00Hash,
            "The historical map selected a different HC00 receipt or schema.");
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["contracts/healthcheck/v1/report.schema.json"] = "report.schema.json",
            ["conformance/healthcheck/common/fixtures.json"] = "fixtures.json",
            ["contracts/healthcheck/v1/README.md"] = "contract.README.md",
            ["conformance/healthcheck/common/README.md"] = "common.README.md"
        };
        var selected = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonNode? item in manifest["files"]!.AsArray())
        {
            string original = item!["originalPath"]!.GetValue<string>();
            string mirror = item["mirror"]!.GetValue<string>();
            Require(expected.TryGetValue(original, out string? filename) && mirror == filename && !selected.ContainsKey(original),
                "The historical map contains an unapproved, redirected or duplicate path.");
            string path = Path.Combine(directory, mirror);
            Require(File.Exists(path) && Hash(path) == item["sha256"]!.GetValue<string>(), "An immutable HC00 historical input changed or is missing: " + original);
            selected.Add(original, path);
        }
        Require(selected.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(expected.Keys), "The historical map omits an approved HC00 original.");
        return selected;
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
