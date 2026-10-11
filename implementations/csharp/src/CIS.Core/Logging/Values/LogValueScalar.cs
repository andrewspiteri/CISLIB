namespace CIS.Logging.Values;

// A typed scalar union: no arbitrary object payload or mutable caller-owned state.
internal readonly struct LogValueScalar
{
    public LogValueScalar(bool value) => Boolean = value;
    public LogValueScalar(double value) => Number = value;
    public LogValueScalar(long value) => Integer = value;
    public LogValueScalar(string value) => Text = value;

    public bool Boolean { get; }
    public double Number { get; }
    public long Integer { get; }
    public string? Text { get; }
}
