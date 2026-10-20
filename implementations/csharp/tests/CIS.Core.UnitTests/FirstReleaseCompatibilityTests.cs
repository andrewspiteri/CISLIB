using CIS.TestSupport;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class FirstReleaseCompatibilityTests
{
    [TestMethod]
    [DataRow("published", "Published history requires cumulative replay")]
    [DataRow("sdk", "Requalify consumer settings")]
    [DataRow("runtime", "Requalify consumer settings")]
    public async Task InapplicableFirstReleaseInputsCannotReceiveQualification(string control, string error)
    {
        using var temp = new TemporaryDirectory();
        string repo = Path.Combine(temp.Path, "repo"), output = Path.Combine(temp.Path, "output");
        string publications = control == "published" ? "[{}]" : "[]";
        string ledger = "{\"publications\":" + publications + "}";
        string ledgerPath = Path.Combine(repo, "compatibility/csharp/publications/ledger.json");
        Directory.CreateDirectory(Path.GetDirectoryName(ledgerPath)!);
        File.WriteAllText(ledgerPath, ledger);
        string policy = Path.Combine(repo, "implementations/csharp/eng/toolchain.json");
        Directory.CreateDirectory(Path.GetDirectoryName(policy)!);
        File.WriteAllText(policy, "{\"sdk\":\"" + (control == "sdk" ? "11.0.100" : "10.0.401") +
            "\",\"targetRuntime\":\"" + (control == "runtime" ? "10.0.13" : "10.0.12") + "\"}");
        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => FirstReleaseCompatibility.RunAsync(
            ["--repo-root", repo, "--candidate-package", Path.Combine(temp.Path, "absent.nupkg"),
                "--dependency-directory", temp.Path, "--output-root", output]));
        StringAssert.Contains(failure.Message, error);
        Assert.IsFalse(File.Exists(Path.Combine(output, "results.json")), "Refused inputs cannot produce a qualification report.");
        Assert.AreEqual(ledger, File.ReadAllText(ledgerPath), "No baseline may be rewritten to make first-release controls apply.");
    }

    [TestMethod]
    public async Task ExistingEvidenceCannotBeOverwrittenByAnotherQualificationRun()
    {
        using var temp = new TemporaryDirectory();
        string output = Path.Combine(temp.Path, "evidence"); Directory.CreateDirectory(output);
        string retained = Path.Combine(output, "retained.txt"); File.WriteAllText(retained, "original evidence");
        InvalidDataException failure = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => FirstReleaseCompatibility.RunAsync(
            ["--repo-root", temp.Path, "--candidate-package", Path.Combine(temp.Path, "absent.nupkg"),
                "--dependency-directory", temp.Path, "--output-root", output]));
        StringAssert.Contains(failure.Message, "empty output directory");
        Assert.AreEqual("original evidence", File.ReadAllText(retained));
        Assert.HasCount(1, Directory.GetFiles(output));
    }
}
