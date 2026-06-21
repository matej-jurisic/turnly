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

    /// <summary>Restricts <see cref="CustomRecurrenceMode.DaysOfWeek"/> to specific occurrences within
    /// each month: 1–4 for the nth weekday, -1 for the last. Empty means every week (the default).</summary>
    public List<int> WeeksOfMonth { get; set; } = new();

    /// <summary>Selected days (1–31) and months (1–12) for
    /// <see cref="CustomRecurrenceMode.DaysOfMonth"/>; empty otherwise.</summary>
    public List<int> DaysOfMonth { get; set; } = new();
    public List<int> Months { get; set; } = new();

    /// <summary>Fixed times-of-day the chore is due on each qualifying day — e.g. 08:00 and 20:00 for
    /// "twice a day". Each time is a distinct occurrence. Empty = a single daily slot at
    /// <see cref="DueTime"/>. Only used for day-resolution schedules (Daily and the custom DaysOfWeek /
    /// DaysOfMonth modes); the validator restricts it to those, and <see cref="DueTime"/> mirrors the
    /// earliest of these when set.</summary>
    public List<TimeOnly> TimesOfDay { get; set; } = new();

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

    /// <summary>Grace window (in minutes) for <see cref="SchedulingPreference.SmartScheduling"/>:
    /// completions more than this many minutes before the scheduled due date reset the cadence from
    /// the completion instead of holding the grid. Null = no grace (pure max). Only meaningful when
    /// <see cref="SchedulingPreference"/> is <see cref="SchedulingPreference.SmartScheduling"/>.</summary>
    public int? GraceMinutes { get; set; }

    /// <summary>When true and <see cref="CompletionsRequired"/> &gt; 1, the background service
    /// automatically expires unfilled slots and advances to the next occurrence once the completion
    /// window has closed (see <see cref="CompletionWindowMinutes"/>). Only meaningful for
    /// non-custom, non-independent chores.</summary>
    public bool AutoAdvanceIncomplete { get; set; }

    /// <summary>How many minutes after <see cref="DueAt"/> the auto-advance window closes. Null means
    /// the occurrence expires immediately when overdue. Only meaningful when
    /// <see cref="AutoAdvanceIncomplete"/> is true.</summary>
    public int? CompletionWindowMinutes { get; set; }

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

    /// <summary>Per-assignee independent schedules — populated only when
    /// <see cref="AssignmentStrategy"/> is <see cref="Enums.AssignmentStrategy.Independent"/>. Each
    /// holds one assignee's own <c>DueAt</c> + quota; <see cref="DueAt"/> mirrors the earliest of them.</summary>
    public ICollection<ChoreAssigneeTrack> AssigneeTracks { get; set; } = new List<ChoreAssigneeTrack>();
}
