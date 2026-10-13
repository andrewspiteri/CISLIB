namespace CIS.Logging.Runtime;

// Only these fixed file-policy failures cross into safe worker diagnostics.
// Do not retain raw filesystem exceptions, paths or content as inner/data/message facts.
internal sealed class FileOutputWriteException : IOException
{
    private FileOutputWriteException(string faultCode) : base("Logging file output failed (" + faultCode + ").") { FaultCode = faultCode; }
    internal string FaultCode { get; }
    internal static FileOutputWriteException RetentionDeleteFailed() => new("RetentionDeleteFailed");
    internal static FileOutputWriteException RenderTooLarge() => new("RenderTooLarge");
}
