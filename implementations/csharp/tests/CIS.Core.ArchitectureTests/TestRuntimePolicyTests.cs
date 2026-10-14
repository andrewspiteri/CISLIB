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
        Assert.AreEqual("Microsoft.NETCore.App", options.GetProperty("framework").GetProperty("name").GetString());
        Assert.AreEqual(runtime, options.GetProperty("framework").GetProperty("version").GetString());
        Assert.AreEqual("Disable", options.GetProperty("rollForward").GetString());
    }
}
