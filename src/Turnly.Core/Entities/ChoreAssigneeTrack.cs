namespace Turnly.Core.Entities;

/// <summary>One assignee's independent schedule for a chore in "everyone independently"
/// (<see cref="Enums.AssignmentStrategy.Independent"/>) mode: their own current due date and their
/// own per-occurrence quota. Each assignee advances on their own — completing one track never moves
/// another's — so a slow person never blocks a fast one. Only present for track-mode chores; for
/// rotating chores the single <see cref="Chore.DueAt"/>/<see cref="Chore.CurrentAssigneeId"/> apply.</summary>
public class ChoreAssigneeTrack
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ChoreId { get; set; }
    public Chore? Chore { get; set; }

    public Guid UserId { get; set; }
    public User? User { get; set; }

    /// <summary>This assignee's current/next occurrence due date; null when nothing is scheduled for
    /// them (e.g. a finished track). The chore's mirror <see cref="Chore.DueAt"/> tracks the earliest
    /// of these so existing "is anything scheduled" checks keep working.</summary>
    public DateTimeOffset? DueAt { get; set; }

    /// <summary>How many completions/skips close this assignee's current occurrence before it advances
    /// to the next due date — their personal quota (e.g. 3 vs 2). Default 1.</summary>
    public int CompletionsRequired { get; set; } = 1;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
