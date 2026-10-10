using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class TestSupportTests
{
    [TestMethod]
    public void CleanupLeavesAnotherFixtureAndItsFilesUntouched()
    {
        using var first = new TemporaryDirectory();
        using var second = new TemporaryDirectory();
        string secondFile = Path.Combine(second.Path, "retained.txt");
        File.WriteAllText(secondFile, "other fixture");

        first.Dispose();
        first.Dispose();

        Assert.IsFalse(Directory.Exists(first.Path));
        Assert.AreEqual("other fixture", File.ReadAllText(secondFile));
    }

    [TestMethod]
    public async Task ChildProcessDrainsIndependentStreamsLargerThanPipeBuffers()
    {
        using var directory = new TemporaryDirectory();
        ProcessResult result = await ProcessRunner.RunAsync(
            "pwsh",
            ["-NoProfile", "-Command", "[Console]::Out.Write('o' * 131072); [Console]::Error.Write('e' * 131072)"],
            directory.Path,
            TimeSpan.FromSeconds(30));

        Assert.AreEqual(0, result.ExitCode);
        Assert.AreEqual(new string('o', 131072), result.StandardOutput);
        Assert.AreEqual(new string('e', 131072), result.StandardError);
    }
}
