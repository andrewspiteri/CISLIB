using System.Reflection;
using System.Text.Json;
using CIS.Configuration;
using CIS.Logging;
using CIS.Tests.UnitTests.Discovery;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class StartupInputOwnershipTests
{
    private static readonly Guid Instance = Guid.Parse("79c80557-1a03-4b3e-9783-43bf152115ae");
    private static readonly DateTimeOffset InitializedAt = new(2026, 10, 11, 5, 0, 0, TimeSpan.Zero);
    private static readonly string[] SetupFields = ["ApplicationAssembly", "ApplicationName", "BuildVersion", "ServiceVersion", "EnvironmentName", "EnvironmentKind", "CommitId", "BuildId", "Logging"];
    private static readonly string[] LoggingFields = ["Level", "ConsoleFormat", "ConsoleLevel", "ConsoleStream", "HumanDetail", "FilePath", "FileFormat", "FileLevel", "QueueCapacity", "FlushIntervalMilliseconds", "ShutdownTimeoutMilliseconds", "FileMaxBytes", "FileRetainedFiles", "SchemaVersion", "MaxEntryBytes"];
    public static IEnumerable<object[]> CapturedFields => from point in new[] { "directory", "variables" } from inputField in SetupFields.Select(name => "setup." + name).Concat(LoggingFields.Select(name => "logging." + name)) select new object[] { point, inputField };

    [TestMethod]
    [DynamicData(nameof(CapturedFields))]
    public void EveryStartupFieldIsCopiedBeforeRequiredSourcesCanChangeTheCaller(string point, string field)
    {
        CollectionAssert.AreEquivalent(SetupFields, typeof(EnvironmentSetup).GetProperties().Select(property => property.Name).ToArray());
        CollectionAssert.AreEquivalent(LoggingFields, typeof(LoggingSetup).GetProperties().Select(property => property.Name).ToArray());
        using var owned = new TemporaryDirectory();
        EnvironmentSetup setup = CreateSetup();
        LoggingSetup logging = setup.Logging!;
        EnvironmentSnapshot baseline = Create(setup, owned.Path);
        AssertOriginal(baseline, owned.Path);
        object target = field.StartsWith("setup.", StringComparison.Ordinal) ? setup : logging;
        PropertyInfo property = target.GetType().GetProperty(field[(field.IndexOf('.') + 1)..])!;
        object? previous = property.GetValue(target);
        var source = new CallbackVariableSource(point == "variables" ? () => Change(target, property) : null);
        int directories = 0;
        EnvironmentSnapshot result = EnvironmentSnapshotFactory.Create(setup, source, runtimeSource: new RuntimeMetadataStub(), readWorkingDirectory: () =>
        {
            directories++;
            if (point == "directory") { Change(target, property); }
            return owned.Path;
        }, initializationFactory: Identity());
        Assert.AreNotEqual(previous, property.GetValue(target), field);
        AssertOriginal(result, owned.Path);
        Assert.AreEqual(JsonSerializer.Serialize(baseline), JsonSerializer.Serialize(result), field);
        Assert.AreEqual(1, directories);
        Assert.AreEqual(21, source.Reads);
        Assert.IsFalse(EnvironmentOptions.IsInitialized);
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());
    }

    [TestMethod]
    [DataRow("directory")]
    [DataRow("variables")]
    public void ApplicationAssemblyIsCapturedBeforeCallerIdentityAndAssemblyReplacement(string point)
    {
        using var owned = new TemporaryDirectory();
        EnvironmentSetup setup = CreateSetup();
        setup.ApplicationName = null; setup.BuildVersion = null;
        Assembly original = setup.ApplicationAssembly!;
        var metadata = new AssemblyMetadataStub();
        void Replace() { setup.ApplicationAssembly = typeof(EnvironmentOptions).Assembly; setup.ApplicationName = "Changed.App"; setup.BuildVersion = "changed-build"; }
        var source = new CallbackVariableSource(point == "variables" ? Replace : null);
        EnvironmentSnapshot result = EnvironmentSnapshotFactory.Create(setup, source, metadata, new RuntimeMetadataStub(), () => { if (point == "directory") { Replace(); } return owned.Path; }, Identity());
        Assert.AreSame(typeof(EnvironmentOptions).Assembly, setup.ApplicationAssembly);
        Assert.AreEqual("Fixture.App", result.ApplicationName);
        Assert.AreEqual("3.4.5-rc.2+sha.AbC123", result.BuildVersion);
        Assert.AreEqual("owned-release", result.ServiceVersion);
        CollectionAssert.AreEqual(new[] { "name", "information" }, metadata.Reads);
        Assert.AreEqual(2, metadata.ObservedAssemblies.Count);
        Assert.IsTrue(metadata.ObservedAssemblies.All(assembly => ReferenceEquals(original, assembly)));
        Assert.IsFalse(EnvironmentOptions.IsInitialized);
    }

    [TestMethod]
    [DataRow("directory")]
    [DataRow("variables")]
    public void CallerRepairAfterCaptureCannotBypassValidationOfTheCapturedInputs(string point)
    {
        using var owned = new TemporaryDirectory();
        EnvironmentSetup setup = CreateSetup();
        setup.Logging!.QueueCapacity = 0; setup.Logging.FilePath = "invalid" + (char)0;
        void Repair() { setup.Logging = CreateSetup().Logging; }
        var source = new CallbackVariableSource(point == "variables" ? Repair : null);
        var runtime = new RuntimeMetadataStub();
        int ids = 0;
        CoreConfigurationException error = Assert.ThrowsExactly<CoreConfigurationException>(() => EnvironmentSnapshotFactory.Create(setup, source, runtimeSource: runtime, readWorkingDirectory: () => { if (point == "directory") { Repair(); } return owned.Path; }, initializationFactory: new(new InitializationTimeProvider(InitializedAt), () => { ids++; return Instance; })));
        CollectionAssert.AreEqual(new[] { "InvalidPath:LoggingSetup.FilePath", "OutOfRange:LoggingSetup.QueueCapacity" }, error.Issues.Select(issue => issue.Code + ":" + issue.Key).ToArray());
        Assert.AreEqual(17, setup.Logging!.QueueCapacity);
        Assert.AreEqual(0, ids); Assert.AreEqual(0, runtime.Reads.Count);
        Assert.IsFalse(EnvironmentOptions.IsInitialized);
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());
    }

    [TestMethod]
    public void SeparateResolvedSnapshotsOwnTheirNestedOutputsAfterSetupReuse()
    {
        using var owned = new TemporaryDirectory();
        EnvironmentSetup setup = CreateSetup();
        EnvironmentSnapshot first = Create(setup, owned.Path);
        AssertOriginal(first, owned.Path);
        string stored = JsonSerializer.Serialize(first);
        setup.ApplicationName = "Second.App"; setup.BuildVersion = "second-build"; setup.ServiceVersion = "second-release";
        setup.EnvironmentName = "Second.Live"; setup.EnvironmentKind = EnvironmentKind.Web;
        setup.CommitId = "second-commit"; setup.BuildId = "second-id";
        setup.Logging = new() { Level = CoreLogLevel.Debug, FilePath = "second.jsonl", MaxEntryBytes = 131072 };
        EnvironmentSnapshot second = Create(setup, owned.Path);
        Assert.AreNotSame(first, second); Assert.AreNotSame(first.Logging, second.Logging); Assert.AreNotSame(first.EventMetadata, second.EventMetadata);
        Assert.AreEqual("Second.App", second.ApplicationName); Assert.AreEqual("second-build", second.BuildVersion); Assert.AreEqual("second-release", second.ServiceVersion);
        Assert.AreEqual("Second.Live", second.EnvironmentName); Assert.AreEqual(EnvironmentKind.Web, second.EnvironmentKind);
        Assert.AreEqual("second-commit", second.CommitId); Assert.AreEqual("second-id", second.BuildId);
        Assert.AreEqual(CoreLogLevel.Debug, second.Logging.Level); Assert.AreEqual(Path.Combine(owned.Path, "second.jsonl"), second.Logging.FilePath); Assert.AreEqual(131072, second.Logging.MaxEntryBytes);
        Assert.AreEqual(stored, JsonSerializer.Serialize(first));
        AssertOriginal(first, owned.Path);
    }

    private static EnvironmentSetup CreateSetup() => new()
    {
        ApplicationAssembly = typeof(StartupInputOwnershipTests).Assembly,
        ApplicationName = " Owned.App ",
        BuildVersion = "1.2.3-rc.4+abc",
        ServiceVersion = " owned-release ",
        EnvironmentName = " Owned.Live ",
        EnvironmentKind = EnvironmentKind.Function,
        CommitId = " commit-owned ",
        BuildId = " build-owned ",
        Logging = new()
        {
            Level = CoreLogLevel.Warning,
            ConsoleFormat = LogOutputFormat.JsonLines,
            ConsoleLevel = CoreLogLevel.Trace,
            ConsoleStream = ConsoleOutputStream.StandardOutput,
            HumanDetail = HumanOutputDetail.Verbose,
            FilePath = "logs/~owned.jsonl",
            FileFormat = LogOutputFormat.Human,
            FileLevel = CoreLogLevel.Critical,
            QueueCapacity = 17,
            FlushIntervalMilliseconds = 31,
            ShutdownTimeoutMilliseconds = 61,
            FileMaxBytes = 131072,
            FileRetainedFiles = 7,
            SchemaVersion = 1,
            MaxEntryBytes = 65536
        }
    };
    private static void Change(object target, PropertyInfo property)
    {
        Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        object replacement = type.IsEnum ? Enum.ToObject(type, 99) : type == typeof(string) ? "CIS_CHANGED_" + property.Name : type == typeof(int) ? 0 : type == typeof(long) ? 0L : type == typeof(Assembly) ? typeof(EnvironmentOptions).Assembly : new LoggingSetup { QueueCapacity = 0 };
        property.SetValue(target, replacement);
    }
    private static InitializationIdentityFactory Identity() => new(new InitializationTimeProvider(InitializedAt), () => Instance);
    private static EnvironmentSnapshot Create(EnvironmentSetup setup, string path) => EnvironmentSnapshotFactory.Create(setup, new EnvironmentVariableStub(), runtimeSource: new RuntimeMetadataStub(), readWorkingDirectory: () => path, initializationFactory: Identity());
    private static void AssertOriginal(EnvironmentSnapshot snapshot, string path)
    {
        Assert.AreEqual("Owned.App", snapshot.ApplicationName); Assert.AreEqual("1.2.3-rc.4+abc", snapshot.BuildVersion); Assert.AreEqual("owned-release", snapshot.ServiceVersion);
        Assert.AreEqual("Owned.Live", snapshot.EnvironmentName); Assert.AreEqual(EnvironmentKind.Function, snapshot.EnvironmentKind);
        Assert.AreEqual("commit-owned", snapshot.CommitId); Assert.AreEqual("build-owned", snapshot.BuildId);
        Assert.AreEqual(Instance.ToString("D"), snapshot.InstanceId); Assert.AreEqual(InitializedAt, snapshot.InitializedAtUtc);
        var expected = new Dictionary<string, object?>
        {
            ["Level"] = CoreLogLevel.Warning,
            ["ConsoleFormat"] = LogOutputFormat.JsonLines,
            ["ConsoleLevel"] = CoreLogLevel.Trace,
            ["ConsoleStream"] = ConsoleOutputStream.StandardOutput,
            ["HumanDetail"] = HumanOutputDetail.Verbose,
            ["FilePath"] = Path.Combine(path, "logs", "~owned.jsonl"),
            ["FileFormat"] = LogOutputFormat.Human,
            ["FileLevel"] = CoreLogLevel.Critical,
            ["QueueCapacity"] = 17,
            ["FlushInterval"] = TimeSpan.FromMilliseconds(31),
            ["ShutdownTimeout"] = TimeSpan.FromMilliseconds(61),
            ["FileMaxBytes"] = 131072L,
            ["FileRetainedFiles"] = 7,
            ["SchemaVersion"] = 1,
            ["MaxEntryBytes"] = 65536,
            ["ConsoleEnabled"] = true,
            ["FileEnabled"] = true
        };
        Assert.AreEqual(expected.Count, typeof(LoggingOptions).GetProperties().Length);
        foreach ((string field, object? value) in expected) { Assert.AreEqual(value, typeof(LoggingOptions).GetProperty(field)!.GetValue(snapshot.Logging), field); }
        foreach (PropertyInfo fact in typeof(EventMetadata).GetProperties()) { Assert.AreEqual(fact.GetValue(snapshot.EventMetadata), typeof(EnvironmentSnapshot).GetProperty(fact.Name)!.GetValue(snapshot), fact.Name); }
    }

    private sealed class CallbackVariableSource(Action? afterCapture) : IEnvironmentVariableSource
    {
        public int Reads { get; private set; }
        public string? GetValue(string name) { if (Reads++ == 0) { afterCapture?.Invoke(); } return null; }
    }
}
