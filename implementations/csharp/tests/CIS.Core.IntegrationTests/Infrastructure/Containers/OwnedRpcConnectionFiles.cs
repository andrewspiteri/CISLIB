using System.Text;
using System.Text.Json;
using CIS.Tests.IntegrationTests.Messaging;

namespace CIS.Tests.IntegrationTests.Infrastructure.Containers;

// A private per-child handoff, not another developer-selectable external-service mode.
internal static class OwnedRpcConnectionFiles
{
    internal static async Task<(string Postgres, string Rabbit)> WriteAsync(string caseDirectory,
        string connectionString, RabbitMqMessageFixture broker, string selectedRole)
    {
        if (selectedRole is not ("service" or "other"))
        {
            throw new ArgumentException("A child may use only its selected service or caller principal.", nameof(selectedRole));
        }
        string root = System.IO.Path.Combine(caseDirectory, "connections-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        PrivateContainerDirectory.Restrict(root);
        RabbitFixtureInformation information = broker.Information;
        string postgres = System.IO.Path.Combine(root, "postgres.txt");
        string rabbit = System.IO.Path.Combine(root, "rabbit.json");
        await PrivateContainerDirectory.WriteNewAsync(postgres, Encoding.UTF8.GetBytes(connectionString));
        await PrivateContainerDirectory.WriteNewAsync(System.IO.Path.Combine(root, "root.pem"), Encoding.UTF8.GetBytes(broker.RootPem));
        var principals = new Dictionary<string, object>();
        foreach (string role in new[] { "service", "other" })
        {
            RabbitFixturePrincipal principal = information.Principals[role];
            principals.Add(role, new
            {
                principal.User,
                Password = role == selectedRole ? principal.Password : ""
            });
        }
        await PrivateContainerDirectory.WriteNewAsync(rabbit, JsonSerializer.SerializeToUtf8Bytes(new
        {
            information.Host,
            information.Port,
            information.ServerName,
            information.VirtualHost,
            information.Namespace,
            information.ContainerId,
            information.Owner,
            RunId = IntegrationServices.RunId,
            ForeignUser = information.Principals["setup"].User,
            RootCertificateFile = "root.pem",
            Principals = principals
        }));
        return (postgres, rabbit);
    }
}
