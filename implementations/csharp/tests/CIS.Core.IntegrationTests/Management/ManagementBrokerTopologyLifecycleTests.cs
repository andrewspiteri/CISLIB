using System.Net;
using System.Text.Json;
using CIS.Management.Service.Metadata;
using CIS.Tests.IntegrationTests.Infrastructure.Containers;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace CIS.Tests.IntegrationTests.Management;

[TestClass]
[DoNotParallelize]
[TestCategory("RequiresDocker")]
[TestCategory("PostgreSql")]
[TestCategory("RabbitMq")]
[TestCategory("ManagementService")]
public sealed class ManagementBrokerTopologyLifecycleTests
{
    [TestMethod]
    public async Task RepeatedPooledManagementFixturesRetireOnlyTheirExactTopologyIncludingFailedSetup()
    {
        OwnedRabbitService broker = await IntegrationServices.GetRabbitAsync();
        await using ManagementHostFixture sibling = await ManagementHostFixture.CreateAsync(replicas: 1);
        RabbitFixtureInformation information = broker.CaptureInformation();
        await using IConnection inspect = await broker.CreateConnectionFactory(information.Principals["setup"])
            .CreateConnectionAsync();
        string siblingNamespace = sibling.Options.Delivery!.Broker.Namespace;
        string[] initialQueues = await ReadOwnedNamesAsync(broker, "list_queues");
        string[] initialExchanges = await ReadOwnedNamesAsync(broker, "list_exchanges");
        for (int index = 0; index < 3; index++)
        {
            ManagementHostFixture fixture = await ManagementHostFixture.CreateAsync(start: false);
            string space = fixture.Options.Delivery!.Broker.Namespace;
            try
            {
                await AssertPresentAsync(inspect, space);
            }
            finally
            {
                await fixture.DisposeAsync();
            }
            await AssertAbsentAsync(inspect, space);
            await fixture.DisposeAsync();
            await AssertPresentAsync(inspect, siblingNamespace);
            using HttpResponseMessage response = await sibling.Client("operator-a").GetAsync("applications/" + sibling.ApplicationA);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, "Removing a disposed fixture must preserve its running sibling's actual service.");
        }

        // This existing invalid registration fails after native topology installation and schema creation.
        Exception? setupFailure = null;
        ManagementHostFixture? unexpected = null;
        try
        {
            unexpected = await ManagementHostFixture.CreateAsync(start: false, initialInstanceNameA: "");
        }
        catch (Exception error)
        {
            setupFailure = error;
        }
        finally
        {
            if (unexpected is not null)
            {
                await unexpected.DisposeAsync();
            }
        }
        Assert.IsNotNull(setupFailure, "The invalid registration must exercise the actual fixture setup failure path.");
        Assert.IsFalse(setupFailure is AggregateException, "Partial setup cleanup must settle without adding a cleanup failure.");
        CollectionAssert.AreEqual(initialQueues, await ReadOwnedNamesAsync(broker, "list_queues"));
        CollectionAssert.AreEqual(initialExchanges, await ReadOwnedNamesAsync(broker, "list_exchanges"));
        await AssertPresentAsync(inspect, siblingNamespace);
    }

    private static async Task AssertPresentAsync(IConnection connection, string space)
    {
        await using IChannel channel = await connection.CreateChannelAsync();
        QueueDeclareOk queue = await channel.QueueDeclarePassiveAsync(space + ".quarantine.management.replies");
        Assert.AreEqual(0U, queue.ConsumerCount);
        foreach (string exchange in Exchanges(space))
        {
            await channel.ExchangeDeclarePassiveAsync(exchange);
        }
    }

    private static async Task AssertAbsentAsync(IConnection connection, string space)
    {
        await using (IChannel channel = await connection.CreateChannelAsync())
        {
            OperationInterruptedException missing = await Assert.ThrowsExactlyAsync<OperationInterruptedException>(
                () => channel.QueueDeclarePassiveAsync(space + ".quarantine.management.replies"));
            Assert.AreEqual((ushort)404, missing.ShutdownReason!.ReplyCode);
        }
        foreach (string exchange in Exchanges(space))
        {
            await using IChannel channel = await connection.CreateChannelAsync();
            OperationInterruptedException missing = await Assert.ThrowsExactlyAsync<OperationInterruptedException>(
                () => channel.ExchangeDeclarePassiveAsync(exchange));
            Assert.AreEqual((ushort)404, missing.ShutdownReason!.ReplyCode);
        }
    }

    private static string[] Exchanges(string space) =>
    [
        space + ".route." + ManagementDeliveryOptions.AuditRoute,
        space + ".reply.management",
        space + ".native-quarantine.management.replies"
    ];

    private static async Task<string[]> ReadOwnedNamesAsync(OwnedRabbitService broker, string command)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await broker.Container.ExecAsync(["rabbitmqctl", command, "-p", broker.VirtualHost,
            "name", "--formatter", "json"], deadline.Token);
        Assert.AreEqual(0L, result.ExitCode, "The exact owned broker must provide its bounded topology observation.");
        Assert.IsLessThanOrEqualTo(65_536, result.Stdout.Length);
        using JsonDocument document = JsonDocument.Parse(result.Stdout);
        return document.RootElement.EnumerateArray().Select(item => item.GetProperty("name").GetString()!)
            .Where(name => name.StartsWith(broker.Namespace + ".", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToArray();
    }
}
