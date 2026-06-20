using System.Net;
using Turnly.Api.Endpoints;
using Turnly.Core.Dtos;
using Turnly.Core.Enums;

namespace Turnly.Tests.Integration;

public class AwardManagementTests : IDisposable
{
    private readonly TurnlyApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private async Task<(HttpClient Admin, AuthResponse AdminAuth)> AdminClientAsync()
    {
        var client = _factory.CreateClient();
        var admin = await client.SetupAdminAsync();
        client.UseBearer(admin.AccessToken);
        return (client, admin);
    }

    private static CreateAwardRequest NewAward(string name = "Ice cream", int cost = 30) =>
        new(name, "A treat", "🍦", cost);

    /// <summary>Completes a chore worth <paramref name="points"/> so the member has a balance to spend.</summary>
    private async Task EarnPointsAsync(HttpClient admin, HttpClient member, AuthResponse memberAuth, int points)
    {
        var chore = await (await admin.PostJsonAsync("/api/chores", new CreateChoreRequest(
            "Dishes", null, "🍽️", points, RepeatType.Daily, null, null, null, null, null, null, 1, false,
            AssignmentStrategy.KeepLastAssigned, SchedulingPreference.FromScheduledDate, null,
            new DateTimeOffset(2026, 6, 17, 9, 0, 0, TimeSpan.Zero),
            [memberAuth.User.Id], memberAuth.User.Id, null))).ReadAsync<ChoreDto>();
        await member.PostJsonAsync($"/api/chores/{chore.Id}/complete", new CompleteChoreRequest(null));
    }

    [Fact]
    public async Task Admin_can_create_award_but_member_cannot()
    {
        var (admin, _) = await AdminClientAsync();
        await admin.PostJsonAsync("/api/users",
            new CreateUserRequest("kid", "Kid", "kidpass1", UserRole.Member, null));
        var member = _factory.CreateClient();
        await member.LoginAsync("kid", "kidpass1");

        var created = await (await admin.PostJsonAsync("/api/awards", NewAward())).ReadAsync<AwardDto>();
        Assert.Equal("Ice cream", created.Name);

        var forbidden = await member.PostJsonAsync("/api/awards", NewAward());
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        // Both can list awards.
        var listed = await (await member.GetAsync("/api/awards")).ReadAsync<List<AwardDto>>();
        Assert.Single(listed);
    }

    [Fact]
    public async Task Member_redeems_and_admin_fulfills()
    {
        var (admin, _) = await AdminClientAsync();
        await admin.PostJsonAsync("/api/users",
            new CreateUserRequest("kid", "Kid", "kidpass1", UserRole.Member, null));
        var member = _factory.CreateClient();
        var memberAuth = await member.LoginAsync("kid", "kidpass1");

        await EarnPointsAsync(admin, member, memberAuth, 50);
        var award = await (await admin.PostJsonAsync("/api/awards", NewAward(cost: 30))).ReadAsync<AwardDto>();

        var redemption = await (await member.PostJsonAsync($"/api/awards/{award.Id}/redeem", new { }))
            .ReadAsync<RedemptionDto>();
        Assert.Equal(RedemptionStatus.Pending, redemption.Status);
        Assert.Equal(30, redemption.PointsSpent);

        // Balance dropped 50 -> 20.
        var me = await (await member.GetAsync("/api/auth/me")).ReadAsync<UserDto>();
        Assert.Equal(20, me.Points);

        var fulfilled = await (await admin.PostJsonAsync($"/api/redemptions/{redemption.Id}/fulfill", new { }))
            .ReadAsync<RedemptionDto>();
        Assert.Equal(RedemptionStatus.Fulfilled, fulfilled.Status);
    }

    [Fact]
    public async Task Member_cannot_redeem_without_enough_points()
    {
        var (admin, _) = await AdminClientAsync();
        await admin.PostJsonAsync("/api/users",
            new CreateUserRequest("kid", "Kid", "kidpass1", UserRole.Member, null));
        var member = _factory.CreateClient();
        await member.LoginAsync("kid", "kidpass1");

        var award = await (await admin.PostJsonAsync("/api/awards", NewAward(cost: 30))).ReadAsync<AwardDto>();

        var response = await member.PostJsonAsync($"/api/awards/{award.Id}/redeem", new { });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Members_only_see_their_own_redemptions()
    {
        var (admin, _) = await AdminClientAsync();
        await admin.PostJsonAsync("/api/users",
            new CreateUserRequest("kid", "Kid", "kidpass1", UserRole.Member, null));
        await admin.PostJsonAsync("/api/users",
            new CreateUserRequest("sib", "Sib", "sibpass1", UserRole.Member, null));
        var kid = _factory.CreateClient();
        var kidAuth = await kid.LoginAsync("kid", "kidpass1");
        var sib = _factory.CreateClient();
        var sibAuth = await sib.LoginAsync("sib", "sibpass1");

        await EarnPointsAsync(admin, kid, kidAuth, 50);
        await EarnPointsAsync(admin, sib, sibAuth, 50);
        var award = await (await admin.PostJsonAsync("/api/awards", NewAward(cost: 10))).ReadAsync<AwardDto>();
        await kid.PostJsonAsync($"/api/awards/{award.Id}/redeem", new { });
        await sib.PostJsonAsync($"/api/awards/{award.Id}/redeem", new { });

        var kidList = await (await kid.GetAsync("/api/redemptions")).ReadAsync<List<RedemptionDto>>();
        Assert.Single(kidList);
        Assert.Equal(kidAuth.User.Id, kidList[0].User.Id);

        var adminList = await (await admin.GetAsync("/api/redemptions")).ReadAsync<List<RedemptionDto>>();
        Assert.Equal(2, adminList.Count);
    }

    [Fact]
    public async Task Admin_cancel_refunds_points()
    {
        var (admin, _) = await AdminClientAsync();
        await admin.PostJsonAsync("/api/users",
            new CreateUserRequest("kid", "Kid", "kidpass1", UserRole.Member, null));
        var member = _factory.CreateClient();
        var memberAuth = await member.LoginAsync("kid", "kidpass1");

        await EarnPointsAsync(admin, member, memberAuth, 50);
        var award = await (await admin.PostJsonAsync("/api/awards", NewAward(cost: 30))).ReadAsync<AwardDto>();
        var redemption = await (await member.PostJsonAsync($"/api/awards/{award.Id}/redeem", new { }))
            .ReadAsync<RedemptionDto>();

        var cancel = await admin.PostJsonAsync($"/api/redemptions/{redemption.Id}/cancel", new { });
        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);

        var me = await (await member.GetAsync("/api/auth/me")).ReadAsync<UserDto>();
        Assert.Equal(50, me.Points); // refunded back to full
    }
}
