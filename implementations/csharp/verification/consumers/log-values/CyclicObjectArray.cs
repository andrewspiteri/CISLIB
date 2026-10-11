using CIS.Logging;

internal static class CyclicObjectArray
{
    internal static object Reject()
    {
        object[] cycle = new object[1];
        cycle[0] = cycle;
        return LogValue.Array(cycle);
    }
}
