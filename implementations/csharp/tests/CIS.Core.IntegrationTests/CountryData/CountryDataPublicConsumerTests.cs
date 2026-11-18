using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using CIS.CommonTesting;
using CIS.Examples.CountryData;
using CIS.RestClient.Http;
using CIS.TestSupport;
using CIS.Tests.IntegrationTests.RestClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.CountryData;

[TestClass]
[DoNotParallelize]
public sealed class CountryDataPublicConsumerTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task OutsideFriendAssemblyUsesActualPackagesAndCompiledCatalogThroughPublicContracts()
    {
        const string assemblyName = "CIS.PublicSubstitution.Consumer";
        Assembly application = typeof(CountryCatalog).Assembly;
        foreach (Assembly input in new[] { typeof(HttpRestClient).Assembly, typeof(ManualHeartbeat).Assembly, application })
        {
            Assert.IsFalse(input.GetCustomAttributes<InternalsVisibleToAttribute>()
                .Any(attribute => attribute.AssemblyName.Split(',')[0] == assemblyName));
        }

        using RestPackage package = await RestPackage.PrepareAsync(TestContext, "CountryData.PublicSubstitution",
            typeof(HttpRestClient).Assembly, typeof(ManualHeartbeat).Assembly);
        string app = Directory.CreateDirectory(Path.Combine(package.Root, "app")).FullName;
        string project = Path.Combine(app, "Consumer.csproj");
        string catalogHash = CandidatePackageFixture.Hash(application.Location);
        string references = Directory.CreateDirectory(Path.Combine(app, "references")).FullName;
        string catalogReference = Path.Combine(references, "CIS.CountryData.dll");
        File.Copy(application.Location, catalogReference);
        Assert.AreEqual(catalogHash, CandidatePackageFixture.Hash(catalogReference));
        File.WriteAllText(Path.Combine(app, "Program.cs"), ConsumerProgram);
        File.WriteAllText(Path.Combine(app, "WorkflowSkillStory.cs"), WorkflowSkillProgram);
        new XDocument(new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"),
            new XElement("PropertyGroup", new XElement("OutputType", "Exe"), new XElement("AssemblyName", assemblyName)),
            new XElement("ItemGroup",
                new XElement("FrameworkReference", new XAttribute("Include", "Microsoft.AspNetCore.App")),
                new[] { "CIS.Core", "CIS.CommonTesting" }.Select(id => new XElement("PackageReference",
                    new XAttribute("Include", id), new XAttribute("Version", "[" + package.Version + "]"))),
                new XElement("Reference", new XAttribute("Include", "CIS.CountryData"),
                    new XElement("HintPath", catalogReference), new XElement("Private", true))))).Save(project);

        await package.RunAsync(["restore", project, "--configfile", package.Config, "--no-cache"], package.Root);
        package.AssertRestored(app);
        string[] approved = ["CIS.Core/" + package.Version, "CIS.CommonTesting/" + package.Version,
            "Microsoft.Extensions.Logging.Abstractions/10.0.12", "Microsoft.Extensions.DependencyInjection.Abstractions/10.0.12"];
        Assert.IsTrue(RestPackage.Packages(RestPackage.Assets(Path.Combine(app, "obj/project.assets.json")))
            .All(dependency => approved.Contains(dependency, StringComparer.Ordinal)));
        await package.RunAsync(["build", project, "-c", "Release", "--no-restore"], package.Root);
        string output = Path.Combine(app, "bin/Release/net10.0");
        JsonObject deps = package.AssertDeployed(output, assemblyName);
        Assert.AreEqual(catalogHash, CandidatePackageFixture.Hash(Path.Combine(output, "CIS.CountryData.dll")));
        Assert.IsFalse(deps["libraries"]!.AsObject().Any(item => item.Key.StartsWith("Polly", StringComparison.OrdinalIgnoreCase)
            || item.Key.StartsWith("OpenTelemetry", StringComparison.OrdinalIgnoreCase)
            || item.Key.StartsWith("MSTest", StringComparison.OrdinalIgnoreCase)));
        string working = Directory.CreateDirectory(Path.Combine(package.Root, "run")).FullName;
        ProcessResult result = await package.RunAsync(["exec", "--fx-version", "10.0.12", "--roll-forward", "Disable",
            Path.Combine(output, assemblyName + ".dll")], working, TimeSpan.FromSeconds(45));
        Assert.AreEqual("", result.StandardError);
        JsonObject report = JsonNode.Parse(result.StandardOutput)!.AsObject();
        Assert.IsTrue(report["aliasesEquivalent"]!.GetValue<bool>());
        Assert.IsTrue(report["lastGoodRetained"]!.GetValue<bool>());
        Assert.IsTrue(report["closeJoinedActualWork"]!.GetValue<bool>());
        Assert.IsTrue(report["publicPipelineUnwound"]!.GetValue<bool>());
        Assert.IsTrue(report["callerScopeStayedAlive"]!.GetValue<bool>());
        Assert.IsTrue(report["pipelineReportSurvivedCleanup"]!.GetValue<bool>());
        Assert.AreEqual(4, report["calls"]!.GetValue<int>());
        Assert.AreEqual(1, report["refreshFailures"]!.GetValue<int>());
        RestPackage.SamePath(Path.Combine(output, "CIS.CountryData.dll"), report["catalogPath"]!.GetValue<string>());
        RestPackage.SamePath(Path.Combine(output, "CIS.Core.dll"), report["corePath"]!.GetValue<string>());
        RestPackage.SamePath(Path.Combine(output, "CIS.CommonTesting.dll"), report["testingPath"]!.GetValue<string>());
        Assert.AreEqual(catalogHash, CandidatePackageFixture.Hash(application.Location));
        package.AssertOriginalAssembliesUnchanged();
        TestContext.WriteLine(result.StandardOutput);
    }

    private const string ConsumerProgram = """"
        using System.Text.Json;
        using CIS.CommonTesting;
        using CIS.Configuration;
        using CIS.Examples.CountryData;
        using CIS.Metrics;
        using CIS.RestClient;
        using CIS.Workflows;
        using CIS.Workflows.Hosting;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Hosting;
        using Microsoft.Extensions.Logging;

        const string json = """{"error":false,"data":[{"name":"Malta","Iso2":"MT","Iso3":"MLT"}]}""";
        var rest = new ScriptedRestClient();
        var health = new RecordingHealthMonitorFactory();
        await using var heartbeat = new ManualHeartbeat();
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
        var logs = new RecordingLogger<CountryCatalog>();
        var catalog = new CountryCatalog(rest, new CountrySettings { RetryPolicyName = null }, new Reader(),
            heartbeat, health, DisabledMetrics.Instance, new Lifetime(), logs, clock, []);
        PendingRestCall? pending = null;
        try
        {
            rest.EnqueueJsonValue(json);
            await catalog.StartAsync(CancellationToken.None);
            var registration = heartbeat.Registrations.Single();
            Check(ReferenceEquals(catalog, registration.Subscriber));
            Check(registration.Options.Interval == TimeSpan.FromHours(1));
            Check(registration.Options.InitialDelay == TimeSpan.FromHours(1));
            Check(ReferenceEquals(registration.Monitor, health.Monitors.Single()));
            bool aliasesEquivalent = catalog.TryGet("MT", out var first) && catalog.TryGet("MLT", out var alpha3)
                && catalog.TryGet("Malta", out var name) && ReferenceEquals(first, alpha3) && ReferenceEquals(first, name);
            Check(aliasesEquivalent);

            rest.EnqueueFailure(RestFailureKind.Http, "provider.unavailable", "temporary", 503);
            var failed = await heartbeat.TriggerAsync(catalog);
            Check(failed.CallbackResult == false && failed.Error is null);
            bool lastGoodRetained = catalog.TryGet("MT", out var retained) && ReferenceEquals(first, retained);
            Check(lastGoodRetained);
            rest.EnqueueJsonValue(json);
            Check((await heartbeat.TriggerAsync(catalog)).CallbackResult == true);

            pending = rest.EnqueuePendingJsonValue(json);
            Task<ManualHeartbeatResult> running = heartbeat.TriggerAsync(catalog);
            await pending.Entered;
            Task closing = catalog.DisposeAsync().AsTask();
            bool closeJoinedActualWork = !closing.IsCompleted && !running.IsCompleted;
            Check(closeJoinedActualWork);
            pending.Release();
            Check((await running).CallbackResult == true);
            await closing;
            Check(heartbeat.Registrations.Count == 0 && health.Monitors.Single().IsWithdrawn);
            await WorkflowSkillStory.RunAsync();
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                aliasesEquivalent, lastGoodRetained, closeJoinedActualWork,
                publicPipelineUnwound = true,
                callerScopeStayedAlive = true,
                pipelineReportSurvivedCleanup = true,
                calls = rest.TotalCalls,
                refreshFailures = health.Monitors.Single().Count(HealthReportKind.RefreshFailure),
                catalogPath = typeof(CountryCatalog).Assembly.Location,
                corePath = typeof(IRestClient).Assembly.Location,
                testingPath = typeof(ManualHeartbeat).Assembly.Location
            }));
        }
        finally
        {
            pending?.Release();
            await catalog.DisposeAsync();
        }

        static void Check(bool condition)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Public consumer contract failed.");
            }
        }

        sealed class Reader : IConfigurationReader
        {
            public TResult Read<TConfiguration, TResult>(TConfiguration configuration, Func<TConfiguration, TResult> read)
                where TConfiguration : class, IConfigurationObject => read(configuration);
        }

        sealed class Lifetime : IHostApplicationLifetime
        {
            public CancellationToken ApplicationStarted => CancellationToken.None;
            public CancellationToken ApplicationStopping => CancellationToken.None;
            public CancellationToken ApplicationStopped => CancellationToken.None;
            public void StopApplication() { }
        }
        """";

    // Fresh public consumer authored from the repository skill and linked guide,
    // then compiled outside all friend assemblies by this existing package fixture.
    private const string WorkflowSkillProgram = """"
        #nullable enable

        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using System.Threading.Tasks;
        using CIS.Workflows;
        using CIS.Workflows.Hosting;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Hosting;
        using Microsoft.Extensions.Logging;

        public static class WorkflowSkillStory
        {
            private const string FailureCode = "story.business_rejected";
            private const string FailureMessage = "The middle operation was rejected.";

            public static async Task RunAsync()
            {
                var context = new StoryContext();
                var observations = new StoryObservations(context);
                IHost host = Host.CreateDefaultBuilder(Array.Empty<string>())
                    .ConfigureLogging(logging => logging.ClearProviders())
                    .UseDefaultServiceProvider(options =>
                    {
                        options.ValidateScopes = true;
                        options.ValidateOnBuild = true;
                    })
                    .ConfigureServices(services =>
                    {
                        services.AddSingleton(observations);
                        services.AddScoped<ScopedDependency>();
                        services.AddCisWorkflows();
                    })
                    .Build();

                await using var hostDisposal = (IAsyncDisposable)host;

                try
                {
                    await host.StartAsync();
                    IPipeline<StoryContext> retainedPipeline = await ExecuteInCallerScopeAsync(host.Services, context, observations);

                    // A terminal public report remains readable after its borrowed caller scope is gone.
                    VerifyReport(retainedPipeline);
                    Require(ReferenceEquals(retainedPipeline.Context, context), "The retained report lost the caller's context.");
                }
                finally
                {
                    await host.StopAsync();
                }
            }

            private static async Task<IPipeline<StoryContext>> ExecuteInCallerScopeAsync(
                IServiceProvider hostServices, StoryContext context, StoryObservations observations)
            {
                var callerScope = hostServices.CreateAsyncScope();
                ScopedDependency? dependency = null;
                IPipeline<StoryContext>? pipeline = null;
                IReadOnlyList<PipelineStepReport>? settledSteps = null;
                PipelineFailure? settledFailure = null;
                TimeSpan? settledDuration = null;

                try
                {
                    dependency = callerScope.ServiceProvider.GetRequiredService<ScopedDependency>();
                    var factory = callerScope.ServiceProvider.GetRequiredService<IPipelineFactory>();
                    pipeline = factory.CreateBuilderInScope("WorkflowSkillStory", context, callerScope.ServiceProvider)
                        .Add<FirstStep>("first")
                        .Add<MiddleStep>("middle")
                        .Add<LastStep>("last")
                        .Build();

                    Require(observations.Instances.Count == 0, "Build activated business steps.");
                    Require(pipeline.Status == PipelineStatus.NotStarted, "The built pipeline was already started.");
                    Require(ReferenceEquals(pipeline.Context, context), "The builder replaced the caller's context.");

                    try
                    {
                        await pipeline.Execute();
                    }
                    catch (Exception exception)
                    {
                        throw new InvalidOperationException(
                            $"A controlled business failure must return normally after cleanup. Status: {pipeline.Status}; " +
                            $"primary: {pipeline.Error?.Code}; handler failures: {pipeline.ErrorHandlingFailures.Count}; " +
                            $"lifecycle failures: {pipeline.LifecycleFailures.Count}.", exception);
                    }

                    VerifyReport(pipeline);
                    VerifyInstances(observations, dependency);
                    settledSteps = pipeline.Steps;
                    settledFailure = pipeline.Error;
                    settledDuration = pipeline.Duration;

                    Require(context.ForwardOrder.SequenceEqual(new[] { "first", "middle" }), "Unexpected forward work.");
                    Require(context.ErrorOrder.SequenceEqual(new[] { "middle", "first" }), "Error did not start with the failed step and reverse earlier work.");
                    Require(observations.HandlerFailures.Count == 2, "Unexpected number of Error invocations.");
                    Require(observations.HandlerFailures.All(failure => ReferenceEquals(failure, pipeline.Error)), "Error handlers did not receive the original failure.");

                    string[] expectedEvents =
                    {
                        "construct:first", "construct:middle", "construct:last",
                        "execute:first", "execute:middle",
                        "error:middle", "error:first",
                        "dispose:last", "dispose:middle", "dispose:first"
                    };
                    Require(observations.Events.SequenceEqual(expectedEvents), "Activation, work, Error or disposal order differed.");
                    Require(dependency.UseCount == 7, "Steps did not use their live dependency for every entered phase and disposal.");
                    Require(dependency.DisposeCount == 0, "The pipeline disposed the borrowed scoped dependency.");
                    Require(ReferenceEquals(dependency, callerScope.ServiceProvider.GetRequiredService<ScopedDependency>()), "The borrowed caller scope was replaced or closed.");

                    // The caller can still use its service after every pipeline-owned step has settled.
                    dependency.Use();
                }
                finally
                {
                    // Ownership stays here, even when one of the observations fails.
                    await callerScope.DisposeAsync();
                }

                Require(dependency is not null && dependency.DisposeCount == 1, "Explicit caller-scope disposal did not dispose its dependency exactly once.");
                Require(observations.Events.LastOrDefault() == "dependency:dispose", "The dependency was disposed before pipeline-owned step cleanup finished.");

                IPipeline<StoryContext> retainedPipeline = pipeline ?? throw new InvalidOperationException("The settled pipeline is missing.");
                Require(settledSteps is not null && retainedPipeline.Steps.SequenceEqual(settledSteps), "Caller-scope disposal changed the retained step reports.");
                Require(ReferenceEquals(retainedPipeline.Error, settledFailure) && retainedPipeline.Duration == settledDuration, "Caller-scope disposal changed the retained primary failure or duration.");

                return retainedPipeline;
            }

            private static void VerifyReport(IPipeline<StoryContext> pipeline)
            {
                Require(pipeline.Status == PipelineStatus.Failed && !pipeline.Success, "The business failure was not reported as Failed.");
                PipelineFailure failure = pipeline.Error ?? throw new InvalidOperationException("The original business failure is missing.");
                Require(failure.Code == FailureCode && failure.Message == FailureMessage, "The original business error changed.");
                Require(failure.Kind == PipelineFailureKind.ReportedFailure && failure.Phase == PipelinePhase.Execute, "Unexpected primary failure classification.");
                Require(failure.StepPosition == 1 && failure.StepName == "middle" && failure.Exception is null, "Unexpected primary failure origin.");
                PipelineStepReport failedStep = pipeline.FailedStep ?? throw new InvalidOperationException("The failed registration is missing.");
                Require(failedStep.Position == 1 && failedStep.Name == "middle", "The failed step was not the middle registration.");
                Require(pipeline.Steps.Count == 3, "The report omitted a configured registration.");

                VerifyStep(pipeline.Steps[0], 0, "first", PipelineStepExecutionStatus.Succeeded, PipelineStepErrorStatus.Succeeded, true, true);
                VerifyStep(pipeline.Steps[1], 1, "middle", PipelineStepExecutionStatus.Failed, PipelineStepErrorStatus.Succeeded, true, true);
                VerifyStep(pipeline.Steps[2], 2, "last", PipelineStepExecutionStatus.NotStarted, PipelineStepErrorStatus.NotCalled, false, false);

                Require(pipeline.Steps[0].ExecutionError is null && pipeline.Steps[2].ExecutionError is null, "A nonfailing step acquired a forward error.");
                Require(Equals(pipeline.Steps[1].ExecutionError, failure), "The middle step did not retain its original forward failure.");
                Require(pipeline.ExecutedSteps.Select(step => step.Position).SequenceEqual(new[] { 0, 1 }), "ExecutedSteps must include the failed step and exclude the final step.");
                Require(pipeline.ErrorHandlingFailures.Count == 0 && pipeline.LifecycleFailures.Count == 0, "Cleanup produced secondary failures.");
                Require(!pipeline.CleanupBudgetExpired, "The immediate Error handlers unexpectedly exhausted their cleanup budget.");
                Require(pipeline.Duration.HasValue && pipeline.Duration.Value >= TimeSpan.Zero, "The settled report lacks its overall duration.");
            }

            private static void VerifyStep(PipelineStepReport step, int position, string name,
                PipelineStepExecutionStatus executionStatus, PipelineStepErrorStatus errorStatus,
                bool executed, bool handled)
            {
                Require(step.Position == position && step.Name == name, "The ordered report changed a step's registration identity.");
                Require(step.ExecutionStatus == executionStatus && step.ErrorStatus == errorStatus, "A step's independent forward/Error statuses differ.");
                Require(step.ExecutionDuration.HasValue == executed && step.ErrorDuration.HasValue == handled, "A method duration does not match actual method entry.");
                Require(step.ErrorHandlingError is null, "An Error handler failed.");
            }

            private static void VerifyInstances(StoryObservations observations, ScopedDependency dependency)
            {
                Require(observations.Instances.Count == 3, "Not all three steps were constructed.");

                for (int index = 0; index < observations.Instances.Count; index++)
                {
                    StoryStep instance = observations.Instances[index];
                    Require(ReferenceEquals(instance.Dependency, dependency), "A step did not share the caller's scoped dependency.");
                    Require(instance.DisposeCount == 1, "A constructed step was not disposed exactly once.");

                    for (int earlier = 0; earlier < index; earlier++)
                    {
                        Require(!ReferenceEquals(instance, observations.Instances[earlier]), "Two positions reused a step instance.");
                    }
                }
            }

            private static void Require(bool condition, string message)
            {
                if (!condition)
                {
                    throw new InvalidOperationException(message);
                }
            }

            public sealed class StoryContext
            {
                public List<string> ForwardOrder { get; } = new();

                public List<string> ErrorOrder { get; } = new();
            }

            public sealed class StoryObservations
            {
                public StoryObservations(StoryContext context)
                {
                    Context = context;
                }

                public StoryContext Context { get; }

                public List<string> Events { get; } = new();

                public List<StoryStep> Instances { get; } = new();

                public List<PipelineFailure> HandlerFailures { get; } = new();
            }

            public sealed class ScopedDependency : IDisposable
            {
                private readonly StoryObservations observations;

                public ScopedDependency(StoryObservations observations)
                {
                    this.observations = observations;
                }

                public int UseCount { get; private set; }

                public int DisposeCount { get; private set; }

                public void Use()
                {
                    Require(DisposeCount == 0, "A pipeline phase used the dependency after scope disposal.");
                    UseCount++;
                }

                public void Dispose()
                {
                    DisposeCount++;
                    observations.Events.Add("dependency:dispose");
                }
            }

            public abstract class StoryStep : IPipelineStep<StoryContext>, IAsyncDisposable
            {
                private readonly string alias;

                private readonly bool reportsFailure;

                private readonly StoryObservations observations;

                protected StoryStep(string alias, bool reportsFailure, StoryObservations observations, ScopedDependency dependency)
                {
                    this.alias = alias;
                    this.reportsFailure = reportsFailure;
                    this.observations = observations;
                    Dependency = dependency;

                    observations.Instances.Add(this);
                    observations.Events.Add("construct:" + alias);
                }

                public ScopedDependency Dependency { get; }

                public int DisposeCount { get; private set; }

                public Task<PipelineStepResult> Execute(StoryContext context, CancellationToken cancellationToken)
                {
                    Require(ReferenceEquals(context, observations.Context), "Forward work received a different context.");
                    Dependency.Use();
                    context.ForwardOrder.Add(alias);
                    observations.Events.Add("execute:" + alias);

                    return Task.FromResult(reportsFailure
                        ? PipelineStepResult.Failed(FailureCode, FailureMessage)
                        : PipelineStepResult.Succeeded());
                }

                public Task Error(StoryContext context, PipelineFailure failure, CancellationToken cancellationToken)
                {
                    Require(ReferenceEquals(context, observations.Context), "Error received a different context.");
                    Dependency.Use();
                    context.ErrorOrder.Add(alias);
                    observations.HandlerFailures.Add(failure);
                    observations.Events.Add("error:" + alias);

                    return Task.CompletedTask;
                }

                public async ValueTask DisposeAsync()
                {
                    // Force an asynchronous continuation so Execute must join real step cleanup.
                    await Task.Yield();
                    Dependency.Use();
                    DisposeCount++;
                    observations.Events.Add("dispose:" + alias);
                }
            }

            public sealed class FirstStep : StoryStep
            {
                public FirstStep(StoryObservations observations, ScopedDependency dependency)
                    : base("first", false, observations, dependency)
                {
                }
            }

            public sealed class MiddleStep : StoryStep
            {
                public MiddleStep(StoryObservations observations, ScopedDependency dependency)
                    : base("middle", true, observations, dependency)
                {
                }
            }

            public sealed class LastStep : StoryStep
            {
                public LastStep(StoryObservations observations, ScopedDependency dependency)
                    : base("last", false, observations, dependency)
                {
                }
            }
        }
    """";
}
