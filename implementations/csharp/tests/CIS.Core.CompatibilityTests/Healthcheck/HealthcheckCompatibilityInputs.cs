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
    internal const string FixtureHash = "56a2baf3c69744e7f1e79fae21480647583dc6033c9000bf5f99afb4d6336b00";
    // These existing payload bytes preserve the original schema and wire examples.
    // The retired HC00 campaign receipt is not needed to execute compatibility checks.
    private static readonly IReadOnlyDictionary<string, string> WireHashes = new Dictionary<string, string>
    {
        ["compatibility/csharp/healthcheck/hc00-originals/fixtures.json"] = "2959e1ae30c50e8be0aa3e0d02af27bf71576383c22bf4b313cd955f797c055c",
        ["conformance/healthcheck/common/invalid/aggregate-contradiction.json"] = "5b65e7ed011107efb161275dfe06f66b7dfd491d384b935dfe74e778db486a6a",
        ["conformance/healthcheck/common/invalid/duplicate-properties-request.txt"] = "d8c766f4df4932a1720f1a51f88b60540366ad213b4bc9c384b904d267dcafbf",
        ["conformance/healthcheck/common/invalid/noncanonical-revision.json"] = "ea41f6746770baf3ed5083e011db5a56d3b862d3794392f8eb3a4b45c65063dc",
        ["conformance/healthcheck/common/invalid/ready-contradiction.json"] = "df8357a5409b6b06380856cd26c83b3cd3d7546da9851b54ce8609524e5b6b50",
        ["conformance/healthcheck/common/invalid/truncated-request.txt"] = "6960086e8ff61bab7f3d41e23b83c4ab3b1abfd5fc3b6f77ff4e42843e5b3b4d",
        ["conformance/healthcheck/common/invalid/unsupported-version-request.json"] = "90d70212366e6cb0de47c33ce832794911595324ab6d3c4dfe72500ce34dbc86",
        ["conformance/healthcheck/common/invalid/view-mismatch-response.json"] = "3b67025375f34200bdc3aed331ed38dda88b5f417bddbfc909f90cdfabe521b9",
        ["conformance/healthcheck/common/valid/healthy-after-startup-details.json"] = "a20cea7c6a83ca3c05cbb40cd58b2ad33aee09df313b0cc9e02e44d0da60309b",
        ["conformance/healthcheck/common/valid/pending-health.json"] = "97d6c909bc2c08efb12304ec0fc6bd1dc828935c6830bb9e290626f9e0578ad2",
        ["conformance/healthcheck/common/valid/pending-readiness.json"] = "97d6c909bc2c08efb12304ec0fc6bd1dc828935c6830bb9e290626f9e0578ad2",
        ["conformance/healthcheck/common/valid/pipe-additive-request.json"] = "602a7e5fae5536b906875df72ea7d0e894d0f4dc67526fe2a0c35164dfd66744",
        ["conformance/healthcheck/common/valid/pipe-invalid-request-response.json"] = "8be1d9c0ee991b27926fee509bcf625f66155a1dc282e9dcf10205091b494bad",
        ["conformance/healthcheck/common/valid/pipe-readiness-request.json"] = "0477dd318eba2367283128bcde3fd5c00146080e1f004c274a84bf901ba6fda5",
        ["conformance/healthcheck/common/valid/pipe-readiness-response.json"] = "e460b6af55d00991126eeed301d9c2941f9c8609e70151ce1cfe6b12e442d592",
        ["conformance/healthcheck/common/valid/report-too-large.json"] = "77398146d71f70126a26ff46ea626795652f3da02a9942471ee83a2356f003d9",
        ["conformance/healthcheck/common/valid/unhealthy-health.json"] = "fda75b64a2eefdae76fe23ed2d0cfa774226d47adff56f46a5bc4f29b26d3abc",
        ["contracts/healthcheck/v1/configuration.json"] = "239907b0645d7373a5f11448781c9b461c9a8adc552e2032617faa8e6886d920",
        ["contracts/healthcheck/v1/pipe-request.schema.json"] = "6b1265e1993605218d3407939c4af9a98fa9e79dcfb88226e6eecd28ae1751ba",
        ["contracts/healthcheck/v1/pipe-response.schema.json"] = "19ebdd039501334f3b2725edd4fb2bd2d56d217c1dd5d70eb7194bcce2c123f4",
        ["compatibility/csharp/healthcheck/hc00-originals/report.schema.json"] = "6bcf89d2e0cf9e0abd7b1c888787562a14c61c817d17a3c5ef4475e168562375",
    };

    internal static readonly Assembly Core = typeof(HealthcheckService).Assembly;
    private static readonly NullabilityInfoContext Nullability = new();
    internal static string Repository()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")) &&
                File.Exists(Path.Combine(directory.FullName, "implementations/csharp/CisLib.slnx"))) { return directory.FullName; }
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
        foreach ((string relative, string expected) in WireHashes)
        {
            Require(Hash(Path.Combine(repository, relative)) == expected, "An original Healthcheck schema/wire fixture changed: " + relative);
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
