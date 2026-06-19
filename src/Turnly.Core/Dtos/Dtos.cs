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
    int? FrequencyCount,
    FrequencyPeriod? FrequencyPeriod,
    AssignmentStrategy AssignmentStrategy,
    SchedulingPreference SchedulingPreference,
    DateTimeOffset StartDate,
    DateTimeOffset? DueAt,
    UserDto? CurrentAssignee,
    UserDto[] Assignees,
    string[] Tags,
    ChoreCompletionDto? LastCompletion,
    int? FrequencyProgress,
    DateTimeOffset CreatedAt)
{
    public static ChoreDto FromEntity(Chore c, ChoreCompletion? lastCompletion = null, int? frequencyProgress = null) =>
        new(c.Id, c.Name, c.Description, c.Emoji, c.Points, c.RepeatType,
            c.CustomMode, c.IntervalCount, c.IntervalUnit,
            c.Weekdays.ToArray(), c.DaysOfMonth.ToArray(), c.Months.ToArray(),
            c.FrequencyCount, c.FrequencyPeriod, c.AssignmentStrategy, c.SchedulingPreference,
            c.StartDate, c.DueAt,
            c.CurrentAssignee is null ? null : UserDto.FromEntity(c.CurrentAssignee),
            c.Assignees.Select(UserDto.FromEntity).OrderBy(u => u.DisplayName).ToArray(),
            c.Tags.Select(t => t.Name).OrderBy(n => n).ToArray(),
            lastCompletion is null ? null : ChoreCompletionDto.FromEntity(lastCompletion),
            frequencyProgress,
            c.CreatedAt);
}

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
    int? FrequencyCount { get; }
    FrequencyPeriod? FrequencyPeriod { get; }
    AssignmentStrategy AssignmentStrategy { get; }
    SchedulingPreference SchedulingPreference { get; }
    DateTimeOffset StartDate { get; }
    Guid[] AssigneeIds { get; }
    Guid CurrentAssigneeId { get; }
    string[]? TagNames { get; }
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
    int? FrequencyCount,
    FrequencyPeriod? FrequencyPeriod,
    AssignmentStrategy AssignmentStrategy,
    SchedulingPreference SchedulingPreference,
    DateTimeOffset StartDate,
    Guid[] AssigneeIds,
    Guid CurrentAssigneeId,
    string[]? TagNames) : IChoreInput;

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
    int? FrequencyCount,
    FrequencyPeriod? FrequencyPeriod,
    AssignmentStrategy AssignmentStrategy,
    SchedulingPreference SchedulingPreference,
    DateTimeOffset StartDate,
    Guid[] AssigneeIds,
    Guid CurrentAssigneeId,
    string[]? TagNames) : IChoreInput;

public record CompleteChoreRequest(string? Notes);

public record ChoreCompletionDto(
    Guid Id,
    Guid ChoreId,
    string ChoreName,
    UserDto CompletedBy,
    DateTimeOffset CompletedAt,
    DateTimeOffset? OccurrenceDueAt,
    string? Notes,
    int PointsAwarded)
{
    public static ChoreCompletionDto FromEntity(ChoreCompletion c) =>
        new(c.Id, c.ChoreId, c.Chore?.Name ?? string.Empty,
            UserDto.FromEntity(c.CompletedBy!), c.CompletedAt, c.OccurrenceDueAt, c.Notes, c.PointsAwarded);
}

public record PointsLogEntryDto(
    Guid Id,
    int Delta,
    PointsLogType Type,
    string? Description,
    Guid? ChoreCompletionId,
    DateTimeOffset CreatedAt)
{
    public static PointsLogEntryDto FromEntity(PointsLogEntry e) =>
        new(e.Id, e.Delta, e.Type, e.Description, e.ChoreCompletionId, e.CreatedAt);
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
