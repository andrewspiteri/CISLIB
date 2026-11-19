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
        File.WriteAllText(Path.Combine(app, "StateMachineMockStory.cs"), StateMachineMockProgram);
        File.WriteAllText(Path.Combine(app, "StateMachineSkillStory.cs"), StateMachineSkillProgram);
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
        Assert.AreEqual(3, report["publicStateMachineOutcomes"]!.GetValue<int>());
        Assert.IsTrue(report["publicStateMachineSkillUsed"]!.GetValue<bool>());
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
            int publicStateMachineOutcomes = await StateMachineMockStory.RunAsync();
            await StateMachineSkillStory.RunAsync();
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                aliasesEquivalent, lastGoodRetained, closeJoinedActualWork,
                publicPipelineUnwound = true,
                callerScopeStayedAlive = true,
                pipelineReportSurvivedCleanup = true,
                publicStateMachineOutcomes,
                publicStateMachineSkillUsed = true,
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

    private const string StateMachineMockProgram = """"
        using CIS.Workflows;
        using CIS.Workflows.Hosting;

        public static class StateMachineMockStory
        {
            public enum State { Pending, Resolved }
            public enum Event { Resolve }

            public static async Task<int> RunAsync()
            {
                int verified = 0;
                foreach (StateTransitionOutcome outcome in new[]
                {
                    StateTransitionOutcome.Succeeded,
                    StateTransitionOutcome.Failed,
                    StateTransitionOutcome.Cancelled
                })
                {
                    bool success = outcome == StateTransitionOutcome.Succeeded;
                    var failure = success ? null : new StateTransitionFailure("mock.result", "Controlled public result.",
                        StateTransitionPhase.Execute, outcome == StateTransitionOutcome.Cancelled
                            ? StateTransitionFailureKind.Cancellation : StateTransitionFailureKind.ReportedFailure);
                    PipelineStepExecutionStatus execution = success ? PipelineStepExecutionStatus.Succeeded
                        : outcome == StateTransitionOutcome.Cancelled ? PipelineStepExecutionStatus.Cancelled
                        : PipelineStepExecutionStatus.Failed;
                    var handler = new StateTransitionHandlerReport("mock", execution,
                        success ? PipelineStepErrorStatus.NotCalled : PipelineStepErrorStatus.Succeeded,
                        executionError: failure, executionDuration: TimeSpan.Zero,
                        errorDuration: success ? null : TimeSpan.Zero);
                    var expected = StateTransitionResult<State, Event>.Create("PublicMock", Guid.NewGuid(), Event.Resolve,
                        State.Pending, State.Resolved, success ? State.Resolved : State.Pending, outcome,
                        error: failure, duration: TimeSpan.Zero, handlerReport: handler);
                    var provider = new NeverResolveProvider();
                    var substitute = new Substitute(expected, scopeArgument =>
                    {
                        if (!ReferenceEquals(scopeArgument, provider))
                        {
                            throw new InvalidOperationException("The per-call scope argument was replaced.");
                        }
                    });
                    IStateMachine<State, Event> machine = substitute;
                    Task<StateTransitionResult<State, Event>> call = machine.TriggerInScope(Event.Resolve, provider);
                    StateTransitionResult<State, Event> actual = await call;
                    if (!call.IsCompletedSuccessfully || !ReferenceEquals(expected, actual)
                        || actual.Success != success || actual.Outcome != outcome
                        || substitute.ScopeCalls != 1 || substitute.PortableCalls != 0)
                    {
                        throw new InvalidOperationException("Public result construction or scope capability dispatch failed.");
                    }
                    verified++;
                }
                return verified;
            }

            private sealed class NeverResolveProvider : IServiceProvider
            {
                public object? GetService(Type serviceType)
                {
                    throw new InvalidOperationException("Consumer scope dispatch must not resolve a Core implementation.");
                }
            }

            private sealed class Substitute(StateTransitionResult<State, Event> result, Action<IServiceProvider> validate)
                : IStateMachine<State, Event>, IStateMachineScopeExecution<State, Event>
            {
                private Action<IServiceProvider>? validateScope = validate;
                internal int ScopeCalls { get; private set; }
                internal int PortableCalls { get; private set; }
                public State CurrentState => result.CurrentState;
                public bool IsExecuting => false;
                public IReadOnlyCollection<Event> PermittedTriggers => [Event.Resolve];
                public StateTransitionResult<State, Event>? LastTransition => result;
                public bool CanTrigger(Event trigger) => trigger == Event.Resolve;
                public Task<StateTransitionResult<State, Event>> Trigger(Event trigger, CancellationToken cancellationToken = default)
                {
                    PortableCalls++;
                    return Task.FromResult(result);
                }
                public Task<StateTransitionResult<State, Event>> TriggerInScope(Event trigger, IServiceProvider scopedProvider,
                    CancellationToken cancellationToken = default)
                {
                    ScopeCalls++;
                    Action<IServiceProvider>? validation = validateScope;
                    validateScope = null;
                    validation?.Invoke(scopedProvider);
                    return Task.FromResult(result);
                }
            }
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
    private const string StateMachineSkillProgram = """"
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using CIS.Workflows;
        using CIS.Workflows.Hosting;
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.Hosting;
        using Microsoft.Extensions.Logging;

        // Test-owned public-package story; timing controls never enter application code.
        public static class StateMachineSkillStory
        {
            private static readonly TimeSpan PromptBound = TimeSpan.FromSeconds(5);

            public static async Task RunAsync()
            {
                IHost host = Host.CreateDefaultBuilder(Array.Empty<string>())
                    .UseDefaultServiceProvider(options =>
                    {
                        options.ValidateScopes = true;
                        options.ValidateOnBuild = true;
                    })
                    .ConfigureLogging(logging => logging.ClearProviders())
                    .ConfigureServices(services =>
                    {
                        services.AddScoped<ScopedDependency>();
                        services.AddCisStateMachines();
                        services.AddCisWorkflows();
                    }).Build();
                try
                {
                    await host.StartAsync();
                    await RunInScopeAsync(host.Services);
                }
                finally
                {
                    try
                    {
                        await host.StopAsync(CancellationToken.None);
                    }
                    finally
                    {
                        if (host is IAsyncDisposable asynchronous)
                        {
                            await asynchronous.DisposeAsync();
                        }
                        else
                        {
                            host.Dispose();
                        }
                    }
                }
            }

            private static async Task RunInScopeAsync(IServiceProvider services)
            {
                var scope = services.CreateAsyncScope();
                ScopedDependency dependency;
                try
                {
                    dependency = scope.ServiceProvider.GetRequiredService<ScopedDependency>();
                    var factory = services.GetRequiredService<IStateMachineFactory>();
                    var successful = new FlowContext(dependency.Id);
                    var machine = Build(factory, successful);
                    var submitted = await SubmitAsync(machine, scope.ServiceProvider, successful);
                    Require(submitted.Success && submitted.CurrentState == State.Submitted, "Submit must commit Submitted.");
                    var approved = await machine.Trigger(Trigger.Approve);
                    Require(approved.Success && approved.CurrentState == State.Approved && approved.HandlerReport is null,
                        "Direct Approve must commit Approved without a handler.");
                    Require(submitted.CurrentState == State.Submitted, "An earlier result must remain immutable.");

                    var rejectedMachine = Build(factory, new FlowContext(dependency.Id));
                    var rejected = await rejectedMachine.Trigger(Trigger.Approve);
                    Require(!rejected.Success && rejected.Outcome == StateTransitionOutcome.Rejected
                        && rejected.CurrentState == State.Draft && rejected.TargetState is null
                        && ReferenceEquals(rejectedMachine.LastTransition, rejected), "Approve from Draft must be rejected.");

                    await CheckHeldFailureAndRetryAsync(factory, scope.ServiceProvider, dependency);
                    Require(!dependency.Disposed, "Borrowed scope must remain caller-owned after all triggers.");
                }
                finally
                {
                    await scope.DisposeAsync();
                }
                Require(dependency.Disposed, "Caller disposal must dispose its scoped dependency.");
            }

            private static async Task CheckHeldFailureAndRetryAsync(IStateMachineFactory factory,
                IServiceProvider scopedServices, ScopedDependency dependency)
            {
                var hold = new ChildHold();
                var context = new FlowContext(dependency.Id) { Fail = true, Hold = hold };
                var machine = Build(factory, context);
                Task<StateTransitionResult<State, Trigger>> accepted = SubmitAsync(machine, scopedServices, context);
                Task<StateTransitionResult<State, Trigger>>? overlapping = null;
                try
                {
                    await hold.Entered.Task.WaitAsync(PromptBound);
                    Require(!accepted.IsCompleted && machine.IsExecuting && machine.CurrentState == State.Draft,
                        "Held child must keep source state visible and the accepted task incomplete.");
                    overlapping = machine.Trigger(Trigger.Submit);
                    var busy = await overlapping.WaitAsync(PromptBound);
                    Require(busy.Outcome == StateTransitionOutcome.Busy && busy.TargetState is null
                        && busy.HandlerReport is null && machine.LastTransition is null,
                        "Overlap must return Busy promptly without replacing LastTransition.");
                    Require(context.Reentrant?.Outcome == StateTransitionOutcome.Busy && context.ChildStarts == 1,
                        "Handler reentry must return Busy promptly without starting another child.");
                }
                finally
                {
                    hold.Release.TrySetResult(true);
                    // Join actual work, including a failed prompt assertion, before the caller scope can close.
                    try
                    {
                        await accepted;
                    }
                    finally
                    {
                        if (overlapping is not null)
                        {
                            await overlapping;
                        }
                    }
                }
                var failed = await accepted;
                Require(accepted.IsCompletedSuccessfully && !failed.Success && failed.Outcome == StateTransitionOutcome.Failed
                    && failed.CurrentState == State.Draft && failed.ErrorCode == "story.submit_declined"
                    && failed.Error?.Kind == StateTransitionFailureKind.ReportedFailure
                    && failed.HandlerReport?.ErrorStatus == PipelineStepErrorStatus.Succeeded
                    && failed.ErrorHandlingFailures.Count == 0 && failed.LifecycleFailures.Count == 0,
                    "Unsuccessful child must become a normal Failed trigger result and retain Draft.");
                Require(context.LastChild?.Status == PipelineStatus.Failed
                    && context.LastChild.Step.ErrorStatus == PipelineStepErrorStatus.Succeeded
                    && context.ChildErrors == 1 && context.OuterErrors == 1, "Child must unwind once; outer Error owns only outer work.");
                Guid childId = context.LastChild!.Id;
                Guid handlerId = context.HandlerId;
                Guid stepId = context.StepId;
                context.Fail = false;
                context.Hold = null;

                var retried = await SubmitAsync(machine, scopedServices, context);
                Require(retried.Success && retried.CurrentState == State.Submitted
                    && context.LastChild?.Status == PipelineStatus.Succeeded && context.LastChild.Id != childId
                    && context.HandlerId != handlerId && context.StepId != stepId,
                    "A successive Submit must activate a fresh handler, real child and step.");
                var approved = await machine.Trigger(Trigger.Approve);
                Require(approved.Success && machine.CurrentState == State.Approved && context.ChildStarts == 2
                    && context.StepDisposals == 2 && context.HandlerDisposals == 2
                    && context.ChildErrors == 1 && context.OuterErrors == 1,
                    "Busy calls must never queue; later success must neither reuse nor recompensate child work.");
                Require(failed.CurrentState == State.Draft && failed.Outcome == StateTransitionOutcome.Failed,
                    "Later success must not mutate the failed result.");
            }

            private static async Task<StateTransitionResult<State, Trigger>> SubmitAsync(
                IStateMachine<State, Trigger> machine, IServiceProvider scopedServices, FlowContext context)
            {
                try
                {
                    return await machine.TriggerInScope(Trigger.Submit, scopedServices);
                }
                finally
                {
                    if (context.ReentrantCall is { } reentrant)
                    {
                        await reentrant;
                    }
                }
            }

            private static IStateMachine<State, Trigger> Build(IStateMachineFactory factory, FlowContext context)
            {
                var builder = factory.CreateBuilder<State, Trigger, FlowContext>("SkillApproval", context,
                    new StateMachineOptions { LogCalls = false });
                builder.State(State.Draft).On<SubmitHandler>(Trigger.Submit, State.Submitted, "submit");
                builder.State(State.Submitted).On(Trigger.Approve, State.Approved);
                context.Machine = builder.Build(State.Draft);
                return context.Machine;
            }

            private static void Require(bool condition, string message)
            {
                if (!condition)
                {
                    throw new InvalidOperationException(message);
                }
            }

            public enum State
            {
                Draft,
                Submitted,
                Approved
            }
            public enum Trigger
            {
                Submit,
                Approve
            }

            public sealed class ScopedDependency : IDisposable
            {
                public Guid Id { get; } = Guid.NewGuid();
                public bool Disposed { get; private set; }
                public void Dispose()
                {
                    Require(!Disposed, "Scoped dependency was disposed twice.");
                    Disposed = true;
                }
            }

            public sealed class ChildHold
            {
                public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
                public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public sealed class FlowContext(Guid expectedDependency)
            {
                public Guid ExpectedDependency { get; } = expectedDependency;
                public bool Fail { get; set; }
                public ChildHold? Hold { get; set; }
                public IStateMachine<State, Trigger>? Machine { get; set; }
                public Task<StateTransitionResult<State, Trigger>>? ReentrantCall { get; set; }
                public StateTransitionResult<State, Trigger>? Reentrant { get; set; }
                public ChildReport? LastChild { get; set; }
                public Guid HandlerId { get; set; }
                public Guid StepId { get; set; }
                public int ChildStarts { get; set; }
                public int ChildErrors { get; set; }
                public int OuterErrors { get; set; }
                public int StepDisposals { get; set; }
                public int HandlerDisposals { get; set; }
            }

            public sealed record ChildReport(Guid Id, PipelineStatus Status, PipelineStepReport Step);
            public sealed record ChildContext(FlowContext Owner);

            public sealed class SubmitHandler(IPipelineFactory pipelines, IServiceProvider scopedServices, ScopedDependency dependency)
                : IStateTransitionHandler<State, Trigger, FlowContext>, IDisposable
            {
                private FlowContext? entered;
                private readonly Guid id = Guid.NewGuid();

                public async Task<TransitionActionResult> Execute(FlowContext context, TransitionInfo<State, Trigger> transition,
                    CancellationToken cancellationToken)
                {
                    Require(entered is null && !dependency.Disposed && dependency.Id == context.ExpectedDependency,
                        "Fresh handler must use the explicitly borrowed scoped dependency.");
                    entered = context;
                    context.HandlerId = id;
                    context.ReentrantCall = context.Machine!.Trigger(Trigger.Submit);
                    context.Reentrant = await context.ReentrantCall.WaitAsync(PromptBound);
                    var child = pipelines.CreateBuilderInScope("SkillSubmitChild", new ChildContext(context), scopedServices,
                        new PipelineOptions { LogCalls = false }).Add<HeldChildStep>("held_submit").Build();
                    try
                    {
                        await child.Execute(cancellationToken);
                    }
                    finally
                    {
                        context.LastChild = new ChildReport(child.ExecutionId, child.Status, child.Steps[0]);
                    }
                    return child.Success ? TransitionActionResult.Succeeded()
                        : TransitionActionResult.Failed(child.Error!.Code, child.Error.Message);
                }

                public Task Error(FlowContext context, TransitionInfo<State, Trigger> transition,
                    StateTransitionFailure failure, CancellationToken cancellationToken)
                {
                    Require(ReferenceEquals(entered, context) && !dependency.Disposed
                        && context.StepDisposals == context.ChildStarts, "Child must already be settled before outer Error.");
                    // No external outer effects exist here; never call the child's Error or disposed steps again.
                    context.OuterErrors++;
                    return Task.CompletedTask;
                }

                public void Dispose()
                {
                    Require(!dependency.Disposed, "Dependency must outlive handler cleanup.");
                    if (entered is not null)
                    {
                        entered.HandlerDisposals++;
                    }
                }
            }

            public sealed class HeldChildStep(ScopedDependency dependency) : IPipelineStep<ChildContext>, IDisposable
            {
                private FlowContext? entered;
                private readonly Guid id = Guid.NewGuid();

                public async Task<PipelineStepResult> Execute(ChildContext context, CancellationToken cancellationToken)
                {
                    Require(entered is null && !dependency.Disposed && dependency.Id == context.Owner.ExpectedDependency,
                        "Fresh child step must share the handler's borrowed scoped dependency.");
                    entered = context.Owner;
                    entered.StepId = id;
                    entered.ChildStarts++;
                    if (entered.Hold is { } hold)
                    {
                        hold.Entered.TrySetResult(true);
                        await hold.Release.Task.WaitAsync(cancellationToken);
                    }
                    return entered.Fail ? PipelineStepResult.Failed("story.submit_declined", "Submission was declined.")
                        : PipelineStepResult.Succeeded();
                }

                public Task Error(ChildContext context, PipelineFailure failure, CancellationToken cancellationToken)
                {
                    Require(ReferenceEquals(entered, context.Owner) && !dependency.Disposed, "Child Error must use its live step.");
                    context.Owner.ChildErrors++;
                    return Task.CompletedTask;
                }

                public void Dispose()
                {
                    Require(!dependency.Disposed, "Dependency must outlive child cleanup.");
                    if (entered is not null)
                    {
                        entered.StepDisposals++;
                    }
                }
            }
        }
        """";

}
