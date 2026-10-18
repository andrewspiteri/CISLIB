using System.Text.Json.Nodes;
using CIS.TestSupport;
using CIS.Verification;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static CIS.Verification.TypeManagerVerificationInputs;

namespace CIS.Tests.ArchitectureTests;

[TestClass]
public sealed class MicrosoftLoggingCompatibilityChecksTests
{
    [TestMethod]
    [DataRow("valid")]
    [DataRow("additive")]
    [DataRow("contract")]
    [DataRow("records")]
    [DataRow("runtime")]
    [DataRow("core-path")]
    [DataRow("logging-path")]
    [DataRow("dependencyInjection-path")]
    [DataRow("consumer-path")]
    [DataRow("core-hash")]
    [DataRow("logging-hash")]
    [DataRow("dependencyInjection-hash")]
    [DataRow("consumer-hash")]
    [DataRow("selected-core")]
    public void ConsumerObservationsRetainResultRuntimeAndLoadedIdentity(string mode)
    {
        using var fixture = new ObservationFixture();
        fixture.Validate();
        if (mode == "additive")
        {
            fixture.Observed["api"]!["interfaces"]!.AsArray().Add("Additional.Interface");
            fixture.Observed["api"]!["methods"]!.AsArray().Add(new JsonObject { ["name"] = "AdditionalMethod", ["returns"] = "System.Void" });
        }
        if (mode == "contract") { fixture.Observed["contract"] = "failed"; }
        if (mode == "records") { fixture.Observed["records"] = 2; }
        if (mode == "runtime") { fixture.Observed["runtime"] = "10.0.99"; }
        if (mode.EndsWith("-path", StringComparison.Ordinal)) { fixture.Observed[mode[..^5]]!["path"] = Path.Combine(fixture.Root, "shadow.dll"); }
        if (mode.EndsWith("-hash", StringComparison.Ordinal)) { fixture.Observed[mode[..^5]]!["sha256"] = "wrong"; }
        if (mode is "valid" or "additive") { fixture.Validate(directory: mode == "valid" ? Path.Combine(fixture.Root, ".") : null); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => fixture.Validate(mode == "selected-core" ? "wrong-core" : null)); }
    }

    [TestMethod]
    [DataRow("type")]
    [DataRow("sealed")]
    [DataRow("interface")]
    [DataRow("method-missing")]
    [DataRow("method-duplicate")]
    [DataRow("returns")]
    [DataRow("constructor-count")]
    [DataRow("constructor-name")]
    [DataRow("constructor-type")]
    [DataRow("constructor-optional")]
    [DataRow("parameter-count")]
    [DataRow("parameter-name")]
    [DataRow("parameter-type")]
    [DataRow("parameter-optional")]
    public void ProviderApiKeepsRequiredMembersAndParameterDetails(string mode)
    {
        using var fixture = new ObservationFixture();
        fixture.Validate();
        JsonNode api = fixture.Observed["api"]!;
        JsonArray methods = api["methods"]!.AsArray();
        if (mode == "type") { api["type"] = "Different.Provider"; }
        if (mode == "sealed") { api["isSealed"] = false; }
        if (mode == "interface") { api["interfaces"]!.AsArray().RemoveAt(0); }
        if (mode == "method-missing") { methods.RemoveAt(0); }
        if (mode == "method-duplicate") { methods.Add(methods[0]!.DeepClone()); }
        if (mode == "returns") { methods[0]!["returns"] = "System.Object"; }
        if (mode.StartsWith("constructor-", StringComparison.Ordinal) || mode.StartsWith("parameter-", StringComparison.Ordinal))
        {
            JsonArray parameters = mode.StartsWith("constructor-", StringComparison.Ordinal) ? api["constructor"]!["parameters"]!.AsArray() : methods[0]!["signature"]!["parameters"]!.AsArray();
            string field = mode[(mode.IndexOf('-') + 1)..];
            if (field == "count") { parameters.Clear(); }
            else if (field == "optional") { parameters[0]![field] = true; }
            else { parameters[0]![field] = "changed"; }
        }
        Assert.ThrowsExactly<InvalidDataException>(() => fixture.Validate());
    }

    [TestMethod]
    [DataRow("valid")]
    [DataRow("missing-runtime")]
    [DataRow("multiple-runtime")]
    [DataRow("missing-file")]
    [DataRow("ambiguous-file")]
    public void ApprovedRuntimeDependenciesMustResolveUnambiguously(string mode)
    {
        using var fixture = new TemporaryDirectory();
        string folder = Path.Combine(fixture.Path, "cache"), core = Path.Combine(fixture.Path, "CIS.Core.dll");
        File.WriteAllText(core, "opaque resolver-policy fixture");
        var targets = new JsonObject(); var libraries = new JsonObject(); var packages = new JsonArray();
        var assets = new JsonObject { ["targets"] = new JsonObject { ["net10.0"] = targets }, ["libraries"] = libraries, ["packageFolders"] = new JsonObject { [folder] = new JsonObject() } };
        string[] names = ["Microsoft.Extensions.Logging.Abstractions", "Microsoft.Extensions.DependencyInjection.Abstractions"];
        foreach (string name in names)
        {
            string key = name + "/10.0.12", packagePath = name.ToLowerInvariant() + "/10.0.12";
            string relative = packagePath + "/lib/net10.0/" + name + ".dll";
            Write(Path.Combine(folder, relative), "opaque dependency-policy fixture");
            targets[key] = new JsonObject { ["runtime"] = new JsonObject { ["lib/net10.0/" + name + ".dll"] = new JsonObject() } };
            libraries[key] = new JsonObject { ["path"] = packagePath };
            packages.Add(new JsonObject { ["id"] = name, ["version"] = "10.0.12" });
        }
        var approved = new JsonObject { ["packages"] = packages };
        string firstKey = names[0] + "/10.0.12", relativeFile = names[0].ToLowerInvariant() + "/10.0.12/lib/net10.0/" + names[0] + ".dll";
        Assert.HasCount(3, MicrosoftLoggingCompatibilityChecks.ResolveLibraries(assets, approved, core));
        if (mode == "missing-runtime") { targets[firstKey]!.AsObject().Remove("runtime"); }
        if (mode == "multiple-runtime") { targets[firstKey]!["runtime"]!["lib/net10.0/extra.dll"] = new JsonObject(); }
        if (mode == "missing-file") { File.Delete(Path.Combine(folder, relativeFile)); }
        if (mode == "ambiguous-file")
        {
            string second = Path.Combine(fixture.Path, "other-cache");
            Write(Path.Combine(second, relativeFile), "duplicate dependency-policy fixture");
            assets["packageFolders"]![second] = new JsonObject();
        }
        if (mode == "valid") { Assert.HasCount(3, MicrosoftLoggingCompatibilityChecks.ResolveLibraries(assets, approved, core)); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => MicrosoftLoggingCompatibilityChecks.ResolveLibraries(assets, approved, core)); }
    }

    [TestMethod]
    [DataRow(".dll")]
    [DataRow(".pdb")]
    [DataRow(".deps.json")]
    [DataRow(".runtimeconfig.json")]
    public void EveryFrozenConsumerInputMustRemainUnchanged(string suffix)
    {
        using var fixture = new TemporaryDirectory();
        var pins = new JsonObject();
        foreach (string extension in new[] { ".dll", ".pdb", ".deps.json", ".runtimeconfig.json" })
        {
            string name = "CIS.Logging.ProviderContractConsumer" + extension;
            File.WriteAllText(Path.Combine(fixture.Path, name), "opaque frozen-input policy fixture");
            pins[name] = Hash(Path.Combine(fixture.Path, name));
        }
        MicrosoftLoggingCompatibilityChecks.RequireUnchanged(fixture.Path, pins);
        File.AppendAllText(Path.Combine(fixture.Path, "CIS.Logging.ProviderContractConsumer" + suffix), "changed");
        Assert.ThrowsExactly<InvalidDataException>(() => MicrosoftLoggingCompatibilityChecks.RequireUnchanged(fixture.Path, pins));
    }

    [TestMethod]
    [DataRow(0, "", true)]
    [DataRow(1, "", false)]
    [DataRow(0, "diagnostic", false)]
    public void ConsumerProcessRequiresSuccessAndEmptyStderr(int exitCode, string stderr, bool valid)
    {
        if (valid) { MicrosoftLoggingCompatibilityChecks.ValidateProcess(exitCode, stderr); }
        else { Assert.ThrowsExactly<InvalidDataException>(() => MicrosoftLoggingCompatibilityChecks.ValidateProcess(exitCode, stderr)); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ExistingOutputIsNeverOverwritten(bool populated)
    {
        using var fixture = new TemporaryDirectory();
        string sentinel = Path.Combine(fixture.Path, "original.txt");
        if (populated) { File.WriteAllText(sentinel, "original"); }
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => MicrosoftLoggingCompatibilityChecks.RunAsync(
            ["--repo-root", fixture.Path, "--candidate-core", Path.Combine(fixture.Path, "absent.dll"), "--output-root", fixture.Path]));
        Assert.AreEqual(populated ? 1 : 0, Directory.GetFileSystemEntries(fixture.Path).Length);
        if (populated) { Assert.AreEqual("original", File.ReadAllText(sentinel)); }
    }

    private sealed class ObservationFixture : IDisposable
    {
        private readonly TemporaryDirectory directory = new();
        internal string Root => directory.Path;
        internal JsonObject Api { get; }
        internal JsonObject Observed { get; }
        private readonly string corePin;

        internal ObservationFixture()
        {
            DirectoryInfo? current = new(AppContext.BaseDirectory);
            while (current is not null && !File.Exists(Path.Combine(current.FullName, "implementations/csharp/eng/toolchain.json"))) { current = current.Parent; }
            Api = Read(Path.Combine(current?.FullName ?? throw new InvalidOperationException("Repository root missing."), "implementations/csharp/verification/compatibility/microsoft-logging/api-contract.json"));
            Observed = new JsonObject { ["contract"] = "passed", ["records"] = 3, ["runtime"] = "10.0.12", ["api"] = Api.DeepClone() };
            foreach ((string key, string file) in new[] { ("core", "CIS.Core.dll"), ("logging", "Microsoft.Extensions.Logging.Abstractions.dll"), ("dependencyInjection", "Microsoft.Extensions.DependencyInjection.Abstractions.dll"), ("consumer", "CIS.Logging.ProviderContractConsumer.dll") })
            {
                string path = Path.Combine(Root, file);
                File.WriteAllText(path, "opaque loaded-identity policy fixture: " + key);
                Observed[key] = new JsonObject { ["path"] = path, ["sha256"] = Hash(path) };
            }
            corePin = (string)Observed["core"]!["sha256"]!;
        }
        internal void Validate(string? selectedCore = null, string? directory = null) => MicrosoftLoggingCompatibilityChecks.ValidateObservation(Observed, directory ?? Root, selectedCore ?? corePin, Api, "10.0.12");
        public void Dispose() => directory.Dispose();
    }
}
