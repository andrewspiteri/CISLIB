using System.Text.Json.Nodes;
using CIS.Tests.ArchitectureTests.Publications;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class PublicationManifestTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [TestMethod]
    [DataRow("EmptyLedger", "")]
    [DataRow("FirstPrerelease", "")]
    [DataRow("IntermediateRelease", "")]
    [DataRow("PartialPromotion", "")]
    [DataRow("AdditiveIdentity", "")]
    [DataRow("IntegrityWithoutAnchor", "")]
    [DataRow("RemovedHistory", "HistoryRemoval")]
    [DataRow("RewrittenHistory", "HistoryRewrite")]
    [DataRow("ReorderedHistory", "HistoryRewrite")]
    [DataRow("IntermediateRemoved", "HistoryRemoval")]
    [DataRow("ChangedAnchor", "HistoryAnchorMismatch")]
    [DataRow("MissingAnchor", "MissingHistoryAnchor")]
    [DataRow("DuplicateIdentity", "DuplicateIdentity")]
    [DataRow("DuplicateVersion", "DuplicatePublication")]
    [DataRow("UnknownIdentity", "UnknownIdentity")]
    [DataRow("SequenceGap", "InvalidSequence")]
    [DataRow("TamperedRecord", "ArtifactHashMismatch")]
    [DataRow("MissingRecord", "MissingArtifact")]
    [DataRow("PayloadHashMismatch", "ArtifactHashMismatch")]
    [DataRow("PayloadLengthMismatch", "ArtifactLengthMismatch")]
    [DataRow("MissingPayload", "MissingArtifact")]
    [DataRow("PathTraversal", "LedgerSchemaInvalid")]
    [DataRow("AbsolutePath", "LedgerSchemaInvalid")]
    [DataRow("ArtifactPathTraversal", "BaselineSchemaInvalid")]
    [DataRow("DuplicateJsonProperty", "DuplicateJsonProperty")]
    [DataRow("InvalidUtf8", "InvalidJson")]
    [DataRow("MissingSources", "BaselineSchemaInvalid")]
    [DataRow("MissingBinaries", "BaselineSchemaInvalid")]
    [DataRow("MissingApiMetadata", "BaselineSchemaInvalid")]
    [DataRow("UnknownSourceTarget", "UnknownFixtureTarget")]
    [DataRow("UnknownSourceFixture", "UnknownSourceFixture")]
    [DataRow("UnknownBinaryFixture", "UnknownBinaryFixture")]
    [DataRow("UnknownOriginalExecution", "UnknownOriginalExecution")]
    [DataRow("AssemblyMismatch", "RecordIdentityMismatch")]
    [DataRow("PackageMismatch", "RecordIdentityMismatch")]
    [DataRow("RecordVersionMismatch", "RecordIdentityMismatch")]
    [DataRow("ImpossibleTime", "InvalidPublicationTime")]
    [DataRow("DuplicateExecution", "DuplicateExecution")]
    [DataRow("DuplicateTarget", "DuplicateTarget")]
    [DataRow("DuplicateContract", "DuplicateContract")]
    [DataRow("InvalidVersion", "LedgerSchemaInvalid")]
    [DataRow("NoncanonicalPrerelease", "InvalidPackageVersion")]
    [DataRow("UnknownSchema", "LedgerSchemaInvalid")]
    public async Task PublicationControlsValidateMembershipBytesAndAppendOnlyHistory(string control, string error)
    {
        using var fixture = new PublicationFixture();
        PublicationFaults.Introduce(fixture, control);
        JsonObject report = await fixture.Validate(RepositoryRoot, error.Length == 0 ? 0 : 2);
        Assert.IsFalse(report["compatibilityQualified"]!.GetValue<bool>(), "Format controls must not claim compatibility.");
        if (error.Length > 0)
        {
            Assert.AreEqual(error, report["issues"]![0]!["code"]!.GetValue<string>(), report.ToJsonString());
            return;
        }

        Assert.AreEqual(fixture.Publications.Count, report["publicationCount"]!.GetValue<int>());
        Assert.AreEqual(fixture.UsePrevious ? "ComparedWithTrustedPrevious" : "NotCompared", report["appendOnlyStatus"]!.GetValue<string>());
        Assert.IsTrue(report["firstReleaseHarnessRequired"]!.GetValue<bool>());
        Assert.IsFalse(report["actualPublicationAttested"]!.GetValue<bool>());
        Assert.AreEqual(0, report["compatibilityChecksExecuted"]!.GetValue<int>());
    }

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

        throw new DirectoryNotFoundException("Run the manifest checks from the CisLib checkout.");
    }
}
