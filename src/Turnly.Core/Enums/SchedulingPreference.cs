namespace Turnly.Core.Enums;

/// <summary>How the next due date is calculated after a chore is marked complete.</summary>
public enum SchedulingPreference
{
    /// <summary>Next due = scheduled date + interval (ignores when it was actually done).</summary>
    FromScheduledDate = 0,
    /// <summary>Next due = actual completion time + interval.</summary>
    FromCompletionDate = 1,
    /// <summary>Next due = the next naturally occurring occurrence after now (skips missed ones).</summary>
    ToFirstNextRepeat = 2,
    /// <summary>Holds the planned cadence but never schedules sooner than one interval after the
    /// actual completion: next due = max(FromScheduledDate, FromCompletionDate). An optional grace
    /// window softens it — when a chore is completed more than the grace early, the early completion
    /// is treated as genuine and the cadence resets from completion instead of holding the grid (so an
    /// early clean doesn't leave an over-long gap). Offered only for interval-style repeats.</summary>
    SmartScheduling = 3
}
