using Turnly.Core.Enums;

namespace Turnly.Core.Entities;

public class Chore
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Emoji { get; set; }
    public int Points { get; set; }

    public RepeatType RepeatType { get; set; } = RepeatType.OneTime;

    /// <summary>The custom recurrence flavour when <see cref="RepeatType"/> is
    /// <see cref="RepeatType.Custom"/>; null otherwise.</summary>
    public CustomRecurrenceMode? CustomMode { get; set; }

    /// <summary>Interval count + unit for <see cref="CustomRecurrenceMode.Interval"/>
    /// (e.g. 2 + <see cref="RecurrenceUnit.Week"/> = every two weeks).</summary>
    public int? IntervalCount { get; set; }
    public RecurrenceUnit? IntervalUnit { get; set; }

    /// <summary>Selected weekdays for <see cref="CustomRecurrenceMode.DaysOfWeek"/>; empty otherwise.</summary>
    public List<DayOfWeek> Weekdays { get; set; } = new();

    /// <summary>Selected days (1–31) and months (1–12) for
    /// <see cref="CustomRecurrenceMode.DaysOfMonth"/>; empty otherwise.</summary>
    public List<int> DaysOfMonth { get; set; } = new();
    public List<int> Months { get; set; } = new();

    /// <summary>How many completions (skips count too) are needed to close the current occurrence
    /// before it advances to the next due date — e.g. 3 = "three times per occurrence". 1 for the
    /// usual one-completion-per-occurrence chore. Only meaningful for the non-custom repeat types
    /// (OneTime/Daily/Weekly/Monthly/Yearly); custom recurrences are always 1.</summary>
    public int CompletionsRequired { get; set; } = 1;

    /// <summary>For multi-completion chores (<see cref="CompletionsRequired"/> &gt; 1): rotate the
    /// assignee after every single completion rather than only when the occurrence is fully complete.
    /// Ignored (and stored false) when only one completion is required. Skips never rotate either way.</summary>
    public bool RotateOnEachCompletion { get; set; }

    /// <summary>How the next assignee is chosen when the chore advances to a new occurrence.</summary>
    public AssignmentStrategy AssignmentStrategy { get; set; } = AssignmentStrategy.KeepLastAssigned;

    /// <summary>How the next due date is calculated after completion.</summary>
    public SchedulingPreference SchedulingPreference { get; set; } = SchedulingPreference.FromScheduledDate;

    /// <summary>When the first occurrence is due (the resolved local instant — date plus
    /// <see cref="DueTime"/>, or end-of-day when none — with the creating client's UTC offset baked
    /// in so the recurrence math keeps a consistent local time-of-day).</summary>
    public DateTimeOffset StartDate { get; set; }

    /// <summary>The user-chosen local time-of-day a chore is due, or <c>null</c> for "no specific
    /// time" (treated as end of day). Stored only so the UI can show/round-trip it; the authoritative
    /// instant lives in <see cref="StartDate"/>/<see cref="DueAt"/>.</summary>
    public TimeOnly? DueTime { get; set; }

    /// <summary>Due date of the current/next occurrence. Null means nothing is scheduled
    /// (e.g. a completed one-time chore).</summary>
    public DateTimeOffset? DueAt { get; set; }

    /// <summary>The user currently assigned to this chore; must be one of <see cref="Assignees"/>.</summary>
    public Guid? CurrentAssigneeId { get; set; }
    public User? CurrentAssignee { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Users eligible to be assigned this chore (at least one).</summary>
    public ICollection<User> Assignees { get; set; } = new List<User>();
    public ICollection<Tag> Tags { get; set; } = new List<Tag>();
    public ICollection<ChoreCompletion> Completions { get; set; } = new List<ChoreCompletion>();

    /// <summary>Assignment history — one row per assignment (initial + each rotation). Backs the
    /// <see cref="AssignmentStrategy.LeastAssigned"/> strategy and undo of rotations.</summary>
    public ICollection<ChoreAssignment> Assignments { get; set; } = new List<ChoreAssignment>();

    /// <summary>Per-chore notification schedule (reminder/due/follow-up entries).</summary>
    public ICollection<ChoreNotification> Notifications { get; set; } = new List<ChoreNotification>();
}
