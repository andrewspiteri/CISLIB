namespace CIS.Configuration;

internal sealed record ApplicationVersionSelection(
    string BuildVersion,
    string ServiceVersion,
    string ServiceVersionSource);
