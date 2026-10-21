namespace Turnly.Core.Entities;

/// <summary>One member nudging another about a chore they hold. The message itself lives in the
/// recipient's inbox (<see cref="UserNotification"/>), which they can clear or switch off; this row
/// exists so the sender's cooldown survives that. Rows older than the cooldown are pruned the next
/// time the same sender nudges about the same chore.</summary>
public class ChoreNudge
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ChoreId { get; set; }
    public Chore? Chore { get; set; }

    public Guid FromUserId { get; set; }
    public User? FromUser { get; set; }

    public Guid ToUserId { get; set; }
    public User? ToUser { get; set; }

    /// <summary>The optional free-text note sent with the nudge, trimmed.</summary>
    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
