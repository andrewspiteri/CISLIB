namespace CIS.Jobs;

/// <summary>The schedule descriptors understood by the integration contract, not a delivered calendar engine.</summary>
public enum JobScheduleForm
{
    /// <summary>A shared delay from completion of the preceding logical occurrence.</summary>
    FixedDelay,
    /// <summary>A local time in an explicit IANA time zone each day.</summary>
    Daily,
    /// <summary>A local time on explicitly selected weekdays in an IANA time zone.</summary>
    Weekly
}

/// <summary>The authoritative scheduler's explicit missed-occurrence behavior.</summary>
public enum JobMissedRunPolicy
{
    /// <summary>Omit missed occurrences.</summary>
    Skip,
    /// <summary>Admit at most one occurrence representing the latest relevant missed schedule.</summary>
    RunLatestOnce,
    /// <summary>Preserve distinct required occurrences within an explicit finite count and window.</summary>
    CatchUpBounded
}

/// <summary>The declared policy for a nonexistent calendar local time.</summary>
public enum JobInvalidLocalTimePolicy
{
    /// <summary>Skip the nonexistent local occurrence.</summary>
    Skip
}

/// <summary>The declared policy for a repeated calendar local time.</summary>
public enum JobRepeatedLocalTimePolicy
{
    /// <summary>Admit once at the earlier corresponding UTC instant.</summary>
    OnceAtEarlierInstant
}

/// <summary>Immutable portable schedule metadata; registration does not calculate due times or enable recurrence.</summary>
public sealed class JobScheduleDescriptor
{
    private JobScheduleDescriptor(JobScheduleForm form, JobMissedRunPolicy missedRunPolicy, TimeSpan? delay,
        TimeOnly? localTime, string? timeZoneId, IReadOnlyList<DayOfWeek> weekdays,
        int? maximumCatchUp, TimeSpan? catchUpWindow)
    {
        if (!Enum.IsDefined(missedRunPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(missedRunPolicy));
        }

        if (missedRunPolicy == JobMissedRunPolicy.CatchUpBounded)
        {
            if (maximumCatchUp is null or < 1 or > 10000 || catchUpWindow is null)
            {
                throw new ArgumentException("Bounded catch-up requires a count from one through 10,000 and a positive window.");
            }

            _ = JobText.Duration(catchUpWindow.Value, nameof(catchUpWindow));
        }
        else if (maximumCatchUp is not null || catchUpWindow is not null)
        {
            throw new ArgumentException("Catch-up bounds belong only to CatchUpBounded.");
        }

        Form = form;
        MissedRunPolicy = missedRunPolicy;
        Delay = delay;
        LocalTime = localTime;
        TimeZoneId = timeZoneId;
        Weekdays = weekdays;
        MaximumCatchUp = maximumCatchUp;
        CatchUpWindow = catchUpWindow;
    }

    /// <summary>Creates a positive completion-relative delay owned by the separate scheduler.</summary>
    public static JobScheduleDescriptor FixedDelay(TimeSpan delay, JobMissedRunPolicy missedRunPolicy,
        int? maximumCatchUp = null, TimeSpan? catchUpWindow = null)
        => new(JobScheduleForm.FixedDelay, missedRunPolicy, JobText.Duration(delay, nameof(delay)), null, null,
            Array.Empty<DayOfWeek>(), maximumCatchUp, catchUpWindow);

    /// <summary>Creates a daily descriptor with explicit IANA metadata and the v1 DST policies.</summary>
    public static JobScheduleDescriptor Daily(TimeOnly localTime, string timeZoneId, JobMissedRunPolicy missedRunPolicy,
        int? maximumCatchUp = null, TimeSpan? catchUpWindow = null)
        => new(JobScheduleForm.Daily, missedRunPolicy, null, localTime, TimeZone(timeZoneId),
            Array.Empty<DayOfWeek>(), maximumCatchUp, catchUpWindow);

    /// <summary>Copies a nonempty distinct weekday set, explicit local time and IANA metadata.</summary>
    public static JobScheduleDescriptor Weekly(IEnumerable<DayOfWeek> weekdays, TimeOnly localTime,
        string timeZoneId, JobMissedRunPolicy missedRunPolicy, int? maximumCatchUp = null, TimeSpan? catchUpWindow = null)
    {
        ArgumentNullException.ThrowIfNull(weekdays);
        var copied = new List<DayOfWeek>();
        foreach (DayOfWeek day in weekdays)
        {
            if (!Enum.IsDefined(day) || copied.Count == 7 || copied.Contains(day))
            {
                throw new ArgumentException("Supply one through seven distinct weekdays.", nameof(weekdays));
            }

            copied.Add(day);
        }

        if (copied.Count == 0)
        {
            throw new ArgumentException("Weekly schedules require at least one weekday.", nameof(weekdays));
        }

        return new(JobScheduleForm.Weekly, missedRunPolicy, null, localTime, TimeZone(timeZoneId),
            copied.Order().ToList().AsReadOnly(), maximumCatchUp, catchUpWindow);
    }

    /// <summary>Gets FixedDelay, Daily or Weekly metadata.</summary>
    public JobScheduleForm Form
    {
        get;
    }
    /// <summary>Gets explicit missed-run semantics for the authoritative scheduler.</summary>
    public JobMissedRunPolicy MissedRunPolicy
    {
        get;
    }
    /// <summary>Gets the shared completion-relative delay for FixedDelay.</summary>
    public TimeSpan? Delay
    {
        get;
    }
    /// <summary>Gets the local wall-clock time for calendar forms.</summary>
    public TimeOnly? LocalTime
    {
        get;
    }
    /// <summary>Gets explicit IANA-shaped metadata; an evaluator must validate its supported zone before activation.</summary>
    public string? TimeZoneId
    {
        get;
    }
    /// <summary>Gets the copied sorted weekly selection, empty for other forms.</summary>
    public IReadOnlyList<DayOfWeek> Weekdays
    {
        get;
    }
    /// <summary>Gets the initial policy for nonexistent calendar times.</summary>
    public JobInvalidLocalTimePolicy InvalidLocalTimePolicy => JobInvalidLocalTimePolicy.Skip;
    /// <summary>Gets the initial policy for repeated calendar times.</summary>
    public JobRepeatedLocalTimePolicy RepeatedLocalTimePolicy => JobRepeatedLocalTimePolicy.OnceAtEarlierInstant;
    /// <summary>Gets the explicit catch-up count, only for CatchUpBounded.</summary>
    public int? MaximumCatchUp
    {
        get;
    }
    /// <summary>Gets the explicit catch-up window, only for CatchUpBounded.</summary>
    public TimeSpan? CatchUpWindow
    {
        get;
    }

    private static string TimeZone(string value)
    {
        string copied = JobText.Text(value, 128, nameof(value));
        if (copied is not "UTC" && (!copied.Contains('/')
            || copied.Split('/').Any(part => part.Length == 0 || part is "." or ".."))
            || copied.Any(character => character is not (>= 'a' and <= 'z') and not (>= 'A' and <= 'Z')
                and not (>= '0' and <= '9') and not '/' and not '_' and not '-' and not '+'))
        {
            throw new ArgumentException("Supply an explicit IANA identifier, for example Europe/Malta or Etc/UTC.", nameof(value));
        }

        return copied;
    }
}
