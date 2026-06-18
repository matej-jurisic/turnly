using Microsoft.EntityFrameworkCore;
using Turnly.Core.Auth;
using Turnly.Core.Common;
using Turnly.Core.Data;
using Turnly.Core.Dtos;
using Turnly.Core.Entities;

namespace Turnly.Core.Services;

public class AuthService
{
    private readonly TurnlyDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly ITokenService _tokens;

    public AuthService(TurnlyDbContext db, IPasswordHasher hasher, ITokenService tokens)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
    }

    public async Task<Result<AuthResult>> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var normalized = username?.Trim() ?? string.Empty;
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == normalized, ct);
        if (user is null || !_hasher.Verify(user.PasswordHash, password ?? string.Empty))
            return Result.Fail<AuthResult>(Error.Unauthorized("Invalid username or password."));

        return Result.Success(await IssueAsync(user, ct));
    }

    public async Task<Result<AuthResult>> RefreshAsync(string? rawRefreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
            return Result.Fail<AuthResult>(Error.Unauthorized("Missing refresh token."));

        var hash = _tokens.HashRefreshToken(rawRefreshToken);
        var token = await _db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (token is null || !token.IsActive || token.User is null)
            return Result.Fail<AuthResult>(Error.Unauthorized("Invalid or expired refresh token."));

        // Rotate: revoke the presented token and link it to its replacement.
        token.RevokedAt = DateTimeOffset.UtcNow;
        return Result.Success(await IssueAsync(token.User, ct, rotatedFrom: token));
    }

    public async Task LogoutAsync(string? rawRefreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
            return;

        var hash = _tokens.HashRefreshToken(rawRefreshToken);
        var token = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is { RevokedAt: null })
        {
            token.RevokedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<Result> ChangePasswordAsync(Guid userId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        if (Validators.Password(newPassword) is { } error)
            return Result.Fail(error);

        var user = await _db.Users.FindAsync([userId], ct);
        if (user is null)
            return Result.Fail(Error.NotFound("User not found."));

        if (!_hasher.Verify(user.PasswordHash, currentPassword ?? string.Empty))
            return Result.Fail(Error.Unauthorized("Current password is incorrect."));

        user.PasswordHash = _hasher.Hash(newPassword);
        await RevokeAllAsync(userId, ct);
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<AuthResult> IssueAsync(User user, CancellationToken ct, RefreshToken? rotatedFrom = null)
    {
        var access = _tokens.CreateAccessToken(user);
        var (raw, entity) = _tokens.CreateRefreshToken(user.Id);

        _db.RefreshTokens.Add(entity);
        if (rotatedFrom is not null)
            rotatedFrom.ReplacedByTokenId = entity.Id;

        await _db.SaveChangesAsync(ct);

        return new AuthResult(
            access.Token,
            access.ExpiresAt,
            raw,
            entity.ExpiresAt,
            UserDto.FromEntity(user));
    }

    private async Task RevokeAllAsync(Guid userId, CancellationToken ct)
    {
        var active = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);
        foreach (var token in active)
            token.RevokedAt = DateTimeOffset.UtcNow;
    }
}
