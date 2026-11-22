namespace CIS.Messaging;

/// <summary>Portable reserved callback identities owned by messaging integration, never selected from wire headers.</summary>
public static class MessagingSystemSubscriptions
{
    /// <summary>The configured callback identity for one native requester endpoint.</summary>
    public const string Replies = "MessagingReplies";
}
