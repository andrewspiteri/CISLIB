using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using CIS.CommonTesting;
using CIS.RestClient;
using CIS.RestClient.Http;
using CIS.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CIS.Tests.Security.Certificates;

[TestClass]
[DoNotParallelize]
public sealed class CertificateRetirementFailureTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RetirementFailurePreservesOriginalFaultAndRequiredAvailabilityWithoutWaitingForHeldBodies(bool holdResponse)
    {
        using var certificates = new CertificateFixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using CertificateTestServer server = await CertificateTestServer.StartAsync(certificates, business: async context =>
        {
            await context.Response.WriteAsync(context.Connection.ClientCertificate!.Thumbprint);
            await context.Response.Body.FlushAsync();
            if (context.Request.Query.ContainsKey("hold"))
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(context.RequestAborted);
            }
        });
        var custody = new FaultCustody();
        var source = new PemSourceFixture(certificates, $"https://localhost:{server.BusinessPort}",
            required: true, nativeKeys: custody.Keys);
        Task<RestResult<string>>? held = null;
        try
        {
            await source.Host.StartAsync();
            Assert.AreEqual(SecurityProfileAvailability.Available, source.Status.Availability);
            Assert.IsTrue(source.Ready);
            if (holdResponse)
            {
                held = source.Client.Get<string>("/business?hold=1");
                await Task.WhenAny(entered.Task, held).WaitAsync(Deadline);
                if (held.IsCompleted)
                {
                    await held;
                    Assert.Fail("The real response must remain held before rotation.");
                }
            }
            else
            {
                Assert.IsTrue((await source.Client.Get<string>("/business")).Success);
            }
            custody.Arm();
            var replacement = certificates.Leaf(certificates.Root);
            source.Write(replacement);
            ManualHeartbeatResult rotation = await source.PollResultAsync().WaitAsync(Deadline);
            if (holdResponse || !OperatingSystem.IsWindows())
            {
                Assert.AreEqual(ManualHeartbeatOutcome.Succeeded, rotation.Outcome, rotation.Error?.ToString());
                Assert.AreEqual(2, source.Reloads.Count(value => value == SecurityCertificateReloadOutcome.Adopted));
                Assert.IsTrue(source.Ready);
                RestResult<string> current = await source.SecondClient.Get<string>("/business");
                Assert.IsTrue(current.Success);
                Assert.AreEqual(replacement.Thumbprint, current.Value);
            }
            else
            {
                Assert.AreEqual(ManualHeartbeatOutcome.Failed, rotation.Outcome);
                Assert.AreSame(custody.Original, rotation.Error);
                Assert.AreEqual(1, source.Reloads.Count(value => value == SecurityCertificateReloadOutcome.Adopted),
                    "Publishing B cannot report healthy adoption when eligible A cleanup has closed admission.");
                Assert.AreEqual(SecurityProfileAvailability.Unavailable, source.Status.Availability);
                Assert.IsFalse(source.Ready, "Eligible cleanup failure updates required availability before another metadata tick.");
            }
            release.TrySetResult();
            if (held is not null)
            {
                if (OperatingSystem.IsWindows())
                {
                    Exception failure = await Assert.ThrowsAsync<Exception>(() => held.WaitAsync(Deadline));
                    Assert.AreSame(custody.Original, failure);
                }
                else
                {
                    Assert.IsTrue((await held.WaitAsync(Deadline)).Success);
                }
            }
            await source.PollAsync(metadataOnly: true);
            if (OperatingSystem.IsWindows())
            {
                Assert.AreEqual(SecurityProfileAvailability.Unavailable, source.Status.Availability);
                Assert.AreEqual(SecurityFailureKind.CredentialUnavailable, source.Status.Failure?.Kind);
                Assert.IsNull(source.Status.CredentialExpiresAtUtc);
                Assert.IsFalse(source.Ready);
                Exception request = await Assert.ThrowsAsync<Exception>(() => source.Client.Get<string>("/business"));
                Assert.AreSame(custody.Original, request);
                ManualHeartbeatResult unchanged = await source.PollResultAsync();
                Assert.AreEqual(ManualHeartbeatOutcome.Failed, unchanged.Outcome);
                Assert.AreSame(custody.Original, unchanged.Error);
                await source.Host.Services.GetRequiredKeyedService<ISecurityCredentialControl>(PemSourceFixture.Profile)
                    .RequestSourceRecheckAsync();
                ManualHeartbeatResult forced = await source.PollResultAsync();
                Assert.AreSame(custody.Original, forced.Error);
                Assert.AreEqual(SecurityProfileAvailability.Unavailable, source.Status.Availability);
                Assert.IsFalse(source.Ready);
            }
            else
            {
                Assert.AreEqual(SecurityProfileAvailability.Available, source.Status.Availability);
                Assert.AreEqual(0, custody.Keys.LiveKeyCount, "Non-Windows native memory ownership imports no user-key backing.");
            }
        }
        finally
        {
            release.TrySetResult();
            if (held is not null)
            {
                try
                {
                    await held.WaitAsync(Deadline);
                }
                catch (Exception error) when (OnlyExpected(error, custody.Original))
                {
                }
            }
            try
            {
                await source.DisposeAsync();
            }
            catch (Exception error) when (OnlyExpected(error, custody.Original))
            {
                custody.DisposalObserved = true;
            }
            custody.VerifySettled();
        }
    }

    private static bool OnlyExpected(Exception error, Exception expected) => ReferenceEquals(error, expected)
        || error is AggregateException aggregate && aggregate.InnerExceptions.Count != 0
            && aggregate.InnerExceptions.All(value => OnlyExpected(value, expected));

    private sealed class FaultCustody
    {
        private readonly object gate = new();
        private readonly string journal;
        private readonly List<RestHttpNativeKeyMetadata> created = [];
        private readonly List<RestHttpNativeKeyMetadata> removed = [];
        private string? first;
        private bool armed;
        private bool threw;

        internal FaultCustody()
        {
            string directory = Path.Combine(Path.GetTempPath(), "retirement-custody-" + Guid.NewGuid().ToString("N"));
            if (OperatingSystem.IsWindows())
            {
                DirectoryInfo createdDirectory = Directory.CreateDirectory(directory);
                using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                SecurityIdentifier owner = identity.User ?? throw new InvalidOperationException("A native fixture owner is required.");
                DirectorySecurity access = createdDirectory.GetAccessControl(AccessControlSections.Access);
                access.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                access.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                createdDirectory.SetAccessControl(access);
            }
            else
            {
                Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            journal = Path.Combine(directory, "keys.jsonl");
            Keys = new(2, Record);
        }

        internal RestHttpNativeKeyScope Keys { get; }
        internal InvalidOperationException Original { get; } = new("Expected fixture Removed custody failure after durable journaling.");
        internal bool DisposalObserved { get; set; }

        internal void Arm()
        {
            lock (gate)
            {
                armed = true;
            }
        }

        private void Record(RestHttpNativeKeyMetadata metadata, RestHttpNativeKeyTransition transition)
        {
            lock (gate)
            {
                byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    transition,
                    metadata.ProviderName,
                    metadata.ContainerName,
                    metadata.UniqueName,
                    metadata.FilePath,
                    metadata.FileLength,
                    metadata.ProviderType,
                    metadata.KeyNumber
                });
                using (var stream = new FileStream(journal, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    stream.Write(bytes);
                    stream.WriteByte((byte)'\n');
                    stream.Flush(flushToDisk: true);
                }
                if (transition == RestHttpNativeKeyTransition.Created)
                {
                    created.Add(metadata);
                    first ??= metadata.UniqueName;
                }
                else
                {
                    removed.Add(metadata);
                    if (armed && !threw && metadata.UniqueName == first)
                    {
                        threw = true;
                        throw Original;
                    }
                }
            }
        }

        internal void VerifySettled()
        {
            CollectionAssert.AreEquivalent(created, removed);
            foreach (RestHttpNativeKeyMetadata metadata in created)
            {
                Assert.IsFalse(File.Exists(metadata.FilePath), "Only exact newly owned key backing is checked.");
            }
            if (threw)
            {
                Assert.IsTrue(DisposalObserved, "The source must preserve its original cleanup fault during disposal.");
                Assert.AreEqual(1, Keys.LiveKeyCount, "The injected failed custody receipt remains a bounded unresolved entry.");
                Assert.ThrowsExactly<InvalidOperationException>(Keys.Dispose);
            }
            else
            {
                Assert.AreEqual(0, Keys.LiveKeyCount);
                Keys.Dispose();
            }
        }
    }
}
