using System.Net;
using Turnly.Api.Endpoints;
using Turnly.Core.Dtos;
using Turnly.Core.Enums;

namespace Turnly.Tests.Integration;

public class NotificationTests : IDisposable
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

    [Fact]
    public async Task Vapid_key_endpoint_returns_the_configured_public_key()
    {
        var (admin, _) = await AdminClientAsync();

        var result = await (await admin.GetAsync("/api/notifications/vapid-key")).ReadAsync<VapidKeyResponse>();

        Assert.Equal("test-vapid-public-key", result.PublicKey);
    }

    [Fact]
    public async Task Subscribe_then_unsubscribe_succeeds()
    {
        var (admin, _) = await AdminClientAsync();

        var subscribe = await admin.PostJsonAsync("/api/notifications/subscribe",
            new PushSubscribeRequest("https://push.example/abc", "p256", "auth"));
        Assert.Equal(HttpStatusCode.NoContent, subscribe.StatusCode);

        var unsubscribe = await admin.PostJsonAsync("/api/notifications/unsubscribe",
            new { endpoint = "https://push.example/abc" });
        Assert.Equal(HttpStatusCode.NoContent, unsubscribe.StatusCode);
    }

    [Fact]
    public async Task Subscribe_rejects_an_incomplete_subscription()
    {
        var (admin, _) = await AdminClientAsync();

        var response = await admin.PostJsonAsync("/api/notifications/subscribe",
            new PushSubscribeRequest("https://push.example/abc", "", ""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Creating_a_chore_with_notifications_round_trips_them()
    {
        var (admin, adminAuth) = await AdminClientAsync();

        var notification = new ChoreNotificationInput(
            NotificationType.Reminder, NotificationTiming.Before, 2, NotificationOffsetUnit.Hours,
            NotificationRecipients.AllAssignees);
        var request = new CreateChoreRequest(
            "Dishes", null, "🍽️", 10, RepeatType.Daily, null, null, null, null, null, null, null, 1, false,
            AssignmentStrategy.KeepLastAssigned, SchedulingPreference.FromScheduledDate, null, false, null,
            Start, [adminAuth.User.Id], adminAuth.User.Id, null, [notification]);

        var created = await (await admin.PostJsonAsync("/api/chores", request)).ReadAsync<ChoreDto>();

        var entry = Assert.Single(created.Notifications);
        Assert.Equal(NotificationType.Reminder, entry.Type);
        Assert.Equal(NotificationTiming.Before, entry.Timing);
        Assert.Equal(2, entry.OffsetValue);
        Assert.Equal(NotificationOffsetUnit.Hours, entry.OffsetUnit);
        Assert.Equal(NotificationRecipients.AllAssignees, entry.Recipients);

        var fetched = await (await admin.GetAsync($"/api/chores/{created.Id}")).ReadAsync<ChoreDto>();
        Assert.Single(fetched.Notifications);
    }

    [Fact]
    public async Task Preferences_endpoint_returns_the_full_matrix()
    {
        var (admin, _) = await AdminClientAsync();

        var prefs = await (await admin.GetAsync("/api/notifications/preferences"))
            .ReadAsync<NotificationPreferenceDto[]>();

        Assert.Equal(Enum.GetValues<NotificationCategory>().Length, prefs.Length);
        Assert.All(prefs, p => Assert.Equal(DeliveryChannel.PushAndInbox, p.Channel));
    }

    [Fact]
    public async Task Setting_a_preference_round_trips()
    {
        var (admin, _) = await AdminClientAsync();

        var updated = await (await admin.PutJsonAsync("/api/notifications/preferences",
                new UpdateNotificationPreferenceRequest(NotificationCategory.ChoreOverdue, DeliveryChannel.Off)))
            .ReadAsync<NotificationPreferenceDto[]>();
        Assert.Equal(DeliveryChannel.Off,
            updated.Single(p => p.Category == NotificationCategory.ChoreOverdue).Channel);

        var fetched = await (await admin.GetAsync("/api/notifications/preferences"))
            .ReadAsync<NotificationPreferenceDto[]>();
        Assert.Equal(DeliveryChannel.Off,
            fetched.Single(p => p.Category == NotificationCategory.ChoreOverdue).Channel);
    }

    [Fact]
    public async Task Setting_a_preference_rejects_the_system_category()
    {
        var (admin, _) = await AdminClientAsync();

        var response = await admin.PutJsonAsync("/api/notifications/preferences",
            new UpdateNotificationPreferenceRequest(NotificationCategory.System, DeliveryChannel.Off));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Muting_a_chore_surfaces_on_the_chore_dto()
    {
        var (admin, adminAuth) = await AdminClientAsync();
        var chore = await (await admin.PostJsonAsync("/api/chores", new CreateChoreRequest(
                "Dishes", null, "🍽️", 10, RepeatType.Daily, null, null, null, null, null, null, null, 1, false,
                AssignmentStrategy.KeepLastAssigned, SchedulingPreference.FromScheduledDate, null, false, null,
                Start, [adminAuth.User.Id], adminAuth.User.Id, null, [])))
            .ReadAsync<ChoreDto>();

        var mute = await admin.PostJsonAsync($"/api/notifications/mutes/{chore.Id}", new MuteChoreRequest(null));
        Assert.Equal(HttpStatusCode.NoContent, mute.StatusCode);

        var muted = await (await admin.GetAsync($"/api/chores/{chore.Id}")).ReadAsync<ChoreDto>();
        Assert.True(muted.IsMuted);

        var unmute = await admin.DeleteAsync($"/api/notifications/mutes/{chore.Id}");
        Assert.Equal(HttpStatusCode.NoContent, unmute.StatusCode);

        var unmuted = await (await admin.GetAsync($"/api/chores/{chore.Id}")).ReadAsync<ChoreDto>();
        Assert.False(unmuted.IsMuted);
    }

    [Fact]
    public async Task A_mute_is_invisible_to_other_members()
    {
        var (admin, adminAuth) = await AdminClientAsync();
        await admin.PostJsonAsync("/api/users",
            new CreateUserRequest("kid", "Kid", "kidpass1", UserRole.Member, null));
        var chore = await (await admin.PostJsonAsync("/api/chores", new CreateChoreRequest(
                "Dishes", null, "🍽️", 10, RepeatType.Daily, null, null, null, null, null, null, null, 1, false,
                AssignmentStrategy.KeepLastAssigned, SchedulingPreference.FromScheduledDate, null, false, null,
                Start, [adminAuth.User.Id], adminAuth.User.Id, null, [])))
            .ReadAsync<ChoreDto>();

        await admin.PostJsonAsync($"/api/notifications/mutes/{chore.Id}", new MuteChoreRequest(null));

        var member = _factory.CreateClient();
        var memberAuth = await member.LoginAsync("kid", "kidpass1");
        member.UseBearer(memberAuth.AccessToken);

        var asMember = await (await member.GetAsync($"/api/chores/{chore.Id}")).ReadAsync<ChoreDto>();
        Assert.False(asMember.IsMuted);
    }

    [Fact]
    public async Task Muting_an_unknown_chore_is_not_found()
    {
        var (admin, _) = await AdminClientAsync();

        var response = await admin.PostJsonAsync($"/api/notifications/mutes/{Guid.NewGuid()}", new MuteChoreRequest(null));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private record VapidKeyResponse(string PublicKey);
}
