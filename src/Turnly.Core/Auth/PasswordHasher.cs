using Microsoft.AspNetCore.Identity;

namespace Turnly.Core.Auth;

/// <summary>
/// Wraps ASP.NET Core Identity's PBKDF2 <see cref="PasswordHasher{TUser}"/> behind a
/// minimal interface. The hasher ignores the user instance, so a shared dummy is used.
/// </summary>
public class PasswordHasher : IPasswordHasher
{
    private static readonly object Subject = new();
    private readonly PasswordHasher<object> _inner = new();

    public string Hash(string password) => _inner.HashPassword(Subject, password);

    public bool Verify(string hash, string password)
        => _inner.VerifyHashedPassword(Subject, hash, password) != PasswordVerificationResult.Failed;
}
