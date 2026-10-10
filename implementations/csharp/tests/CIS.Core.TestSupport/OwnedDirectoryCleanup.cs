namespace CIS.TestSupport;

internal static class OwnedDirectoryCleanup
{
    internal const int MaximumAttempts = 20;

    public static void Delete(string path, Action<string> delete, Action wait)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                delete(path);
                return;
            }
            catch (IOException exception) when (
                OperatingSystem.IsWindows() &&
                (exception.HResult & 0xffff) is 32 or 33 &&
                attempt < MaximumAttempts)
            {
                wait();
            }
        }
    }
}
