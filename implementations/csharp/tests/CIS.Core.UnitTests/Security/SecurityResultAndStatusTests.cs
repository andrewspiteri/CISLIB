using CIS.CommonTesting;
using CIS.RestClient;
using CIS.Security;
using CIS.Security.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Security;

[TestClass]
public sealed class SecurityResultAndStatusTests
{
    [TestMethod]
    public async Task SecurityAuthenticationDetailKeepsTheExistingOuterRestContractAndNullCallCompatibility()
    {
        var failure = new SecurityFailure(SecurityFailureKind.CredentialUnavailable, "The configured workload source is unavailable.");
        var request = new RestRequestContext("GET", "https://service.example.test/value", new("fixture", "read", 0),
            new RestHeaders(), new RestQuery(), null);
        var authentication = new RestAuthenticationContext(request, (_, _) => ValueTask.FromResult(ReadOnlyMemory<byte>.Empty));

        authentication.FailWithSecurity(failure);

        Assert.AreEqual(RestFailureKind.Authentication, authentication.Failure!.Kind);
        Assert.AreEqual("client.authentication_failed", authentication.Failure.Code);
        Assert.AreSame(failure, authentication.Failure.SecurityFailure);
        authentication.Fail("The first declared outcome remains.", null);
        Assert.AreSame(failure, authentication.Failure.SecurityFailure);
        await authentication.CompleteAsync();

        var original = new RestResultContext(null, failureKind: RestFailureKind.Authentication,
            clientName: "fixture", operationName: "read");
        RestResultContext detailed = original.WithSecurityFailure(failure);
        Assert.IsNull(original.SecurityFailure);
        Assert.AreSame(failure, detailed.SecurityFailure);
        Assert.AreEqual(RestFailureKind.Authentication, detailed.FailureKind);
        Assert.IsNull(detailed.HttpStatusCode);
        Assert.ThrowsExactly<InvalidOperationException>(() => new RestResultContext().WithSecurityFailure(failure));
    }

    [TestMethod]
    public void StatusGaugeUsesOnlyRegisteredCertificateStateAndNeverQueriesASource()
    {
        SecurityOptions options = SecurityTestSupport.Options(SecurityProfileKind.ClientCertificate);
        using ServiceProvider root = SecurityTestSupport.Services(options).BuildServiceProvider();
        ISecurityProfileStatusReporter status = root.GetRequiredService<ISecurityProfileStatusReporter>();
        SecurityComposition composition = root.GetRequiredService<SecurityComposition>();
        Assert.IsEmpty(composition.ObserveCertificateRemaining());

        status.Publish(new(SecurityTestSupport.Profile, SecurityProfileAvailability.Available,
            credentialExpiresAtUtc: SecurityTestSupport.Now.AddSeconds(5)));
        Assert.AreEqual(5d, composition.ObserveCertificateRemaining().Single().Value);
        ((ManualTimeProvider)root.GetRequiredService<TimeProvider>()).Advance(TimeSpan.FromSeconds(6));
        Assert.AreEqual(0d, composition.ObserveCertificateRemaining().Single().Value);

        Assert.ThrowsExactly<ArgumentException>(() => status.Publish(new("unregistered", SecurityProfileAvailability.Available)));
        Assert.ThrowsExactly<ArgumentException>(() => status.RecordCertificateReload("unregistered", SecurityCertificateReloadOutcome.Adopted));
        Assert.HasCount(1, root.GetRequiredService<ISecurityProfileStatusSource>().CaptureStatus());
    }

    [TestMethod]
    public void ApplicationCredentialsCannotInventAHumanWithoutVerifiedDelegation()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new CallerContext("fixture", "certificate", SecurityTestSupport.Subject,
            SecurityActorKind.Application, "caller-a", "Development", new(SecurityTestSupport.Now), "v1",
            humanSubject: new("human-issuer", "operator")));
    }
}
