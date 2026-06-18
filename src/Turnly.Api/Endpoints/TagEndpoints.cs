using Turnly.Core.Services;

namespace Turnly.Api.Endpoints;

public static class TagEndpoints
{
    public static void MapTagEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tags").RequireAuthorization();

        group.MapGet("", async (TagService tags, CancellationToken ct) =>
            Results.Ok(await tags.ListAsync(ct)));
    }
}
