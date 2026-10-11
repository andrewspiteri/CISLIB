namespace CIS.Configuration;

internal readonly struct ConfigurationCandidate<T>
{
    private readonly T value;

    public ConfigurationCandidate(T? candidate)
    {
        if (candidate is string text)
        {
            HasValue = !string.IsNullOrWhiteSpace(text);
            value = HasValue ? (T)(object)text.Trim() : default!;
        }
        else
        {
            HasValue = candidate is not null;
            value = candidate!;
        }
    }

    public bool HasValue { get; }
    public T Value => HasValue ? value : throw new InvalidOperationException("The configuration candidate is absent.");
}
