namespace Turnly.Core.Notifications;

/// <summary>Pure timing and bucketing for the per-user daily digest (unit-tested, no I/O). Every
/// time is household-local: the scan converts "now" and each due date into the family timezone first.</summary>
public static class DailyDigest
{
    /// <summary>How long after the chosen time a digest may still go out (a server that was down, or
    /// quiet hours that ran past it). Later than that the day's digest is dropped as stale.</summary>
    public static readonly TimeSpan LateWindow = TimeSpan.FromHours(6);

    /// <summary>Whether a digest set for <paramref name="digestTime"/> should go out at
    /// <paramref name="localNow"/>: at or after the time, within <see cref="LateWindow"/> and the same
    /// day, and outside quiet hours (which postpone it rather than demote it - a digest that only
    /// reaches the inbox is no digest).</summary>
    public static bool IsDue(TimeOnly digestTime, TimeOnly localNow, TimeOnly? quietStart, TimeOnly? quietEnd)
    {
        if (localNow < digestTime)
            return false;
        if (localNow.ToTimeSpan() - digestTime.ToTimeSpan() >= LateWindow)
            return false;
        return !QuietHours.Contains(quietStart, quietEnd, localNow);
    }

    public enum Bucket { Overdue, Today, Later }

    /// <summary>Which part of the digest an occurrence belongs to, mirroring the client's
    /// <c>dueStatus</c>: an all-day due is stamped at the end of its own date, so it names its day and
    /// is overdue once that day is behind the household day; a timed one goes overdue at its instant
    /// and belongs to whichever household day it falls in. Both inputs are household-local.</summary>
    public static Bucket Classify(DateTime localDue, bool hasDueTime, DateTime localNow, TimeSpan dayStart)
    {
        var today = DateOnly.FromDateTime(localNow - dayStart);
        var dueDay = DateOnly.FromDateTime(hasDueTime ? localDue - dayStart : localDue);
        if (hasDueTime ? localDue < localNow : dueDay < today)
            return Bucket.Overdue;
        return dueDay <= today ? Bucket.Today : Bucket.Later;
    }
}
