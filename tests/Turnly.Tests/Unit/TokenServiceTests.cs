using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using Turnly.Core.Auth;
using Turnly.Core.Entities;
using Turnly.Core.Enums;

namespace Turnly.Tests.Unit;

public class TokenServiceTests
{
    private readonly TokenService _service = new(Options.Create(new JwtOptions
    {
        Secret = "unit-test-secret-key-long-enough-1234567890",
        Issuer = "Turnly",
        Audience = "Turnly",
        AccessTokenMinutes = 15,
        RefreshTokenDays = 180
    }));

    private static User SampleUser() => new()
    {
        Username = "admin",
        DisplayName = "Admin",
        Role = UserRole.Admin
    };

    [Fact]
    public void AccessToken_carries_sub_username_and_role_claims()
    {
        var user = SampleUser();

        var access = _service.CreateAccessToken(user);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(access.Token);
        Assert.Equal(user.Id.ToString(), jwt.Claims.Single(c => c.Type == "sub").Value);
        Assert.Equal("admin", jwt.Claims.Single(c => c.Type == "username").Value);
        Assert.Equal("Admin", jwt.Claims.Single(c => c.Type == TokenService.RoleClaimType).Value);
        Assert.True(access.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public void RefreshToken_raw_value_is_unique_per_call()
    {
        var first = _service.CreateRefreshToken(Guid.NewGuid());
        var second = _service.CreateRefreshToken(Guid.NewGuid());
        Assert.NotEqual(first.RawToken, second.RawToken);
    }

    [Fact]
    public void RefreshToken_stores_hash_not_raw_and_sets_expiry()
    {
        var userId = Guid.NewGuid();

        var (raw, entity) = _service.CreateRefreshToken(userId);

        Assert.Equal(userId, entity.UserId);
        Assert.NotEqual(raw, entity.TokenHash);
        Assert.Equal(_service.HashRefreshToken(raw), entity.TokenHash);
        Assert.True(entity.ExpiresAt > DateTimeOffset.UtcNow.AddDays(179));
        Assert.True(entity.IsActive);
    }

    [Fact]
    public void HashRefreshToken_is_deterministic()
        => Assert.Equal(_service.HashRefreshToken("abc"), _service.HashRefreshToken("abc"));
}
