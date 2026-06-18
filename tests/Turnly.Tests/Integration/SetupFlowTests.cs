using System.Net;
using System.Net.Http.Json;
using Turnly.Api.Endpoints;
using Turnly.Core.Dtos;
using Turnly.Core.Enums;

namespace Turnly.Tests.Integration;

public class SetupFlowTests : IClassFixture<TurnlyApiFactory>
{
    private readonly TurnlyApiFactory _factory;

    public SetupFlowTests(TurnlyApiFactory factory) => _factory = factory;

    private record StatusResponse(bool NeedsSetup);

    [Fact]
    public async Task Full_setup_flow_locks_after_first_admin()
    {
        var client = _factory.CreateClient();

        // 1) Fresh instance needs setup.
        var statusBefore = await (await client.GetAsync("/api/setup/status")).ReadAsync<StatusResponse>();
        Assert.True(statusBefore.NeedsSetup);

        // 2) Create the first admin.
        var setupResponse = await client.PostJsonAsync("/api/setup",
            new SetupRequest("admin", "Admin", "password123", "#ef4444"));
        Assert.Equal(HttpStatusCode.OK, setupResponse.StatusCode);
        var auth = await setupResponse.ReadAsync<AuthResponse>();
        Assert.Equal(UserRole.Admin, auth.User.Role);
        Assert.Contains("turnly_refresh", string.Join(';', setupResponse.Headers.GetValues("Set-Cookie")));

        // 3) Setup is now complete.
        var statusAfter = await (await client.GetAsync("/api/setup/status")).ReadAsync<StatusResponse>();
        Assert.False(statusAfter.NeedsSetup);

        // 4) A second setup attempt is rejected.
        var second = await client.PostJsonAsync("/api/setup",
            new SetupRequest("other", "Other", "password123", null));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }
}
