using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using G = CIS.Verification.HealthcheckGroupedMutation;

namespace CIS.Tests.Verification;

// Archive/hash parser fixtures only: these byte strings are not executed native tools.
[TestClass]
public sealed class HealthcheckPlatformProofArchiveTests
{
    [TestMethod]
    [DataRow("valid")]
    [DataRow("changed-bytes")]
    [DataRow("extra-file")]
    [DataRow("missing-file")]
    [DataRow("renamed-file")]
    public void PortableSourceArchiveMustContainEveryExactFrozenInput(string change)
    {
        using var directory = new TemporaryDirectory();
        string path = Path.Combine(directory.Path, "source.zip");
        byte[] original = Encoding.UTF8.GetBytes("frozen current source");
        using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            if (change != "missing-file") { Put(archive, change == "renamed-file" ? "../outside.cs" : "input.cs", change == "changed-bytes" ? [1, 2, 3] : original); }
            if (change == "extra-file") { Put(archive, "unbound.cs", original); }
        }
        G.Input[] inputs = [new("input.cs", Hash(original))];
        if (change == "valid") { G.VerifyPlatformArchive(path, inputs); }
        else if (change == "renamed-file") { Assert.ThrowsExactly<InvalidOperationException>(() => G.VerifyPlatformArchive(path, inputs)); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => G.VerifyPlatformArchive(path, inputs)); }
    }

    [TestMethod]
    [DataRow("valid")]
    [DataRow("missing-frozen-tool")]
    [DataRow("missing-adapter")]
    [DataRow("duplicate-original")]
    [DataRow("duplicate-entry")]
    [DataRow("extra-archive-entry")]
    [DataRow("changed-bytes")]
    [DataRow("changed-byte-count")]
    [DataRow("unsafe-entry")]
    [DataRow("changed-cli")]
    [DataRow("changed-producer")]
    [DataRow("changed-adapter")]
    [DataRow("empty-adapters")]
    public void PortableToolClosureCannotLoseRebindOrChangeAnActualPinnedInput(string change)
    {
        using var directory = new TemporaryDirectory();
        string[] names = ["Stryker.CLI.dll", "Stryker.Core.dll", "CIS.DependencyChecker.dll", "CIS.Core.TestSupport.dll", "MSTest.TestAdapter.dll"];
        byte[][] contents = names.Select(name => Encoding.UTF8.GetBytes("parser control: " + name)).ToArray();
        G.PlatformTool[] pins = names.Select((name, index) => new G.PlatformTool("C:\\frozen\\" + name,
            index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + "/" + name, Hash(contents[index]), contents[index].Length)).ToArray();
        G.Input[] toolFiles = pins.Take(4).Select(pin => new G.Input(pin.OriginalPath, pin.Sha256)).ToArray();
        G.Input[] adapters = [new(names[4], pins[4].Sha256)];
        var tools = new G.Toolchain("cis.healthcheck.grouped-toolchain/v1", "Windows", "host", "subject", "tool", "10.0.401", "10.0.12", "8.0.31", toolFiles, adapters);
        var main = tools with { Files = toolFiles.Select(pin => pin with { Path = "/chosen/" + pin.Path.Replace('\\', '/').Split('/')[^1] }).ToArray() };
        string producer = pins[2].Sha256, helper = pins[3].Sha256;
        if (change == "missing-frozen-tool") { pins = pins.Where(pin => !pin.OriginalPath.EndsWith("Stryker.Core.dll", StringComparison.Ordinal)).ToArray(); }
        if (change == "missing-adapter") { pins = pins[..4]; }
        if (change == "duplicate-original") { pins = [.. pins, pins[0] with { Entry = "9999/copy.dll" }]; }
        if (change == "duplicate-entry") { pins = [.. pins, pins[0] with { OriginalPath = "C:\\another\\copy.dll" }]; }
        if (change == "changed-byte-count") { pins[0] = pins[0] with { Bytes = pins[0].Bytes + 1 }; }
        if (change == "unsafe-entry") { pins[0] = pins[0] with { Entry = "../Stryker.CLI.dll" }; }
        if (change == "changed-cli") { main = main with { Files = main.Files.Select(pin => pin.Path.EndsWith("Stryker.CLI.dll", StringComparison.Ordinal) ? pin with { Sha256 = "other" } : pin).ToArray() }; }
        if (change == "changed-producer") { producer = "other"; }
        if (change == "changed-adapter") { main = main with { Adapters = [new(names[4], "other")] }; }
        if (change == "empty-adapters") { tools = tools with { Adapters = [] }; }
        string path = Path.Combine(directory.Path, "tools.zip");
        using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (G.PlatformTool pin in pins)
            {
                int index = Array.FindIndex(names, name => pin.OriginalPath.EndsWith(name, StringComparison.Ordinal));
                byte[] bytes = index >= 0 ? contents[index] : [0];
                Put(archive, pin.Entry, change == "changed-bytes" && index == 0 ? [1, 2, 3] : bytes);
            }
            if (change == "extra-archive-entry") { Put(archive, "9999/unbound.dll", [0]); }
        }
        if (change == "valid") { G.ValidatePlatformTools(path, pins, tools, main, producer, helper); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => G.ValidatePlatformTools(path, pins, tools, main, producer, helper)); }
    }

    private static void Put(ZipArchive archive, string name, byte[] bytes)
    {
        using Stream destination = archive.CreateEntry(name, CompressionLevel.Optimal).Open(); destination.Write(bytes);
    }
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
