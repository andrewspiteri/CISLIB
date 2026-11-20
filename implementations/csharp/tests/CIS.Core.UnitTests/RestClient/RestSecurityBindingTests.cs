using CIS.CommonTesting;
using CIS.RestClient;
using CIS.RestClient.Http;
using CIS.Security;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.RestClient;

[TestClass]
public sealed class RestSecurityBindingTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ACertificateTransportCannotBeBorrowedByAnUnboundOrDifferentOwner(bool unbound)
    {
        RestSecurityBinding binding = Binding(RestSecurityCredentialKind.ClientCertificate);
        await using RestHttpTransport transport = RestHttpTransport.CreateSecurityBound(binding);
        var options = new RestClientOptions("bound")
        {
            BaseUrl = "https://service.invalid/",
            SecurityBinding = unbound ? null : Binding(RestSecurityCredentialKind.ClientCertificate)
        };
        Assert.ThrowsExactly<InvalidOperationException>(() => new HttpRestClient(options, transport));
        transport.ValidateForHost(null);
    }

    [TestMethod]
    public async Task ExactBorrowerRetainsTheOriginPolicyAndDoesNotDisposeItsOwner()
    {
        RestSecurityBinding binding = Binding(RestSecurityCredentialKind.ClientCertificate);
        await using RestHttpTransport transport = RestHttpTransport.CreateSecurityBound(binding);
        var options = new RestClientOptions("bound") { BaseUrl = "https://service.invalid/", SecurityBinding = binding };
        await using (var borrower = new HttpRestClient(options, transport))
        {
            RestResult<RestNoValue> unavailable = await borrower.Get<RestNoValue>("");
            Assert.AreEqual(RestFailureKind.Authentication, unavailable.Context.FailureKind);
            Assert.AreEqual("client.authentication_failed", unavailable.ErrorCode);
            Assert.AreEqual(SecurityFailureKind.CredentialUnavailable, unavailable.Context.SecurityFailure?.Kind);
            Assert.AreEqual(0, unavailable.Context.AttemptCount);
            Assert.IsNull(unavailable.Context.HttpStatusCode);
        }
        transport.ValidateForHost(null);
        options.AllowedOrigins.Add("https://service.invalid");
        options.AllowedOrigins.Add("https://other.invalid");
        Assert.ThrowsExactly<ArgumentException>(() => new HttpRestClient(options, transport));
    }

    [TestMethod]
    [DataRow("disable")]
    [DataRow("replace")]
    public async Task BoundCallsRejectSuppressionAndReplacementBeforeAnyAuthenticationWork(string mutation)
    {
        int authenticationCalls = 0;
        var options = RequestOptions((_, _) =>
        {
            authenticationCalls++;
            return Task.CompletedTask;
        });
        await using var client = new HttpRestClient(options);
        var call = mutation == "disable" ? new RestRequestOptions { DisableAuthentication = true }
            : new RestRequestOptions { Authenticate = (_, _) => Task.CompletedTask };
        Assert.ThrowsExactly<ArgumentException>(() => client.Get<RestNoValue>("", call));
        Assert.AreEqual(0, authenticationCalls);
    }

    [TestMethod]
    [DataRow("trace")]
    [DataRow("method")]
    [DataRow("destination")]
    [DataRow("plain-http")]
    public async Task FinalizedMethodAndDestinationViolationsRejectBeforeCredentialsOrDispatch(string mutation)
    {
        int authenticationCalls = 0;
        var options = RequestOptions((_, _) =>
        {
            authenticationCalls++;
            return Task.CompletedTask;
        });
        options.ConfigureRequest = (request, _) =>
        {
            if (mutation == "method")
            {
                request.Method = "POST";
            }
            if (mutation == "destination")
            {
                request.Url = "https://other.invalid/";
            }
            if (mutation == "plain-http")
            {
                request.Url = "http://service.invalid/";
            }
            return Task.CompletedTask;
        };
        await using var client = new HttpRestClient(options);
        RestResult<RestNoValue> result = mutation == "trace" ? await client.Trace<RestNoValue>("")
            : await client.Get<RestNoValue>("");
        Assert.AreEqual(RestFailureKind.Preparation, result.Context.FailureKind);
        Assert.AreEqual(0, result.Context.AttemptCount);
        Assert.AreEqual(0, authenticationCalls);
    }

    [TestMethod]
    public async Task DeclaredCredentialFailurePreservesTypedLocalEvidenceWithoutFabricatingAResponse()
    {
        var failure = new SecurityFailure(SecurityFailureKind.CredentialUnavailable, "The selected fixture credential is unavailable.",
            providerCode: "fixture.unavailable");
        var options = RequestOptions((context, _) =>
        {
            context.FailWithSecurity(failure);
            return Task.CompletedTask;
        });
        await using var client = new HttpRestClient(options);
        RestResult<RestNoValue> result = await client.Get<RestNoValue>("");
        Assert.AreEqual("client.authentication_failed", result.ErrorCode);
        Assert.AreEqual(RestFailureKind.Authentication, result.Context.FailureKind);
        Assert.AreSame(failure, result.Context.SecurityFailure);
        Assert.AreEqual("fixture.unavailable", result.Context.SecurityFailure?.ProviderCode);
        Assert.IsNull(result.Context.RemoteErrorCode);
        Assert.AreEqual(0, result.Context.AttemptCount);
        Assert.IsNull(result.Context.HttpStatusCode);
    }

    [TestMethod]
    public async Task TotalDeadlineSupersedesAnEarlierCredentialExplanation()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        var options = RequestOptions((context, _) =>
        {
            context.FailWithSecurity(new(SecurityFailureKind.CredentialUnavailable, "Unavailable before the deadline."));
            clock.Advance(TimeSpan.FromSeconds(2));
            return Task.CompletedTask;
        });
        options.Timeout = TimeSpan.FromSeconds(1);
        await using var client = new HttpRestClient(options, null, clock);
        RestResult<RestNoValue> result = await client.Get<RestNoValue>("");
        Assert.AreEqual(RestFailureKind.Timeout, result.Context.FailureKind);
        Assert.IsNull(result.Context.SecurityFailure);
        Assert.AreEqual(0, result.Context.AttemptCount);
    }

    [TestMethod]
    public async Task AuthenticationProgrammingFaultRemainsTheOriginalFaultAfterADeclaredFailure()
    {
        var original = new InvalidOperationException("Fixture authentication invariant fault.");
        var options = RequestOptions((context, _) =>
        {
            context.FailWithSecurity(new(SecurityFailureKind.CredentialUnavailable, "Unavailable."));
            throw original;
        });
        await using var client = new HttpRestClient(options);
        InvalidOperationException observed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => client.Get<RestNoValue>(""));
        Assert.AreSame(original, observed);
    }

    [TestMethod]
    public async Task OwnedBodyCleanupFaultIsNotHiddenBehindAnOrdinaryCredentialFailure()
    {
        var original = new InvalidOperationException("Fixture input cleanup fault.");
        var options = RequestOptions((context, _) =>
        {
            context.FailWithSecurity(new(SecurityFailureKind.CredentialUnavailable, "Unavailable."));
            return Task.CompletedTask;
        });
        await using var client = new HttpRestClient(options);
        var body = new FailingCleanupStream(original);
        AggregateException observed = await Assert.ThrowsExactlyAsync<AggregateException>(() => client.Post<RestNoValue>("",
            new()
            {
                Body = RestHttpBody.Stream(body, ownsStream: true)
            }));
        Assert.IsTrue(observed.Flatten().InnerExceptions.Any(error => ReferenceEquals(error, original)));
        Assert.IsFalse(body.CanRead);
    }

    [TestMethod]
    public async Task InfinitePoolLifetimeRemainsAnOrdinaryOptionButIsRejectedWhenSecurityBound()
    {
        var options = new RestHttpTransportOptions { PooledConnectionLifetime = Timeout.InfiniteTimeSpan };
        await using RestHttpTransport ordinary = RestHttpTransport.Create(options: options);
        Assert.ThrowsExactly<ArgumentException>(() => RestHttpTransport.CreateSecurityBound(
            Binding(RestSecurityCredentialKind.ClientCertificate), options: options));
    }

    [TestMethod]
    public void MetadataCopiesOriginsButCannotMasqueradeAsAnActualProviderPolicyOwner()
    {
        string[] origins = ["https://service.invalid"];
        var binding = new RestSecurityBinding("fixture", new SecuritySubject("fixture-issuer", "fixture-client"),
            "fixture-policy", RestSecurityCredentialKind.ClientCertificate, origins);
        origins[0] = "https://changed.invalid";
        Assert.AreEqual("https://service.invalid", binding.AllowedOrigins.Single());
        var options = new SecurityOptions { PolicyVersion = "fixture-policy" };
        options.Profiles.Add(new("fixture", SecurityProfileKind.Native, SecurityProfileRoles.Inbound,
            authenticationScheme: "fixture"));
        SecurityPolicySnapshot policy = SecurityPolicySnapshot.Capture(options, 16);
        Assert.ThrowsExactly<InvalidOperationException>(() => binding.ValidateForPolicy(policy));
        Assert.ThrowsExactly<ArgumentException>(() => new RestSecurityBinding(policy, "fixture",
            RestSecurityCredentialKind.ClientCertificate));
    }

    private static RestSecurityBinding Binding(RestSecurityCredentialKind kind) => new("fixture",
        new SecuritySubject("fixture-issuer", "fixture-application"), "fixture-policy", kind, ["https://service.invalid"]);

    private static RestClientOptions RequestOptions(Func<RestAuthenticationContext, CancellationToken, Task> authenticate)
        => new("fixture")
        {
            BaseUrl = "https://service.invalid/",
            SecurityBinding = Binding(RestSecurityCredentialKind.RequestAuthentication),
            Authenticate = authenticate,
            LogCalls = false
        };

    private sealed class FailingCleanupStream(Exception failure) : MemoryStream([1, 2, 3])
    {
        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            throw failure;
        }
    }
}
