using CIS.Logging;

namespace CIS.Configuration;

internal static class EnvironmentSnapshotFactory
{
    // Required source failures escape without publishing; optional discovery owns its filters.
    public static EnvironmentSnapshot Create(EnvironmentSetup setup,
        IEnvironmentVariableSource? variableSource = null,
        IApplicationAssemblyMetadata? applicationSource = null,
        IRuntimeMetadataSource? runtimeSource = null,
        Func<string>? readWorkingDirectory = null,
        InitializationIdentityFactory? initializationFactory = null)
    {
        StartupInputCapture input = StartupInputCapture.Capture(setup);
        InitializationWorkingDirectory directory = InitializationWorkingDirectory.Capture(readWorkingDirectory);
        CapturedEnvironment variables = CapturedEnvironment.Capture(variableSource);
        ApplicationIdentity application = ApplicationIdentityDiscovery.Discover(input.Identity, applicationSource);
        ConfigurationResolution selected = ConfigurationResolver.Resolve(input.Configuration, application, variables);
        ValidatedConfiguration validated = ConfigurationValidator.Validate(selected, directory);
        RuntimeMetadata runtime = RuntimeMetadataDiscovery.Discover(runtimeSource);
        InitializationIdentity initialization = (initializationFactory ?? new InitializationIdentityFactory()).Create();
        var metadata = new EventMetadata(validated, runtime, initialization);
        return new EnvironmentSnapshot(metadata, new LoggingOptions(validated));
    }
}
