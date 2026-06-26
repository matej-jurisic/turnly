using Turnly.Core.Entities;
using Turnly.Core.Enums;

namespace Turnly.Core.Dtos;

public record UserDto(
    Guid Id,
    string Username,
    string DisplayName,
    string AvatarColor,
    UserRole Role,
    int Points,
    int WeeklyPoints,
    DateTimeOffset CreatedAt,
    string? QuietHoursStart = null,
    string? QuietHoursEnd = null)
{
    public bool IsFrozen { get; init; }

    /// <summary>Equipped avatar frame cosmetic key (or null). Carried on every user projection so the
    /// frame renders wherever an avatar shows.</summary>
    public string? EquippedFrameKey { get; init; }

    /// <summary>Equipped app theme palette key (or null). Only meaningful for the owner's own view.</summary>
    public string? EquippedThemeKey { get; init; }

    public static UserDto FromEntity(User u, int weeklyPoints = 0) =>
        new(u.Id, u.Username, u.DisplayName, u.AvatarColor, u.Role, u.Points, weeklyPoints, u.CreatedAt,
            u.QuietHoursStart?.ToString("HH:mm"), u.QuietHoursEnd?.ToString("HH:mm"))
            { IsFrozen = u.IsFrozen, EquippedFrameKey = u.EquippedFrameKey, EquippedThemeKey = u.EquippedThemeKey };
}

public record LeaderboardEntryDto(
    Guid Id,
    string DisplayName,
    string AvatarColor,
    int Points,
    int WeeklyPoints,
    string? EquippedFrameKey = null);

/// <summary>
/// Result of a successful authentication. The raw <see cref="RefreshToken"/> is set by
/// the API into an httpOnly cookie and never returned in the response body.
/// </summary>
public record AuthResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    UserDto User);

public record LoginRequest(string Username, string Password);

public record SetupRequest(string Username, string DisplayName, string Password, string? AvatarColor);

public record CreateUserRequest(string Username, string DisplayName, string Password, UserRole Role, string? AvatarColor);

public record UpdateUserRequest(string DisplayName, UserRole Role);

public record UpdateProfileRequest(string? QuietHoursStart = null, string? QuietHoursEnd = null);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record SetPasswordRequest(string NewPassword);

public record AdjustPointsRequest(int Delta, string? Description);

public record FreezeReassignmentDto(
    Guid ChoreId, string ChoreName, string? ChoreEmoji,
    Guid NewAssigneeId, string NewAssigneeName, string NewAssigneeAvatarColor);

public record FreezeUnassignableDto(Guid ChoreId, string ChoreName, string? ChoreEmoji);

public record UserFreezePreviewDto(FreezeReassignmentDto[] Reassignments, FreezeUnassignableDto[] Unassignable);

/// <summary>App-wide settings. <see cref="TimeZone"/> is the configured family zone (null = unset);
/// <see cref="ServerTimeZone"/> is the host's local zone, used as the fallback and shown in the UI.</summary>
public record AppSettingsDto(string? TimeZone, string ServerTimeZone);

public record UpdateAppSettingsRequest(string? TimeZone);

public record ChoreDto(
    Guid Id,
    string Name,
    string? Description,
    string? Emoji,
    int Points,
    RepeatType RepeatType,
    CustomRecurrenceMode? CustomMode,
    int? IntervalCount,
    RecurrenceUnit? IntervalUnit,
    DayOfWeek[] Weekdays,
    int[] WeeksOfMonth,
    int[] DaysOfMonth,
    int[] Months,
    int CompletionsRequired,
    bool RotateOnEachCompletion,
    AssignmentStrategy AssignmentStrategy,
    SchedulingPreference SchedulingPreference,
    int? GraceMinutes,
    bool AutoAdvanceIncomplete,
    int? CompletionWindowMinutes,
    DateTimeOffset StartDate,
    string? DueTime,
    string[] TimesOfDay,
    DateTimeOffset? DueAt,
    UserDto? CurrentAssignee,
    UserDto? NextAssignee,
    UserDto[] Assignees,
    string[] Tags,
    ChoreNotificationDto[] Notifications,
    ChoreCompletionDto? LastCompletion,
    int? OccurrenceProgress,
    int CurrentStreak,
    ChoreAssigneeTrackDto[] Tracks,
    DateTimeOffset CreatedAt)
{
    public static ChoreDto FromEntity(Chore c, ChoreCompletion? lastCompletion = null, int? occurrenceProgress = null,
        User? nextAssignee = null, ChoreAssigneeTrackDto[]? tracks = null, int currentStreak = 0) =>
        new(c.Id, c.Name, c.Description, c.Emoji, c.Points, c.RepeatType,
            c.CustomMode, c.IntervalCount, c.IntervalUnit,
            c.Weekdays.ToArray(), c.WeeksOfMonth.ToArray(), c.DaysOfMonth.ToArray(), c.Months.ToArray(),
            c.CompletionsRequired, c.RotateOnEachCompletion, c.AssignmentStrategy, c.SchedulingPreference,
            c.GraceMinutes, c.AutoAdvanceIncomplete, c.CompletionWindowMinutes,
            c.StartDate, c.DueTime?.ToString("HH\\:mm"),
            c.TimesOfDay.OrderBy(t => t).Select(t => t.ToString("HH\\:mm")).ToArray(), c.DueAt,
            c.CurrentAssignee is null ? null : UserDto.FromEntity(c.CurrentAssignee),
            nextAssignee is null ? null : UserDto.FromEntity(nextAssignee),
            c.Assignees.Select(UserDto.FromEntity).OrderBy(u => u.DisplayName).ToArray(),
            c.Tags.Select(t => t.Name).OrderBy(n => n).ToArray(),
            c.Notifications.OrderBy(n => n.CreatedAt).Select(ChoreNotificationDto.FromEntity).ToArray(),
            lastCompletion is null ? null : ChoreCompletionDto.FromEntity(lastCompletion),
            occurrenceProgress,
            currentStreak,
            tracks ?? [],
            c.CreatedAt) { IsFrozen = c.IsFrozen };

    public bool IsFrozen { get; init; }

    /// <summary>Achievements the credited user just unlocked with this completion — set only on the
    /// response to a self-completion so the client can show a celebration popup. Empty otherwise; not
    /// part of the chore's persistent state.</summary>
    public AchievementDto[] UnlockedAchievements { get; init; } = [];
}

/// <summary>One assignee's independent schedule on a track-mode chore: their own due date, quota,
/// and how many completions/skips they have logged toward the current occurrence. <c>Started</c> is
/// false until they have logged any completion/skip — it tells a future due date that is just the
/// not-yet-reached first occurrence (the chore's start date is in the future) apart from one that is
/// in the future because the assignee completed the prior occurrence.</summary>
public record ChoreAssigneeTrackDto(
    UserDto User,
    DateTimeOffset? DueAt,
    int CompletionsRequired,
    int Progress,
    bool Started,
    int Streak);

public record ChoreNotificationDto(
    Guid Id,
    NotificationType Type,
    NotificationTiming Timing,
    int OffsetValue,
    NotificationOffsetUnit OffsetUnit,
    NotificationRecipients Recipients)
{
    public static ChoreNotificationDto FromEntity(ChoreNotification n) =>
        new(n.Id, n.Type, n.Timing, n.OffsetValue, n.OffsetUnit, n.Recipients);
}

/// <summary>A notification schedule entry as supplied on chore create/update (no id).</summary>
public record ChoreNotificationInput(
    NotificationType Type,
    NotificationTiming Timing,
    int OffsetValue,
    NotificationOffsetUnit OffsetUnit,
    NotificationRecipients Recipients);

/// <summary>The shared shape of a chore create/update request, so the service can validate and
/// apply both through one code path.</summary>
public interface IChoreInput
{
    string Name { get; }
    string? Description { get; }
    string? Emoji { get; }
    int Points { get; }
    RepeatType RepeatType { get; }
    CustomRecurrenceMode? CustomMode { get; }
    int? IntervalCount { get; }
    RecurrenceUnit? IntervalUnit { get; }
    DayOfWeek[]? Weekdays { get; }
    int[]? WeeksOfMonth { get; }
    int[]? DaysOfMonth { get; }
    int[]? Months { get; }
    int CompletionsRequired { get; }
    bool RotateOnEachCompletion { get; }
    AssignmentStrategy AssignmentStrategy { get; }
    SchedulingPreference SchedulingPreference { get; }
    /// <summary>Grace window in minutes for <see cref="SchedulingPreference.SmartScheduling"/>; null
    /// (or non-positive) means no grace. Ignored for other scheduling preferences.</summary>
    int? GraceMinutes { get; }
    /// <summary>When true, the background service auto-expires unfilled slots and advances the
    /// occurrence once the completion window closes. Only meaningful when
    /// <see cref="CompletionsRequired"/> &gt; 1.</summary>
    bool AutoAdvanceIncomplete { get; }
    /// <summary>Minutes after <see cref="DueAt"/> before auto-advance fires; null = immediately
    /// when overdue. Only meaningful when <see cref="AutoAdvanceIncomplete"/> is true.</summary>
    int? CompletionWindowMinutes { get; }
    DateTimeOffset StartDate { get; }
    /// <summary>Optional local time-of-day ("HH:mm") the chore is due; null means end of day. The
    /// client bakes the resolved instant into <see cref="StartDate"/>; this is stored for round-trip.</summary>
    string? DueTime { get; }
    /// <summary>Optional fixed times-of-day ("HH:mm") for "N times a day" — each is its own
    /// occurrence. Only valid for Daily and the custom DaysOfWeek / DaysOfMonth modes; null/empty
    /// means a single daily slot at <see cref="DueTime"/>.</summary>
    string[]? TimesOfDay { get; }
    Guid[] AssigneeIds { get; }
    /// <summary>The current assignee for rotating chores; null for
    /// <see cref="Enums.AssignmentStrategy.Independent"/> (per-assignee tracks instead).</summary>
    Guid? CurrentAssigneeId { get; }
    string[]? TagNames { get; }
    ChoreNotificationInput[]? Notifications { get; }
    /// <summary>Per-assignee quotas for <see cref="Enums.AssignmentStrategy.Independent"/> chores —
    /// one entry per assignee. Ignored (and cleared) for rotating chores.</summary>
    TrackInput[]? Tracks { get; }
}

/// <summary>One assignee's quota as supplied on create/update of a track-mode chore.</summary>
public record TrackInput(Guid UserId, int CompletionsRequired);

public record CreateChoreRequest(
    string Name,
    string? Description,
    string? Emoji,
    int Points,
    RepeatType RepeatType,
    CustomRecurrenceMode? CustomMode,
    int? IntervalCount,
    RecurrenceUnit? IntervalUnit,
    DayOfWeek[]? Weekdays,
    int[]? WeeksOfMonth,
    int[]? DaysOfMonth,
    int[]? Months,
    int CompletionsRequired,
    bool RotateOnEachCompletion,
    AssignmentStrategy AssignmentStrategy,
    SchedulingPreference SchedulingPreference,
    int? GraceMinutes,
    bool AutoAdvanceIncomplete,
    int? CompletionWindowMinutes,
    DateTimeOffset StartDate,
    Guid[] AssigneeIds,
    Guid? CurrentAssigneeId,
    string[]? TagNames,
    ChoreNotificationInput[]? Notifications = null,
    string? DueTime = null,
    TrackInput[]? Tracks = null,
    string[]? TimesOfDay = null) : IChoreInput;

public record UpdateChoreRequest(
    string Name,
    string? Description,
    string? Emoji,
    int Points,
    RepeatType RepeatType,
    CustomRecurrenceMode? CustomMode,
    int? IntervalCount,
    RecurrenceUnit? IntervalUnit,
    DayOfWeek[]? Weekdays,
    int[]? WeeksOfMonth,
    int[]? DaysOfMonth,
    int[]? Months,
    int CompletionsRequired,
    bool RotateOnEachCompletion,
    AssignmentStrategy AssignmentStrategy,
    SchedulingPreference SchedulingPreference,
    int? GraceMinutes,
    bool AutoAdvanceIncomplete,
    int? CompletionWindowMinutes,
    DateTimeOffset StartDate,
    Guid[] AssigneeIds,
    Guid? CurrentAssigneeId,
    string[]? TagNames,
    ChoreNotificationInput[]? Notifications = null,
    string? DueTime = null,
    TrackInput[]? Tracks = null,
    string[]? TimesOfDay = null) : IChoreInput;

public record CopyChoreRequest(string NewName);

public record CompleteChoreRequest(string? Notes, Guid? CompletedByUserId = null);

/// <summary><see cref="UserId"/> names whose track to skip on a track-mode chore (required there);
/// ignored for rotating chores, which only have one schedule to skip.</summary>
public record SkipChoreRequest(string? Notes, Guid? UserId = null);

public record ReassignChoreRequest(Guid AssigneeId);

/// <summary><see cref="UserId"/> names whose track to reschedule on a track-mode chore (required
/// there); ignored for rotating chores.</summary>
public record RescheduleChoreRequest(DateTimeOffset DueAt, string? DueTime, Guid? UserId = null);

public record ChoreCompletionDto(
    Guid Id,
    Guid ChoreId,
    string ChoreName,
    UserDto? CompletedBy,
    DateTimeOffset CompletedAt,
    DateTimeOffset? OccurrenceDueAt,
    string? Notes,
    int PointsAwarded,
    bool IsSkip,
    bool IsExpired)
{
    public static ChoreCompletionDto FromEntity(ChoreCompletion c) =>
        new(c.Id, c.ChoreId, c.Chore?.Name ?? string.Empty,
            c.CompletedBy is null ? null : UserDto.FromEntity(c.CompletedBy),
            c.CompletedAt, c.OccurrenceDueAt, c.Notes, c.PointsAwarded, c.IsSkip, c.IsExpired);
}

/// <summary>A single entry in the chore history feed — either a completion/skip (a
/// <see cref="ChoreCompletion"/>) or a manual reassignment (a <see cref="ChoreAssignment"/> with an
/// acting user). <see cref="Kind"/> discriminates: "completion" | "skip" | "reassignment".</summary>
public record ChoreHistoryEntryDto(
    Guid Id,
    string Kind,
    Guid ChoreId,
    string ChoreName,
    UserDto? Actor,
    DateTimeOffset At,
    DateTimeOffset? OccurrenceDueAt,
    string? Notes,
    int PointsAwarded,
    UserDto? FromAssignee,
    UserDto? ToAssignee)
{
    public static ChoreHistoryEntryDto FromCompletion(ChoreCompletion c, User? expiredAssignee = null) =>
        new(c.Id, c.IsExpired ? "expired" : c.IsSkip ? "skip" : "completion",
            c.ChoreId, c.Chore?.Name ?? string.Empty,
            c.IsExpired
                ? expiredAssignee is null ? null : UserDto.FromEntity(expiredAssignee)
                : c.CompletedBy is null ? null : UserDto.FromEntity(c.CompletedBy),
            c.CompletedAt, c.OccurrenceDueAt, c.Notes, c.PointsAwarded, null, null);

    public static ChoreHistoryEntryDto FromReassignment(ChoreAssignment a) =>
        new(a.Id, "reassignment", a.ChoreId, a.Chore?.Name ?? string.Empty,
            a.AssignedBy is null ? null : UserDto.FromEntity(a.AssignedBy), a.AssignedAt, null, null, 0,
            a.PreviousAssignee is null ? null : UserDto.FromEntity(a.PreviousAssignee),
            a.User is null ? null : UserDto.FromEntity(a.User));
}

public record PointsLogEntryDto(
    Guid Id,
    int Delta,
    PointsLogType Type,
    string? Description,
    Guid? ChoreCompletionId,
    Guid? RedemptionId,
    DateTimeOffset CreatedAt)
{
    public static PointsLogEntryDto FromEntity(PointsLogEntry e) =>
        new(e.Id, e.Delta, e.Type, e.Description, e.ChoreCompletionId, e.RedemptionId, e.CreatedAt);
}

public record AwardDto(
    Guid Id,
    string Name,
    string? Description,
    string? Emoji,
    int Cost,
    DateTimeOffset CreatedAt)
{
    public static AwardDto FromEntity(Award a) =>
        new(a.Id, a.Name, a.Description, a.Emoji, a.Cost, a.CreatedAt);
}

public record CreateAwardRequest(string Name, string? Description, string? Emoji, int Cost);

public record UpdateAwardRequest(string Name, string? Description, string? Emoji, int Cost);

public record RedemptionDto(
    Guid Id,
    Guid? AwardId,
    string AwardName,
    string? AwardEmoji,
    UserDto User,
    int PointsSpent,
    RedemptionStatus Status,
    DateTimeOffset RedeemedAt,
    DateTimeOffset? FulfilledAt)
{
    public static RedemptionDto FromEntity(Redemption r) =>
        new(r.Id, r.AwardId, r.AwardName, r.AwardEmoji,
            UserDto.FromEntity(r.User!), r.PointsSpent, r.Status, r.RedeemedAt, r.FulfilledAt);

    /// <summary>Achievements the user just unlocked by redeeming — set only on the redeem response so the
    /// client can show a celebration popup. Empty otherwise.</summary>
    public AchievementDto[] UnlockedAchievements { get; init; } = [];
}

public record PushSubscribeRequest(string Endpoint, string P256dh, string Auth);

/// <summary>Register (or refresh) the native app's FCM token. <see cref="Token"/> on unsubscribe too.</summary>
public record FcmSubscribeRequest(string Token);

public record PushDeviceDto(Guid Id, string Label, string Endpoint, DateTimeOffset CreatedAt)
{
    public static PushDeviceDto FromEntity(Turnly.Core.Entities.PushSubscription s) =>
        new(s.Id, string.IsNullOrWhiteSpace(s.DeviceLabel) ? "Unknown device" : s.DeviceLabel!, s.Endpoint, s.CreatedAt);
}

public record NotificationInboxDto(
    Guid Id,
    string Title,
    string Body,
    Guid? ChoreId,
    bool Read,
    DateTimeOffset CreatedAt)
{
    public static NotificationInboxDto FromEntity(Turnly.Core.Entities.UserNotification n) =>
        new(n.Id, n.Title, n.Body, n.ChoreId, n.ReadAt is not null, n.CreatedAt);
}

public record CreateTagRequest(string Name);

public record TagDto(Guid Id, string Name)
{
    public static TagDto FromEntity(Tag t) => new(t.Id, t.Name);
}

public record UserStatsDto(
    Guid UserId,
    string DisplayName,
    string AvatarColor,
    int WeeklyCount,
    int MonthlyCount,
    int AllTimeCount,
    int OnTimeCount,
    int OverdueCount,
    int MissedCount);

public record UserWeeklyCountDto(
    Guid UserId,
    string DisplayName,
    string AvatarColor,
    int Count);

public record ChartWeekDto(
    string Label,
    DateTimeOffset WeekStart,
    IEnumerable<UserWeeklyCountDto> UserCounts);

public record StatsDto(
    IEnumerable<UserStatsDto> UserStats,
    IEnumerable<ChartWeekDto> Chart,
    int TotalMissedCount);

/// <summary>A catalog achievement projected for one user: its definition plus the user's current
/// <see cref="Progress"/> toward <see cref="Threshold"/> and whether it's been earned. Cosmetic —
/// no points are involved.</summary>
public record AchievementDto(
    string Key,
    string Name,
    string Description,
    string Emoji,
    string Category,
    int Threshold,
    int Progress,
    bool Earned,
    DateTimeOffset? EarnedAt);

/// <summary>A gacha cosmetic projected for one user: catalog definition + ownership/equip state +
/// the dust price to craft it if unowned.</summary>
public record CosmeticDto(
    string Key,
    string Name,
    string Description,
    CosmeticSlot Slot,
    CosmeticRarity Rarity,
    bool Owned,
    bool Equipped,
    int Count,
    int DustCraftCost,
    string? Value = null);

/// <summary>One rarity tier's published economy: pull odds + dust values. Shown to the user.</summary>
public record RarityOddsDto(
    CosmeticRarity Rarity,
    double Odds,
    int DustAward,
    int DustCraftCost);

/// <summary>Everything the gacha page needs: balances, pull pricing, pity progress, the published
/// odds, and the full catalog with per-user ownership.</summary>
public record GachaStateDto(
    int Points,
    int Dust,
    int PullCost,
    int TenPullCost,
    int PullsSinceLegendary,
    int PityThreshold,
    RarityOddsDto[] Odds,
    CosmeticDto[] Cosmetics);

/// <summary>Outcome of a single roll within a pull: the cosmetic, whether it was newly unlocked, and
/// the dust awarded when it was a duplicate.</summary>
public record PullResultDto(
    CosmeticDto Cosmetic,
    bool IsNew,
    int DustAwarded);

public record PullRequest(int Count = 1);

public record CraftRequest(string Key);

public record EquipRequest(CosmeticSlot Slot, string? Key);
