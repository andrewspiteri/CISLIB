namespace CIS.Configuration;

internal sealed record ApplicationIdentity(
    string? ApplicationName,
    string BuildVersion,
    string? CommitId,
    string? BuildId);
