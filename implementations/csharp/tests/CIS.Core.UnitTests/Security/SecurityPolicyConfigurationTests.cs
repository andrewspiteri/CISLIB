using CIS.Configuration;
using CIS.Security;
using CIS.Security.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.UnitTests.Security;

[TestClass]
public sealed class SecurityPolicyConfigurationTests
{
    [TestMethod]
    public void DirectPolicyIsDetachedAndRetainsExistingCanonicalIdentity()
    {
        SecurityOptions options = SecurityTestSupport.Options();
        options.Grants.Add(new(SecurityTestSupport.Profile, "rules.read", "target-a", "Development", subject: SecurityTestSupport.Subject));
        ServiceCollection services = SecurityTestSupport.Services(options);
        options.Profiles.Clear();
        options.Capabilities.Clear();
        options.Grants.Clear();
        using ServiceProvider root = services.BuildServiceProvider();

        SecurityPolicySnapshot policy = root.GetRequiredService<SecurityPolicySnapshot>();

        Assert.HasCount(1, policy.Profiles);
        Assert.HasCount(1, policy.Grants);
        Assert.IsTrue(policy.IsRegisteredCapability("rules.read"));
        LocalApplicationIdentity identity = LocalApplicationIdentity.Capture();
        Assert.AreSame(EnvironmentOptions.ApplicationName, identity.Application);
        Assert.AreSame(EnvironmentOptions.EnvironmentName, identity.Environment);
        Assert.AreSame(EnvironmentOptions.InstanceId, identity.InstanceId);
    }

    [TestMethod]
    public void ReaderBackedRegistrationCopiesTheEntireSnapshotInsideTheReadGate()
    {
        var settings = new RetainedSecuritySettings();
        settings.Options.Profiles.Add(new(SecurityTestSupport.Profile, SecurityProfileKind.Native,
            SecurityProfileRoles.Inbound, authenticationScheme: SecurityTestSupport.Profile,
            identityMappings: [new(SecurityTestSupport.Subject, "caller-a", "Development")]));
        settings.Options.Capabilities.Add("rules.read");
        settings.Options.Grants.Add(new(SecurityTestSupport.Profile, "rules.read", "target-a", "Development", subject: SecurityTestSupport.Subject));
        var reader = new MutatingReader(settings);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(settings);
        services.AddSingleton<IConfigurationReader>(reader);
        services.AddCisSecurity<RetainedSecuritySettings>(configuration =>
        {
            Assert.IsTrue(reader.InRead);
            return configuration.Options;
        });
        using ServiceProvider root = services.BuildServiceProvider();

        SecurityPolicySnapshot snapshot = root.GetRequiredService<SecurityPolicySnapshot>();

        Assert.IsTrue(snapshot.IsRegisteredCapability("rules.read"));
        Assert.HasCount(1, snapshot.Grants);
        Assert.IsEmpty(settings.Options.Capabilities,
            "The retained input was mutated immediately after the protected callback; capture outside the gate would fail.");
        Assert.IsFalse(reader.InRead);
    }

    [TestMethod]
    public void ReceiverOnlyProfileNeedsNoOutboundIdentityOrOrigins()
    {
        SecurityOptions options = SecurityTestSupport.Options();
        using ServiceProvider root = SecurityTestSupport.Services(options).BuildServiceProvider();

        SecurityProfile profile = root.GetRequiredService<SecurityPolicySnapshot>().GetProfile(SecurityTestSupport.Profile);

        Assert.AreEqual(SecurityProfileRoles.Inbound, profile.Roles);
        Assert.IsNull(profile.OutboundIdentity);
        Assert.IsEmpty(profile.AllowedOrigins);
    }

    [TestMethod]
    public void CommonRegistrationAddsNoUnneededKeyRingOrHostedBackgroundOwner()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddCisSecurity(SecurityTestSupport.Options());

        Assert.IsFalse(services.Any(descriptor => descriptor.ServiceType.Namespace?.StartsWith(
            "Microsoft.AspNetCore.DataProtection", StringComparison.Ordinal) == true));
        Assert.IsFalse(services.Any(descriptor => descriptor.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)),
            "Security uses the existing sequential Startup owner; it must not introduce DataProtectionHostedService.");
    }

    [TestMethod]
    public void SelectedLimitsUnknownReferencesAndRolelessEntraGrantsFailValidation()
    {
        SecurityOptions overCapacity = SecurityTestSupport.Options();
        overCapacity.MaximumCapabilities = 1;
        Assert.ThrowsExactly<ArgumentException>(() => new ServiceCollection().AddCisSecurity(overCapacity));

        SecurityOptions unknownCapability = SecurityTestSupport.Options();
        unknownCapability.Grants.Add(new(SecurityTestSupport.Profile, "missing.action", "target-a", "Development",
            subject: SecurityTestSupport.Subject));
        Assert.ThrowsExactly<ArgumentException>(() => new ServiceCollection().AddCisSecurity(unknownCapability));

        SecurityOptions roleless = SecurityTestSupport.Options(SecurityProfileKind.EntraWorkloadIdentity);
        roleless.Grants.Add(new(SecurityTestSupport.Profile, "rules.read", "target-a", "Development",
            subject: SecurityTestSupport.Subject));
        Assert.ThrowsExactly<ArgumentException>(() => new ServiceCollection().AddCisSecurity(roleless));
    }

    [TestMethod]
    [DataRow("Rules.read")]
    [DataRow("rules..read")]
    [DataRow("rules.*")]
    [DataRow("1rules.read")]
    public void CapabilityGrammarIsRejectedRatherThanNormalized(string capability)
    {
        SecurityOptions options = SecurityTestSupport.Options();
        options.Capabilities.Add(capability);

        Assert.ThrowsExactly<ArgumentException>(() => new ServiceCollection().AddCisSecurity(options));
    }

    [TestMethod]
    public void OpaqueIdentifiersRemainExactAndBoundedWithoutUnicodeTruncation()
    {
        Assert.AreNotEqual(new SecuritySubject("issuer", "subject"), new SecuritySubject("issuer", " subject "));
        Assert.AreNotEqual(new SecuritySubject("issuer", "subject"), new SecuritySubject("Issuer", "subject"));
        string valid = string.Concat(Enumerable.Repeat("😀", 1024));
        Assert.AreEqual(valid, new SecuritySubject("issuer", valid).Identifier);
        Assert.ThrowsExactly<ArgumentException>(() => new SecuritySubject("issuer", valid + "x"));
        Assert.ThrowsExactly<ArgumentException>(() => new SecuritySubject("issuer", "broken\ud800"));
        Assert.ThrowsExactly<ArgumentException>(() => new SecuritySubject("issuer", new string(' ', 100000)));
    }

    private sealed class RetainedSecuritySettings : ConfigurationObject
    {
        internal SecurityOptions Options { get; } = new();
    }

    private sealed class MutatingReader(RetainedSecuritySettings settings) : IConfigurationReader
    {
        internal bool InRead
        {
            get; private set;
        }

        public TResult Read<TConfiguration, TResult>(TConfiguration configuration, Func<TConfiguration, TResult> read)
            where TConfiguration : class, IConfigurationObject
        {
            InRead = true;
            try
            {
                TResult result = read(configuration);
                settings.Options.Capabilities.Clear();
                return result;
            }
            finally
            {
                InRead = false;
            }
        }
    }
}
