using System.Text.Json;
using System.Text.Json.Nodes;

namespace CIS.Tests.UnitTests;

internal static class QualificationFixtureInputs
{
    internal static JsonObject ReadLogging(string name)
        => JsonNode.Parse(ReadLoggingText(name))!.AsObject();

    internal static string ReadLoggingText(string name)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string path = Path.Combine(directory.FullName, "conformance", "logging", name);
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Cannot find portable value fixtures.");
    }

    internal static byte[] ReadHealthcheck(string category, string name)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string path = Path.Combine(directory.FullName, "conformance", "healthcheck", "common", category, name);
            if (File.Exists(path)) { return File.ReadAllBytes(path); }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("The repository Healthcheck fixture is required.", name);
    }
}

// Keep only immutable portable inputs. Every caller receives private mutable
// JSON, and every product execution and its cleanup still run for each case.
internal sealed class LoggingFixtureInputs
{
    private readonly string catalog;
    private readonly Dictionary<string, string> rows;
    internal IReadOnlyList<string> Ids { get; }

    internal LoggingFixtureInputs(string name)
    {
        catalog = QualificationFixtureInputs.ReadLoggingText(name);
        using JsonDocument document = JsonDocument.Parse(catalog);
        JsonElement[] cases = document.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        rows = cases.ToDictionary(
            row => row.GetProperty("id").GetString()!, row => row.GetRawText(), StringComparer.Ordinal);
        Ids = Array.AsReadOnly(cases.Select(row => row.GetProperty("id").GetString()!).ToArray());
    }

    internal JsonObject Catalog() => JsonNode.Parse(catalog)!.AsObject();
    internal JsonObject Row(string id) => JsonNode.Parse(rows[id])!.AsObject();
}
