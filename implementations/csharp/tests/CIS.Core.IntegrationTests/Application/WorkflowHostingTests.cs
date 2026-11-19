using CIS.Hosting;
using CIS.Startup;
using CIS.TestSupport;
using CIS.Workflows;
using CIS.Workflows.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.IntegrationTests.Application;

[TestClass]
public sealed class WorkflowHostingTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task FacadeDrainsActualUnwindAndDisposalBeforeBusinessTeardownEvenWhenStopWaitIsCancelled(bool web)
    {
        using var directory = new TemporaryDirectory();
        var inventory = ApplicationTestSupport.Inventory(directory);
        var context = new HeldContext();
        var business = new BusinessDependency();
        void Configure(CISApplicationBuilder builder)
        {
            ApplicationTestSupport.Configure(builder, inventory);
            builder.Services.AddSingleton(_ => business);
            builder.Services.AddSingleton<IHostedService>(root => root.GetRequiredService<BusinessDependency>());
            builder.Services.AddScoped<ScopedDependency>();
            builder.Services.AddCisWorkflows();
            builder.Services.AddCisWorkflows();
        }

        CISApplication app = web ? CISApplication.CreateWeb([], Configure, _ => { }) : new CISApplication(Configure);
        Task? execution = null;
        Task? disposal = null;
        try
        {
            await app.StartAsync().WaitAsync(ApplicationTestSupport.Deadline);
            IPipelineFactory factory = app.Services.GetRequiredService<IPipelineFactory>();
            IPipeline<HeldContext> pipeline = factory.CreateBuilder("HeldCleanup", context)
                .Add<HeldStep>("held").Build();
            execution = pipeline.Execute();
            await context.ErrorEntered.Task.WaitAsync(ApplicationTestSupport.Deadline);

            using var cancelledWait = new CancellationTokenSource();
            Task stopWait = app.StopAsync(cancelledWait.Token);
            cancelledWait.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => stopWait);
            disposal = app.DisposeAsync().AsTask();
            Assert.IsFalse(disposal.IsCompleted);
            business.Use();
            Assert.IsFalse(context.ExecutionToken.IsCancellationRequested,
                "The host does not replace the caller's execution cancellation policy.");
            Assert.Throws<InvalidOperationException>(() => factory.CreateBuilder("AfterStop", new HeldContext())
                .Add<HeldStep>().Build().Execute());

            context.ReleaseError.TrySetResult();
            await context.DisposalEntered.Task.WaitAsync(ApplicationTestSupport.Deadline);
            business.Use();
            Assert.IsFalse(execution.IsCompleted);
            Assert.IsFalse(disposal.IsCompleted);
            Assert.AreEqual(PipelineStatus.Unwinding, pipeline.Status);

            context.ReleaseDisposal.TrySetResult();
            await execution.WaitAsync(ApplicationTestSupport.Deadline);
            await disposal.WaitAsync(ApplicationTestSupport.Deadline);
            Assert.AreEqual(PipelineStatus.Failed, pipeline.Status);
            Assert.AreEqual(1, context.StepDisposals);
            Assert.AreEqual(1, business.Stops);
            Assert.AreEqual(1, business.Disposals);
            Assert.AreEqual(1, context.ScopeDisposals);
        }
        finally
        {
            context.ReleaseError.TrySetResult();
            context.ReleaseDisposal.TrySetResult();
            await ApplicationTestSupport.SettleAsync(execution);
            await (disposal ?? app.DisposeAsync().AsTask()).WaitAsync(ApplicationTestSupport.Deadline);
        }
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task FailedBuildOrStartupCapturesTheActuallyInstantiatedWorkflowOwnerBeforeProviderDisposal(bool web, bool duringBuild)
    {
        using var directory = new TemporaryDirectory();
        var inventory = ApplicationTestSupport.Inventory(directory);
        var context = new HeldContext();
        var business = new BusinessDependency();
        var original = new InvalidOperationException("Native startup failed after accepted workflow work.");
        Task? execution = null;
        IPipelineFactory? factory = null;
        void Configure(CISApplicationBuilder builder)
        {
            ApplicationTestSupport.Configure(builder, inventory);
            builder.Services.AddSingleton(_ => business);
            builder.Services.AddScoped<ScopedDependency>();
            builder.Services.AddCisWorkflows();
            void StartWorkflow(IServiceProvider root)
            {
                factory = root.GetRequiredService<IPipelineFactory>();
                execution = factory.CreateBuilder("BuildFailure", context).Add<HeldStep>().Build().Execute();
            }

            if (duringBuild)
            {
                builder.Services.Replace(ServiceDescriptor.Singleton<IHostLifetime>(root =>
                {
                    StartWorkflow(root);
                    throw original;
                }));
            }
            else
            {
                builder.Services.AddSingleton<IStartable>(root => new ApplicationTestSupport.Participant(_ =>
                {
                    StartWorkflow(root);
                    return Task.FromException(original);
                }));
            }
        }

        CISApplication app = web ? CISApplication.CreateWeb([], Configure, _ => { }) : new CISApplication(Configure);
        Task? disposal = null;
        try
        {
            Assert.AreSame(original, await ApplicationTestSupport.FailureAsync(app.StartAsync()));
            await context.ErrorEntered.Task.WaitAsync(ApplicationTestSupport.Deadline);
            disposal = app.DisposeAsync().AsTask();
            Assert.IsFalse(disposal.IsCompleted);
            business.Use();
            Assert.Throws<InvalidOperationException>(() => factory!.CreateBuilder("ClosedBuild", new HeldContext())
                .Add<HeldStep>().Build().Execute());

            context.ReleaseError.TrySetResult();
            await context.DisposalEntered.Task.WaitAsync(ApplicationTestSupport.Deadline);
            Assert.IsFalse(disposal.IsCompleted);
            business.Use();
            context.ReleaseDisposal.TrySetResult();
            await execution!.WaitAsync(ApplicationTestSupport.Deadline);
            await disposal.WaitAsync(ApplicationTestSupport.Deadline);
            Assert.AreSame(original, app.Failure);
            Assert.AreEqual(1, business.Disposals);
            Assert.AreEqual(1, context.ScopeDisposals);
        }
        finally
        {
            context.ReleaseError.TrySetResult();
            context.ReleaseDisposal.TrySetResult();
            await ApplicationTestSupport.SettleAsync(execution);
            await (disposal ?? app.DisposeAsync().AsTask()).WaitAsync(ApplicationTestSupport.Deadline);
        }
    }

    [TestMethod]
    public async Task NativeHostRejectsBusinessTeardownAfterTheWorkflowBridge()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Services.AddCisWorkflows();
        builder.Services.AddHostedService<BusinessDependency>();
        using IHost host = builder.Build();
        Exception failure = await ApplicationTestSupport.FailureAsync(host.StartAsync());
        StringAssert.Contains(failure.Message, "ordinary business hosted services");
        await host.StopAsync();
    }

    [TestMethod]
    public async Task BorrowedScopeUsesTheExactRequestDependencyAndDoesNotOwnItsDisposal()
    {
        var services = new ServiceCollection();
        services.AddCisWorkflows();
        services.AddScoped<ScopedDependency>();
        await using ServiceProvider root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var context = new ScopeContext();
        await using (AsyncServiceScope caller = root.CreateAsyncScope())
        {
            ScopedDependency expected = caller.ServiceProvider.GetRequiredService<ScopedDependency>();
            IPipelineFactory factory = root.GetRequiredService<IPipelineFactory>();
            await factory.CreateBuilderInScope("Borrowed", context, caller.ServiceProvider)
                .Add<ScopeStep>().Build().Execute();
            Assert.AreSame(expected, context.Dependency);
            Assert.AreEqual(0, expected.Disposals);
        }

        Assert.AreEqual(1, context.Dependency!.Disposals);
    }

    [TestMethod]
    public async Task NativeListenerMayAbortTheRequestBeforeTheWorkflowBarrierJoinsItsActualCleanup()
    {
        using var directory = new TemporaryDirectory();
        var inventory = ApplicationTestSupport.Inventory(directory);
        var context = new HeldContext();
        var business = new BusinessDependency();
        Task? execution = null;
        CISApplication app = CISApplication.CreateWeb([], builder =>
        {
            ApplicationTestSupport.Configure(builder, inventory);
            builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.Zero);
            builder.Services.AddSingleton(_ => business);
            builder.Services.AddSingleton<IHostedService>(root => root.GetRequiredService<BusinessDependency>());
            builder.Services.AddScoped<ScopedDependency>();
            builder.Services.AddCisWorkflows();
        }, application => application.MapGet("/held", async (HttpContext request, IPipelineFactory factory) =>
        {
            execution = factory.CreateBuilderInScope("RequestCleanup", context, request.RequestServices)
                .Add<HeldStep>().Build().Execute(request.RequestAborted);
            await execution;
            return Results.Ok();
        }));
        Task? disposal = null;
        try
        {
            await app.StartAsync().WaitAsync(ApplicationTestSupport.Deadline);
            using HttpClient client = ApplicationTestSupport.Client(app);
            Task<HttpResponseMessage> response = client.GetAsync("/held");
            await context.ErrorEntered.Task.WaitAsync(ApplicationTestSupport.Deadline);
            disposal = app.DisposeAsync().AsTask();
            await Assert.ThrowsAsync<HttpRequestException>(async () => await response.WaitAsync(ApplicationTestSupport.Deadline));
            business.Use();
            Assert.IsFalse(disposal.IsCompleted, "Listener abort does not mean application cleanup has settled.");
            context.ReleaseError.TrySetResult();
            await context.DisposalEntered.Task.WaitAsync(ApplicationTestSupport.Deadline);
            business.Use();
            Assert.IsFalse(disposal.IsCompleted);
            context.ReleaseDisposal.TrySetResult();
            await execution!.WaitAsync(ApplicationTestSupport.Deadline);
            await disposal.WaitAsync(ApplicationTestSupport.Deadline);
            Assert.AreEqual(1, business.Stops);
            Assert.AreEqual(1, context.ScopeDisposals);
        }
        finally
        {
            context.ReleaseError.TrySetResult();
            context.ReleaseDisposal.TrySetResult();
            await ApplicationTestSupport.SettleAsync(execution);
            await (disposal ?? app.DisposeAsync().AsTask()).WaitAsync(ApplicationTestSupport.Deadline);
        }
    }

    private sealed class HeldContext
    {
        internal TaskCompletionSource ErrorEntered { get; } = ApplicationTestSupport.Signal();
        internal TaskCompletionSource ReleaseError { get; } = ApplicationTestSupport.Signal();
        internal TaskCompletionSource DisposalEntered { get; } = ApplicationTestSupport.Signal();
        internal TaskCompletionSource ReleaseDisposal { get; } = ApplicationTestSupport.Signal();
        internal CancellationToken ExecutionToken { get; set; }
        internal int StepDisposals { get; set; }
        internal int ScopeDisposals { get; set; }
    }

    private sealed class HeldStep(BusinessDependency business, ScopedDependency scoped) : IPipelineStep<HeldContext>, IAsyncDisposable
    {
        private HeldContext? context;

        public Task<PipelineStepResult> Execute(HeldContext value, CancellationToken cancellationToken)
        {
            context = value;
            value.ExecutionToken = cancellationToken;
            scoped.Context = value;
            business.Use();
            return Task.FromResult(PipelineStepResult.Failed("expected", "Controlled expected failure."));
        }

        public async Task Error(HeldContext value, PipelineFailure failure, CancellationToken cancellationToken)
        {
            value.ErrorEntered.TrySetResult();
            await value.ReleaseError.Task;
            business.Use();
            scoped.Use();
        }

        public async ValueTask DisposeAsync()
        {
            context!.DisposalEntered.TrySetResult();
            await context.ReleaseDisposal.Task;
            business.Use();
            scoped.Use();
            context.StepDisposals++;
        }
    }

    private sealed class BusinessDependency : IHostedService, IAsyncDisposable
    {
        public BusinessDependency()
        {
        }

        internal int Stops { get; private set; }
        internal int Disposals { get; private set; }
        internal void Use()
        {
            Assert.AreEqual(0, Stops, "Business Stop must follow actual workflow settlement.");
            Assert.AreEqual(0, Disposals, "Provider disposal must follow actual workflow settlement.");
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken)
        {
            Stops++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposals = 1;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ScopedDependency : IDisposable
    {
        public ScopedDependency()
        {
        }

        internal HeldContext? Context { get; set; }
        internal int Disposals { get; private set; }
        internal void Use() => Assert.AreEqual(0, Disposals);
        public void Dispose()
        {
            Disposals++;
            if (Context is { } context)
            {
                context.ScopeDisposals++;
            }
        }
    }

    private sealed class ScopeContext
    {
        internal ScopedDependency? Dependency { get; set; }
    }

    private sealed class ScopeStep(ScopedDependency dependency) : IPipelineStep<ScopeContext>
    {
        public Task<PipelineStepResult> Execute(ScopeContext context, CancellationToken cancellationToken)
        {
            context.Dependency = dependency;
            return Task.FromResult(PipelineStepResult.Succeeded());
        }

        public Task Error(ScopeContext context, PipelineFailure failure, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
