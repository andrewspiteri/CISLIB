using CIS.Text;

namespace CIS.Logging;

/// <summary>Immutable startup registration for one named independent destination.</summary>
public sealed class LogWriterRegistration
{
    /// <summary>Registers a writer and its captured threshold. Names are ordinal and must be unique.</summary>
    /// <remarks>The runtime borrows the writer unless ownsWriter is true. Ownership transfers only after
    /// startup succeeds; on startup failure the caller retains custom writers. A borrowed writer must remain
    /// alive until the runtime's Completion task settles, including after a timed-out StopAsync.</remarks>
    public LogWriterRegistration(string name, ILogWriter writer, CoreLogLevel level = CoreLogLevel.Information, bool ownsWriter = false)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(writer);
        if (string.IsNullOrWhiteSpace(name) || name is "console" or "file" || !UnicodeScalarText.IsValid(name, 255, allowControls: false))
        { throw new ArgumentException("Use a unique nonblank destination name other than console or file.", nameof(name)); }
        if (!Enum.IsDefined(level) || level == CoreLogLevel.None)
        { throw new ArgumentOutOfRangeException(nameof(level), "Register an enabled declared threshold."); }
        Name = name; Writer = writer; Level = level; OwnsWriter = ownsWriter;
    }

    /// <summary>The destination identity used in admission, diagnostics and flush/stop results.</summary>
    public string Name { get; }
    /// <summary>The registered destination.</summary>
    public ILogWriter Writer { get; }
    /// <summary>The independent minimum severity.</summary>
    public CoreLogLevel Level { get; }
    /// <summary>Whether successful runtime startup transfers disposal responsibility to the runtime.</summary>
    public bool OwnsWriter { get; }
}
