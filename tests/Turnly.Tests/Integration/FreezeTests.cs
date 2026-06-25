using System.Net;
using Turnly.Api.Endpoints;
using Turnly.Core.Dtos;
using Turnly.Core.Enums;

namespace Turnly.Tests.Integration;

public class FreezeTests : IDisposable
{
    private readonly TurnlyApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static readonly DateTimeOffset Start = new(2026, 6, 17, 9, 0, 0, TimeSpan.Zero);

    private async Task<(HttpClient Admin, AuthResponse AdminAuth)> AdminClientAsync()
    {
        var client = _factory.CreateClient();
        var admin = await client.SetupAdminAsync();
        client.UseBearer(admin.AccessToken);
        return (client, admin);
    }

    private static CreateChoreRequest DailyChore(Guid assignee) =>
        new("Dishes", null, "🍽️", 10, RepeatType.Daily, null, null, null,
            null, null, null, null, 1, false,
            AssignmentStrategy.KeepLastAssigned, SchedulingPreference.FromScheduledDate, null, false, null,
            Start, [assignee], assignee, null);

    // ── Per-chore freeze flow ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Admin_can_freeze_chore_and_complete_is_blocked_then_unfreeze_restores()
    {
        var (admin, adminAuth) = await AdminClientAsync();

        // Create a daily chore
        var chore = await (await admin.PostJsonAsync("/api/chores", DailyChore(adminAuth.User.Id)))
            .ReadAsync<ChoreDto>();

        // Freeze it
        var frozen = await (await admin.PostJsonAsync($"/api/chores/{chore.Id}/freeze", new { }))
            .ReadAsync<ChoreDto>();
        Assert.True(frozen.IsFrozen);

        // Complete should now fail with 400
        var complete = await admin.PostJsonAsync($"/api/chores/{chore.Id}/complete",
            new CompleteChoreRequest(null));
        Assert.Equal(HttpStatusCode.BadRequest, complete.StatusCode);

        // Unfreeze it
        var unfrozen = await (await admin.PostJsonAsync($"/api/chores/{chore.Id}/unfreeze", new { }))
            .ReadAsync<ChoreDto>();
        Assert.False(unfrozen.IsFrozen);
        // DueAt should have advanced (Start is in the past)
        Assert.True(unfrozen.DueAt >= DateTimeOffset.UtcNow);

        // Complete now succeeds
        var completeOk = await admin.PostJsonAsync($"/api/chores/{chore.Id}/complete",
            new CompleteChoreRequest(null));
        Assert.Equal(HttpStatusCode.OK, completeOk.StatusCode);
    }

    [Fact]
    public async Task Member_cannot_freeze_or_unfreeze_a_chore()
    {
        var (admin, adminAuth) = await AdminClientAsync();
        await admin.PostJsonAsync("/api/users",
            new CreateUserRequest("kid", "Kid", "kidpass1", UserRole.Member, null));
        var chore = await (await admin.PostJsonAsync("/api/chores", DailyChore(adminAuth.User.Id)))
            .ReadAsync<ChoreDto>();

        var member = _factory.CreateClient();
        await member.LoginAsync("kid", "kidpass1");

        var freezeResp = await member.PostJsonAsync($"/api/chores/{chore.Id}/freeze", new { });
        Assert.Equal(HttpStatusCode.Forbidden, freezeResp.StatusCode);

        var unfreezeResp = await member.PostJsonAsync($"/api/chores/{chore.Id}/unfreeze", new { });
        Assert.Equal(HttpStatusCode.Forbidden, unfreezeResp.StatusCode);
    }

    [Fact]
    public async Task Frozen_chore_appears_with_isFrozen_true_in_list()
    {
        var (admin, adminAuth) = await AdminClientAsync();
        var chore = await (await admin.PostJsonAsync("/api/chores", DailyChore(adminAuth.User.Id)))
            .ReadAsync<ChoreDto>();
        await admin.PostJsonAsync($"/api/chores/{chore.Id}/freeze", new { });

        var list = await (await admin.GetAsync("/api/chores")).ReadAsync<ChoreDto[]>();

        var found = Assert.Single(list, c => c.Id == chore.Id);
        Assert.True(found.IsFrozen);
    }

    // ── Per-user freeze flow ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Admin_can_preview_freeze_then_freeze_user_and_chore_gets_reassigned()
    {
        var (admin, adminAuth) = await AdminClientAsync();
        var memberResp = await admin.PostJsonAsync("/api/users",
            new CreateUserRequest("kid", "Kid", "kidpass1", UserRole.Member, null));
        var member = await memberResp.ReadAsync<UserDto>();

        // Chore assigned to admin (both users are assignees)
        var chore = await (await admin.PostJsonAsync("/api/chores",
            new CreateChoreRequest("Dishes", null, "🍽️", 10, RepeatType.Daily, null, null, null,
                null, null, null, null, 1, false,
                AssignmentStrategy.KeepLastAssigned, SchedulingPreference.FromScheduledDate, null, false, null,
                Start, [adminAuth.User.Id, member.Id], adminAuth.User.Id, null)))
            .ReadAsync<ChoreDto>();

        // Preview should show the chore will be reassigned to member
        var preview = await (await admin.GetAsync($"/api/users/{adminAuth.User.Id}/freeze-preview"))
            .ReadAsync<UserFreezePreviewDto>();
        Assert.Single(preview.Reassignments);
        Assert.Equal(chore.Id, preview.Reassignments[0].ChoreId);
        Assert.Equal(member.Id, preview.Reassignments[0].NewAssigneeId);
        Assert.Empty(preview.Unassignable);

        // Freeze admin
        var frozenUser = await (await admin.PostJsonAsync($"/api/users/{adminAuth.User.Id}/freeze", new { }))
            .ReadAsync<UserDto>();
        Assert.True(frozenUser.IsFrozen);

        // Chore should now have member as current assignee
        var updatedChore = await (await admin.GetAsync($"/api/chores/{chore.Id}")).ReadAsync<ChoreDto>();
        Assert.Equal(member.Id, updatedChore.CurrentAssignee?.Id);

        // Unfreeze admin
        var unfrozen = await (await admin.PostJsonAsync($"/api/users/{adminAuth.User.Id}/unfreeze", new { }))
            .ReadAsync<UserDto>();
        Assert.False(unfrozen.IsFrozen);
    }

    [Fact]
    public async Task Member_cannot_freeze_or_unfreeze_a_user()
    {
        var (admin, adminAuth) = await AdminClientAsync();
        await admin.PostJsonAsync("/api/users",
            new CreateUserRequest("kid", "Kid", "kidpass1", UserRole.Member, null));

        var member = _factory.CreateClient();
        await member.LoginAsync("kid", "kidpass1");

        var freezeResp = await member.PostJsonAsync($"/api/users/{adminAuth.User.Id}/freeze", new { });
        Assert.Equal(HttpStatusCode.Forbidden, freezeResp.StatusCode);

        var unfreezeResp = await member.PostJsonAsync($"/api/users/{adminAuth.User.Id}/unfreeze", new { });
        Assert.Equal(HttpStatusCode.Forbidden, unfreezeResp.StatusCode);
    }
}
