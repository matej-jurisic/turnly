using Turnly.Core.Entities;

namespace Turnly.Core.Auth;

public record AccessToken(string Token, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    AccessToken CreateAccessToken(User user);

    /// <summary>
    /// Creates a refresh token: returns the raw value (sent to the client once) and the
    /// entity (storing only the hash) to persist.
    /// </summary>
    (string RawToken, RefreshToken Entity) CreateRefreshToken(Guid userId);

    string HashRefreshToken(string rawToken);
}
