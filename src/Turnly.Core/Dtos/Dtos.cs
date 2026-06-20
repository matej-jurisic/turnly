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
    DateTimeOffset CreatedAt)
{
    public static UserDto FromEntity(User u, int weeklyPoints = 0) =>
        new(u.Id, u.Username, u.DisplayName, u.AvatarColor, u.Role, u.Points, weeklyPoints, u.CreatedAt);
}

public record LeaderboardEntryDto(
    Guid Id,
    string DisplayName,
    string AvatarColor,
    int Points,
    int WeeklyPoints);

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

public record UpdateUserRequest(string DisplayName, string AvatarColor, UserRole Role);

public record UpdateProfileRequest(string AvatarColor);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record SetPasswordRequest(string NewPassword);

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
    int[] DaysOfMonth,
    int[] Months,
    int CompletionsRequired,
    bool RotateOnEachCompletion,
    AssignmentStrategy AssignmentStrategy,
    SchedulingPreference SchedulingPreference,
    int? GraceMinutes,
    DateTimeOffset StartDate,
    string? DueTime,
    DateTimeOffset? DueAt,
    UserDto? CurrentAssignee,
    UserDto? NextAssignee,
    UserDto[] Assignees,
    string[] Tags,
    ChoreNotificationDto[] Notifications,
    ChoreCompletionDto? LastCompletion,
    int? OccurrenceProgress,
    DateTimeOffset CreatedAt)
{
    public static ChoreDto FromEntity(Chore c, ChoreCompletion? lastCompletion = null, int? occurrenceProgress = null, User? nextAssignee = null) =>
        new(c.Id, c.Name, c.Description, c.Emoji, c.Points, c.RepeatType,
            c.CustomMode, c.IntervalCount, c.IntervalUnit,
            c.Weekdays.ToArray(), c.DaysOfMonth.ToArray(), c.Months.ToArray(),
            c.CompletionsRequired, c.RotateOnEachCompletion, c.AssignmentStrategy, c.SchedulingPreference,
            c.GraceMinutes, c.StartDate, c.DueTime?.ToString("HH\\:mm"), c.DueAt,
            c.CurrentAssignee is null ? null : UserDto.FromEntity(c.CurrentAssignee),
            nextAssignee is null ? null : UserDto.FromEntity(nextAssignee),
            c.Assignees.Select(UserDto.FromEntity).OrderBy(u => u.DisplayName).ToArray(),
            c.Tags.Select(t => t.Name).OrderBy(n => n).ToArray(),
            c.Notifications.OrderBy(n => n.CreatedAt).Select(ChoreNotificationDto.FromEntity).ToArray(),
            lastCompletion is null ? null : ChoreCompletionDto.FromEntity(lastCompletion),
            occurrenceProgress,
            c.CreatedAt);
}

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
    int[]? DaysOfMonth { get; }
    int[]? Months { get; }
    int CompletionsRequired { get; }
    bool RotateOnEachCompletion { get; }
    AssignmentStrategy AssignmentStrategy { get; }
    SchedulingPreference SchedulingPreference { get; }
    /// <summary>Grace window in minutes for <see cref="SchedulingPreference.SmartScheduling"/>; null
    /// (or non-positive) means no grace. Ignored for other scheduling preferences.</summary>
    int? GraceMinutes { get; }
    DateTimeOffset StartDate { get; }
    /// <summary>Optional local time-of-day ("HH:mm") the chore is due; null means end of day. The
    /// client bakes the resolved instant into <see cref="StartDate"/>; this is stored for round-trip.</summary>
    string? DueTime { get; }
    Guid[] AssigneeIds { get; }
    Guid CurrentAssigneeId { get; }
    string[]? TagNames { get; }
    ChoreNotificationInput[]? Notifications { get; }
}

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
    int[]? DaysOfMonth,
    int[]? Months,
    int CompletionsRequired,
    bool RotateOnEachCompletion,
    AssignmentStrategy AssignmentStrategy,
    SchedulingPreference SchedulingPreference,
    int? GraceMinutes,
    DateTimeOffset StartDate,
    Guid[] AssigneeIds,
    Guid CurrentAssigneeId,
    string[]? TagNames,
    ChoreNotificationInput[]? Notifications = null,
    string? DueTime = null) : IChoreInput;

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
    int[]? DaysOfMonth,
    int[]? Months,
    int CompletionsRequired,
    bool RotateOnEachCompletion,
    AssignmentStrategy AssignmentStrategy,
    SchedulingPreference SchedulingPreference,
    int? GraceMinutes,
    DateTimeOffset StartDate,
    Guid[] AssigneeIds,
    Guid CurrentAssigneeId,
    string[]? TagNames,
    ChoreNotificationInput[]? Notifications = null,
    string? DueTime = null) : IChoreInput;

public record CompleteChoreRequest(string? Notes, Guid? CompletedByUserId = null);

public record SkipChoreRequest(string? Notes);

public record ReassignChoreRequest(Guid AssigneeId);

public record ChoreCompletionDto(
    Guid Id,
    Guid ChoreId,
    string ChoreName,
    UserDto CompletedBy,
    DateTimeOffset CompletedAt,
    DateTimeOffset? OccurrenceDueAt,
    string? Notes,
    int PointsAwarded,
    bool IsSkip)
{
    public static ChoreCompletionDto FromEntity(ChoreCompletion c) =>
        new(c.Id, c.ChoreId, c.Chore?.Name ?? string.Empty,
            UserDto.FromEntity(c.CompletedBy!), c.CompletedAt, c.OccurrenceDueAt, c.Notes, c.PointsAwarded, c.IsSkip);
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
    public static ChoreHistoryEntryDto FromCompletion(ChoreCompletion c) =>
        new(c.Id, c.IsSkip ? "skip" : "completion", c.ChoreId, c.Chore?.Name ?? string.Empty,
            UserDto.FromEntity(c.CompletedBy!), c.CompletedAt, c.OccurrenceDueAt, c.Notes,
            c.PointsAwarded, null, null);

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
}

public record PushSubscribeRequest(string Endpoint, string P256dh, string Auth);

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
    int OverdueCount);

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
    IEnumerable<ChartWeekDto> Chart);
