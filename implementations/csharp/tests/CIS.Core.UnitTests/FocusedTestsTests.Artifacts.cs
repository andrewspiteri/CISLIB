using CIS.TestSupport;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

public sealed partial class FocusedTestsTests
{
    [TestMethod]
    public void PassingIterationCleansOnlyItsOwnDisposableNativeArtifacts()
    {
        using var directory = new TemporaryDirectory();
        string focused = Path.Combine(directory.Path, "focused");
        string output = Path.Combine(focused, Guid.NewGuid().ToString("N"));
        string otherRun = Path.Combine(focused, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(otherRun);
        File.WriteAllText(Path.Combine(otherRun, "selected.trx"), "other run");
        foreach (string name in new[] { "build.log", "test.log", "selected.trx" }) { File.WriteAllText(Path.Combine(output, name), "validated"); }
        string attachment = Path.Combine(output, "attachments");
        Directory.CreateDirectory(attachment);
        File.WriteAllText(Path.Combine(attachment, "disposable.bin"), "disposable native attachment");

        FocusedTests.Artifact[] artifacts = FocusedTests.FinalizeArtifacts(focused, output, passed: true);

        Assert.HasCount(0, Directory.GetFileSystemEntries(output));
        Assert.IsTrue(artifacts.All(item => item.Availability == "cleaned-after-validation" && item.OriginalBytes > 0 && item.RetainedBytes == 0));
        Assert.AreEqual("other run", File.ReadAllText(Path.Combine(otherRun, "selected.trx")));
    }

    [TestMethod]
    public void FailedIterationKeepsBoundedDiagnosticAndReportsOversizedNativeResultAsUnavailable()
    {
        using var directory = new TemporaryDirectory();
        string focused = Path.Combine(directory.Path, "focused");
        string output = Path.Combine(focused, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        FocusedTests.WriteDiagnosticLog(Path.Combine(output, "test.log"), "first" + new string('x', 200_000) + "last", "error");
        string report = Path.Combine(output, "selected.trx");
        using (FileStream stream = File.Create(report)) { stream.SetLength(FocusedTests.MaximumReportBytes + 1L); }
        Assert.Throws<InvalidDataException>(() => FocusedTests.ReadResults(report, DateTimeOffset.UtcNow));

        FocusedTests.Artifact[] artifacts = FocusedTests.FinalizeArtifacts(focused, output, passed: false);

        FocusedTests.Artifact retained = artifacts.Single(item => item.Name == "test.log");
        Assert.AreEqual("retained", retained.Availability);
        Assert.IsTrue(retained.RetainedBytes < 512 * 1024);
        string log = File.ReadAllText(Path.Combine(output, "test.log"));
        StringAssert.Contains(log, "first");
        StringAssert.Contains(log, "last");
        StringAssert.Contains(log, "error");
        StringAssert.Contains(log, "middle omitted");
        Assert.AreEqual("discarded-size-limit", artifacts.Single(item => item.Name == "selected.trx").Availability);
        Assert.AreEqual("missing", artifacts.Single(item => item.Name == "build.log").Availability);
        Assert.IsFalse(File.Exists(report));
    }

    [TestMethod]
    public void FailedIterationPreservesNativeReportBytesUntilExplicitCleanup()
    {
        using var directory = new TemporaryDirectory();
        string focused = Path.Combine(directory.Path, "focused");
        string output = Path.Combine(focused, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        byte[] original = "native failure diagnosis"u8.ToArray();
        File.WriteAllBytes(Path.Combine(output, "selected.trx"), original);

        FocusedTests.Artifact[] artifacts = FocusedTests.FinalizeArtifacts(focused, output, passed: false);

        CollectionAssert.AreEqual(original, File.ReadAllBytes(Path.Combine(output, "selected.trx")));
        Assert.AreEqual("retained", artifacts.Single(item => item.Name == "selected.trx").Availability);
    }

    [TestMethod]
    public void BoundedProcessCaptureIsExplicitInTheRetainedDiagnostic()
    {
        using var directory = new TemporaryDirectory();
        string log = Path.Combine(directory.Path, "test.log");

        FocusedTests.WriteDiagnosticLog(log, "prefixsuffix", "error", captureTruncated: true);

        StringAssert.Contains(File.ReadAllText(log), "omitted content is unavailable");
    }

    [TestMethod]
    public void CleanupRejectsDirectoriesOutsideItsExactGuidChild()
    {
        using var directory = new TemporaryDirectory();
        string focused = Path.Combine(directory.Path, "focused");
        string unrelated = Path.Combine(directory.Path, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(focused);
        Directory.CreateDirectory(unrelated);
        string source = Path.Combine(unrelated, "source.cs");
        File.WriteAllText(source, "preserve");

        Assert.Throws<InvalidDataException>(() => FocusedTests.FinalizeArtifacts(focused, unrelated, passed: true));
        Assert.AreEqual("preserve", File.ReadAllText(source));
    }

    [TestMethod]
    public void UnexpectedDiagnosticDirectoryCannotBypassTheFailedRunLimit()
    {
        using var directory = new TemporaryDirectory();
        string focused = Path.Combine(directory.Path, "focused");
        string output = Path.Combine(focused, Guid.NewGuid().ToString("N"));
        string unexpected = Path.Combine(output, "build.log");
        Directory.CreateDirectory(unexpected);
        File.WriteAllText(Path.Combine(unexpected, "disposable.bin"), "not a native log file");

        FocusedTests.Artifact[] artifacts = FocusedTests.FinalizeArtifacts(focused, output, passed: false);

        Assert.IsFalse(Directory.Exists(unexpected));
        Assert.AreEqual("missing", artifacts.Single(item => item.Name == "build.log").Availability);
    }
}
