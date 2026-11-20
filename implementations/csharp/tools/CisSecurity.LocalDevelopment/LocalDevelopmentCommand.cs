using System.Security.Cryptography;
using System.Text.Json;

namespace CIS.Security.LocalDevelopment;

/// <summary>The small, nonpackable local credential tool's actual command boundary.</summary>
/// <remarks>Creates restricted PEM files only. It never installs trust, starts a host or imports a TLS key.</remarks>
public static class LocalDevelopmentCommand
{
    /// <summary>Runs setup, renew or deliberate trust reset; reports only non-secret paths and workspace identifiers.</summary>
    public static int Run(IReadOnlyList<string> arguments, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        try
        {
            Command command = Parse(arguments);
            WorkspaceCredentials.Execute(command, output);
            return 0;
        }
        catch (DevelopmentToolException exception)
        {
            error.WriteLine(exception.Message);
            return 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or CryptographicException or JsonException or ArgumentException or InvalidOperationException or AggregateException)
        {
            error.WriteLine("The local credential operation could not complete. Keep the isolated directory for inspection; no credential contents are printed.");
            return 1;
        }
    }

    private static Command Parse(IReadOnlyList<string> arguments)
    {
        if (arguments.Count is < 3 or > 9 || arguments.Count % 2 != 1
            || arguments.Any(value => value is null || value.Length is < 1 or > 4096)
            || arguments[0] is not ("setup" or "renew" or "reset"))
        {
            throw new DevelopmentToolException("Use setup|renew|reset --workspace <absolute-directory>; setup optionally accepts --service and --caller, reset requires --confirm <workspace-id>.");
        }
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int index = 1; index < arguments.Count; index += 2)
        {
            if (arguments[index] is not ("--workspace" or "--service" or "--caller" or "--confirm")
                || !options.TryAdd(arguments[index], arguments[index + 1]))
            {
                throw new DevelopmentToolException("Select each supported command option exactly once.");
            }
        }
        if (!options.TryGetValue("--workspace", out string? workspace) || !Path.IsPathFullyQualified(workspace)
            || options.ContainsKey("--confirm") != (arguments[0] == "reset")
            || arguments[0] != "setup" && (options.ContainsKey("--service") || options.ContainsKey("--caller")))
        {
            throw new DevelopmentToolException("Supply an absolute workspace; application names belong only to setup, and reset requires its exact workspace-id confirmation.");
        }
        string service = options.GetValueOrDefault("--service", "CountryData");
        string caller = options.GetValueOrDefault("--caller", "CountryData.Caller");
        ValidateApplication(service);
        ValidateApplication(caller);
        if (service == caller)
        {
            throw new DevelopmentToolException("Service and caller application names must be distinct.");
        }
        return new(arguments[0], Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace)), service, caller,
            options.GetValueOrDefault("--confirm"));
    }

    internal static void ValidateApplication(string value)
    {
        if (value is null || value.Length is < 1 or > 128 || !value.All(character => char.IsAsciiLetterOrDigit(character)
            || character is '.' or '_' or '-'))
        {
            throw new DevelopmentToolException("Tool application names use 1–128 ASCII letters, digits, dots, underscores or hyphens. This is a tool filename/URI subset, not a change to CIS identity contracts.");
        }
    }
}

internal sealed record Command(string Operation, string Workspace, string Service, string Caller, string? Confirmation);

internal sealed class DevelopmentToolException(string message) : Exception(message);
