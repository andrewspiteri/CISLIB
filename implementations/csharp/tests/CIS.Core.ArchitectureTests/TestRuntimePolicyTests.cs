using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class TestRuntimePolicyTests
{
    [TestMethod]
    public void ActualTestHostAndGeneratedConfigurationMatchTheSelectedRuntime()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "implementations/csharp/eng/toolchain.json")))
        { directory = directory.Parent; }
        Assert.IsNotNull(directory, "Repository toolchain policy must be available.");
        using JsonDocument policy = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory.FullName, "implementations/csharp/eng/toolchain.json")));
        string runtime = policy.RootElement.GetProperty("targetRuntime").GetString()!;
        Assert.AreEqual(".NET " + runtime, RuntimeInformation.FrameworkDescription);
        string configPath = Path.Combine(AppContext.BaseDirectory, typeof(TestRuntimePolicyTests).Assembly.GetName().Name + ".runtimeconfig.json");
        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(configPath));
        JsonElement options = config.RootElement.GetProperty("runtimeOptions");
        Assert.IsFalse(options.TryGetProperty("framework", out _), "Core-backed tests require both approved shared frameworks.");
        JsonElement[] frameworks = options.GetProperty("frameworks").EnumerateArray().ToArray();
        Assert.AreEqual(2, frameworks.Length);
        CollectionAssert.AreEquivalent(new[] { "Microsoft.NETCore.App", "Microsoft.AspNetCore.App" },
            frameworks.Select(framework => framework.GetProperty("name").GetString()).ToArray());
        foreach (JsonElement framework in frameworks)
        {
            Assert.AreEqual(runtime, framework.GetProperty("version").GetString());
        }
        Assert.AreEqual("Disable", options.GetProperty("rollForward").GetString());
    }
}
