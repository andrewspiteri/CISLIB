using System.Text.Json.Nodes;

namespace CIS.Tests.ArchitectureTests.Publications;

internal static class PublicationFaults
{
    internal static void Introduce(PublicationFixture fixture, string control)
    {
        if (TryPositive(fixture, control) || TryHistory(fixture, control) || TryLedger(fixture, control) || TryArtifacts(fixture, control))
        {
            return;
        }

        if (TryRecordShape(fixture.FirstRecord, control) || TryRecordLinks(fixture.FirstRecord, control))
        {
            fixture.SaveRecord();
            return;
        }

        throw new ArgumentOutOfRangeException(nameof(control), control, "Unknown publication control.");
    }

    private static bool TryPositive(PublicationFixture fixture, string control)
    {
        switch (control)
        {
            case "EmptyLedger":
                fixture.Publications.Clear();
                fixture.UsePrevious = false;
                break;
            case "FirstPrerelease":
                break;
            case "IntermediateRelease":
                fixture.FreezePrevious();
                fixture.AddPublication("0.1.0");
                break;
            case "PartialPromotion":
                fixture.FirstRecord["aggregatePromotionStateAtExposure"] = "partial";
                fixture.SaveRecord();
                break;
            case "AdditiveIdentity":
                fixture.Ledger["identities"]!.AsArray().Add(new JsonObject
                {
                    ["identityId"] = "cis.optional.csharp",
                    ["packageId"] = "CIS.Optional",
                    ["assemblyName"] = "CIS.Optional",
                    ["rootNamespace"] = "CIS.Optional",
                    ["compatibilityLine"] = "permanent"
                });
                break;
            case "IntegrityWithoutAnchor":
                fixture.UsePrevious = false;
                break;
            default:
                return false;
        }

        return true;
    }

    private static bool TryHistory(PublicationFixture fixture, string control)
    {
        switch (control)
        {
            case "RemovedHistory":
                fixture.FreezePrevious();
                fixture.Publications.Clear();
                break;
            case "RewrittenHistory":
                fixture.FreezePrevious();
                fixture.FirstRecord["aggregatePromotionStateAtExposure"] = "partial";
                fixture.SaveRecord();
                break;
            case "ReorderedHistory":
                fixture.AddPublication("0.1.0");
                fixture.FreezePrevious();
                JsonNode first = fixture.Publications[0]!.DeepClone();
                fixture.Publications[0] = fixture.Publications[1]!.DeepClone();
                fixture.Publications[1] = first;
                break;
            case "IntermediateRemoved":
                fixture.AddPublication("0.1.0");
                fixture.FreezePrevious();
                fixture.Publications.RemoveAt(0);
                break;
            case "ChangedAnchor":
                File.AppendAllText(fixture.PreviousPath, " ");
                break;
            case "MissingAnchor":
                fixture.SupplyPreviousHash = false;
                break;
            default:
                return false;
        }

        return true;
    }

    private static bool TryLedger(PublicationFixture fixture, string control)
    {
        switch (control)
        {
            case "DuplicateIdentity":
                fixture.Ledger["identities"]!.AsArray().Add(fixture.Ledger["identities"]![0]!.DeepClone());
                break;
            case "DuplicateVersion":
                fixture.AddPublication("0.1.0-RC.1");
                break;
            case "UnknownIdentity":
                fixture.Publications[0]!["identityId"] = "unknown.identity";
                break;
            case "SequenceGap":
                fixture.Publications[0]!["sequence"] = 2;
                break;
            case "PathTraversal":
                fixture.Publications[0]!["record"]!["path"] = "../outside.json";
                break;
            case "AbsolutePath":
                fixture.Publications[0]!["record"]!["path"] = "C:/outside.json";
                break;
            case "DuplicateJsonProperty":
                File.WriteAllText(fixture.LedgerPath, "{\"schemaVersion\":1," + fixture.Ledger.ToJsonString()[1..]);
                break;
            case "InvalidUtf8":
                File.WriteAllBytes(fixture.LedgerPath, [0xff, 0xfe, 0x7b, 0x7d]);
                break;
            case "RecordVersionMismatch":
                fixture.Publications[0]!["packageVersion"] = "9.9.9";
                break;
            case "InvalidVersion":
                fixture.Publications[0]!["packageVersion"] = "1.0";
                break;
            case "NoncanonicalPrerelease":
                fixture.Publications[0]!["packageVersion"] = "1.0.0-rc.01";
                break;
            case "UnknownSchema":
                fixture.Ledger["schemaVersion"] = 2;
                break;
            default:
                return false;
        }

        return true;
    }

    private static bool TryArtifacts(PublicationFixture fixture, string control)
    {
        switch (control)
        {
            case "TamperedRecord":
                File.WriteAllText(fixture.RecordPath(), File.ReadAllText(fixture.RecordPath()).Replace("CIS.Core", "CIS.Bore", StringComparison.Ordinal));
                break;
            case "MissingRecord":
                File.Delete(fixture.RecordPath());
                break;
            case "PayloadHashMismatch":
                byte[] bytes = File.ReadAllBytes(fixture.PayloadPath);
                bytes[0] = (byte)'S';
                File.WriteAllBytes(fixture.PayloadPath, bytes);
                break;
            case "PayloadLengthMismatch":
                File.AppendAllText(fixture.PayloadPath, "x");
                break;
            case "MissingPayload":
                File.Delete(fixture.PayloadPath);
                break;
            default:
                return false;
        }

        return true;
    }

    private static bool TryRecordShape(JsonObject record, string control)
    {
        switch (control)
        {
            case "ArtifactPathTraversal":
                record["verifiedCandidatePackage"]!["path"] = "../outside.json";
                break;
            case "MissingSources":
                record["sourceFixtures"] = new JsonArray();
                break;
            case "MissingBinaries":
                record["binaryFixtures"] = new JsonArray();
                break;
            case "MissingApiMetadata":
                record["targets"]![0]!.AsObject().Remove("publicApiMetadata");
                break;
            case "AssemblyMismatch":
                record["targets"]![0]!["assemblyIdentity"]!["name"] = "CIS.Other";
                break;
            case "PackageMismatch":
                record["packageId"] = "CIS.Other";
                break;
            case "ImpossibleTime":
                record["exposedAtUtc"] = "2026-02-30T20:00:00.0000000Z";
                break;
            default:
                return false;
        }

        return true;
    }

    private static bool TryRecordLinks(JsonObject record, string control)
    {
        switch (control)
        {
            case "UnknownSourceTarget":
                record["sourceFixtures"]![0]!["contractTargetFramework"] = "net8.0";
                break;
            case "UnknownSourceFixture":
                record["binaryFixtures"]![0]!["sourceFixtureId"] = "absent";
                break;
            case "UnknownBinaryFixture":
                record["executions"]![0]!["consumerFixtureIds"] = new JsonArray("absent");
                break;
            case "UnknownOriginalExecution":
                record["binaryFixtures"]![0]!["originalExecutionId"] = "absent";
                break;
            case "DuplicateExecution":
                record["executions"]!.AsArray().Add(record["executions"]![0]!.DeepClone());
                break;
            case "DuplicateTarget":
                record["targets"]!.AsArray().Add(record["targets"]![0]!.DeepClone());
                break;
            case "DuplicateContract":
                record["contracts"]!.AsArray().Add(record["contracts"]![0]!.DeepClone());
                break;
            default:
                return false;
        }

        return true;
    }
}
