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
        new(name, null, "🍽️", 10, RepeatType.Daily, null, null, null, null, null, null, null, null,
            AssignmentStrategy.KeepLastAssigned, SchedulingPreference.FromScheduledDate,
            Start, [assignee], assignee, tags);

    [Fact]
    public async Task GetHistory_returns_all_completions_when_no_filters_applied()
    {
        var (admin, adminAuth) = await AdminClientAsync();

        var chore = await (await admin.PostJsonAsync("/api/chores", NewChore(adminAuth.User.Id)))
            .ReadAsync<ChoreDto>();
        await admin.PostJsonAsync($"/api/chores/{chore.Id}/complete", new CompleteChoreRequest(null));
        await admin.PostJsonAsync($"/api/chores/{chore.Id}/complete", new CompleteChoreRequest("second"));

        var history = await (await admin.GetAsync("/api/history")).ReadAsync<List<ChoreCompletionDto>>();

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
            new CreateChoreRequest("Dishes", null, "🍽️", 10, RepeatType.Daily, null, null, null, null, null, null, null, null,
                AssignmentStrategy.KeepLastAssigned, SchedulingPreference.FromScheduledDate,
                Start, [adminAuth.User.Id, memberAuth.User.Id], adminAuth.User.Id, null)))
            .ReadAsync<ChoreDto>();

        await admin.PostJsonAsync($"/api/chores/{chore.Id}/complete", new CompleteChoreRequest(null));
        await member.PostJsonAsync($"/api/chores/{chore.Id}/complete", new CompleteChoreRequest(null));

        var adminHistory = await (await admin.GetAsync($"/api/history?userId={adminAuth.User.Id}"))
            .ReadAsync<List<ChoreCompletionDto>>();
        var memberHistory = await (await admin.GetAsync($"/api/history?userId={memberAuth.User.Id}"))
            .ReadAsync<List<ChoreCompletionDto>>();

        Assert.All(adminHistory, c => Assert.Equal(adminAuth.User.Id, c.CompletedBy.Id));
        Assert.All(memberHistory, c => Assert.Equal(memberAuth.User.Id, c.CompletedBy.Id));
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
            .ReadAsync<List<ChoreCompletionDto>>();

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
            .ReadAsync<List<ChoreCompletionDto>>();

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

        var history = await (await admin.GetAsync("/api/history")).ReadAsync<List<ChoreCompletionDto>>();

        Assert.True(history[0].CompletedAt >= history[1].CompletedAt);
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
    public async Task GetHistory_unauthenticated_returns_401()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/history");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
