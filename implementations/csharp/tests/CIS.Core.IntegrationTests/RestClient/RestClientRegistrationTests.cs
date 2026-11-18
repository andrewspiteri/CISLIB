using CIS.RestClient;
using CIS.RestClient.Hosting;
using CIS.RestClient.Http;
using CIS.Tests.UnitTests.RestClient;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics.Metrics;

namespace CIS.Tests.IntegrationTests.RestClient;

[TestClass]
public sealed class RestClientRegistrationTests
{
    [TestMethod]
    public async Task KeyedClientsUseCopiedOptionsAndSeparateDefaultsWithoutAnOptionalAdapter()
    {
        await using var server = await RestHttpLocalServer.StartAsync(async context =>
        {
            await context.Response.WriteAsJsonAsync(context.Request.Headers["X-Client"].ToString());
        });
        var first = new RestClientOptions("orders") { BaseUrl = server.Address };
        first.Headers.Set("X-Client", "first");
        var second = new RestClientOptions("catalog") { BaseUrl = server.Address };
        second.Headers.Set("X-Client", "second");
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Host.UseDefaultServiceProvider(settings => { settings.ValidateOnBuild = true; settings.ValidateScopes = true; });
        IServiceCollection services = builder.Services;
        services.AddCisRestClient(first).AddCisRestClient(second);
        first.BaseUrl = "http://127.0.0.1:1"; first.Headers.Set("X-Client", "changed");
        await using WebApplication application = builder.Build();
        IServiceProvider root = application.Services;
        RestResult<string>[] results = await Task.WhenAll(
            root.GetRequiredKeyedService<IRestClient>("orders").Get<string>("/"),
            root.GetRequiredKeyedService<IRestClient>("catalog").Get<string>("/")).WaitAsync(RestHttpTestSupport.Deadline);
        Assert.IsTrue(results.All(result => result.Success));
        CollectionAssert.AreEqual(new[] { "first", "second" }, results.Select(result => result.Value).ToArray());
        Assert.IsTrue(results.All(result => result.Context.AttemptCount == 1
            && result.Context.RetryStopReason is null));
        Assert.AreEqual("orders", results[0].Context.ClientName);
        Assert.AreEqual("catalog", results[1].Context.ClientName);
    }

    [TestMethod]
    public void RegistrationRejectsDuplicateAliasesAndFiniteRegistryOverflowWithoutCreatingResources()
    {
        var services = new ServiceCollection();
        services.AddCisRestClient(new("orders"));
        Assert.ThrowsExactly<InvalidOperationException>(() => services.AddCisRestClient(new("orders")));
        for (int index = 1; index < 16; index++) { services.AddCisRestClient(new("client" + index)); }
        Assert.ThrowsExactly<ArgumentException>(() => services.AddCisRestClient(new("overflow")));
        Assert.AreEqual(16, services.Count(descriptor => descriptor.IsKeyedService && descriptor.ServiceType == typeof(IRestClient)));

        var crowded = new RestClientOptions("crowded");
        for (int index = 0; index < 31; index++) { crowded.Operations.Add("operation" + index); }
        var pairs = new ServiceCollection(); pairs.AddCisRestClient(crowded);
        Assert.ThrowsExactly<ArgumentException>(() => pairs.AddCisRestClient(new("extra")));
        Assert.AreEqual(1, pairs.Count(descriptor => descriptor.IsKeyedService && descriptor.ServiceType == typeof(IRestClient)));
    }

    [TestMethod]
    public async Task MissingSelectedPolicyFailsBeforeAnyBorrowedTransportFactoryRuns()
    {
        int factories = 0;
        var services = new ServiceCollection();
        services.AddCisRestClient(new("orders") { RetryPolicy = RestRetrySelection.Named("missing") }, _ =>
        {
            factories++;
            throw new InvalidOperationException("The factory must not run.");
        });
        await using ServiceProvider root = services.BuildServiceProvider();
        Assert.ThrowsExactly<InvalidOperationException>(() => root.GetRequiredKeyedService<IRestClient>("orders"));
        Assert.AreEqual(0, factories);
        Assert.ThrowsExactly<InvalidOperationException>(() => new HttpRestClient(new("standalone")
        { RetryPolicy = RestRetrySelection.Named("missing") }));
    }

    [TestMethod]
    public async Task BorrowedHostTransportRequiresTheExactRootFactoryAndRemainsCallerOwned()
    {
        await using RestHttpTransport foreign = RestHttpTransport.Create();
        var invalid = new ServiceCollection(); invalid.AddCisRestClient(new("invalid"), _ => foreign);
        await using (ServiceProvider root = invalid.BuildServiceProvider())
        { Assert.ThrowsExactly<InvalidOperationException>(() => root.GetRequiredKeyedService<IRestClient>("invalid")); }
        foreign.ValidateForHost(null);

        RestHttpTransport? borrowed = null;
        var valid = new ServiceCollection();
        valid.AddCisRestClient(new("valid"), root => borrowed = RestHttpTransport.Create(root.GetRequiredService<IMeterFactory>()));
        try
        {
            await using (ServiceProvider root = valid.BuildServiceProvider())
            { _ = root.GetRequiredKeyedService<IRestClient>("valid"); }
            Assert.IsNotNull(borrowed);
            borrowed.ValidateForHost(null);
        }
        finally { if (borrowed is not null) { await borrowed.DisposeAsync(); } }
    }

    [TestMethod]
    public void IdempotencyKeyCannotBeDeclaredAsRefreshableAuthentication()
    {
        var options = new RestClientOptions("orders"); options.CredentialHeaders.Add("Idempotency-Key");
        Assert.ThrowsExactly<ArgumentException>(() => new ServiceCollection().AddCisRestClient(options));
        Assert.ThrowsExactly<ArgumentException>(() => new HttpRestClient(options));
    }
}
