using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class SourceInventoryTests
{
    [TestMethod]
    public async Task WorkingTreeInventoryIncludesCurrentInputsWithoutStagingDeletions()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "implementations", "csharp", "eng", "check-source-inventory.ps1"))) { root = root.Parent; }
        Assert.IsNotNull(root);
        using var directory = new TemporaryDirectory();
        ProcessResult result = await ProcessRunner.RunAsync("pwsh", ["-NoProfile", "-File", Path.Combine(root.FullName, "implementations", "csharp", "eng", "check-source-inventory.ps1"), "-FixtureRoot", directory.Path], root.FullName, TimeSpan.FromSeconds(20));
        Assert.AreEqual(0, result.ExitCode, result.StandardError);
        Assert.IsTrue(result.StandardOutput.StartsWith("Passed:", StringComparison.Ordinal));
    }
}
