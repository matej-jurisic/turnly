namespace Turnly.Core.Entities;

/// <summary>One time-of-day slot's own turn on a chore with <see cref="Chore.TurnPerSlot"/> set:
/// that slot's current/next due date and who holds it. Each slot advances and rotates on its own,
/// so the morning and evening of a twice-a-day chore can belong to different people and be done in
/// any order. The chore's <see cref="Chore.DueAt"/>/<see cref="Chore.CurrentAssigneeId"/>/
/// <see cref="Chore.CurrentPartnerId"/> mirror the earliest slot so the single-schedule checks keep
/// working. Only present while the setting is on.</summary>
public class ChoreSlotTurn
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ChoreId { get; set; }
    public Chore? Chore { get; set; }

    /// <summary>The slot: one of the chore's <see cref="Chore.TimesOfDay"/>.</summary>
    public TimeOnly Time { get; set; }

    /// <summary>This slot's current/next occurrence.</summary>
    public DateTimeOffset? DueAt { get; set; }

    /// <summary>Who holds this slot's turn; null on an unassigned Manual chore.</summary>
    public Guid? CurrentAssigneeId { get; set; }
    public User? CurrentAssignee { get; set; }

    /// <summary>Who shares this slot's turn on a paired chore. Read through
    /// <see cref="Chore.HeldPartnerId(Guid?, Guid?)"/>, same as the chore's own column.</summary>
    public Guid? CurrentPartnerId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
