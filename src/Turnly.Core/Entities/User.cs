using Turnly.Core.Enums;

namespace Turnly.Core.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Username { get; set; }
    public required string DisplayName { get; set; }
    public string AvatarColor { get; set; } = "#6366f1";

    /// <summary>The emoji shown on the avatar in place of initials, or null to use initials. Set from
    /// an equipped <see cref="CosmeticSlot.Emoji"/> cosmetic's value; threads through
    /// <c>UserDto.FromEntity</c> to every avatar render site. Visible to everyone.</summary>
    public string? AvatarEmoji { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Member;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Denormalized point balance, kept in sync with <see cref="PointsLog"/>.</summary>
    public int Points { get; set; }

    /// <summary>Start of the user's quiet-hours window (server-local time). During quiet hours push
    /// notifications are suppressed (the in-app inbox row is still written). Null when quiet hours are
    /// off; <see cref="QuietHoursStart"/> and <see cref="QuietHoursEnd"/> are set together. A window
    /// where start &gt; end spans midnight (e.g. 22:00–07:00).</summary>
    public TimeOnly? QuietHoursStart { get; set; }
    public TimeOnly? QuietHoursEnd { get; set; }

    /// <summary>Household-local time the user gets their daily digest (what they hold today, what's
    /// overdue, open offers), or null when the digest is off. See <c>NotificationService.ProcessDigestsAsync</c>.</summary>
    public TimeOnly? DigestTime { get; set; }

    /// <summary>The household-local date the last digest was considered for this user (sent, or skipped
    /// for having nothing to say). One digest per day: the scan's dedup key.</summary>
    public DateOnly? DigestSentOn { get; set; }

    /// <summary>The language the user last picked in the app ("en" / "hr"), synced from their device.
    /// Only used to word push and inbox notifications; null = English.</summary>
    public string? Language { get; set; }

    /// <summary>When true the user is suspended: excluded from assignment rotation, their Independent
    /// tracks get no auto-advance or notifications, and rotating chores they own are reassigned on
    /// freeze. Admin-only toggle. On unfreeze, stale Independent tracks are stepped forward.</summary>
    public bool IsFrozen { get; set; }

    /// <summary>Key of the avatar frame the user has equipped (a <c>CosmeticDefinition.Key</c> in the
    /// <see cref="Turnly.Core.Cosmetics.CosmeticCatalog"/>), or null for none. Lives on the user so it
    /// threads through <c>UserDto.FromEntity</c> to every avatar render site. Frames are visible to
    /// everyone.</summary>
    public string? EquippedFrameKey { get; set; }

    /// <summary>Key of the app theme palette the user has equipped, or null for the default. Only
    /// recolors the owner's own view.</summary>
    public string? EquippedThemeKey { get; set; }

    /// <summary>Gacha dust balance — earned from duplicate pulls, spent to craft a specific cosmetic.</summary>
    public int Dust { get; set; }

    /// <summary>Pity counter: pulls made since the last Legendary. When it reaches the catalog's pity
    /// threshold the next pull is forced to a Legendary, then this resets to zero.</summary>
    public int PullsSinceLegendary { get; set; }

    /// <summary>Cosmetic pull tickets: the only currency pulls cost, so cosmetics never compete with
    /// awards for points. One is granted per <see cref="Turnly.Core.Cosmetics.CosmeticCatalog.PointsPerTicket"/>
    /// points earned (see <see cref="TrackEarned"/>); spending points never touches them.</summary>
    public int PullTickets { get; set; }

    /// <summary>Earned points counted toward the next pull ticket, below
    /// <see cref="Turnly.Core.Cosmetics.CosmeticCatalog.PointsPerTicket"/>. Negative when a reversal took
    /// back more than was left and there was no ticket to take; later earnings pay that off first.</summary>
    public int TicketProgress { get; set; }

    /// <summary>Feeds an earned-points change (a completion, its reversal, an admin adjustment) into the
    /// ticket counter: grants a ticket per full step, and on a reversal takes unspent tickets back
    /// rather than leaving a ticket for points that were never really earned. Not for spends, refunds
    /// or bounties.</summary>
    public void TrackEarned(int delta)
    {
        var step = Turnly.Core.Cosmetics.CosmeticCatalog.PointsPerTicket;
        TicketProgress += delta;
        while (TicketProgress >= step)
        {
            PullTickets++;
            TicketProgress -= step;
        }
        while (TicketProgress < 0 && PullTickets > 0)
        {
            PullTickets--;
            TicketProgress += step;
        }
    }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
    public ICollection<PointsLogEntry> PointsLog { get; set; } = new List<PointsLogEntry>();
}
