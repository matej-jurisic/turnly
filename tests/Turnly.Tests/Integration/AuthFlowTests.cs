using System.Net;
using Turnly.Api.Endpoints;
using Turnly.Core.Dtos;

namespace Turnly.Tests.Integration;

public class AuthFlowTests : IDisposable
{
    private readonly TurnlyApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Login_me_refresh_logout_lifecycle()
    {
        await _factory.CreateClient().SetupAdminAsync();

        var client = _factory.CreateClient();

        // Login establishes the refresh cookie and returns an access token.
        var login = await client.PostJsonAsync("/api/auth/login", new LoginRequest("admin", "password123"));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var auth = await login.ReadAsync<AuthResponse>();

        // Access token authorizes /me.
        client.UseBearer(auth.AccessToken);
        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal("admin", (await me.ReadAsync<UserDto>()).Username);

        // Refresh (cookie-based) succeeds and rotates the token.
        var refresh = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);

        // Logout revokes the refresh token...
        var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        // ...so a further refresh is rejected.
        var refreshAfterLogout = await client.PostAsync("/api/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAfterLogout.StatusCode);
    }

    [Fact]
    public async Task Login_with_wrong_password_is_unauthorized()
    {
        await _factory.CreateClient().SetupAdminAsync();
        var client = _factory.CreateClient();

        var login = await client.PostJsonAsync("/api/auth/login", new LoginRequest("admin", "wrong-password"));

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Me_without_token_is_unauthorized()
    {
        await _factory.CreateClient().SetupAdminAsync();

        var response = await _factory.CreateClient().GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
