using System.Security.Cryptography;
using System.Text;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

public sealed partial class HealthcheckMutationNativeAuditTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ExplicitRetainedExtentsReadTheOriginalCompleteRequest(bool rotated)
    {
        using var fixture = new TraceFixture();
        RetainedDiagnosticInputs retained = fixture.Retained(rotated);
        var audit = HealthcheckMutationNativeAudit.AuditRetainedDiagnosticExtentsForTest(fixture.Report,
            fixture.Configuration, retained, Path.Combine(AppContext.BaseDirectory, "Microsoft.VisualStudio.TestPlatform.ObjectModel.dll"));
        CollectionAssert.AreEqual(new[] { "1" }, audit.AssertionKilledIds);
        Assert.HasCount(0, audit.Issues);
        Assert.AreEqual(1, audit.NormalHostCount);
    }

    [TestMethod]
    public void TheHistoricalPhysicalEntryDoesNotGuessAnExtentMapping()
    {
        using var fixture = new TraceFixture();
        RetainedDiagnosticInputs retained = fixture.Retained(true);
        var audit = HealthcheckMutationNativeAudit.Audit(fixture.Report, fixture.Configuration,
            Directory.GetFiles(fixture.DiagnosticRoot), Path.Combine(AppContext.BaseDirectory, "Microsoft.VisualStudio.TestPlatform.ObjectModel.dll"));
        Assert.HasCount(0, audit.AssertionKilledIds);
        Assert.IsNotEmpty(audit.Issues);
        retained.Recheck();
    }

    [TestMethod]
    [DataRow("abort")]
    [DataRow("missing-callback")]
    public void AnExtentMappingCannotClearAnOriginalExecutionFailure(string mode)
    {
        using var fixture = new TraceFixture();
        if (mode == "abort") { fixture.ChangeCompletion(row => row["IsAborted"] = true); }
        else { fixture.Controller.RemoveAt(fixture.Controller.Count - 1); }
        var audit = HealthcheckMutationNativeAudit.AuditRetainedDiagnosticExtentsForTest(fixture.Report,
            fixture.Configuration, fixture.Retained(true), Path.Combine(AppContext.BaseDirectory, "Microsoft.VisualStudio.TestPlatform.ObjectModel.dll"));
        Assert.HasCount(0, audit.AssertionKilledIds);
        Assert.IsNotEmpty(audit.Issues);
    }

    [TestMethod]
    [DataRow("wrong-hash")]
    [DataRow("reverse")]
    [DataRow("repeat")]
    [DataRow("missing-file")]
    [DataRow("extra-file")]
    [DataRow("partial-record")]
    [DataRow("bare-protocol")]
    public void InvalidPhysicalExtentsCannotRepairNativeEvidence(string mode)
    {
        using var fixture = new TraceFixture();
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Retained(true, mode));
    }

    [TestMethod]
    public void ExtentInputsAreRecheckedAfterBinding()
    {
        using var fixture = new TraceFixture();
        RetainedDiagnosticInputs retained = fixture.Retained(true);
        File.AppendAllText(Path.Combine(fixture.DiagnosticRoot, "Runner 0-log.txt"), "changed\n");
        Assert.ThrowsExactly<InvalidDataException>(retained.Recheck);
    }

    [TestMethod]
    public void TheProductionExtentEntryRequiresActualKernelEvidence()
    {
        using var fixture = new TraceFixture();
        Assert.ThrowsExactly<InvalidDataException>(() => HealthcheckMutationNativeAudit.AuditRetainedDiagnosticExtents(
            fixture.Report, fixture.Configuration, fixture.Retained(true), "unused", null!));
    }

    private sealed partial class TraceFixture
    {
        internal string DiagnosticRoot => temporary.Path;

        internal RetainedDiagnosticInputs Retained(bool rotated, string? mode = null)
        {
            string active = Path.Combine(temporary.Path, "Runner 0-log.txt");
            string backup = Path.Combine(temporary.Path, "Runner 0-log.bak.txt");
            string host = Path.Combine(temporary.Path, "Runner 0-log.host.synthetic.txt");
            if (rotated)
            {
                File.WriteAllLines(backup, Controller.Take(2), new UTF8Encoding(false));
                File.WriteAllLines(active, Controller.Skip(2), new UTF8Encoding(false));
            }
            else { File.WriteAllLines(active, Controller, new UTF8Encoding(false)); }
            File.WriteAllLines(host, Host, new UTF8Encoding(false));
            if (mode == "partial-record") { File.WriteAllText(backup, string.Join('\n', Controller.Take(2)), new UTF8Encoding(false)); }
            if (mode == "bare-protocol")
            {
                File.AppendAllText(active, "10, bare DesignModeClient.ProcessRequests: Processing Message: (TestExecution.Cancel) -> {bad}\n");
            }
            NativeDiagnosticExtentReader.Extent Pin(string path) => new(path, new FileInfo(path).Length,
                Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
            NativeDiagnosticExtentReader.Extent[] physical = Directory.GetFiles(temporary.Path).Order(StringComparer.Ordinal).Select(Pin).ToArray();
            NativeDiagnosticExtentReader.Extent[] chain = (rotated ? new[] { backup, active } : [active]).Select(Pin).ToArray();
            if (mode == "wrong-hash") { physical[0] = physical[0] with { Sha256 = new string('0', 64) }; }
            if (mode == "reverse") { Array.Reverse(chain); }
            if (mode == "repeat") { chain = [chain[0], chain[0]]; }
            if (mode == "missing-file") { physical = physical.Where(row => row.Path != host).ToArray(); }
            if (mode == "extra-file") { File.WriteAllText(Path.Combine(temporary.Path, "unlisted.txt"), "extra\n"); }
            return new(temporary.Path, physical, new Dictionary<string, IReadOnlyList<NativeDiagnosticExtentReader.Extent>>(StringComparer.Ordinal) { [active] = chain });
        }
    }
}
