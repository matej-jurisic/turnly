using System.Net;
using Turnly.Api.Endpoints;
using Turnly.Core.Dtos;
using Turnly.Core.Enums;

namespace Turnly.Tests.Integration;

public class ChoreManagementTests : IDisposable
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

    private static CreateChoreRequest NewChore(Guid assignee, string[]? tags = null) =>
        new("Dishes", "Wash up", "🍽️", 10, RepeatType.Daily, null, null, null, null, null, null, null, 1, false,
            AssignmentStrategy.KeepLastAssigned, SchedulingPreference.FromScheduledDate, null,
            Start, [assignee], assignee, tags);

    [Fact]
    public async Task Admin_can_create_get_update_and_delete_a_chore()
    {
        var (admin, adminAuth) = await AdminClientAsync();

        var created = await (await admin.PostJsonAsync("/api/chores", NewChore(adminAuth.User.Id, ["kitchen"])))
            .ReadAsync<ChoreDto>();
        Assert.Equal("Dishes", created.Name);
        Assert.Equal(Start, created.DueAt);
        Assert.Contains("kitchen", created.Tags);

        var fetched = await (await admin.GetAsync($"/api/chores/{created.Id}")).ReadAsync<ChoreDto>();
        Assert.Equal(created.Id, fetched.Id);

        var update = NewChore(adminAuth.User.Id) with { Name = "Wash dishes", Points = 20 };
        var updated = await (await admin.PutJsonAsync($"/api/chores/{created.Id}", update)).ReadAsync<ChoreDto>();
        Assert.Equal("Wash dishes", updated.Name);
        Assert.Equal(20, updated.Points);

        var delete = await admin.DeleteAsync($"/api/chores/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
    }

    [Fact]
    public async Task Member_cannot_create_but_can_complete_a_chore()
    {
        var (admin, _) = await AdminClientAsync();
        await admin.PostJsonAsync("/api/users",
            new CreateUserRequest("kid", "Kid", "kidpass1", UserRole.Member, null));
        var member = _factory.CreateClient();
        var memberAuth = await member.LoginAsync("kid", "kidpass1");

        var chore = await (await admin.PostJsonAsync("/api/chores", NewChore(memberAuth.User.Id)))
            .ReadAsync<ChoreDto>();

        var forbidden = await member.PostJsonAsync("/api/chores", NewChore(memberAuth.User.Id));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        var completed = await (await member.PostJsonAsync($"/api/chores/{chore.Id}/complete",
            new CompleteChoreRequest("all done"))).ReadAsync<ChoreDto>();
        Assert.Equal(Start.AddDays(1), completed.DueAt);
        Assert.NotNull(completed.LastCompletion);
    }

    [Fact]
    public async Task Independent_chore_tracks_advance_per_assignee_and_personalise_to_viewer()
    {
        var (admin, _) = await AdminClientAsync();
        await admin.PostJsonAsync("/api/users", new CreateUserRequest("alice", "Alice", "alicepw1", UserRole.Member, null));
        await admin.PostJsonAsync("/api/users", new CreateUserRequest("bob", "Bob", "bobpw123", UserRole.Member, null));
        var alice = _factory.CreateClient();
        var aAuth = await alice.LoginAsync("alice", "alicepw1");
        var bob = _factory.CreateClient();
        var bAuth = await bob.LoginAsync("bob", "bobpw123");

        var req = new CreateChoreRequest("Dishes", null, "🍽️", 10, RepeatType.Weekly, null, null, null,
            null, null, null, null, 1, false, AssignmentStrategy.Independent,
            SchedulingPreference.FromScheduledDate, null, Start,
            [aAuth.User.Id, bAuth.User.Id], aAuth.User.Id, null, null, null,
            [new TrackInput(aAuth.User.Id, 1), new TrackInput(bAuth.User.Id, 1)]);

        var chore = await (await admin.PostJsonAsync("/api/chores", req)).ReadAsync<ChoreDto>();
        Assert.Null(chore.CurrentAssignee);
        Assert.Equal(2, chore.Tracks.Length);

        // Alice completes her share; only her track rolls to next week.
        await alice.PostJsonAsync($"/api/chores/{chore.Id}/complete", new CompleteChoreRequest(null));

        var aliceList = await (await alice.GetAsync("/api/chores")).ReadAsync<ChoreDto[]>();
        var bobList = await (await bob.GetAsync("/api/chores")).ReadAsync<ChoreDto[]>();
        Assert.Equal(Start.AddDays(7), aliceList.Single().DueAt); // personalised to Alice's advanced track
        Assert.Equal(Start, bobList.Single().DueAt);              // Bob's own track is untouched
    }

    [Fact]
    public async Task Completing_records_points_in_the_log_and_undo_reverses_it()
    {
        var (admin, _) = await AdminClientAsync();
        await admin.PostJsonAsync("/api/users",
            new CreateUserRequest("kid", "Kid", "kidpass1", UserRole.Member, null));
        var member = _factory.CreateClient();
        var memberAuth = await member.LoginAsync("kid", "kidpass1");

        var chore = await (await admin.PostJsonAsync("/api/chores", NewChore(memberAuth.User.Id)))
            .ReadAsync<ChoreDto>();
        var completed = await (await member.PostJsonAsync($"/api/chores/{chore.Id}/complete",
            new CompleteChoreRequest(null))).ReadAsync<ChoreDto>();

        var log = await (await member.GetAsync($"/api/users/{memberAuth.User.Id}/points-log"))
            .ReadAsync<List<PointsLogEntryDto>>();
        Assert.Single(log);
        Assert.Equal(10, log[0].Delta);

        var undo = await member.DeleteAsync($"/api/completions/{completed.LastCompletion!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, undo.StatusCode);

        var logAfter = await (await member.GetAsync($"/api/users/{memberAuth.User.Id}/points-log"))
            .ReadAsync<List<PointsLogEntryDto>>();
        Assert.Empty(logAfter);
    }

    [Fact]
    public async Task Member_cannot_read_another_users_points_log()
    {
        var (admin, adminAuth) = await AdminClientAsync();
        await admin.PostJsonAsync("/api/users",
            new CreateUserRequest("kid", "Kid", "kidpass1", UserRole.Member, null));
        var member = _factory.CreateClient();
        await member.LoginAsync("kid", "kidpass1");

        var response = await member.GetAsync($"/api/users/{adminAuth.User.Id}/points-log");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Custom_recurrence_round_trips_and_rotates_assignee_on_completion()
    {
        var (admin, adminAuth) = await AdminClientAsync();
        await admin.PostJsonAsync("/api/users",
            new CreateUserRequest("kid", "Kid", "kidpass1", UserRole.Member, null));
        var member = _factory.CreateClient();
        var memberAuth = await member.LoginAsync("kid", "kidpass1");

        // Custom days-of-week chore, round robin across both users (admin created first).
        var request = new CreateChoreRequest(
            "Trash", null, "🗑️", 5, RepeatType.Custom, CustomRecurrenceMode.DaysOfWeek,
            null, null, [DayOfWeek.Monday, DayOfWeek.Thursday], null, null, null, 1, false,
            AssignmentStrategy.RoundRobin, SchedulingPreference.FromScheduledDate, null,
            Start, [adminAuth.User.Id, memberAuth.User.Id], adminAuth.User.Id, null);

        var created = await (await admin.PostJsonAsync("/api/chores", request)).ReadAsync<ChoreDto>();
        Assert.Equal(CustomRecurrenceMode.DaysOfWeek, created.CustomMode);
        Assert.Equal(AssignmentStrategy.RoundRobin, created.AssignmentStrategy);
        Assert.Equal(2, created.Weekdays.Length);
        Assert.Equal(adminAuth.User.Id, created.CurrentAssignee!.Id);

        var completed = await (await admin.PostJsonAsync($"/api/chores/{created.Id}/complete",
            new CompleteChoreRequest(null))).ReadAsync<ChoreDto>();
        Assert.Equal(memberAuth.User.Id, completed.CurrentAssignee!.Id); // rotated
    }

    [Fact]
    public async Task Admin_can_skip_a_recurring_occurrence_without_points()
    {
        var (admin, _) = await AdminClientAsync();
        await admin.PostJsonAsync("/api/users",
            new CreateUserRequest("kid", "Kid", "kidpass1", UserRole.Member, null));
        var member = _factory.CreateClient();
        var memberAuth = await member.LoginAsync("kid", "kidpass1");

        var chore = await (await admin.PostJsonAsync("/api/chores", NewChore(memberAuth.User.Id)))
            .ReadAsync<ChoreDto>();

        var skipped = await (await admin.PostJsonAsync($"/api/chores/{chore.Id}/skip",
            new SkipChoreRequest("away this week"))).ReadAsync<ChoreDto>();
        Assert.Equal(Start.AddDays(1), skipped.DueAt); // advanced
        Assert.NotNull(skipped.LastCompletion);
        Assert.True(skipped.LastCompletion!.IsSkip);

        // No points were awarded for the skip.
        var log = await (await member.GetAsync($"/api/users/{memberAuth.User.Id}/points-log"))
            .ReadAsync<List<PointsLogEntryDto>>();
        Assert.Empty(log);
    }

    [Fact]
    public async Task Member_cannot_skip_a_chore()
    {
        var (admin, _) = await AdminClientAsync();
        await admin.PostJsonAsync("/api/users",
            new CreateUserRequest("kid", "Kid", "kidpass1", UserRole.Member, null));
        var member = _factory.CreateClient();
        var memberAuth = await member.LoginAsync("kid", "kidpass1");

        var chore = await (await admin.PostJsonAsync("/api/chores", NewChore(memberAuth.User.Id)))
            .ReadAsync<ChoreDto>();

        var forbidden = await member.PostJsonAsync($"/api/chores/{chore.Id}/skip", new SkipChoreRequest(null));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Skipping_a_one_time_chore_is_rejected()
    {
        var (admin, adminAuth) = await AdminClientAsync();
        var chore = await (await admin.PostJsonAsync("/api/chores",
            NewChore(adminAuth.User.Id) with { RepeatType = RepeatType.OneTime })).ReadAsync<ChoreDto>();

        var response = await admin.PostJsonAsync($"/api/chores/{chore.Id}/skip", new SkipChoreRequest(null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Member_can_reassign_the_current_occurrence()
    {
        var (admin, adminAuth) = await AdminClientAsync();
        await admin.PostJsonAsync("/api/users",
            new CreateUserRequest("kid", "Kid", "kidpass1", UserRole.Member, null));
        var member = _factory.CreateClient();
        var memberAuth = await member.LoginAsync("kid", "kidpass1");

        var request = NewChore(memberAuth.User.Id) with
        {
            AssigneeIds = [adminAuth.User.Id, memberAuth.User.Id],
        };
        var chore = await (await admin.PostJsonAsync("/api/chores", request)).ReadAsync<ChoreDto>();

        var reassigned = await (await member.PostJsonAsync($"/api/chores/{chore.Id}/reassign",
            new ReassignChoreRequest(adminAuth.User.Id))).ReadAsync<ChoreDto>();
        Assert.Equal(adminAuth.User.Id, reassigned.CurrentAssignee!.Id);
    }

    [Fact]
    public async Task Tags_are_listed_after_being_used_on_a_chore()
    {
        var (admin, adminAuth) = await AdminClientAsync();
        await admin.PostJsonAsync("/api/chores", NewChore(adminAuth.User.Id, ["kitchen", "weekly"]));

        var tags = await (await admin.GetAsync("/api/tags")).ReadAsync<List<TagDto>>();
        Assert.Equal(2, tags.Count);
        Assert.Contains(tags, t => t.Name == "kitchen");
    }
}
