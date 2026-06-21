using Microsoft.EntityFrameworkCore;
using Turnly.Core.Auth;
using Turnly.Core.Common;
using Turnly.Core.Data;
using Turnly.Core.Dtos;
using Turnly.Core.Entities;
using Turnly.Core.Enums;

namespace Turnly.Core.Services;

public class UserService
{
    private const string DefaultAvatarColor = "#6366f1";

    private readonly TurnlyDbContext _db;
    private readonly IPasswordHasher _hasher;

    public UserService(TurnlyDbContext db, IPasswordHasher hasher)
    {
        _db = db;
        _hasher = hasher;
    }

    public async Task<List<UserDto>> ListAsync(CancellationToken ct = default)
    {
        var weeklyByUser = await ComputeWeeklyPointsAsync(ct);
        var users = await _db.Users.OrderBy(u => u.DisplayName).ToListAsync(ct);
        return users.Select(u => UserDto.FromEntity(u, weeklyByUser.GetValueOrDefault(u.Id))).ToList();
    }

    public async Task<List<LeaderboardEntryDto>> GetLeaderboardAsync(CancellationToken ct = default)
    {
        var weeklyByUser = await ComputeWeeklyPointsAsync(ct);
        var users = await _db.Users.ToListAsync(ct);
        return users
            .OrderByDescending(u => u.Points)
            .Select(u => new LeaderboardEntryDto(
                u.Id, u.DisplayName, u.AvatarColor, u.Points,
                weeklyByUser.GetValueOrDefault(u.Id)))
            .ToList();
    }

    // SQLite can't translate DateTimeOffset comparisons to SQL, so filter in memory.
    // Project to minimal columns to avoid loading full entities.
    private async Task<Dictionary<Guid, int>> ComputeWeeklyPointsAsync(CancellationToken ct)
    {
        var weekStart = GetCurrentWeekStart();
        var log = await _db.PointsLog
            .Select(e => new { e.UserId, e.Delta, e.CreatedAt })
            .ToListAsync(ct);
        return log
            .Where(e => e.CreatedAt >= weekStart)
            .GroupBy(e => e.UserId)
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Delta));
    }

    private static DateTimeOffset GetCurrentWeekStart()
    {
        var now = DateTimeOffset.UtcNow;
        var daysToMonday = (int)now.DayOfWeek == 0 ? 6 : (int)now.DayOfWeek - 1;
        return new DateTimeOffset(now.AddDays(-daysToMonday).Date, TimeSpan.Zero);
    }

    public async Task<StatsDto> GetStatsAsync(CancellationToken ct = default)
    {
        var users = await _db.Users.OrderBy(u => u.DisplayName).ToListAsync(ct);
        var allCompletions = await _db.ChoreCompletions
            .Where(c => !c.IsSkip) // skips are not real completions — exclude from stats
            .Select(c => new { c.CompletedByUserId, c.CompletedAt, c.OccurrenceDueAt, c.IsExpired, c.PreviousAssigneeId })
            .ToListAsync(ct);

        var weekStart = GetCurrentWeekStart();
        var now = DateTimeOffset.UtcNow;
        var monthStart = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);

        // Expired rows have null CompletedByUserId so they never match the per-user completion counts below.
        var userStats = users.Select(u => new UserStatsDto(
            u.Id, u.DisplayName, u.AvatarColor,
            allCompletions.Count(c => c.CompletedByUserId == u.Id && c.CompletedAt >= weekStart),
            allCompletions.Count(c => c.CompletedByUserId == u.Id && c.CompletedAt >= monthStart),
            allCompletions.Count(c => c.CompletedByUserId == u.Id),
            allCompletions.Count(c => c.CompletedByUserId == u.Id
                && c.OccurrenceDueAt.HasValue && c.CompletedAt <= c.OccurrenceDueAt.Value),
            allCompletions.Count(c => c.CompletedByUserId == u.Id
                && c.OccurrenceDueAt.HasValue && c.CompletedAt > c.OccurrenceDueAt.Value),
            allCompletions.Count(c => c.IsExpired && c.PreviousAssigneeId == u.Id)
        )).ToList();

        var totalMissedCount = allCompletions.Count(c => c.IsExpired);

        // Last 8 weeks oldest-first so the chart reads left-to-right chronologically.
        var chart = Enumerable.Range(0, 8).Reverse().Select(weeksAgo =>
        {
            var ws = weekStart.AddDays(-7 * weeksAgo);
            var we = ws.AddDays(7);
            var end = we.AddDays(-1);
            var label = ws.Month == end.Month
                ? $"{ws:MMM d}–{end.Day}"
                : $"{ws:MMM d}–{end:MMM d}";
            var userCounts = users.Select(u => new UserWeeklyCountDto(
                u.Id, u.DisplayName, u.AvatarColor,
                allCompletions.Count(c => c.CompletedByUserId == u.Id && c.CompletedAt >= ws && c.CompletedAt < we)
            )).ToList();
            return new ChartWeekDto(label, ws, userCounts);
        }).ToList();

        return new StatsDto(userStats, chart, totalMissedCount);
    }

    public async Task<Result<List<PointsLogEntryDto>>> GetPointsLogAsync(Guid userId, CancellationToken ct = default)
    {
        if (!await _db.Users.AnyAsync(u => u.Id == userId, ct))
            return Result.Fail<List<PointsLogEntryDto>>(Error.NotFound("User not found."));

        // Order client-side: SQLite can't ORDER BY DateTimeOffset.
        var entries = (await _db.PointsLog
                .Where(e => e.UserId == userId)
                .ToListAsync(ct))
            .OrderByDescending(e => e.CreatedAt)
            .Select(PointsLogEntryDto.FromEntity)
            .ToList();

        return Result.Success(entries);
    }

    public async Task<Result<UserDto>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync([id], ct);
        return user is null
            ? Result.Fail<UserDto>(Error.NotFound("User not found."))
            : Result.Success(UserDto.FromEntity(user));
    }

    public async Task<Result<UserDto>> CreateAsync(CreateUserRequest req, CancellationToken ct = default)
    {
        if (Validators.Username(req.Username) is { } usernameError)
            return Result.Fail<UserDto>(usernameError);
        if (Validators.DisplayName(req.DisplayName) is { } displayNameError)
            return Result.Fail<UserDto>(displayNameError);
        if (Validators.Password(req.Password) is { } passwordError)
            return Result.Fail<UserDto>(passwordError);

        var username = req.Username.Trim();
        if (await _db.Users.AnyAsync(u => u.Username == username, ct))
            return Result.Fail<UserDto>(Error.Conflict("Username is already taken."));

        var user = new User
        {
            Username = username,
            DisplayName = req.DisplayName.Trim(),
            AvatarColor = string.IsNullOrWhiteSpace(req.AvatarColor) ? DefaultAvatarColor : req.AvatarColor!,
            Role = req.Role,
            PasswordHash = _hasher.Hash(req.Password)
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        return Result.Success(UserDto.FromEntity(user));
    }

    public async Task<Result<UserDto>> UpdateAsync(Guid id, UpdateUserRequest req, CancellationToken ct = default)
    {
        if (Validators.DisplayName(req.DisplayName) is { } displayNameError)
            return Result.Fail<UserDto>(displayNameError);

        var user = await _db.Users.FindAsync([id], ct);
        if (user is null)
            return Result.Fail<UserDto>(Error.NotFound("User not found."));

        // Guard against demoting the only remaining admin.
        if (user.Role == UserRole.Admin && req.Role != UserRole.Admin && await IsLastAdminAsync(ct))
            return Result.Fail<UserDto>(Error.Conflict("Cannot demote the last admin."));

        user.DisplayName = req.DisplayName.Trim();
        if (!string.IsNullOrWhiteSpace(req.AvatarColor))
            user.AvatarColor = req.AvatarColor;
        user.Role = req.Role;

        await _db.SaveChangesAsync(ct);
        return Result.Success(UserDto.FromEntity(user));
    }

    // Self-service: a member may change their own avatar color (but not role or display name).
    public async Task<Result<UserDto>> UpdateProfileAsync(Guid id, UpdateProfileRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.AvatarColor))
            return Result.Fail<UserDto>(Error.Validation("Avatar color is required."));

        var user = await _db.Users.FindAsync([id], ct);
        if (user is null)
            return Result.Fail<UserDto>(Error.NotFound("User not found."));

        user.AvatarColor = req.AvatarColor.Trim();
        await _db.SaveChangesAsync(ct);
        return Result.Success(UserDto.FromEntity(user));
    }

    public async Task<Result> DeleteAsync(Guid id, Guid actingUserId, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync([id], ct);
        if (user is null)
            return Result.Fail(Error.NotFound("User not found."));

        if (id == actingUserId)
            return Result.Fail(Error.Conflict("You cannot delete your own account."));

        if (user.Role == UserRole.Admin && await IsLastAdminAsync(ct))
            return Result.Fail(Error.Conflict("Cannot delete the last admin."));

        // Phase 2 extension point: once chores/completions exist, the spec requires the
        // admin to pick a replacement assignee for this user's currently-assigned chores
        // and to wipe their completion history (reversing awarded points) before deletion.
        _db.Users.Remove(user); // refresh tokens cascade-delete
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<Result<UserDto>> AdjustPointsAsync(Guid id, AdjustPointsRequest req, CancellationToken ct = default)
    {
        if (req.Delta == 0)
            return Result.Fail<UserDto>(Error.Validation("Delta must be non-zero."));

        var user = await _db.Users.FindAsync([id], ct);
        if (user is null)
            return Result.Fail<UserDto>(Error.NotFound("User not found."));

        _db.PointsLog.Add(new PointsLogEntry
        {
            UserId = id,
            Delta = req.Delta,
            Type = PointsLogType.Adjustment,
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
        });
        user.Points += req.Delta;

        await _db.SaveChangesAsync(ct);
        return Result.Success(UserDto.FromEntity(user));
    }

    public async Task<Result> SetPasswordAsync(Guid id, string newPassword, CancellationToken ct = default)
    {
        if (Validators.Password(newPassword) is { } passwordError)
            return Result.Fail(passwordError);

        var user = await _db.Users.FindAsync([id], ct);
        if (user is null)
            return Result.Fail(Error.NotFound("User not found."));

        user.PasswordHash = _hasher.Hash(newPassword);

        // Revoke the user's active sessions so the old password stops working everywhere.
        var active = await _db.RefreshTokens
            .Where(t => t.UserId == id && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var token in active)
            token.RevokedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<bool> IsLastAdminAsync(CancellationToken ct)
        => await _db.Users.CountAsync(u => u.Role == UserRole.Admin, ct) <= 1;
}
