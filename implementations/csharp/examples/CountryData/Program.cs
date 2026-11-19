using CIS.Hosting;

namespace CIS.Examples.CountryData;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Contains("--role=caller", StringComparer.Ordinal))
        {
            return await CountryCaller.RunAsync(args);
        }
        await using CISApplication app = CountryApplication.CreateService(args);

        return await app.RunAsync();
    }
}
