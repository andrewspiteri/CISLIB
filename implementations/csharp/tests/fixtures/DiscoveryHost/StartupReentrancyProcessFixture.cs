using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using CIS.Configuration;

namespace CIS.Tests.DiscoveryFixture;

internal static class StartupReentrancyProcessFixture
{
    public static int Run(string scenario)
    {
        EnvironmentSnapshot? nested = null;
        string? nestedError = null;
        bool pendingInitialized = true;
        string? pendingCurrentError = null;
        string? nestedNullParameter = null;
        var sourceFailure = new ControlledRequiredSourceException();
        var assembly = new CallbackAssembly(() =>
        {
            pendingInitialized = EnvironmentOptions.IsInitialized;
            try { _ = EnvironmentOptions.Current; } catch (InvalidOperationException error) { pendingCurrentError = error.GetType().Name; }
            try { EnvironmentOptions.Initialize(null!); } catch (ArgumentNullException error) { nestedNullParameter = error.ParamName; }
            try { nested = EnvironmentOptions.Initialize(new() { ApplicationName = "Nested.App", BuildVersion = "nested-build" }); }
            catch (InvalidOperationException error) { nestedError = error.GetType().Name; }
            if (scenario == "required-failure") { throw sourceFailure; }
        });
        EnvironmentSnapshot? outer = null;
        Exception? outerError = null;
        try
        {
            outer = EnvironmentOptions.Initialize(new()
            {
                ApplicationAssembly = assembly,
                BuildVersion = "outer-build",
                ServiceVersion = "outer-release",
                Logging = scenario == "reentrant-invalid" ? new() { QueueCapacity = 0 } : null
            });
        }
        catch (CoreConfigurationException error) { outerError = error; }
        catch (ControlledRequiredSourceException error) { outerError = error; }
        bool initializedAfterAttempt = EnvironmentOptions.IsInitialized;
        EnvironmentSnapshot? retry = null;
        string? retryError = null;
        if (scenario != "reentrant-valid")
        {
            try { retry = EnvironmentOptions.Initialize(new() { ApplicationName = "Retry.App", BuildVersion = "retry-build" }); }
            catch (InvalidOperationException error) { retryError = error.GetType().Name; }
        }
        EnvironmentSnapshot current = EnvironmentOptions.Current;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            scenario,
            pendingInitialized,
            pendingCurrentError,
            nestedNullParameter,
            nestedSucceeded = nested is not null,
            nestedError,
            outerSucceeded = outer is not null,
            outerError = outerError?.GetType().Name,
            issues = (outerError as CoreConfigurationException)?.Issues.Select(issue => issue.Code + ":" + issue.Key).ToArray() ?? [],
            originalRequiredFailurePreserved = ReferenceEquals(sourceFailure, outerError),
            initializedAfterAttempt,
            retrySucceeded = retry is not null,
            retryError,
            successfulInitializers = (nested is null ? 0 : 1) + (outer is null ? 0 : 1) + (retry is null ? 0 : 1),
            current,
            winnerReferenceStable = ReferenceEquals(current, scenario == "reentrant-valid" ? outer : retry),
            callbackReads = assembly.Reads,
            observed = new { runtimeDescription = RuntimeInformation.FrameworkDescription, fixtureSha256 = Hash(typeof(StartupReentrancyProcessFixture).Assembly.Location), coreSha256 = Hash(typeof(EnvironmentOptions).Assembly.Location) }
        }));
        return 0;
    }
    internal sealed class CallbackAssembly(Action callback) : Assembly
    {
        private int reads;
        public int Reads => reads;
        public override AssemblyName GetName()
        {
            if (Interlocked.Increment(ref reads) == 1) { callback(); }
            return new() { Name = "Outer.App", Version = new(1, 0, 0, 0) };
        }
    }
    private sealed class ControlledRequiredSourceException : Exception;
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
