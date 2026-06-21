using System.Net;
using Turnly.Api.Endpoints;
using Turnly.Core.Dtos;
using Turnly.Core.Enums;

namespace Turnly.Tests.Integration;

public class HistoryTests : IDisposable
{
    private readonly TurnlyApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static readonly DateTimeOffset Start = new(2026, 6, 17, 9, 0, 0, TimeSpan.Zero);

    private async Task<(HttpClient Client, AuthResponse Auth)> AdminClientAsync()
    {
        var client = _factory.CreateClient();
        var auth = await client.SetupAdminAsync();
        client.UseBearer(auth.AccessToken);
        return (client, auth);
    }

    private static CreateChoreRequest NewChore(Guid assignee, string name = "Dishes", string[]? tags = null) =>
        new(name, null, "🍽️", 10, RepeatType.Daily, null, null, null, null, null, null, null, 1, false,
            AssignmentStrategy.KeepLastAssigned, SchedulingPreference.FromScheduledDate, null, false, null,
            Start, [assignee], assignee, tags);

    [Fact]
    public async Task GetHistory_returns_all_completions_when_no_filters_applied()
    {
        var (admin, adminAuth) = await AdminClientAsync();

        var chore = await (await admin.PostJsonAsync("/api/chores", NewChore(adminAuth.User.Id)))
            .ReadAsync<ChoreDto>();
        await admin.PostJsonAsync($"/api/chores/{chore.Id}/complete", new CompleteChoreRequest(null));
        await admin.PostJsonAsync($"/api/chores/{chore.Id}/complete", new CompleteChoreRequest("second"));

        var history = await (await admin.GetAsync("/api/history")).ReadAsync<List<ChoreHistoryEntryDto>>();

        Assert.Equal(2, history.Count);
        Assert.All(history, c => Assert.Equal(chore.Id, c.ChoreId));
    }

    [Fact]
    public async Task GetHistory_filters_by_completedBy_user()
    {
        var (admin, adminAuth) = await AdminClientAsync();
        await admin.PostJsonAsync("/api/users",
            new CreateUserRequest("kid", "Kid", "kidpass1", UserRole.Member, null));
        var member = _factory.CreateClient();
        var memberAuth = await member.LoginAsync("kid", "kidpass1");

        var chore = await (await admin.PostJsonAsync("/api/chores",
            new CreateChoreRequest("Dishes", null, "🍽️", 10, RepeatType.Daily, null, null, null, null, null, null, null, 1, false,
                AssignmentStrategy.KeepLastAssigned, SchedulingPreference.FromScheduledDate, null, false, null,
                Start, [adminAuth.User.Id, memberAuth.User.Id], adminAuth.User.Id, null)))
            .ReadAsync<ChoreDto>();

        await admin.PostJsonAsync($"/api/chores/{chore.Id}/complete", new CompleteChoreRequest(null));
        await member.PostJsonAsync($"/api/chores/{chore.Id}/complete", new CompleteChoreRequest(null));

        var adminHistory = await (await admin.GetAsync($"/api/history?userId={adminAuth.User.Id}"))
            .ReadAsync<List<ChoreHistoryEntryDto>>();
        var memberHistory = await (await admin.GetAsync($"/api/history?userId={memberAuth.User.Id}"))
            .ReadAsync<List<ChoreHistoryEntryDto>>();

        Assert.All(adminHistory, c => Assert.Equal(adminAuth.User.Id, c.Actor!.Id));
        Assert.All(memberHistory, c => Assert.Equal(memberAuth.User.Id, c.Actor!.Id));
    }

    [Fact]
    public async Task GetHistory_filters_by_choreId()
    {
        var (admin, adminAuth) = await AdminClientAsync();

        var chore1 = await (await admin.PostJsonAsync("/api/chores", NewChore(adminAuth.User.Id, "Dishes")))
            .ReadAsync<ChoreDto>();
        var chore2 = await (await admin.PostJsonAsync("/api/chores", NewChore(adminAuth.User.Id, "Vacuum")))
            .ReadAsync<ChoreDto>();

        await admin.PostJsonAsync($"/api/chores/{chore1.Id}/complete", new CompleteChoreRequest(null));
        await admin.PostJsonAsync($"/api/chores/{chore2.Id}/complete", new CompleteChoreRequest(null));

        var filtered = await (await admin.GetAsync($"/api/history?choreId={chore1.Id}"))
            .ReadAsync<List<ChoreHistoryEntryDto>>();

        Assert.Single(filtered);
        Assert.Equal(chore1.Id, filtered[0].ChoreId);
    }

    [Fact]
    public async Task GetHistory_filters_by_tag()
    {
        var (admin, adminAuth) = await AdminClientAsync();

        var tagged = await (await admin.PostJsonAsync("/api/chores", NewChore(adminAuth.User.Id, "Dishes", ["kitchen"])))
            .ReadAsync<ChoreDto>();
        var untagged = await (await admin.PostJsonAsync("/api/chores", NewChore(adminAuth.User.Id, "Vacuum")))
            .ReadAsync<ChoreDto>();

        await admin.PostJsonAsync($"/api/chores/{tagged.Id}/complete", new CompleteChoreRequest(null));
        await admin.PostJsonAsync($"/api/chores/{untagged.Id}/complete", new CompleteChoreRequest(null));

        var filtered = await (await admin.GetAsync("/api/history?tag=kitchen"))
            .ReadAsync<List<ChoreHistoryEntryDto>>();

        Assert.Single(filtered);
        Assert.Equal(tagged.Id, filtered[0].ChoreId);
    }

    [Fact]
    public async Task GetHistory_returns_completions_newest_first()
    {
        var (admin, adminAuth) = await AdminClientAsync();

        var chore = await (await admin.PostJsonAsync("/api/chores", NewChore(adminAuth.User.Id)))
            .ReadAsync<ChoreDto>();
        await admin.PostJsonAsync($"/api/chores/{chore.Id}/complete", new CompleteChoreRequest("first"));
        await admin.PostJsonAsync($"/api/chores/{chore.Id}/complete", new CompleteChoreRequest("second"));

        var history = await (await admin.GetAsync("/api/history")).ReadAsync<List<ChoreHistoryEntryDto>>();

        Assert.True(history[0].At >= history[1].At);
    }

    [Fact]
    public async Task GetStats_returns_eight_chart_weeks_and_per_user_counts()
    {
        var (admin, adminAuth) = await AdminClientAsync();

        var chore = await (await admin.PostJsonAsync("/api/chores", NewChore(adminAuth.User.Id)))
            .ReadAsync<ChoreDto>();
        await admin.PostJsonAsync($"/api/chores/{chore.Id}/complete", new CompleteChoreRequest(null));

        var stats = await (await admin.GetAsync("/api/stats")).ReadAsync<StatsDto>();

        Assert.Equal(8, stats.Chart.Count());

        var adminStats = stats.UserStats.Single(u => u.UserId == adminAuth.User.Id);
        Assert.Equal(1, adminStats.WeeklyCount);
        Assert.Equal(1, adminStats.MonthlyCount);
        Assert.Equal(1, adminStats.AllTimeCount);
    }

    [Fact]
    public async Task GetHistory_includes_reassignments_only_when_requested()
    {
        var (admin, adminAuth) = await AdminClientAsync();
        await admin.PostJsonAsync("/api/users",
            new CreateUserRequest("kid", "Kid", "kidpass1", UserRole.Member, null));
        var member = _factory.CreateClient();
        var memberAuth = await member.LoginAsync("kid", "kidpass1");

        var chore = await (await admin.PostJsonAsync("/api/chores",
            new CreateChoreRequest("Dishes", null, "🍽️", 10, RepeatType.Daily, null, null, null, null, null, null, null, 1, false,
                AssignmentStrategy.KeepLastAssigned, SchedulingPreference.FromScheduledDate, null, false, null,
                Start, [adminAuth.User.Id, memberAuth.User.Id], adminAuth.User.Id, null)))
            .ReadAsync<ChoreDto>();

        await admin.PostJsonAsync($"/api/chores/{chore.Id}/reassign",
            new ReassignChoreRequest(memberAuth.User.Id));

        var withoutReassignments = await (await admin.GetAsync("/api/history"))
            .ReadAsync<List<ChoreHistoryEntryDto>>();
        Assert.Empty(withoutReassignments);

        var withReassignments = await (await admin.GetAsync("/api/history?includeReassignments=true"))
            .ReadAsync<List<ChoreHistoryEntryDto>>();
        var entry = Assert.Single(withReassignments);
        Assert.Equal("reassignment", entry.Kind);
        Assert.Equal(adminAuth.User.Id, entry.Actor!.Id);
        Assert.Equal(adminAuth.User.Id, entry.FromAssignee!.Id);
        Assert.Equal(memberAuth.User.Id, entry.ToAssignee!.Id);
    }

    [Fact]
    public async Task GetHistory_unauthenticated_returns_401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/history");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
