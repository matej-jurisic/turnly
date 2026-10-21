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

    /// <summary>Optional display names for individual <see cref="TimesOfDay"/> slots — e.g. 08:00 →
    /// "Morning", 20:00 → "Evening". Sparse: a slot without an entry simply has no label. Keys are
    /// always a subset of <see cref="TimesOfDay"/>. Purely presentational; never touches the
    /// recurrence math.</summary>
    public Dictionary<TimeOnly, string> TimeLabels { get; set; } = new();

    /// <summary>The label of the slot an occurrence falls on, or null when that slot is unlabelled.
    /// Reads the time-of-day in the instant's own stored offset, which is how the slot scanner stamps
    /// it.</summary>
    public string? SlotLabel(DateTimeOffset? dueAt) =>
        dueAt is { } due && TimeLabels.TryGetValue(TimeOnly.FromTimeSpan(due.TimeOfDay), out var label) ? label : null;

    /// <summary>How many completions (skips count too) are needed to close the current occurrence
    /// before it advances to the next due date — e.g. 3 = "three times per occurrence". 1 for the
    /// usual one-completion-per-occurrence chore. Only meaningful for the non-custom repeat types
    /// (OneTime/Daily/Weekly/Monthly/Yearly); custom and on-demand chores are always 1.</summary>
    public int CompletionsRequired { get; set; } = 1;

    /// <summary>For multi-completion chores (<see cref="CompletionsRequired"/> &gt; 1): rotate the
    /// assignee after every single completion rather than only when the occurrence is fully complete.
    /// Ignored (and stored false) when only one completion is required. Skips never rotate either way.</summary>
    public bool RotateOnEachCompletion { get; set; }

    /// <summary>How often the assignee hands over. A calendar period (day, week, month, quarter, year)
    /// keeps the same person across every occurrence inside it and only rotates when the due date
    /// crosses into the next one. Stored as <see cref="RotationPeriod.EveryOccurrence"/> wherever it
    /// can't mean anything (one-time, independent, keep-last-assigned, and repeats that don't come due
    /// more than once per period, see <see cref="Recurrence.RotationBoundary.AppliesTo"/>).</summary>
    public RotationPeriod RotationPeriod { get; set; } = RotationPeriod.EveryOccurrence;

    /// <summary>How the next assignee is chosen when the chore advances to a new occurrence.</summary>
    public AssignmentStrategy AssignmentStrategy { get; set; } = AssignmentStrategy.KeepLastAssigned;

    /// <summary>How the next due date is calculated after completion.</summary>
    public SchedulingPreference SchedulingPreference { get; set; } = SchedulingPreference.FromScheduledDate;

    /// <summary>Grace window (in minutes) for <see cref="SchedulingPreference.SmartScheduling"/>:
    /// completions more than this many minutes before the scheduled due date reset the cadence from
    /// the completion instead of holding the grid. Null = no grace (pure max). Only meaningful when
    /// <see cref="SchedulingPreference"/> is <see cref="SchedulingPreference.SmartScheduling"/>.</summary>
    public int? GraceMinutes { get; set; }

    /// <summary>When true, the background service automatically expires any unfilled slots and advances
    /// to the next occurrence once the completion window has closed (see <see cref="CompletionWindowMinutes"/>).
    /// Writes one <c>IsExpired</c> row per missing completion (so single-completion chores get one), then
    /// advances and rotates. Applies to recurring non-independent chores (custom included; OneTime
    /// excluded).</summary>
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

    /// <summary>Whether the due instant carries a concrete time-of-day, as opposed to an all-day
    /// "end of day" due. Mirrors <c>choreHasDueTime</c> on the client.</summary>
    public bool HasDueTime => DueTime is not null || TimesOfDay.Count > 0;

    /// <summary>How long past its due instant an occurrence still counts as on time. Only an all-day
    /// chore gets the household's day start (see <c>AppSettingsService.DayStartKey</c>): its "end of
    /// day" is when the next day starts, not midnight. A timed chore is late the moment its time passes.</summary>
    public TimeSpan LateGrace(TimeSpan dayStart) => HasDueTime ? TimeSpan.Zero : dayStart;

    /// <summary>Due date of the current/next occurrence. Null means nothing is scheduled
    /// (e.g. a completed one-time chore).</summary>
    public DateTimeOffset? DueAt { get; set; }

    /// <summary>The user currently assigned to this chore; must be one of <see cref="Assignees"/>.</summary>
    public Guid? CurrentAssigneeId { get; set; }
    public User? CurrentAssignee { get; set; }

    /// <summary>On a paired chore, who shares the current turn with <see cref="CurrentAssigneeId"/>.
    /// Stored because a person can be in several pairs, so the pair can't be derived from the
    /// assignee alone. Read it through <see cref="HeldPartnerId"/> / <see cref="CurrentPartner"/>,
    /// which also cover rows written before this column existed. A plain id, not a navigation.</summary>
    public Guid? CurrentPartnerId { get; set; }

    /// <summary>Fixed pairs of assignees who do this chore together. The pair is the unit: it holds
    /// the turn, rotates and is credited as one. Pairs may overlap (Ana &amp; Ben, Ana &amp; Cy): each
    /// is its own turn in the rotation. Assignees outside every pair take their turn alone.
    /// Always empty on <see cref="AssignmentStrategy.Independent"/> chores.</summary>
    public List<ChorePair> Pairs { get; set; } = new();

    /// <summary>The pairs both of whose members are around to do the chore (assignees, not frozen).
    /// Needs <see cref="Assignees"/> loaded.</summary>
    public IEnumerable<ChorePair> LivePairs() =>
        Pairs.Where(p => IsAvailable(p.First) && IsAvailable(p.Second));

    private bool IsAvailable(Guid userId) => Assignees.Any(a => a.Id == userId && !a.IsFrozen);

    /// <summary>The partner the current turn belongs to alongside the current assignee, frozen or
    /// not: the stored one while the two are still a pair, otherwise (never set, or the pair was
    /// edited away) the assignee's first live pair. Null when they hold it alone.
    /// Needs <see cref="Assignees"/> loaded.</summary>
    public Guid? HeldPartnerId()
    {
        if (CurrentAssigneeId is not { } current) return null;
        if (CurrentPartnerId is { } stored && Pairs.Any(p => p.Is(current, stored))) return stored;
        return LivePairs().FirstOrDefault(p => p.Has(current))?.Other(current);
    }

    /// <summary>Who shares the current turn with the current assignee, or null when there is nobody.
    /// A frozen partner counts as none: they're away, so the other member carries the turn alone.
    /// Needs <see cref="Assignees"/> loaded.</summary>
    public User? CurrentPartner() =>
        HeldPartnerId() is { } id ? Assignees.FirstOrDefault(a => a.Id == id && !a.IsFrozen) : null;

    /// <summary>Everyone holding the current occurrence: the current assignee plus their partner on a
    /// paired chore. Empty when unassigned. Needs <see cref="Assignees"/> loaded.</summary>
    public IEnumerable<Guid> CurrentHolderIds()
    {
        if (CurrentAssigneeId is not { } current) yield break;
        yield return current;
        if (CurrentPartner() is { } partner) yield return partner.Id;
    }

    /// <summary>Who is credited alongside <paramref name="userId"/> when they do the turn held by
    /// <paramref name="holderId"/> and <paramref name="holderPartnerId"/>. A holder shares it with
    /// their co-holder. Someone stepping in from outside brings their partner only when that is
    /// unambiguous (they are in exactly one live pair); otherwise they are credited alone.
    /// Needs <see cref="Assignees"/> loaded.</summary>
    public User? CreditedPartner(Guid userId, Guid? holderId, Guid? holderPartnerId)
    {
        Guid? partnerId;
        if (userId == holderId) partnerId = holderPartnerId;
        else if (userId == holderPartnerId) partnerId = holderId;
        else
        {
            var mine = LivePairs().Where(p => p.Has(userId)).Take(2).ToList();
            partnerId = mine.Count == 1 ? mine[0].Other(userId) : null;
        }
        return partnerId is { } id ? Assignees.FirstOrDefault(a => a.Id == id && !a.IsFrozen) : null;
    }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When true the chore is suspended: completions and skips are blocked, the auto-advance
    /// service skips it, and notifications are suppressed. Admin-only toggle. On unfreeze, overdue
    /// recurring chores are stepped forward to the next future due date.</summary>
    public bool IsFrozen { get; set; }

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
