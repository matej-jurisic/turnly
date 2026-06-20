namespace Turnly.Core.Entities;

/// <summary>An in-app inbox record of a notification shown to a single user. Written whenever a
/// scheduled chore notification fires (one per recipient) or a test push is sent, so users can see
/// their notification history in-app independent of whether a push device was reachable. The
/// optional <see cref="ChoreId"/> lets the UI deep-link the originating chore (cleared if the chore
/// is later deleted).</summary>
public class UserNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }
    public User? User { get; set; }

    public required string Title { get; set; }
    public required string Body { get; set; }

    /// <summary>The chore this notification is about, if any. <c>SetNull</c> on chore deletion.</summary>
    public Guid? ChoreId { get; set; }
    public Chore? Chore { get; set; }

    /// <summary>When the user marked this read; <c>null</c> while unread.</summary>
    public DateTimeOffset? ReadAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
