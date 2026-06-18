using System.Net;
using Turnly.Api.Endpoints;
using Turnly.Core.Dtos;
using Turnly.Core.Enums;

namespace Turnly.Tests.Integration;

public class UserManagementTests : IDisposable
{
    private readonly TurnlyApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<(HttpClient AdminClient, AuthResponse Admin)> AdminClientAsync()
    {
        var client = _factory.CreateClient();
        var admin = await client.SetupAdminAsync();
        client.UseBearer(admin.AccessToken);
        return (client, admin);
    }

    private static CreateUserRequest Member(string username = "kid") =>
        new(username, "Kid", "kidpass1", UserRole.Member, "#22c55e");

    [Fact]
    public async Task Admin_can_create_and_list_users()
    {
        var (admin, _) = await AdminClientAsync();

        var create = await admin.PostJsonAsync("/api/users", Member());
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var users = await (await admin.GetAsync("/api/users")).ReadAsync<List<UserDto>>();
        Assert.Equal(2, users.Count);
        Assert.Contains(users, u => u is { Username: "kid", Role: UserRole.Member });
    }

    [Fact]
    public async Task Member_cannot_access_admin_endpoints()
    {
        var (admin, _) = await AdminClientAsync();
        await admin.PostJsonAsync("/api/users", Member());

        var memberClient = _factory.CreateClient();
        var login = await (await memberClient.PostJsonAsync("/api/auth/login",
            new LoginRequest("kid", "kidpass1"))).ReadAsync<AuthResponse>();
        memberClient.UseBearer(login.AccessToken);

        var response = await memberClient.GetAsync("/api/users");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task User_can_change_own_password_with_correct_current_password()
    {
        var (admin, _) = await AdminClientAsync();

        var wrong = await admin.PostJsonAsync("/api/auth/change-password",
            new ChangePasswordRequest("not-the-password", "new-password1"));
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        var ok = await admin.PostJsonAsync("/api/auth/change-password",
            new ChangePasswordRequest("password123", "new-password1"));
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

        var relogin = await _factory.CreateClient().PostJsonAsync("/api/auth/login",
            new LoginRequest("admin", "new-password1"));
        Assert.Equal(HttpStatusCode.OK, relogin.StatusCode);
    }

    [Fact]
    public async Task Admin_can_set_another_users_password()
    {
        var (admin, _) = await AdminClientAsync();
        var member = await (await admin.PostJsonAsync("/api/users", Member())).ReadAsync<UserDto>();

        var setPassword = await admin.PostJsonAsync($"/api/users/{member.Id}/password",
            new SetPasswordRequest("reset-pass-1"));
        Assert.Equal(HttpStatusCode.NoContent, setPassword.StatusCode);

        var relogin = await _factory.CreateClient().PostJsonAsync("/api/auth/login",
            new LoginRequest("kid", "reset-pass-1"));
        Assert.Equal(HttpStatusCode.OK, relogin.StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_delete_their_own_account()
    {
        var (admin, auth) = await AdminClientAsync();

        var response = await admin.DeleteAsync($"/api/users/{auth.User.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_delete_a_member()
    {
        var (admin, _) = await AdminClientAsync();
        var member = await (await admin.PostJsonAsync("/api/users", Member())).ReadAsync<UserDto>();

        var response = await admin.DeleteAsync($"/api/users/{member.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
