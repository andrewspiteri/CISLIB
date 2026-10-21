using System.Text.Json;
using CIS.TestSupport;
using CIS.Verification;

if (args.Length > 0 && args[0] is "healthcheck-package" or "healthcheck-coverage" or "healthcheck-platform-prepare" or "healthcheck-critical-mutation" or "healthcheck-pipe-identities" or "healthcheck-rollback-mutation" or "healthcheck-mutation-children" or "healthcheck-child-audit-controls")
{
    try
    {
        return args[0] switch
        {
            "healthcheck-package" => await HealthcheckPackageVerification.RunAsync(args[1..]),
            "healthcheck-coverage" => await HealthcheckCoverageVerification.RunAsync(args[1..]),
            "healthcheck-platform-prepare" => await HealthcheckPlatformPreparation.RunAsync(args[1..]),
            "healthcheck-pipe-identities" => await HealthcheckPipeIdentityVerification.RunAsync(args[1..]),
            "healthcheck-rollback-mutation" => await HealthcheckRollbackMutationVerification.RunAsync(args[1..]),
            "healthcheck-mutation-children" => await HealthcheckChildMutationVerification.RunAsync(args[1..]),
            "healthcheck-child-audit-controls" => await HealthcheckChildAuditControls.RunAsync(args[1..]),
            _ => await HealthcheckCriticalMutationVerification.RunAsync(args[1..])
        };
    }
    catch (ArgumentException error) { Console.Error.WriteLine(error.Message); return 2; }
    catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or JsonException or System.Xml.XmlException or System.ComponentModel.Win32Exception or TimeoutException)
    { Console.Error.WriteLine(error.Message); return 1; }
}

if (args.Length > 0 && args[0] is "logging-mutation" or "core-mutation" or "build-helper-mutation")
{
    try { return await LoggingMutationQualification.RunAsync(args[1..], args[0] switch { "core-mutation" => "core", "build-helper-mutation" => "helper", _ => "frontend" }); }
    catch (ArgumentException error) { Console.Error.WriteLine(error.Message); return 2; }
    catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or JsonException or System.Xml.XmlException or System.ComponentModel.Win32Exception or TimeoutException)
    { Console.Error.WriteLine(error.Message); return 1; }
}

if (args.Length > 0 && args[0] == "first-release-compatibility")
{
    try { return await FirstReleaseCompatibility.RunAsync(args[1..]); }
    catch (ArgumentException error) { Console.Error.WriteLine(error.Message); return 2; }
    catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or JsonException or System.Xml.XmlException or System.ComponentModel.Win32Exception or TimeoutException)
    { Console.Error.WriteLine(error.Message); return 1; }
}

if (args.Length > 0 && args[0] is "microsoft-logging-bundle" or "source-replay" or "toolchain-qualification" or "record-toolchain-inputs" or "microsoft-logging-qualification" or "collect-shipping-coverage" or "microsoft-logging-compatibility" or "type-manager-source-inventory" or "type-manager-example-boundaries" or "type-manager-baseline" or "type-manager-frozen-tests" or "type-manager-runtime" or "type-manager-instrumentation" or "type-manager-instrumentation-controls" or "type-manager-inventory" or "type-manager-package" or "type-manager-examples" or "type-manager-mutation-prepare" or "type-manager-mutation-prepare-controls" or "type-manager-measure" or "type-manager-measure-controls")
{
    try
    {
        return args[0] switch
        {
            "microsoft-logging-bundle" => await MicrosoftLoggingBundle.RunAsync(args[1..]),
            "source-replay" => await SourceReplay.RunAsync(args[1..]),
            "toolchain-qualification" => await ToolchainQualification.RunAsync(args[1..]),
            "record-toolchain-inputs" => await ToolchainInputs.RunAsync(args[1..]),
            "microsoft-logging-qualification" => await MicrosoftLoggingQualification.RunAsync(args[1..]),
            "collect-shipping-coverage" => await ShippingCoverageCollection.RunAsync(args[1..]),
            "microsoft-logging-compatibility" => await MicrosoftLoggingCompatibilityChecks.RunAsync(args[1..]),
            "type-manager-source-inventory" => await TypeManagerSourceInventoryChecks.RunAsync(args[1..]),
            "type-manager-example-boundaries" => await TypeManagerExampleBoundaryChecks.RunAsync(args[1..]),
            "type-manager-baseline" => await TypeManagerBaselineChecks.RunAsync(args[1..]),
            "type-manager-frozen-tests" => await TypeManagerFrozenTests.RunAsync(args[1..]),
            "type-manager-runtime" => await TypeManagerRuntimeVerification.RunAsync(args[1..]),
            "type-manager-inventory" => await TypeManagerInventoryVerification.RunAsync(args[1..]),
            "type-manager-package" => await TypeManagerPackageVerification.RunAsync(args[1..]),
            "type-manager-measure-controls" => await TypeManagerMeasurement.RunControlsAsync(args[1..]),
            "type-manager-measure" => await TypeManagerMeasurement.RunAsync(args[1..]),
            "type-manager-mutation-prepare-controls" => await TypeManagerMutationPreparation.RunControlsAsync(args[1..]),
            "type-manager-mutation-prepare" => TypeManagerMutationPreparation.Run(args[1..]),
            "type-manager-examples" => await TypeManagerExamplesVerification.RunAsync(args[1..]),
            "type-manager-instrumentation" => await TypeManagerInstrumentationVerification.RunAsync(args[1..]),
            _ => await TypeManagerInstrumentationControls.RunAsync(args[1..])
        };
    }
    catch (ArgumentException error) { Console.Error.WriteLine(error.Message); return 2; }
    catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or JsonException or System.Xml.XmlException or System.ComponentModel.Win32Exception or TimeoutException)
    { Console.Error.WriteLine(error.Message); return 1; }
}


if (args.Length > 0 && args[0] == "dependencies")
{
    return await DependencyChecks.RunAsync(args[1..]);
}

if (args.Length > 0 && args[0] == "dependency-role-control")
{
    return await DependencyRoleControl.RunAsync(args[1..]);
}

if (args.Length > 0 && args[0] == "compiler-checks")
{
    return await CompilerChecks.RunAsync(args[1..]);
}

if (args.Length > 0 && args[0] == "publications")
{
    return PublicationValidation.Run(args[1..]);
}

if (args.Length > 0 && args[0] == "release-evidence")
{
    return VerificationEvidence.Run(args[1..]);
}

if (args.Length > 0 && args[0] == "source-inventory")
{
    return await SourceInventory.RunAsync(args[1..]);
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
