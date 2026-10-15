using CIS;
using CIS.Logging;

// First access populates readonly defaults and applies environment overrides.
EnvironmentOptions.EnvironmentKind = EnvironmentKind.Console;
EnvironmentOptions.Logging.Prepare(Directory.GetCurrentDirectory());
var metadata = new EventMetadata();
Console.WriteLine("CIS.Core logging example scaffold is ready.");
Console.WriteLine("This scaffold demonstrates configuration; runnable logging examples are still pending.");
Console.WriteLine($"Application: {metadata.ApplicationName}; build: {metadata.BuildVersion}; process: {metadata.ProcessId}; OS: {metadata.OperatingSystem}; entry limit: {EnvironmentOptions.Logging.MaxEntryBytes} bytes.");
