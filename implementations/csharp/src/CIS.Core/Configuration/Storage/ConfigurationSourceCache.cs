using System.Text.Json;

namespace CIS.Configuration;

// Source equality is independent of accepted effective values. In particular an
// invalid payload is retained so an Unchanged response cannot erase its error.
internal sealed class ConfigurationSourceCache
{
    internal bool HasState { get; private set; }
    internal JsonElement? Payload { get; private set; }
    internal string? Token { get; private set; }

    internal async ValueTask<string?> ReadAsync(IConfigurationStore store, ConfigurationRecordKey key,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ConfigurationReadResult? result;
        try { result = await store.ReadAsync(key, HasState ? Token : null, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return "Configuration.StoreRead"; }
        cancellationToken.ThrowIfCancellationRequested();
        if (result is null) { return "Configuration.StoreRead"; }
        switch (result.Kind)
        {
            case ConfigurationReadKind.Found:
                HasState = true;
                Payload = result.Payload;
                Token = result.SourceToken;
                return null;
            case ConfigurationReadKind.Missing:
                HasState = true;
                Payload = null;
                Token = result.SourceToken;
                return null;
            case ConfigurationReadKind.Unchanged:
                return HasState ? null : "Configuration.UnchangedWithoutCache";
            default:
                return "Configuration.StoreRead";
        }
    }
}
