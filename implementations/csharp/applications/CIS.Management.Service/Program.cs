using CIS.Management.Service.Security;
using CIS.Management.Service.Metadata;
using CIS.Management.Service.Backends;

namespace CIS.Management.Service;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        bool migrate = args.Contains("--migrate", StringComparer.Ordinal);
        bool installTopology = args.Contains("--install-topology", StringComparer.Ordinal);
        if (migrate && installTopology)
        {
            throw new ArgumentException("Select one explicit deployment action per invocation.");
        }
        string[] configurationArgs = args.Where(argument => argument is not ("--migrate" or "--install-topology")).ToArray();
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().AddCommandLine(configurationArgs).Build();
        string? settingsFile = configuration["settings"];
        if (settingsFile is not null)
        {
            if (!Path.IsPathFullyQualified(settingsFile))
            {
                throw new ArgumentException("Select an absolute deployment settings file.");
            }
            configuration = new ConfigurationBuilder().AddJsonFile(settingsFile, optional: false, reloadOnChange: false)
                .AddEnvironmentVariables().AddCommandLine(configurationArgs).Build();
        }
        if (installTopology)
        {
            ManagementDeliveryOptions topology = ManagementDeliveryOptions.FromConfiguration(configuration.GetSection("Delivery"), setupCredentials: true);
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            await topology.InstallTopologyAsync(deadline.Token);
            return 0;
        }
        string connectionVariable = configuration["DatabaseConnectionEnvironment"] ?? "CIS_MANAGEMENT_POSTGRES";
        string cursorVariable = configuration["CursorKeyEnvironment"] ?? "CIS_MANAGEMENT_CURSOR_KEY";
        string connection = RequiredEnvironment(connectionVariable);
        byte[] cursorKey = Convert.FromBase64String(RequiredEnvironment(cursorVariable));
        ManagementServiceOptions options;
        try
        {
            options = new(connection, cursorKey, basePath: configuration["BasePath"] ?? "",
                delivery: migrate ? null
                    : ManagementDeliveryOptions.FromConfiguration(configuration.GetSection("Delivery")),
                backend: ManagementBackendOptions.FromConfiguration(configuration.GetSection("Backend")));
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(cursorKey);
        }
        if (migrate)
        {
            await ManagementServiceApplication.MigrateAsync(configurationArgs, options);
            return 0;
        }
        ManagementSecurityOptions security = ManagementSecurityOptions.FromConfiguration(configuration.GetSection("Security"));
        await using var application = ManagementServiceApplication.Create(args, options, security);
        return await application.RunAsync();
    }

    private static string RequiredEnvironment(string name)
    {
        if (name.Length is < 1 or > 128 || name.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
        {
            throw new ArgumentException("Select a bounded environment variable name for deployment secrets.");
        }
        return System.Environment.GetEnvironmentVariable(name)
            ?? throw new ArgumentException("A required management deployment secret is absent.");
    }
}
