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
        => await _db.Users
            .OrderBy(u => u.DisplayName)
            .Select(u => new UserDto(u.Id, u.Username, u.DisplayName, u.AvatarColor, u.Role, u.Points, u.CreatedAt))
            .ToListAsync(ct);

    public async Task<Result<List<PointsLogEntryDto>>> GetPointsLogAsync(Guid userId, CancellationToken ct = default)
    {
        if (!await _db.Users.AnyAsync(u => u.Id == userId, ct))
            return Result.Fail<List<PointsLogEntryDto>>(Error.NotFound("User not found."));

        // Order client-side: SQLite can't ORDER BY DateTimeOffset.
        var entries = (await _db.PointsLog
                .Where(e => e.UserId == userId)
                .ToListAsync(ct))
            .OrderByDescending(e => e.CreatedAt)
            .Select(e => new PointsLogEntryDto(e.Id, e.Delta, e.Type, e.Description, e.ChoreCompletionId, e.CreatedAt))
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
