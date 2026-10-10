using System.Text.Json;
using CIS.TestSupport;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: CIS.DependencyChecker <assembly-path> <allowed-reference-names.json>");
    return 2;
}

try
{
    if (System.Reflection.AssemblyName.GetAssemblyName(args[0]).Name != "CIS.Core")
    {
        Console.Error.WriteLine("Unexpected compiled shipping assembly identity.");
        return 1;
    }

    string[] allowed = JsonSerializer.Deserialize<string[]>(File.ReadAllText(args[1]))
        ?? throw new InvalidDataException("The reference allowlist is missing.");
    IReadOnlyList<string> unexpected = AssemblyReferences.FindUnexpected(args[0], allowed);
    if (unexpected.Count != 0)
    {
        Console.Error.WriteLine($"Unapproved compiled assembly reference: {string.Join(", ", unexpected)}");
        return 1;
    }

    Console.WriteLine(JsonSerializer.Serialize(new { Assembly = args[0], References = AssemblyReferences.Read(args[0]) }));
    return 0;
}
catch (Exception exception) when (exception is IOException or BadImageFormatException or JsonException)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
