namespace CIS.Jobs.Hosting;

internal sealed class JobsComposition
{
    internal JobsComposition(JobHostingOptions? options = null) => Options = options ?? new();
    internal object RegistrationIdentity { get; } = new();
    internal JobHostingOptions Options
    {
        get;
    }
}
