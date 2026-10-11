using System.Text.Json;
using System.Text.Json.Nodes;
using CIS.Configuration;
using CIS.Logging;

namespace CIS.Tests.DiscoveryFixture;

// Nonshipping adapter builds genuine Core snapshots from the unchanged shared input vectors.
internal static class StartupMetadataFixtureAdapter
{
    public static JsonObject Execute(JsonObject row, string directory)
    {
        JsonObject input = row["inputs"]!.AsObject();
        var sources = new StartupMetadataFixtureSources(input);
        EnvironmentSetup setup = Setup(input);
        DateTimeOffset initialized = input["initializedAt"] is { } instant ? instant.GetValue<DateTimeOffset>() :
            new DateTimeOffset(2026, 10, 10, 7, 5, 0, TimeSpan.Zero);
        Guid firstId = Guid.Parse(input["firstExplicitUuid"]?.GetValue<string>() ?? "00000000-0000-4000-8000-000000000011");
        int directoryReads = 0;
        EnvironmentSnapshot? snapshot = null;
        CoreConfigurationException? error = null;
        try
        {
            snapshot = EnvironmentSnapshotFactory.Create(setup, sources, sources, sources,
                () =>
                {
                    directoryReads++;
                    return directory;
                },
                new InitializationIdentityFactory(new FixedClock(initialized), () => firstId));
        }
        catch (CoreConfigurationException failure)
        {
            error = failure;
        }

        sources.BlockFurtherReads();
        EnvironmentSnapshot? second = SecondSnapshot(input, initialized, directory);
        JsonObject facts = Observations(snapshot, second, setup);
        string? originalInstance = snapshot?.InstanceId;
        ApplyMutation(row, setup);
        facts["stableAfterCallerMutation"] = snapshot is null ||
            JsonNode.DeepEquals(JsonSerializer.SerializeToNode(snapshot), facts["originalSnapshot"]);
        facts["stableWithinRun"] = snapshot is not null && originalInstance == snapshot.InstanceId &&
            originalInstance == snapshot.EventMetadata.InstanceId && facts["stableAfterCallerMutation"]!.GetValue<bool>();
        facts.Remove("originalSnapshot");
        facts["variableReads"] = sources.VariableReads;
        facts["runtimeReads"] = sources.RuntimeReads;
        facts["directoryReads"] = directoryReads;
        facts["publicFacadeInitialized"] = EnvironmentOptions.IsInitialized;
        return new JsonObject
        {
            ["caseId"] = row["id"]!.DeepClone(),
            ["snapshot"] = JsonSerializer.SerializeToNode(snapshot),
            ["secondSnapshot"] = JsonSerializer.SerializeToNode(second),
            ["issues"] = new JsonArray(error?.Issues.Select(issue => (JsonNode)new JsonObject
            {
                ["code"] = issue.Code,
                ["key"] = issue.Key
            }).ToArray() ?? []),
            ["observed"] = facts
        };
    }

    public static void Validate(JsonObject row, JsonObject actual)
    {
        Require(actual["caseId"]!.GetValue<string>() == row["id"]!.GetValue<string>(), "Wrong metadata row.");
        JsonArray issues = actual["issues"]!.AsArray();
        if (row["expectedIssue"] is { } expectedIssue)
        {
            Require(issues.Count == 1 && JsonNode.DeepEquals(expectedIssue, issues[0]), "Wrong metadata diagnostic.");
            Require(actual["snapshot"] is null, "Invalid metadata published a snapshot.");
            Require(actual["observed"]!["runtimeReads"]!.GetValue<int>() == 0, "Invalid metadata read runtime facts.");
        }
        else
        {
            Require(issues.Count == 0 && actual["snapshot"] is JsonObject, "Metadata did not resolve.");
            foreach ((string key, JsonNode? expected) in row["expected"]!.AsObject())
            {
                JsonNode? value = key is "substitutesInitializationForProcessStart" or "stableWithinRun" or "differentAcrossSuccessfulRuns" or "sharesCallerMutableReferences"
                    ? actual["observed"]![key] : Property(actual["snapshot"]!, key);
                bool equal = key is "ProcessStartedAtUtc" or "InitializedAtUtc"
                    ? value?.GetValue<DateTimeOffset>() == expected?.GetValue<DateTimeOffset>()
                    : JsonNode.DeepEquals(value, expected);
                Require(equal, "Metadata expectation differs: " + key);
            }
            JsonObject snapshot = actual["snapshot"]!.AsObject();
            JsonObject metadata = snapshot["EventMetadata"]!.AsObject();
            Require(metadata.Count == 17, "Metadata allowlist changed.");
            foreach ((string key, JsonNode? value) in metadata)
            {
                Require(JsonNode.DeepEquals(value, snapshot[key]), "Metadata differs from snapshot: " + key);
            }
            Require(actual["observed"]!["runtimeReads"]!.GetValue<int>() == 8, "Runtime capture count differs.");
        }
        Require(actual["observed"]!["stableAfterCallerMutation"]!.GetValue<bool>(), "Caller mutation changed owned metadata.");
        Require(actual["observed"]!["variableReads"]!.GetValue<int>() == 21, "Variable capture count differs.");
        Require(actual["observed"]!["directoryReads"]!.GetValue<int>() == 1, "Directory capture count differs.");
        Require(!actual["observed"]!["publicFacadeInitialized"]!.GetValue<bool>(), "Local vector builder published global state.");
    }

    private static EnvironmentSetup Setup(JsonObject input) => new()
    {
        ApplicationAssembly = input["explicitAssembly"] is null ? null : typeof(StartupMetadataFixtureAdapter).Assembly,
        ApplicationName = input["applicationName"]?.GetValue<string>() ?? input["setup.ApplicationName"]?.GetValue<string>(),
        BuildVersion = input["setupBuildVersion"]?.GetValue<string>() ?? input["buildVersion"]?.GetValue<string>(),
        Logging = new LoggingSetup { QueueCapacity = input["setup.Logging.QueueCapacity"]?.GetValue<int>() }
    };

    private static EnvironmentSnapshot? SecondSnapshot(JsonObject input, DateTimeOffset initialized, string directory)
    {
        if (input["secondExplicitUuid"] is not { } secondId)
        {
            return null;
        }
        var sources = new StartupMetadataFixtureSources(input);
        return EnvironmentSnapshotFactory.Create(Setup(input), sources, sources, sources, () => directory,
            new InitializationIdentityFactory(new FixedClock(initialized), () => Guid.Parse(secondId.GetValue<string>())));
    }

    private static JsonObject Observations(EnvironmentSnapshot? snapshot, EnvironmentSnapshot? second, EnvironmentSetup setup) => new()
    {
        ["originalSnapshot"] = JsonSerializer.SerializeToNode(snapshot),
        ["differentAcrossSuccessfulRuns"] = snapshot is not null && second is not null && snapshot.InstanceId != second.InstanceId,
        ["substitutesInitializationForProcessStart"] = snapshot is not null && snapshot.ProcessStartedAtUtc == snapshot.InitializedAtUtc,
        ["sharesCallerMutableReferences"] = ReferenceEquals(snapshot, setup) || ReferenceEquals(snapshot?.Logging, setup.Logging)
    };

    private static void ApplyMutation(JsonObject row, EnvironmentSetup setup)
    {
        if (row["mutationAfterPublication"] is not JsonObject mutation)
        {
            return;
        }
        setup.ApplicationName = mutation["setup.ApplicationName"]!.GetValue<string>();
        setup.Logging!.QueueCapacity = mutation["setup.Logging.QueueCapacity"]!.GetValue<int>();
    }

    private static JsonNode? Property(JsonNode root, string path)
    {
        JsonNode? value = root;
        foreach (string part in path.Split('.'))
        {
            value = value?[part];
        }
        return value;
    }

    private static void Require(bool value, string message)
    {
        if (!value)
        {
            throw new InvalidDataException(message);
        }
    }

    private sealed class FixedClock(DateTimeOffset instant) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instant;
    }
}
