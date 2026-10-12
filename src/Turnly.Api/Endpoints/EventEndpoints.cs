using Turnly.Api.Realtime;

namespace Turnly.Api.Endpoints;

public static class EventEndpoints
{
    // Short enough to survive reverse-proxy idle timeouts and let the client spot a dead connection.
    private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(20);

    public static void MapEventEndpoints(this IEndpointRouteBuilder app)
    {
        // Server-Sent Events: one long-lived response per client, a "change" event whenever shared
        // data changes. Read with fetch (not EventSource) on the client so it can send the bearer.
        app.MapGet("/api/events", async (
            HttpContext context, ChangeBroadcaster broadcaster, IHostApplicationLifetime lifetime) =>
        {
            // Also end on shutdown, otherwise open streams hold the host until its shutdown timeout.
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(
                context.RequestAborted, lifetime.ApplicationStopping);
            var ct = cts.Token;

            var response = context.Response;
            response.ContentType = "text/event-stream";
            response.Headers.CacheControl = "no-cache";
            response.Headers["X-Accel-Buffering"] = "no"; // nginx: don't buffer the stream

            using var subscription = broadcaster.Subscribe();
            try
            {
                await response.WriteAsync(": connected\n\n", ct);
                await response.Body.FlushAsync(ct);

                var pending = subscription.Reader.WaitToReadAsync(ct).AsTask();
                while (!ct.IsCancellationRequested)
                {
                    var winner = await Task.WhenAny(pending, Task.Delay(Heartbeat, ct));
                    if (winner == pending)
                    {
                        await pending;
                        while (subscription.Reader.TryRead(out _)) { }
                        pending = subscription.Reader.WaitToReadAsync(ct).AsTask();
                        await response.WriteAsync("event: change\ndata: {}\n\n", ct);
                    }
                    else
                    {
                        await response.WriteAsync(": ping\n\n", ct);
                    }
                    await response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException)
            {
                // Client went away or the host is stopping.
            }
        }).RequireAuthorization();
    }
}
