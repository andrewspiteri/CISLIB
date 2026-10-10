using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class StartupContractTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [TestMethod]
    public void CatalogMatchesEveryDocumentedConfigurationKeyAndAlias()
    {
        using JsonDocument catalog = Read("contracts/logging/v1/configuration.json");
        JsonElement[] settings = catalog.RootElement.GetProperty("settings").EnumerateArray().ToArray();
        string guide = File.ReadAllText(Path.Combine(RepositoryRoot, "planning/logging/sources/core-library-implementation-guide-v1.md"));
        string[] documented = Regex.Matches(guide, @"^\| `(CORE_[A-Z_]+)` \|", RegexOptions.Multiline).Select(match => match.Groups[1].Value).Order().ToArray();
        string[] actual = settings.Select(setting => setting.GetProperty("key").GetString()!).Order().ToArray();
        CollectionAssert.AreEqual(documented.Append("CORE_LOG_MAX_ENTRY_BYTES").Order().ToArray(), actual);
        Assert.AreEqual(18, actual.Length);
        string[] aliases = settings.SelectMany(setting => setting.GetProperty("aliases").EnumerateArray()).Select(alias => alias.GetString()!).Order().ToArray();
        CollectionAssert.AreEqual(new[] { "DD_ENV", "DD_SERVICE", "DD_VERSION" }, aliases);
        using JsonDocument contract = Read("implementations/csharp/verification/contracts/startup/surface.json");
        Assert.AreEqual("CIS.EnvironmentOptions", contract.RootElement.GetProperty("facade").GetString());
        foreach (JsonElement setting in settings)
        {
            string[] path = setting.GetProperty("property").GetString()!.Split('.');
            JsonElement properties = contract.RootElement.GetProperty(path.Length == 1 ? "setup" : "loggingSetup");
            Assert.IsTrue(properties.TryGetProperty(path[^1], out _), setting.GetProperty("key").GetString());
        }
    }

    [TestMethod]
    public void EverySettingHasRequiredFixtureCategoriesAndBoundaries()
    {
        using JsonDocument catalog = Read("contracts/logging/v1/configuration.json");
        using JsonDocument fixtures = Read("conformance/logging/configuration-cases.json");
        JsonElement[] cases = fixtures.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        Assert.AreEqual(fixtures.RootElement.GetProperty("caseCount").GetInt32(), cases.Length);
        Assert.AreEqual(cases.Length, cases.Select(item => item.GetProperty("id").GetString()).Distinct().Count());
        foreach (JsonElement setting in catalog.RootElement.GetProperty("settings").EnumerateArray())
        {
            string key = setting.GetProperty("key").GetString()!;
            JsonElement[] matching = cases.Where(item => item.GetProperty("key").GetString() == key).ToArray();
            foreach (string category in new[] { "valid", "absent", "invalid", "precedence" })
            {
                Assert.IsTrue(matching.Any(item => item.GetProperty("category").GetString() == category), $"{key}: {category}");
            }

            string kind = setting.GetProperty("kind").GetString()!;
            if (kind == "integer")
            {
                foreach (string suffix in new[] { "MIN", "MAX", "BELOW", "ABOVE" })
                {
                    JsonElement boundary = matching.Single(item => item.GetProperty("id").GetString()!.EndsWith('-' + suffix, StringComparison.Ordinal));
                    long value = long.Parse(boundary.GetProperty("inputs").GetProperty("environment").GetProperty(key).GetString()!, System.Globalization.CultureInfo.InvariantCulture);
                    long expected = suffix switch
                    {
                        "MIN" => setting.GetProperty("minimum").GetInt64(),
                        "MAX" => setting.GetProperty("maximum").GetInt64(),
                        "BELOW" => setting.GetProperty("minimum").GetInt64() - 1,
                        _ => setting.GetProperty("maximum").GetInt64() + 1
                    };
                    Assert.AreEqual(expected, value, key);
                    Assert.AreEqual(suffix is "MIN" or "MAX" ? "Resolved" : "Invalid", boundary.GetProperty("expected").GetProperty("status").GetString());
                }
            }

            if (kind == "identity")
            {
                JsonElement limit = matching.Single(item => item.GetProperty("id").GetString()!.EndsWith("AT-LIMIT", StringComparison.Ordinal));
                Assert.AreEqual(setting.GetProperty("maxScalars").GetInt32(), limit.GetProperty("inputs").GetProperty("environment").GetProperty(key).GetString()!.EnumerateRunes().Count());
            }
        }

        Assert.AreEqual(9, fixtures.RootElement.GetProperty("crossOptionCases").GetArrayLength());
    }

    [TestMethod]
    public void MetadataAndLifecycleExpectationsPreserveRequiredDistinctions()
    {
        using JsonDocument metadata = Read("conformance/logging/metadata-cases.json");
        JsonElement[] cases = metadata.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        JsonElement release = cases.Single(item => item.GetProperty("id").GetString() == "META-TELEMETRY-RELEASE").GetProperty("expected");
        Assert.AreNotEqual(release.GetProperty("BuildVersion").GetString(), release.GetProperty("ServiceVersion").GetString());
        JsonElement times = cases.Single(item => item.GetProperty("id").GetString() == "META-PROCESS-AND-INITIALIZATION-TIMES").GetProperty("expected");
        Assert.AreNotEqual(times.GetProperty("ProcessStartedAtUtc").GetString(), times.GetProperty("InitializedAtUtc").GetString());
        JsonElement unknown = cases.Single(item => item.GetProperty("id").GetString() == "META-OPTIONAL-UNAVAILABLE").GetProperty("expected");
        Assert.AreEqual(JsonValueKind.Null, unknown.GetProperty("ProcessStartedAtUtc").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, unknown.GetProperty("HostName").ValueKind);
        JsonElement assembly = cases.Single(item => item.GetProperty("id").GetString() == "META-APPLICATION-ASSEMBLY").GetProperty("expected");
        Assert.AreEqual("3.4.5-rc.2+sha.AbC123", assembly.GetProperty("BuildVersion").GetString());
        using JsonDocument lifecycle = Read("conformance/logging/startup-lifecycle-cases.json");
        Assert.AreEqual(6, lifecycle.RootElement.GetProperty("cases").GetArrayLength());
        JsonElement noReset = lifecycle.RootElement.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("id").GetString() == "STARTUP-NO-RESET-OR-SIDE-EFFECTS");
        foreach (JsonProperty rule in noReset.GetProperty("expected").EnumerateObject())
        {
            Assert.IsFalse(rule.Value.GetBoolean(), rule.Name);
        }
    }

    [TestMethod]
    public void CompilerControlsUseAReferenceOnlySurfaceWithImmutableOutputsAndFrozenEnums()
    {
        const string relative = ".artifacts/csharp/verification/Developer/contracts/startup";
        using JsonDocument report = Read(relative + "/summary.json");
        Assert.AreEqual("Passed", report.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(0, report.RootElement.GetProperty("exitCode").GetInt32());
        Assert.AreEqual(21, report.RootElement.GetProperty("controls").GetArrayLength());
        foreach (JsonElement control in report.RootElement.GetProperty("controls").EnumerateArray())
        {
            Assert.IsTrue(control.GetProperty("matched").GetBoolean(), control.GetProperty("id").GetString());
        }

        foreach (JsonElement input in report.RootElement.GetProperty("source").EnumerateArray())
        {
            string path = Path.Combine(RepositoryRoot, input.GetProperty("path").GetString()!);
            Assert.AreEqual(input.GetProperty("sha256").GetString(), Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))), path);
        }

        string reference = Path.Combine(RepositoryRoot, relative, report.RootElement.GetProperty("referenceAssembly").GetProperty("path").GetString()!);
        using var stream = File.OpenRead(reference);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        Assert.IsTrue(reader.GetAssemblyDefinition().GetCustomAttributes().Any(handle => IsReferenceAssemblyAttribute(reader, handle)));
        using JsonDocument specification = Read("implementations/csharp/verification/contracts/startup/surface.json");
        var observedEnums = new HashSet<string>(StringComparer.Ordinal);
        foreach (TypeDefinitionHandle handle in reader.TypeDefinitions)
        {
            TypeDefinition type = reader.GetTypeDefinition(handle);
            string name = reader.GetString(type.Name);
            string fullName = reader.GetString(type.Namespace) + '.' + name;
            if (specification.RootElement.GetProperty("enums").TryGetProperty(fullName, out JsonElement members))
            {
                observedEnums.Add(fullName);
                string[] actualMembers = type.GetFields().Select(reader.GetFieldDefinition).Where(field => !field.GetDefaultValue().IsNil).Select(field => reader.GetString(field.Name)).Order().ToArray();
                CollectionAssert.AreEqual(members.EnumerateObject().Select(member => member.Name).Order().ToArray(), actualMembers, fullName);
                foreach (FieldDefinitionHandle fieldHandle in type.GetFields())
                {
                    FieldDefinition field = reader.GetFieldDefinition(fieldHandle);
                    if (field.GetDefaultValue().IsNil)
                    {
                        continue;
                    }

                    Constant constant = reader.GetConstant(field.GetDefaultValue());
                    Assert.AreEqual(members.GetProperty(reader.GetString(field.Name)).GetInt32(), reader.GetBlobReader(constant.Value).ReadInt32(), fullName);
                }
            }

            if (name is "EnvironmentSnapshot" or "LoggingOptions" or "EventMetadata" or "ConfigurationIssue" or "CoreConfigurationException" or "LogValue" or "LogWriteResult" or "LogOutputWriteResult" or "LogFlushResult" or "LogStopResult" or "LogOutputDrainResult" or "LogOutputRejectionSnapshot" or "CoreLogDiagnostics" or "LogOutputDiagnostics" or "CoreLogInitializationException")
            {
                Assert.IsTrue((type.Attributes & TypeAttributes.Sealed) != 0, fullName);
                foreach (PropertyDefinitionHandle propertyHandle in type.GetProperties())
                {
                    Assert.IsTrue(reader.GetPropertyDefinition(propertyHandle).GetAccessors().Setter.IsNil, fullName);
                }
            }
        }

        CollectionAssert.AreEquivalent(specification.RootElement.GetProperty("enums").EnumerateObject().Select(item => item.Name).ToArray(), observedEnums.ToArray());
    }

    private static bool IsReferenceAssemblyAttribute(MetadataReader reader, CustomAttributeHandle handle)
    {
        CustomAttribute attribute = reader.GetCustomAttribute(handle);
        if (attribute.Constructor.Kind != HandleKind.MemberReference)
        {
            return false;
        }

        MemberReference constructor = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
        if (constructor.Parent.Kind != HandleKind.TypeReference)
        {
            return false;
        }

        TypeReference type = reader.GetTypeReference((TypeReferenceHandle)constructor.Parent);
        return reader.GetString(type.Namespace) == "System.Runtime.CompilerServices" && reader.GetString(type.Name) == "ReferenceAssemblyAttribute";
    }

    private static JsonDocument Read(string path) => JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot, path)));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "implementations", "csharp", "CisLib.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Run the contract checks from the CisLib checkout.");
    }
}
