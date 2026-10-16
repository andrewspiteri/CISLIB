using System.Text.Json;
using CIS.TestSupport;
using CIS.Verification;

if (args.Length > 0 && args[0] is "type-manager-runtime" or "type-manager-instrumentation" or "type-manager-instrumentation-controls")
{
    try
    {
        return args[0] switch
        {
            "type-manager-runtime" => await TypeManagerRuntimeVerification.RunAsync(args[1..]),
            "type-manager-instrumentation" => await TypeManagerInstrumentationVerification.RunAsync(args[1..]),
            _ => await TypeManagerInstrumentationControls.RunAsync(args[1..])
        };
    }
    catch (ArgumentException error) { Console.Error.WriteLine(error.Message); return 2; }
    catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or JsonException or System.Xml.XmlException or System.ComponentModel.Win32Exception or TimeoutException)
    { Console.Error.WriteLine(error.Message); return 1; }
}


if (args.Length > 0 && args[0] == "select-jsonl-inputs")
{
    return JsonlInputSelector.Run(args[1..]);
}

if (args.Length > 0 && args[0] == "shipping-coverage")
{
    return await ShippingCoverage.RunAsync(args[1..]);
}

if (args.Length > 0 && args[0] == "focused-test")
{
    return await FocusedTests.RunAsync(args[1..]);
}

if (args.Length == 4 && args[0] == "architecture")
{
    try { await ArchitectureInspection.Write(args[1], args[2], args[3]); return 0; }
    catch (Exception exception) when (exception is IOException or InvalidDataException or BadImageFormatException or InvalidOperationException or ArgumentException or JsonException or System.Xml.XmlException)
    {
        Console.Error.WriteLine("Architecture inspection failed: " + exception.Message);
        return 1;
    }
}
if (args.Length == 3 && args[0] == "inspect")
{
    try { AssemblyInspection.Write(args[1], args[2]); return 0; }
    catch (Exception exception) when (exception is IOException or InvalidDataException or BadImageFormatException or InvalidOperationException or ArgumentException)
    {
        Console.Error.WriteLine("Assembly inspection failed: " + exception.Message);
        return 1;
    }
}

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
catch (Exception exception) when (exception is IOException or InvalidDataException or BadImageFormatException or JsonException)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
