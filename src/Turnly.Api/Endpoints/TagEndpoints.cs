using Turnly.Core.Dtos;
using Turnly.Core.Services;

namespace Turnly.Api.Endpoints;

public static class TagEndpoints
{
    public static void MapTagEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tags").RequireAuthorization();

        group.MapGet("", async (TagService tags, CancellationToken ct) =>
            Results.Ok(await tags.ListAsync(ct)));

        group.MapPost("", async (CreateTagRequest req, TagService tags, CancellationToken ct) =>
        {
            var result = await tags.CreateAsync(req.Name, ct);
            return result.Succeeded ? Results.Ok(result.Value) : result.Error!.ToProblem();
        }).RequireAuthorization("Admin");

        group.MapDelete("/{id:guid}", async (Guid id, TagService tags, CancellationToken ct) =>
        {
            var result = await tags.DeleteAsync(id, ct);
            return result.Succeeded ? Results.NoContent() : result.Error!.ToProblem();
        }).RequireAuthorization("Admin");
    }
}
