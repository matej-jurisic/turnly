using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Turnly.Api.Realtime;

/// <summary>In-process fan-out of "shared data changed" signals to the open <c>/api/events</c>
/// streams. The signal carries no payload: clients just refetch, so nothing here needs to know
/// what a given viewer is allowed to see. Singleton.</summary>
public class ChangeBroadcaster
{
    private readonly ConcurrentDictionary<Guid, Channel<bool>> _subscribers = new();

    public int SubscriberCount => _subscribers.Count;

    public Subscription Subscribe()
    {
        // Capacity 1 + DropWrite coalesces a burst of changes into a single pending signal.
        var channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite
        });
        var id = Guid.NewGuid();
        _subscribers[id] = channel;
        return new Subscription(this, id, channel.Reader);
    }

    public void Publish()
    {
        foreach (var channel in _subscribers.Values)
            channel.Writer.TryWrite(true);
    }

    /// <summary>Whether a finished request changed data other clients may be showing: any successful
    /// write to the API, minus the per-user areas (sign-in, own notification inbox/settings).</summary>
    public static bool IsSharedChange(HttpRequest request, int statusCode)
    {
        if (statusCode is < 200 or >= 300)
            return false;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method))
            return false;
        var path = request.Path;
        return path.StartsWithSegments("/api")
            && !path.StartsWithSegments("/api/auth")
            && !path.StartsWithSegments("/api/notifications");
    }

    public sealed class Subscription : IDisposable
    {
        private readonly ChangeBroadcaster _owner;
        private readonly Guid _id;

        internal Subscription(ChangeBroadcaster owner, Guid id, ChannelReader<bool> reader)
        {
            _owner = owner;
            _id = id;
            Reader = reader;
        }

        public ChannelReader<bool> Reader { get; }

        public void Dispose() => _owner._subscribers.TryRemove(_id, out _);
    }
}

public static class ChangeBroadcastExtensions
{
    /// <summary>Publishes a change signal after every successful shared write. Runs once the endpoint
    /// has finished, so any transaction is committed before clients are told to refetch.</summary>
    public static IApplicationBuilder UseChangeBroadcast(this IApplicationBuilder app)
    {
        var broadcaster = app.ApplicationServices.GetRequiredService<ChangeBroadcaster>();
        return app.Use(async (context, next) =>
        {
            await next(context);
            if (ChangeBroadcaster.IsSharedChange(context.Request, context.Response.StatusCode))
                broadcaster.Publish();
        });
    }
}
