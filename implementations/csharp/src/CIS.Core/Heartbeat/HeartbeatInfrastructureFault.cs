using System.Text;

namespace CIS.Heartbeat;

/// <summary>The first bounded, detached scheduler or reporting fault, separate from subscriber outcomes.</summary>
public sealed class HeartbeatInfrastructureFault
{
    internal HeartbeatInfrastructureFault(string code, Exception error)
    {
        Code = code;
        string message;
        try { message = error.Message ?? "Infrastructure exception message unavailable."; }
        catch (Exception) { message = "Infrastructure exception message unavailable."; }
        var bounded = new StringBuilder();
        int count = 0;
        foreach (Rune rune in message.EnumerateRunes())
        {
            if (count++ == 1024) { break; }
            bounded.Append(rune.ToString());
        }
        Message = bounded.ToString();
    }

    /// <summary>Gets a stable infrastructure fault category.</summary>
    public string Code { get; }
    /// <summary>Gets at most 1,024 Unicode scalar values without retaining an exception graph.</summary>
    public string Message { get; }
}
