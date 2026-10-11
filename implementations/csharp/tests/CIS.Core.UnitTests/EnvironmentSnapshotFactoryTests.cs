using System.Reflection;
using System.Text.Json.Nodes;
using CIS.Configuration;
using CIS.Logging;
using CIS.Tests.UnitTests.Discovery;
using CIS.TestSupport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests;

[TestClass]
public sealed class EnvironmentSnapshotFactoryTests
{
    public static IEnumerable<object[]> SurfaceTypes => new[] { "CIS.EnvironmentKind", "CIS.Logging.CoreLogLevel", "CIS.Logging.LogOutputFormat", "CIS.Logging.ConsoleOutputStream", "CIS.Logging.HumanOutputDetail", "CIS.EnvironmentSetup", "CIS.Logging.LoggingSetup", "CIS.EnvironmentSnapshot", "CIS.Logging.LoggingOptions", "CIS.Logging.EventMetadata", "CIS.EnvironmentOptions" }.Select(name => new object[] { name });

    [TestMethod]
    public void BuilderComposesValidatedPublicOptionsAndAllowlistedMetadataWithoutPublication()
    {
        using var owned = new TemporaryDirectory();
        var source = new EnvironmentVariableStub();
        source.Values["CORE_SERVICE_NAME"] = " Env.App ";
        source.Values["CORE_SERVICE_VERSION"] = "deploy-release";
        source.Values["DD_AGENT_HOST"] = "never-use-collector";
        var runtime = new RuntimeMetadataStub();
        var clock = new InitializationTimeProvider(new DateTimeOffset(2026, 10, 11, 4, 5, 6, TimeSpan.FromHours(2)));
        int ids = 0;
        int directories = 0;
        Guid id = Guid.Parse("5416ea07-dcd9-4f80-8daa-816ea978a31e");
        var setup = new EnvironmentSetup
        {
            ApplicationName = "Setup.App",
            BuildVersion = "1.2.3-rc.4+build.5",
            ServiceVersion = "setup-release",
            EnvironmentName = "Live-\u03a9",
            EnvironmentKind = EnvironmentKind.Service,
            CommitId = "commit-8",
            BuildId = "build-9",
            Logging = new LoggingSetup { Level = CoreLogLevel.Warning, ConsoleLevel = CoreLogLevel.None, ConsoleFormat = LogOutputFormat.JsonLines, FilePath = "logs/~app.jsonl", FileLevel = CoreLogLevel.Debug, FileFormat = LogOutputFormat.JsonLines, ConsoleStream = ConsoleOutputStream.StandardOutput, HumanDetail = HumanOutputDetail.Verbose, QueueCapacity = 2, FlushIntervalMilliseconds = 11, ShutdownTimeoutMilliseconds = 12, FileMaxBytes = 131072, FileRetainedFiles = 3, MaxEntryBytes = 65536 }
        };
        EnvironmentSnapshot snapshot = EnvironmentSnapshotFactory.Create(setup, source, runtimeSource: runtime, readWorkingDirectory: () => { directories++; return owned.Path; }, initializationFactory: new(clock, () => { ids++; return id; }));
        Assert.IsFalse(EnvironmentOptions.IsInitialized);
        Assert.AreEqual("Env.App", snapshot.ApplicationName);
        Assert.AreEqual("1.2.3-rc.4+build.5", snapshot.BuildVersion);
        Assert.AreEqual("deploy-release", snapshot.ServiceVersion);
        Assert.AreEqual("Live-\u03a9", snapshot.EnvironmentName);
        Assert.AreEqual(EnvironmentKind.Service, snapshot.EnvironmentKind);
        Assert.AreEqual(id.ToString("D"), snapshot.InstanceId);
        Assert.AreEqual(clock.UtcNow.ToUniversalTime(), snapshot.InitializedAtUtc);
        Assert.AreEqual(runtime.ProcessStartedAt!.Value.ToUniversalTime(), snapshot.ProcessStartedAtUtc);
        Assert.AreEqual("commit-8", snapshot.CommitId);
        Assert.AreEqual("build-9", snapshot.BuildId);
        Assert.AreEqual("ApplicationVisible.Host", snapshot.HostName);
        Assert.AreEqual("windows", snapshot.OperatingSystem);
        Assert.AreEqual("x64", snapshot.OSArchitecture);
        Assert.AreEqual("x86", snapshot.ProcessArchitecture);
        Assert.AreEqual(123, snapshot.ProcessId);
        Assert.AreEqual(".NET Fixture", snapshot.RuntimeDescription);
        Assert.AreEqual(CoreLogLevel.Warning, snapshot.Logging.Level);
        Assert.AreEqual(CoreLogLevel.Debug, snapshot.Logging.FileLevel);
        Assert.AreEqual(LogOutputFormat.JsonLines, snapshot.Logging.ConsoleFormat);
        Assert.AreEqual(ConsoleOutputStream.StandardOutput, snapshot.Logging.ConsoleStream);
        Assert.AreEqual(HumanOutputDetail.Verbose, snapshot.Logging.HumanDetail);
        Assert.IsFalse(snapshot.Logging.ConsoleEnabled);
        Assert.IsTrue(snapshot.Logging.FileEnabled);
        Assert.AreEqual(Path.Combine(owned.Path, "logs", "~app.jsonl"), snapshot.Logging.FilePath);
        Assert.AreEqual(TimeSpan.FromMilliseconds(11), snapshot.Logging.FlushInterval);
        Assert.AreEqual(TimeSpan.FromMilliseconds(12), snapshot.Logging.ShutdownTimeout);
        Assert.AreEqual(2, snapshot.Logging.QueueCapacity);
        Assert.AreEqual(131072L, snapshot.Logging.FileMaxBytes);
        Assert.AreEqual(3, snapshot.Logging.FileRetainedFiles);
        Assert.AreEqual(65536, snapshot.Logging.MaxEntryBytes);
        foreach (PropertyInfo property in typeof(EventMetadata).GetProperties())
        {
            Assert.AreEqual(typeof(EnvironmentSnapshot).GetProperty(property.Name)!.GetValue(snapshot), property.GetValue(snapshot.EventMetadata));
        }
        Assert.IsNull(typeof(EventMetadata).GetProperty("Logging"));
        Assert.IsNull(typeof(EventMetadata).GetProperty("FilePath"));
        Assert.AreEqual(1, directories);
        Assert.AreEqual(1, ids);
        Assert.AreEqual(1, clock.Reads);
        Assert.AreEqual(21, source.Reads.Count);
        Assert.AreEqual(8, runtime.Reads.Count);
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());
    }

    [TestMethod]
    public void DefaultPublicInputsRemainAbsentAndResolveToDocumentedValues()
    {
        var setup = new EnvironmentSetup();
        Assert.AreEqual(EnvironmentKind.Unknown, setup.EnvironmentKind);
        foreach (PropertyInfo property in typeof(EnvironmentSetup).GetProperties().Where(property => property.Name != "EnvironmentKind")) { Assert.IsNull(property.GetValue(setup)); }
        var logging = new LoggingSetup();
        foreach (PropertyInfo property in typeof(LoggingSetup).GetProperties()) { Assert.IsNull(property.GetValue(logging)); }
        using var owned = new TemporaryDirectory();
        EnvironmentSnapshot result = Create(new() { ApplicationName = "Fixture.App", BuildVersion = "fixture-build" }, owned.Path);
        Assert.AreEqual(CoreLogLevel.Information, result.Logging.Level);
        Assert.AreEqual(CoreLogLevel.Information, result.Logging.ConsoleLevel);
        Assert.AreEqual(CoreLogLevel.Information, result.Logging.FileLevel);
        Assert.AreEqual(LogOutputFormat.Human, result.Logging.ConsoleFormat);
        Assert.AreEqual(LogOutputFormat.JsonLines, result.Logging.FileFormat);
        Assert.AreEqual(ConsoleOutputStream.StandardError, result.Logging.ConsoleStream);
        Assert.AreEqual(HumanOutputDetail.Normal, result.Logging.HumanDetail);
        Assert.AreEqual(1024, result.Logging.QueueCapacity);
        Assert.AreEqual(65536, result.Logging.MaxEntryBytes);
        Assert.AreEqual(TimeSpan.FromMilliseconds(1000), result.Logging.FlushInterval);
        Assert.AreEqual(TimeSpan.FromMilliseconds(5000), result.Logging.ShutdownTimeout);
        Assert.AreEqual(10485760L, result.Logging.FileMaxBytes);
        Assert.AreEqual(10, result.Logging.FileRetainedFiles);
        Assert.AreEqual(1, result.Logging.SchemaVersion);
        Assert.IsNull(result.Logging.FilePath);
        Assert.IsNull(result.CommitId);
        Assert.IsNull(result.BuildId);
        Assert.IsTrue(result.Logging.ConsoleEnabled);
        Assert.IsFalse(result.Logging.FileEnabled);
    }

    [TestMethod]
    [DataRow("EnvironmentKind")]
    [DataRow("Level")]
    [DataRow("ConsoleFormat")]
    [DataRow("ConsoleLevel")]
    [DataRow("ConsoleStream")]
    [DataRow("HumanDetail")]
    [DataRow("FileFormat")]
    [DataRow("FileLevel")]
    public void UndefinedPublicEnumsReachActualValidationWithoutNarrowing(string field)
    {
        var setup = new EnvironmentSetup { ApplicationName = "Fixture.App", BuildVersion = "fixture-build", Logging = new LoggingSetup() };
        PropertyInfo property = field == "EnvironmentKind" ? typeof(EnvironmentSetup).GetProperty(field)! : typeof(LoggingSetup).GetProperty(field)!;
        Type enumType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        property.SetValue(field == "EnvironmentKind" ? setup : setup.Logging, Enum.ToObject(enumType, 99));
        using var owned = new TemporaryDirectory();
        CoreConfigurationException error = Assert.ThrowsExactly<CoreConfigurationException>(() => Create(setup, owned.Path));
        Assert.AreEqual(1, error.Issues.Count);
        Assert.AreEqual(field == "EnvironmentKind" ? ConfigurationIssueCodes.InvalidEnvironmentKind : ConfigurationIssueCodes.InvalidToken, error.Issues[0].Code);
        Assert.AreEqual(field == "EnvironmentKind" ? "EnvironmentSetup.EnvironmentKind" : "LoggingSetup." + field, error.Issues[0].Key);
    }

    [TestMethod]
    public void InvalidConfigurationDoesNotReadRuntimeOrGenerateInitializationIdentity()
    {
        using var owned = new TemporaryDirectory();
        var runtime = new RuntimeMetadataStub();
        int ids = 0;
        var setup = new EnvironmentSetup { ApplicationName = "Fixture.App", BuildVersion = "fixture-build", Logging = new() { QueueCapacity = 0, FilePath = "bad" + (char)0 } };
        CoreConfigurationException error = Assert.ThrowsExactly<CoreConfigurationException>(() => EnvironmentSnapshotFactory.Create(setup, new EnvironmentVariableStub(), runtimeSource: runtime, readWorkingDirectory: () => owned.Path, initializationFactory: new(new InitializationTimeProvider(DateTimeOffset.UtcNow), () => { ids++; return Guid.NewGuid(); })));
        CollectionAssert.AreEqual(new[] { "LoggingSetup.FilePath", "LoggingSetup.QueueCapacity" }, error.Issues.Select(issue => issue.Key).ToArray());
        Assert.AreEqual(0, runtime.Reads.Count);
        Assert.AreEqual(0, ids);
        Assert.IsFalse(EnvironmentOptions.IsInitialized);
        Assert.AreEqual(0, Directory.EnumerateFileSystemEntries(owned.Path).Count());
    }

    [TestMethod]
    public void RequiredBuilderSourceFailuresPropagateWithoutInventingSnapshotValues()
    {
        using var owned = new TemporaryDirectory();
        var setup = new EnvironmentSetup { ApplicationName = "Fixture.App", BuildVersion = "fixture-build" };
        var failure = new IOException("controlled required source failure");
        Assert.AreSame(failure, Assert.ThrowsExactly<IOException>(() => EnvironmentSnapshotFactory.Create(setup, new EnvironmentVariableStub(), readWorkingDirectory: () => throw failure)));
        Assert.AreSame(failure, Assert.ThrowsExactly<IOException>(() => EnvironmentSnapshotFactory.Create(setup, new EnvironmentVariableStub { FailingName = "CORE_LOG_LEVEL", Failure = failure }, readWorkingDirectory: () => owned.Path)));
        Assert.ThrowsExactly<InvalidOperationException>(() => EnvironmentSnapshotFactory.Create(setup, new EnvironmentVariableStub(), runtimeSource: new RuntimeMetadataStub(), readWorkingDirectory: () => owned.Path, initializationFactory: new(new InitializationTimeProvider(DateTimeOffset.UtcNow), () => Guid.Empty)));
        Assert.ThrowsExactly<ArgumentNullException>(() => EnvironmentSnapshotFactory.Create(null!));
        Assert.IsFalse(EnvironmentOptions.IsInitialized);
    }

    [TestMethod]
    public void UnavailableOptionalRuntimeFactsStayNullInBothPublicProjections()
    {
        using var owned = new TemporaryDirectory();
        var runtime = new RuntimeMetadataStub { ProcessStartedAt = null, HostName = null, Platform = null, OSDescription = null, OSArchitecture = null, ProcessArchitecture = null, RuntimeDescription = null, ProcessId = null };
        EnvironmentSnapshot result = EnvironmentSnapshotFactory.Create(new() { ApplicationName = "Fixture.App", BuildVersion = "fixture-build" }, new EnvironmentVariableStub(), runtimeSource: runtime, readWorkingDirectory: () => owned.Path);
        Assert.AreEqual("unknown", result.OperatingSystem);
        foreach (string field in new[] { "HostName", "ProcessStartedAtUtc", "OperatingSystemDescription", "OSArchitecture", "ProcessArchitecture", "RuntimeDescription", "ProcessId", "CommitId", "BuildId" })
        {
            Assert.IsNull(typeof(EnvironmentSnapshot).GetProperty(field)!.GetValue(result));
            Assert.IsNull(typeof(EventMetadata).GetProperty(field)!.GetValue(result.EventMetadata));
        }
    }

    [TestMethod]
    [DynamicData(nameof(SurfaceTypes))]
    public void ActualStartupTypesMatchTheDeclaredPublicSignatureAndImmutability(string name)
    {
        JsonObject spec = ReadSurface();
        Type type = typeof(EnvironmentOptions).Assembly.GetType(name)!;
        Assert.IsNotNull(type);
        Assert.IsTrue(type.IsPublic);
        if (type.IsEnum)
        {
            JsonObject members = spec["enums"]![name]!.AsObject();
            CollectionAssert.AreEqual(members.Select(pair => pair.Key).Order().ToArray(), Enum.GetNames(type).Order().ToArray());
            foreach ((string member, JsonNode? value) in members) { Assert.AreEqual(value!.GetValue<int>(), Convert.ToInt32(Enum.Parse(type, member), System.Globalization.CultureInfo.InvariantCulture)); }
            return;
        }
        string part = name switch { "CIS.EnvironmentSetup" => "setup", "CIS.Logging.LoggingSetup" => "loggingSetup", "CIS.EnvironmentSnapshot" => "snapshot", "CIS.Logging.LoggingOptions" => "logging", "CIS.Logging.EventMetadata" => "eventMetadata", _ => "convenienceGetters" };
        bool input = part is "setup" or "loggingSetup";
        Assert.IsTrue(type.IsSealed);
        Assert.AreEqual(spec[part]!.AsObject().Count + (name == "CIS.EnvironmentOptions" ? 2 : 0), type.GetProperties().Length);
        foreach ((string propertyName, JsonNode? kind) in spec[part]!.AsObject())
        {
            PropertyInfo property = type.GetProperty(propertyName)!;
            Assert.IsNotNull(property, propertyName);
            Assert.AreEqual(kind!.GetValue<string>(), TypeName(property), propertyName);
            Assert.AreEqual(input, property.SetMethod is not null, propertyName);
            if (!input) { Assert.IsNull(property.GetSetMethod(nonPublic: true)); }
        }
        if (input) { Assert.IsNotNull(type.GetConstructor(Type.EmptyTypes)); }
        else { Assert.AreEqual(0, type.GetConstructors().Length); }
        if (name == "CIS.EnvironmentOptions")
        {
            MethodInfo method = type.GetMethod("Initialize")!;
            Assert.AreEqual(typeof(EnvironmentSnapshot), method.ReturnType);
            Assert.AreEqual("setup", method.GetParameters().Single().Name);
            Assert.AreEqual(typeof(EnvironmentSetup), method.GetParameters().Single().ParameterType);
            Assert.IsNull(type.GetMethod("Reset"));
            Assert.IsNull(typeof(EnvironmentOptions).Assembly.GetType("CIS.EnviormentsOptions"));
        }
    }

    private static EnvironmentSnapshot Create(EnvironmentSetup setup, string directory) => EnvironmentSnapshotFactory.Create(setup, new EnvironmentVariableStub(), runtimeSource: new RuntimeMetadataStub(), readWorkingDirectory: () => directory);
    private static string TypeName(PropertyInfo property)
    {
        Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        string name = type == typeof(string) ? "string" : type == typeof(int) ? "int" : type == typeof(long) ? "long" : type == typeof(bool) ? "bool" : type.Name;
        return name + (new NullabilityInfoContext().Create(property).ReadState == NullabilityState.Nullable ? "?" : "");
    }
    private static JsonObject ReadSurface()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string path = Path.Combine(directory.FullName, "implementations", "csharp", "verification", "contracts", "startup", "surface.json");
            if (File.Exists(path)) { return JsonNode.Parse(File.ReadAllText(path))!.AsObject(); }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Cannot find the declared startup surface.");
    }
}
