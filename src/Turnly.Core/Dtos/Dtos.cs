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
    DateTimeOffset CreatedAt)
{
    public static UserDto FromEntity(User u) =>
        new(u.Id, u.Username, u.DisplayName, u.AvatarColor, u.Role, u.Points, u.CreatedAt);
}

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
    DayOfWeek[] Weekdays,
    DateTimeOffset StartDate,
    DateTimeOffset? DueAt,
    UserDto? CurrentAssignee,
    UserDto[] Assignees,
    string[] Tags,
    ChoreCompletionDto? LastCompletion,
    DateTimeOffset CreatedAt)
{
    public static ChoreDto FromEntity(Chore c, ChoreCompletion? lastCompletion = null) =>
        new(c.Id, c.Name, c.Description, c.Emoji, c.Points, c.RepeatType,
            c.Weekdays.ToArray(), c.StartDate, c.DueAt,
            c.CurrentAssignee is null ? null : UserDto.FromEntity(c.CurrentAssignee),
            c.Assignees.Select(UserDto.FromEntity).OrderBy(u => u.DisplayName).ToArray(),
            c.Tags.Select(t => t.Name).OrderBy(n => n).ToArray(),
            lastCompletion is null ? null : ChoreCompletionDto.FromEntity(lastCompletion),
            c.CreatedAt);
}

public record CreateChoreRequest(
    string Name,
    string? Description,
    string? Emoji,
    int Points,
    RepeatType RepeatType,
    DayOfWeek[]? Weekdays,
    DateTimeOffset StartDate,
    Guid[] AssigneeIds,
    Guid CurrentAssigneeId,
    string[]? TagNames);

public record UpdateChoreRequest(
    string Name,
    string? Description,
    string? Emoji,
    int Points,
    RepeatType RepeatType,
    DayOfWeek[]? Weekdays,
    DateTimeOffset StartDate,
    Guid[] AssigneeIds,
    Guid CurrentAssigneeId,
    string[]? TagNames);

public record CompleteChoreRequest(string? Notes);

public record ChoreCompletionDto(
    Guid Id,
    Guid ChoreId,
    string ChoreName,
    UserDto CompletedBy,
    DateTimeOffset CompletedAt,
    string? Notes,
    int PointsAwarded)
{
    public static ChoreCompletionDto FromEntity(ChoreCompletion c) =>
        new(c.Id, c.ChoreId, c.Chore?.Name ?? string.Empty,
            UserDto.FromEntity(c.CompletedBy!), c.CompletedAt, c.Notes, c.PointsAwarded);
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

public record TagDto(Guid Id, string Name)
{
    public static TagDto FromEntity(Tag t) => new(t.Id, t.Name);
}
