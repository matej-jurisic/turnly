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
            AssignmentStrategy.KeepLastAssigned, SchedulingPreference.FromScheduledDate, null,
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

    private record VapidKeyResponse(string PublicKey);
}
