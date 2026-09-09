namespace ServerMonitor.Web.Presentation;

/// <summary>
/// How a machine's state is worded and coloured in the interface.
/// </summary>
/// <remarks>
/// Extracted from the pages because it was wrong in one of them and nothing could have caught it.
/// The machine detail screen printed "Live" whenever the browser was not paused — a fact about the
/// page, not about the machine — so a host that had stopped reporting hours ago was announced as
/// live, with a stale clock beside it.
/// <para>
/// That is the failure this project treats as the worst kind: a monitor that is broken gets
/// noticed, a monitor that lies does not. Whether a machine is alive may only ever be derived from
/// how fresh its data is.
/// </para>
/// </remarks>
public static class MachineState
{
    /// <summary>The word shown to a person for a machine in this state.</summary>
    /// <remarks>
    /// Anything that is not explicitly healthy reads as offline. An unrecognised value arriving
    /// from the API is a bug, and claiming a machine is fine is the wrong way to be wrong about it.
    /// </remarks>
    public static string Label(string? health) => health switch
    {
        "Online" => "Live",
        "Stale" => "Stale",
        _ => "Offline"
    };

    /// <summary>The CSS modifier for the status dot, matching the wording above.</summary>
    public static string DotClass(string? health) => health switch
    {
        "Online" => string.Empty,
        "Stale" => "stale",
        _ => "offline"
    };

    /// <summary>Whether readings are actually arriving, which is what the animations indicate.</summary>
    public static bool IsReporting(string? health) => health == "Online";

    /// <summary>How long ago the machine was last heard from, in words.</summary>
    /// <param name="nowUtc">Taken as an argument so this can be tested without waiting.</param>
    public static string LastSeen(DateTime? lastSeenUtc, DateTime nowUtc)
    {
        if (lastSeenUtc is null)
        {
            return "never";
        }

        var age = nowUtc - lastSeenUtc.Value;

        // A machine whose clock runs ahead of the server's would otherwise be reported as last
        // seen a negative number of seconds ago.
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        return age.TotalSeconds switch
        {
            < 60 => $"{(int)age.TotalSeconds}s ago",
            < 3600 => $"{(int)age.TotalMinutes}m ago",
            < 86400 => $"{(int)age.TotalHours}h ago",
            _ => $"{(int)age.TotalDays}d ago"
        };
    }
}
