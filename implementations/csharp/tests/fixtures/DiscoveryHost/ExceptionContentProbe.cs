using System.Collections;

namespace CIS.Tests.DiscoveryFixture;

internal interface IExceptionMetadataTrap
{
    object Body { get; }
    object Headers { get; }
    object Environment { get; }
}

internal sealed class ExceptionContentProbe(string label)
{
    internal string Label { get; } = label;
    internal string? Text { get; set; } = label;
    internal string? Trace { get; set; }
    internal IDictionary RawData { get; set; } = null!;
    internal int MessageReads { get; private set; }
    internal int StackReads { get; private set; }
    internal int ForbiddenCalls { get; private set; }

    internal string Message()
    {
        MessageReads++;
        return Text!;
    }

    internal string? Stack()
    {
        StackReads++;
        return Trace;
    }

    internal T Deny<T>()
    {
        ForbiddenCalls++;
        throw new InvalidOperationException("Excluded exception member was accessed.");
    }

    internal void ResetForbiddenCalls() => ForbiddenCalls = 0;
}

internal sealed class ExceptionPrivatePayload
{
    // An attachment belongs to caller data, never to the captured exception tree.
    internal byte[] Attachment { get; } = new byte[65536];
    public override string ToString() => throw new InvalidOperationException("Excluded payload formatting.");
}
