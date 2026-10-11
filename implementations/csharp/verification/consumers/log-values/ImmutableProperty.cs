using CIS.Logging;

internal static class ImmutableProperty
{
    internal static void Reject()
    {
        LogValue value = LogValue.Null();
        value.IsTruncated = true;
    }
}
