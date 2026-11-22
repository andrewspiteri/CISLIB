namespace CIS.Messaging.Hosting;

/// <summary>Passive exact registration identity and finite aliases for one Messaging host.</summary>
public sealed class MessagingComposition
{
    internal MessagingComposition(MessagingSnapshot snapshot)
    {
        RegistrationIdentity = snapshot.RegistrationIdentity;
        RouteAliases = Array.AsReadOnly(snapshot.Routes.Select(item => item.Alias).ToArray());
        SubscriptionAliases = Array.AsReadOnly(snapshot.Subscriptions.Select(item => item.Alias).ToArray());
        RequestAliases = Array.AsReadOnly(snapshot.Requests.Select(item => item.Alias).ToArray());
        StoreAliases = Array.AsReadOnly(snapshot.Stores.Select(item => item.Alias).ToArray());
    }

    /// <summary>Gets the exact in-process registration identity, never a persisted identity.</summary>
    public object RegistrationIdentity { get; }
    /// <summary>Gets bounded registered route aliases.</summary>
    public IReadOnlyList<string> RouteAliases { get; }
    /// <summary>Gets bounded logical subscription aliases, excluding the provider reply callback.</summary>
    public IReadOnlyList<string> SubscriptionAliases { get; }
    /// <summary>Gets bounded typed request aliases.</summary>
    public IReadOnlyList<string> RequestAliases { get; }
    /// <summary>Gets bounded store aliases.</summary>
    public IReadOnlyList<string> StoreAliases { get; }
}
