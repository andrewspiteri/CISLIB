namespace CIS.Configuration;

internal sealed record InitializationIdentity(string InstanceId, DateTimeOffset InitializedAtUtc);
