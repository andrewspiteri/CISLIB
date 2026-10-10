using System.Text.Json.Nodes;

namespace CIS.Tests.ArchitectureTests.Publications;

// Format controls only: these payloads are not published packages or executable consumers.
internal static class SyntheticPublication
{
    internal static JsonObject Build(int sequence, string version, JsonObject artifact)
    {
        JsonNode Copy() => artifact.DeepClone();
        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["recordKind"] = "actual-package-publication",
            ["registryId"] = "cislib.csharp.publications",
            ["sequence"] = sequence,
            ["identityId"] = "cis.core.csharp",
            ["packageId"] = "CIS.Core",
            ["packageVersion"] = version,
            ["exposedAtUtc"] = "2026-10-10T20:00:00.0000000Z",
            ["aggregateReleaseId"] = "synthetic-format-control",
            ["aggregatePromotionStateAtExposure"] = "complete",
            ["publicationSource"] = new JsonObject
            {
                ["location"] = "https://example.invalid/format-control",
                ["receipt"] = Copy()
            },
            ["verifiedCandidatePackage"] = Copy(),
            ["distributedPackage"] = Copy(),
            ["sourceState"] = new JsonObject
            {
                ["commit"] = null,
                ["sourceArchive"] = Copy(),
                ["sourceInventory"] = Copy()
            },
            ["targets"] = new JsonArray(Target(artifact)),
            ["contracts"] = new JsonArray(Contract(artifact)),
            ["sourceFixtures"] = new JsonArray(SourceFixture(artifact)),
            ["binaryFixtures"] = new JsonArray(BinaryFixture(artifact)),
            ["executions"] = new JsonArray(Execution(artifact)),
            ["verificationBundle"] = Copy(),
            ["verificationSummary"] = Copy()
        };
    }

    private static JsonObject Target(JsonObject artifact)
    {
        JsonNode Copy() => artifact.DeepClone();
        return new JsonObject
        {
            ["targetFramework"] = "net10.0",
            ["minimumRuntimeFamily"] = "10.0",
            ["runtimeIdentifier"] = null,
            ["assetPath"] = "lib/net10.0/CIS.Core.dll",
            ["assemblyIdentity"] = new JsonObject
            {
                ["name"] = "CIS.Core",
                ["assemblyVersion"] = "1.0.0.0",
                ["fileVersion"] = "0.1.0.1",
                ["informationalVersion"] = "synthetic-format-control",
                ["culture"] = "neutral",
                ["publicKeyToken"] = null
            },
            ["assembly"] = Copy(),
            ["publicApiMetadata"] = Copy()
        };
    }

    private static JsonObject Contract(JsonObject artifact)
    {
        JsonNode Copy() => artifact.DeepClone();
        return new JsonObject
        {
            ["contractId"] = "cis.logging",
            ["contractVersion"] = "v1",
            ["apiAndBehaviorSpecification"] = Copy(),
            ["configurationFixtures"] = Copy(),
            ["wireFixtures"] = Copy(),
            ["behaviorFixtures"] = Copy()
        };
    }

    private static JsonObject SourceFixture(JsonObject artifact)
    {
        JsonNode Copy() => artifact.DeepClone();
        return new JsonObject
        {
            ["fixtureId"] = "source-1",
            ["contractTargetFramework"] = "net10.0",
            ["languageVersion"] = "14.0",
            ["nullable"] = "enable",
            ["warningsAsErrors"] = true,
            ["sourceArchive"] = Copy(),
            ["buildSettings"] = Copy(),
            ["dependencyInventory"] = Copy(),
            ["expectedBehavior"] = Copy()
        };
    }

    private static JsonObject BinaryFixture(JsonObject artifact)
    {
        JsonNode Copy() => artifact.DeepClone();
        return new JsonObject
        {
            ["fixtureId"] = "binary-1",
            ["sourceFixtureId"] = "source-1",
            ["consumerAssembly"] = Copy(),
            ["runtimeConfiguration"] = Copy(),
            ["dependencyManifest"] = Copy(),
            ["dependencyInventory"] = Copy(),
            ["dependencyArtifacts"] = new JsonArray(Copy()),
            ["expectedBehavior"] = Copy(),
            ["originalExecutionId"] = "capture-1"
        };
    }

    private static JsonObject Execution(JsonObject artifact)
    {
        JsonNode Copy() => artifact.DeepClone();
        return new JsonObject
        {
            ["executionId"] = "capture-1",
            ["laneId"] = "windows-x64",
            ["osDescription"] = "synthetic format host",
            ["architecture"] = "x64",
            ["targetFramework"] = "net10.0",
            ["sdkVersion"] = "10.0.401",
            ["runtimeVersion"] = "10.0.12",
            ["rollForward"] = "Disable",
            ["executionMode"] = "framework-dependent",
            ["consumerFixtureIds"] = new JsonArray("binary-1"),
            ["toolchainManifest"] = Copy(),
            ["prerequisitesInventory"] = Copy(),
            ["materializationArchive"] = Copy(),
            ["nativeReports"] = new JsonArray(Copy())
        };
    }
}
