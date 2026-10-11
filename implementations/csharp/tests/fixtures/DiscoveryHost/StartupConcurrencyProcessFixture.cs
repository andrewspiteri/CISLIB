using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using CIS.Configuration;
using CIS.Logging;

namespace CIS.Tests.DiscoveryFixture;

internal static class StartupConcurrencyProcessFixture
{
    public static int Run(string scenario, string afterDirectory)
    {
        string beforeDirectory = Directory.GetCurrentDirectory();
        Require(!EnvironmentOptions.IsInitialized, "fresh process initialized");
        string? readerError = null;
        EnvironmentSnapshot? readerSnapshot = null;
        bool blockedReader = false;
        bool blockedContender = false;
        bool? initializedBeforeRetry = null;
        int successfulReaderChecks = 0;
        int uninitializedReaderChecks = 0;
        Attempt[] attempts;
        if (scenario is "held-valid" or "held-invalid")
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            using var readerStarted = new ManualResetEventSlim();
            using var contenderStarted = new ManualResetEventSlim();
            var assembly = new StartupReentrancyProcessFixture.CallbackAssembly(() => { entered.Set(); Require(release.Wait(TimeSpan.FromSeconds(5)), "held source release deadline"); });
            EnvironmentSetup firstSetup = Setup(0);
            firstSetup.ApplicationName = null; firstSetup.ApplicationAssembly = assembly;
            if (scenario == "held-invalid") { firstSetup.Logging!.QueueCapacity = 0; }
            Attempt? first = null; Attempt? second = null;
            var initial = new Thread(() => first = TryInitialize(0, firstSetup)) { IsBackground = true };
            var contender = new Thread(() => { contenderStarted.Set(); second = TryInitialize(1, Setup(1)); }) { IsBackground = true };
            var reader = new Thread(() =>
            {
                readerStarted.Set();
                try { readerSnapshot = EnvironmentOptions.Current; }
                catch (InvalidOperationException error) { readerError = error.GetType().Name; }
            })
            { IsBackground = true };
            try
            {
                initial.Start(); Require(entered.Wait(TimeSpan.FromSeconds(5)), "initial source was not entered");
                contender.Start(); reader.Start();
                Require(contenderStarted.Wait(TimeSpan.FromSeconds(5)) && readerStarted.Wait(TimeSpan.FromSeconds(5)), "contender/reader start deadline");
                Require(SpinWait.SpinUntil(() => (!contender.IsAlive || (contender.ThreadState & ThreadState.WaitSleepJoin) != 0) && (!reader.IsAlive || (reader.ThreadState & ThreadState.WaitSleepJoin) != 0), TimeSpan.FromSeconds(5)), "monitor blocking observation deadline");
                blockedContender = contender.IsAlive && (contender.ThreadState & ThreadState.WaitSleepJoin) != 0;
                blockedReader = reader.IsAlive && (reader.ThreadState & ThreadState.WaitSleepJoin) != 0;
                Require(blockedContender && blockedReader, "pending publication did not block contender and reader");
            }
            finally { release.Set(); }
            Require(initial.Join(TimeSpan.FromSeconds(5)) && contender.Join(TimeSpan.FromSeconds(5)) && reader.Join(TimeSpan.FromSeconds(5)), "held source threads did not terminate");
            attempts = [first!, second!];
        }
        else
        {
            int count = scenario == "valid-2" ? 2 : scenario == "valid-32" ? 32 : 16;
            bool allInvalid = scenario == "invalid-then-valid";
            const int readerCount = 4;
            using var start = new Barrier(count + readerCount + 1);
            Task<Attempt>[] publishers = Enumerable.Range(0, count).Select(index => Task.Factory.StartNew(() =>
            {
                EnvironmentSetup setup = Setup(index);
                if (allInvalid || (scenario == "mixed-16" && index % 2 == 0)) { setup.Logging!.QueueCapacity = 0; }
                Require(start.SignalAndWait(TimeSpan.FromSeconds(5)), "publisher barrier deadline");
                return TryInitialize(index, setup);
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
            Task[] readers = Enumerable.Range(0, readerCount).Select(_ => Task.Factory.StartNew(() =>
            {
                Require(start.SignalAndWait(TimeSpan.FromSeconds(5)), "reader barrier deadline");
                EnvironmentSnapshot? observed = null;
                for (int iteration = 0; iteration < 64; iteration++)
                {
                    try
                    {
                        EnvironmentSnapshot value = EnvironmentOptions.Current;
                        CheckCandidate(value, beforeDirectory);
                        Require(observed is null || ReferenceEquals(observed, value), "reader observed replacement");
                        observed = value;
                        Interlocked.Increment(ref successfulReaderChecks);
                    }
                    catch (InvalidOperationException) { Interlocked.Increment(ref uninitializedReaderChecks); }
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
            Require(start.SignalAndWait(TimeSpan.FromSeconds(5)), "coordinator barrier deadline");
            Task.WhenAll(publishers.Cast<Task>().Concat(readers)).WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
            attempts = publishers.Select(task => task.Result).ToArray();
            if (allInvalid)
            {
                initializedBeforeRetry = EnvironmentOptions.IsInitialized;
                Require(!initializedBeforeRetry.Value, "invalid contenders published");
                try { _ = EnvironmentOptions.Current; throw new InvalidDataException("invalid contenders exposed Current"); } catch (InvalidOperationException) { }
                attempts = [.. attempts, TryInitialize(count, Setup(count))];
            }
        }
        EnvironmentSnapshot winner = EnvironmentOptions.Current;
        Attempt successful = attempts.Single(attempt => attempt.Snapshot is not null);
        Require(ReferenceEquals(successful.Snapshot, winner), "success and Current differ");
        CheckCandidate(winner, beforeDirectory, scenario == "held-valid");
        if (readerSnapshot is not null) { Require(ReferenceEquals(readerSnapshot, winner), "held reader differs from winner"); CheckCandidate(readerSnapshot, beforeDirectory, scenario == "held-valid"); }
        string stored = JsonSerializer.Serialize(winner);
        int publishedCandidate = successful.Index;
        Directory.SetCurrentDirectory(afterDirectory);
        Require(TryInitialize(999, Setup(999)).Error == "InvalidOperationException", "later call did not reject");
        try { EnvironmentOptions.Initialize(null!); throw new InvalidDataException("null later call accepted"); } catch (ArgumentNullException error) { Require(error.ParamName == "setup", "null argument changed"); }
        Require(stored == JsonSerializer.Serialize(EnvironmentOptions.Current), "stored winner changed");
        foreach (string name in new[] { "ApplicationName", "BuildVersion", "ServiceVersion", "EnvironmentName", "HostName", "OperatingSystem", "Logging", "EventMetadata" }) { Require(Equals(typeof(EnvironmentSnapshot).GetProperty(name)!.GetValue(winner), typeof(EnvironmentOptions).GetProperty(name)!.GetValue(null)), "convenience getter differs: " + name); }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            scenario,
            publishedCandidate,
            snapshot = winner,
            attempts = attempts.Select(attempt => new { index = attempt.Index, success = attempt.Snapshot is not null, error = attempt.Error, issues = attempt.Issues }).ToArray(),
            observed = new
            {
                beforeDirectory,
                afterDirectory = Directory.GetCurrentDirectory(),
                blockedReader,
                blockedContender,
                readerError,
                readerReturnedWinner = ReferenceEquals(readerSnapshot, winner),
                successfulReaderChecks,
                uninitializedReaderChecks,
                initializedBeforeRetry,
                successfulPublishers = attempts.Count(attempt => attempt.Snapshot is not null),
                storedProjectionStable = stored == JsonSerializer.Serialize(EnvironmentOptions.Current),
                runtimeDescription = RuntimeInformation.FrameworkDescription,
                operatingSystem = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "unknown",
                fixtureSha256 = Hash(typeof(StartupConcurrencyProcessFixture).Assembly.Location),
                coreSha256 = Hash(typeof(EnvironmentOptions).Assembly.Location)
            }
        }));
        return 0;
    }
    private static Attempt TryInitialize(int index, EnvironmentSetup setup)
    {
        try { return new(index, EnvironmentOptions.Initialize(setup), null, []); }
        catch (CoreConfigurationException error) { return new(index, null, error.GetType().Name, error.Issues.Select(issue => issue.Code + ":" + issue.Key).ToArray()); }
        catch (InvalidOperationException error) { return new(index, null, error.GetType().Name, []); }
    }
    private static EnvironmentSetup Setup(int index) => new()
    {
        ApplicationName = "Candidate." + index,
        BuildVersion = "build-" + index,
        ServiceVersion = "release-" + index,
        EnvironmentName = "environment-" + index,
        EnvironmentKind = EnvironmentKind.Service,
        CommitId = index.ToString(System.Globalization.CultureInfo.InvariantCulture),
        BuildId = "id-" + index,
        Logging = new()
        {
            Level = CoreLogLevel.Warning,
            ConsoleLevel = CoreLogLevel.Trace,
            ConsoleFormat = LogOutputFormat.JsonLines,
            FileLevel = CoreLogLevel.Debug,
            FilePath = "logs/candidate-" + index + ".jsonl",
            FileFormat = LogOutputFormat.JsonLines,
            QueueCapacity = 32 + index,
            FlushIntervalMilliseconds = 100 + index,
            ShutdownTimeoutMilliseconds = 1000 + index,
            FileMaxBytes = 1048576,
            FileRetainedFiles = 2 + index % 4,
            MaxEntryBytes = 65536
        }
    };
    private static void CheckCandidate(EnvironmentSnapshot value, string directory, bool fromAssembly = false)
    {
        int index = int.Parse(value.CommitId!, System.Globalization.CultureInfo.InvariantCulture);
        Require(value.ApplicationName == (fromAssembly ? "Outer.App" : "Candidate." + index) && value.BuildVersion == "build-" + index && value.ServiceVersion == "release-" + index && value.EnvironmentName == "environment-" + index && value.BuildId == "id-" + index, "partial identity tuple");
        Require(value.Logging.QueueCapacity == 32 + index && value.Logging.FlushInterval == TimeSpan.FromMilliseconds(100 + index) && value.Logging.ShutdownTimeout == TimeSpan.FromMilliseconds(1000 + index) && value.Logging.FileRetainedFiles == 2 + index % 4 && value.Logging.FilePath == Path.Combine(directory, "logs", "candidate-" + index + ".jsonl"), "partial option tuple");
        Require(Guid.TryParseExact(value.InstanceId, "D", out Guid instance) && instance != Guid.Empty && value.InitializedAtUtc.Offset == TimeSpan.Zero, "partial initialization identity");
        foreach (PropertyInfo fact in typeof(EventMetadata).GetProperties()) { Require(Equals(fact.GetValue(value.EventMetadata), typeof(EnvironmentSnapshot).GetProperty(fact.Name)!.GetValue(value)), "partial metadata tuple"); }
    }
    private sealed record Attempt(int Index, EnvironmentSnapshot? Snapshot, string? Error, string[] Issues);
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Require(bool value, string message) { if (!value) { throw new InvalidDataException(message); } }
}
