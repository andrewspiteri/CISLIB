using System.Runtime.CompilerServices;
using System.Text.Json;
using CIS.Security;
using CIS.Security.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Security;

[TestClass]
public sealed class PortableSecurityFixtureTests
{
    public static IEnumerable<object[]> GrantCases
    {
        get
        {
            using JsonDocument data = Read("grant-cases.json");
            return data.RootElement.GetProperty("cases").EnumerateArray()
                .Select(row => new object[] { row.GetProperty("id").GetString()! }).ToArray();
        }
    }

    public static IEnumerable<object[]> CallerCases
    {
        get
        {
            using JsonDocument data = Read("caller-cases.json");
            return data.RootElement.GetProperty("cases").EnumerateArray()
                .Select(row => new object[] { row.GetProperty("id").GetString()! }).ToArray();
        }
    }

    [TestMethod]
    [DynamicData(nameof(GrantCases))]
    public async Task PortableCompleteGrantCasesUseTheActualNativeBackedEvaluator(string id)
    {
        using JsonDocument document = Read("grant-cases.json");
        JsonElement data = document.RootElement;
        JsonElement row = data.GetProperty("cases").EnumerateArray().Single(item => item.GetProperty("id").GetString() == id);
        var subject = new SecuritySubject(data.GetProperty("subject").GetProperty("issuer").GetString()!,
            data.GetProperty("subject").GetProperty("identifier").GetString()!);
        var options = new SecurityOptions { PolicyVersion = "portable-v1" };
        options.Profiles.Add(new("fixture", SecurityProfileKind.Native, SecurityProfileRoles.Inbound,
            authenticationScheme: "fixture", identityMappings: [new(subject, "caller-a", "Development")]));
        foreach (JsonElement capability in data.GetProperty("capabilities").EnumerateArray())
        {
            options.Capabilities.Add(capability.GetString()!);
        }
        foreach (JsonElement grant in data.GetProperty("grants").EnumerateArray())
        {
            options.Grants.Add(new("fixture", grant.GetProperty("capability").GetString()!,
                grant.GetProperty("application").GetString()!, grant.GetProperty("environment").GetString()!,
                subject: subject, resourceSelection: Enum.Parse<CapabilityResourceSelection>(grant.GetProperty("selection").GetString()!),
                resourceType: Optional(grant, "resourceType"), resourceIdentifier: Optional(grant, "resourceIdentifier")));
        }
        await using ServiceProvider root = SecurityTestSupport.Services(options).BuildServiceProvider();
        var actualSubject = new SecuritySubject(Optional(row, "issuer") ?? subject.Issuer, subject.Identifier);
        await using SecurityHostContextScope scope = await SecurityHostContextScope.CreateAsync(root,
            "fixture", SecurityTestSupport.NativeAuthentication(actualSubject));
        var target = new CapabilityTarget(row.GetProperty("application").GetString()!, row.GetProperty("environment").GetString()!,
            Optional(row, "resourceType"), Optional(row, "resourceIdentifier"));

        AuthorizationDecision result = await scope.Services.GetRequiredService<ICapabilityAuthorizer>().AuthorizeAsync(
            row.GetProperty("requires").EnumerateArray().Select(item => item.GetString()!).ToArray(), target);

        Assert.AreEqual(row.GetProperty("allow").GetBoolean(), result.IsAllowed, id);
    }

    [TestMethod]
    [DynamicData(nameof(CallerCases))]
    public void PortableInstanceCasesRetainTheirActualProvenance(string id)
    {
        using JsonDocument document = Read("caller-cases.json");
        JsonElement row = document.RootElement.GetProperty("cases").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == id);

        var evidence = new CallerInstanceEvidence(Optional(row, "reportedIdentifier"), Optional(row, "verifiedIdentifier"));

        Assert.AreEqual(row.GetProperty("kind").GetString(), evidence.Kind.ToString());
        Assert.AreEqual(Optional(row, "reportedIdentifier"), evidence.ReportedIdentifier);
        Assert.AreEqual(Optional(row, "verifiedIdentifier"), evidence.VerifiedIdentifier);
        Assert.IsFalse(document.RootElement.GetProperty("serializationEstablishesAuthority").GetBoolean());
    }

    [TestMethod]
    public void PortableFailureCatalogCoversEveryCategoryAndRetainsActualProtocolFacts()
    {
        using JsonDocument document = Read("result-categories.json");
        JsonElement[] categories = document.RootElement.GetProperty("categories").EnumerateArray().ToArray();
        Assert.AreEqual(Enum.GetValues<SecurityFailureKind>().Length, categories.Length);
        foreach (JsonElement row in categories)
        {
            var failure = new SecurityFailure(Enum.Parse<SecurityFailureKind>(row.GetProperty("kind").GetString()!), "Safe known reason.");
            Assert.AreEqual(row.GetProperty("code").GetString(), failure.Code);
        }
        foreach (JsonElement row in document.RootElement.GetProperty("protocolObservations").EnumerateArray())
        {
            SecurityFailureKind kind = Enum.Parse<SecurityFailureKind>(row.GetProperty("knownKind").GetString()!);
            JsonElement status = row.GetProperty("httpStatusCode");
            int? expectedStatus = status.ValueKind == JsonValueKind.Null ? null : status.GetInt32();
            var failure = new SecurityFailure(kind, "Only the stated evidence is known.",
                kind == SecurityFailureKind.CauseUnknown ? SecurityFailureSource.Unknown : SecurityFailureSource.Local,
                expectedStatus);
            Assert.AreEqual(expectedStatus, failure.HttpStatusCode);
            Assert.AreEqual(kind, failure.Kind);
        }
    }

    private static string? Optional(JsonElement value, string name) => value.TryGetProperty(name, out JsonElement property)
        && property.ValueKind != JsonValueKind.Null ? property.GetString() : null;

    private static JsonDocument Read(string name, [CallerFilePath] string source = "")
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../../../../../"));
        return JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "contracts", "security", "v1", name)));
    }
}
