using Microsoft.EntityFrameworkCore;
using Turnly.Core.Auth;
using Turnly.Core.Common;
using Turnly.Core.Data;
using Turnly.Core.Dtos;
using Turnly.Core.Entities;
using Turnly.Core.Enums;

namespace Turnly.Core.Services;

/// <summary>
/// Handles first-run bootstrapping: a single household per instance starts with no users,
/// and the first admin is created through a one-time setup flow.
/// </summary>
public class SetupService
{
    private readonly TurnlyDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly AuthService _auth;

    public SetupService(TurnlyDbContext db, IPasswordHasher hasher, AuthService auth)
    {
        _db = db;
        _hasher = hasher;
        _auth = auth;
    }

    public async Task<bool> NeedsSetupAsync(CancellationToken ct = default)
        => !await _db.Users.AnyAsync(ct);

    public async Task<Result<AuthResult>> CreateFirstAdminAsync(SetupRequest req, CancellationToken ct = default)
    {
        if (await _db.Users.AnyAsync(ct))
            return Result.Fail<AuthResult>(Error.Conflict("Setup has already been completed."));

        if (Validators.Username(req.Username) is { } usernameError)
            return Result.Fail<AuthResult>(usernameError);
        if (Validators.DisplayName(req.DisplayName) is { } displayNameError)
            return Result.Fail<AuthResult>(displayNameError);
        if (Validators.Password(req.Password) is { } passwordError)
            return Result.Fail<AuthResult>(passwordError);

        var user = new User
        {
            Username = req.Username.Trim(),
            DisplayName = req.DisplayName.Trim(),
            AvatarColor = string.IsNullOrWhiteSpace(req.AvatarColor) ? "#6366f1" : req.AvatarColor!,
            Role = UserRole.Admin,
            PasswordHash = _hasher.Hash(req.Password)
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        // Log the new admin straight in so the client receives a token pair.
        return await _auth.LoginAsync(req.Username, req.Password, ct);
    }
}
