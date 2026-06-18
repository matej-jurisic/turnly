using Turnly.Api.Auth;
using Turnly.Core.Dtos;
using Turnly.Core.Services;

namespace Turnly.Api.Endpoints;

public static class SetupEndpoints
{
    public static void MapSetupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/setup");

        group.MapGet("/status", async (SetupService setup, CancellationToken ct) =>
            Results.Ok(new { needsSetup = await setup.NeedsSetupAsync(ct) }));

        group.MapPost("", async (SetupRequest req, SetupService setup, RefreshCookieManager cookies, HttpContext http, CancellationToken ct) =>
        {
            var result = await setup.CreateFirstAdminAsync(req, ct);
            return result.Succeeded
                ? result.Value!.WriteAuth(cookies, http)
                : result.Error!.ToProblem();
        });
    }
}
