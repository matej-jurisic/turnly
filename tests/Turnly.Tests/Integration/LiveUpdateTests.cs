using System.Net;
using Turnly.Api.Realtime;
using Turnly.Core.Dtos;
using Turnly.Core.Enums;

namespace Turnly.Tests.Integration;

public class LiveUpdateTests : IDisposable
{
    private readonly TurnlyApiFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static async Task<StreamReader> OpenStreamAsync(HttpClient client, CancellationToken ct)
    {
        var response = await client.GetAsync("/api/events", HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
        Assert.Equal(": connected", await reader.ReadLineAsync(ct));
        return reader;
    }

    private static async Task<bool> ReadsChangeAsync(StreamReader reader, CancellationToken ct)
    {
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line == "event: change")
                return true;
        }
        return false;
    }

    [Fact]
    public async Task Events_requires_authentication()
    {
        var client = _factory.CreateClient();
        await client.SetupAdminAsync();

        var response = await client.GetAsync("/api/events");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Events_stream_signals_a_change_made_by_another_client()
    {
        using var cts = new CancellationTokenSource(Timeout);
        var writer = _factory.CreateClient();
        var admin = await writer.SetupAdminAsync();
        writer.UseBearer(admin.AccessToken);
        var listener = _factory.CreateClient();
        listener.UseBearer(admin.AccessToken);

        using var reader = await OpenStreamAsync(listener, cts.Token);

        var chore = new CreateChoreRequest("Dishes", "Wash up", "🍽️", 10, RepeatType.Daily, null, null, null, null,
            null, null, null, 1, false, AssignmentStrategy.KeepLastAssigned, SchedulingPreference.FromScheduledDate,
            null, false, null, new DateTimeOffset(2026, 6, 17, 9, 0, 0, TimeSpan.Zero), [admin.User.Id],
            admin.User.Id, null);
        (await writer.PostJsonAsync("/api/chores", chore)).EnsureSuccessStatusCode();

        Assert.True(await ReadsChangeAsync(reader, cts.Token));
    }

    [Fact]
    public async Task Events_stream_ignores_reads_failed_writes_and_personal_writes()
    {
        using var cts = new CancellationTokenSource(Timeout);
        var writer = _factory.CreateClient();
        var admin = await writer.SetupAdminAsync();
        writer.UseBearer(admin.AccessToken);
        var listener = _factory.CreateClient();
        listener.UseBearer(admin.AccessToken);

        using var reader = await OpenStreamAsync(listener, cts.Token);

        await writer.GetAsync("/api/chores");
        Assert.False((await writer.PostJsonAsync("/api/tags", new { name = "" })).IsSuccessStatusCode);
        (await writer.PostJsonAsync("/api/notifications/inbox/read", new { })).EnsureSuccessStatusCode();
        // The first signal on the stream must be this one: the tag is the only shared write.
        (await writer.PostJsonAsync("/api/tags", new { name = "kitchen" })).EnsureSuccessStatusCode();

        Assert.Equal("", await reader.ReadLineAsync(cts.Token)); // blank line closing ": connected"
        Assert.Equal("event: change", await reader.ReadLineAsync(cts.Token));
        Assert.Equal("data: {}", await reader.ReadLineAsync(cts.Token));
        Assert.Equal("", await reader.ReadLineAsync(cts.Token));

    }

    [Fact]
    public void Broadcaster_coalesces_a_burst_and_drops_disposed_subscribers()
    {
        var broadcaster = new ChangeBroadcaster();
        var subscription = broadcaster.Subscribe();

        broadcaster.Publish();
        broadcaster.Publish();

        Assert.True(subscription.Reader.TryRead(out _));
        Assert.False(subscription.Reader.TryRead(out _));

        subscription.Dispose();
        Assert.Equal(0, broadcaster.SubscriberCount);
    }
}
