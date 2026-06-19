namespace Turnly.Core.Enums;

/// <summary>How the next due date is calculated after a chore is marked complete.</summary>
public enum SchedulingPreference
{
    /// <summary>Next due = scheduled date + interval (ignores when it was actually done).</summary>
    FromScheduledDate = 0,
    /// <summary>Next due = actual completion time + interval.</summary>
    FromCompletionDate = 1,
    /// <summary>Next due = the next naturally occurring occurrence after now (skips missed ones).</summary>
    ToFirstNextRepeat = 2
}
